# Verificação do módulo `fiscal`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-fiscal.ps1
#
# Âmbito reduzido pelo ADR-036: taxa com vigência e determinação à data do
# facto gerador. Exportação SAF-T e declarações periódicas continuam adiadas
# — mas o motor de IRT/INSS existe desde 2026-08-30 (ver casos 13-19).
#
# Re-executável: cada corrida usa um código de taxa próprio, derivado do
# carimbo temporal, para os casos que exercitam a série em si (1-12). Os
# casos 13-19 semeiam INSS e a tabela de IRT com os códigos e datas
# *reais* que `payroll` consome — por isso são idempotentes por desenho, e
# não por código único: uma segunda corrida encontra-os já semeados e
# confirma-o em vez de os duplicar.
#
# **Carrega-se antes de `verify-payroll`** (verify-all.ps1): sem INSS e IRT
# em vigor, `AddPayrollItem` recusa por `NoRateInForce`/`NoScheduleInForce`
# — mesma dependência de ordem que `verify-payables` → `verify-ledger`.

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_ambiente.ps1")
$base = Get-RivoBaseUrl
$failures = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        $detail = & $Body
        Write-Host ("  PASSA  " + $Name + $(if ($detail) { "  -- $detail" } else { "" })) -ForegroundColor Green
    }
    catch {
        Write-Host ("  FALHA  " + $Name + "  -- " + $_.Exception.Message) -ForegroundColor Red
        $script:failures++
    }
}

function Get-StatusCode {
    param([scriptblock]$Request)
    try { & $Request | Out-Null; return 200 }
    catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        if ($_.Exception.Message -match "401|Unauthorized") { return 401 }
        throw
    }
}

function Invoke-Sql { param([string]$q) return (Invoke-RivoSql $q) }

$dotenv = Get-RivoCredentials

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

# Utilizador com perfil Sales, para verificar que quem vende nao fixa a taxa
# que a sua propria venda vai liquidar.
$salesEmail = "vendas-$stamp@rivo.ao"
$body = @{ email = $salesEmail; password = $pass } | ConvertTo-Json
$salesUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
$body = @{ profile = "Sales" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$salesUserId/roles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
$salesHeaders = @{ Authorization = "Bearer " + (Get-Token $salesEmail $pass) }

# Codigo de taxa proprio desta corrida. Maximo 10 caracteres na base de dados,
# por isso so os ultimos seis digitos do carimbo.
$codigo = "V" + ("$stamp".Substring("$stamp".Length - 6))

Write-Host "`n=== Modulo fiscal ===`n"

Test-Case "1. Schema fiscal com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from fiscal.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de fiscal" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='fiscal'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='fiscal' and table_name in ('app_user','audit_event','customer','sales_invoice')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema fiscal" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. Quem vende nao fixa a taxa (ADR-036)" {
    $has = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value like 'fiscal.%'"
    if ($has -ne "0") { throw "Sales tem permissoes de fiscal" }

    $body = @{ code = "X$stamp"; description = "Tentativa" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 403) { throw "esperado 403 para Sales, obtido $code" }
    "Sales sem fiscal.*; escrita devolve 403"
}

Test-Case "3. Abrir serie de taxa" {
    $body = @{ code = $codigo; description = "IVA - suite de verificacao" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.scheduleId) { throw "sem scheduleId na resposta" }
    $script:scheduleId = $r.scheduleId
    "serie $codigo aberta"
}

Test-Case "4. Serie duplicada e recusada" {
    $body = @{ code = $codigo; description = "Outra" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "duas series com o mesmo codigo tornariam a determinacao ambigua"
}

Test-Case "5. Introduzir duas versoes com vigencias que nao se sobrepoem" {
    $b1 = @{ percentage = 5; effectiveFrom = "2026-01-01"; effectiveTo = "2026-06-30"; legalInstrument = "Lei 14/23" } | ConvertTo-Json
    Invoke-RestMethod "$base/fiscal/tax-rates/$($script:scheduleId)/versions" -Method Post -Body $b1 -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $b2 = @{ percentage = 7; effectiveFrom = "2026-07-01"; legalInstrument = "Lei 20/26" } | ConvertTo-Json
    Invoke-RestMethod "$base/fiscal/tax-rates/$($script:scheduleId)/versions" -Method Post -Body $b2 -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $n = Invoke-Sql "select count(*) from fiscal.tax_rate_version v join fiscal.tax_rate_schedule s on s.id=v.tax_rate_schedule_id where s.code='$codigo'"
    if ($n -ne "2") { throw "esperadas 2 versoes, obtidas $n" }
    "5% ate Jun/2026, 7% a partir de Jul"
}

Test-Case "6. Vigencia sobreposta e recusada com 409" {
    $body = @{ percentage = 9; effectiveFrom = "2026-03-01"; legalInstrument = "Colide" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates/$($script:scheduleId)/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 e nao 400: o pedido esta bem formado, colide com o estado"
}

Test-Case "7. Instrumento legal em branco e 400, nao 409 (ADR-011)" {
    $body = @{ percentage = 3; effectiveFrom = "2030-01-01"; legalInstrument = "   " } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates/$($script:scheduleId)/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "campo mal preenchido distingue-se de conflito de estado"
}

Test-Case "8. Determinacao segue a data do facto gerador (ADR-011 par.3)" {
    $marco = Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$codigo&taxPointDate=2026-03-15" -Headers $adminHeaders
    if ($marco.percentage -ne 5) { throw "esperado 5% em Marco, obtido $($marco.percentage)" }
    if ($marco.legalInstrument -ne "Lei 14/23") { throw "instrumento legal errado: $($marco.legalInstrument)" }

    $setembro = Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$codigo&taxPointDate=2026-09-01" -Headers $adminHeaders
    if ($setembro.percentage -ne 7) { throw "esperado 7% em Setembro, obtido $($setembro.percentage)" }

    "Marco=5% (Lei 14/23), Setembro=7% (Lei 20/26)"
}

Test-Case "9. Sem taxa em vigor a data devolve 404, nao a mais proxima" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$codigo&taxPointDate=2020-01-01" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "recair na versao mais proxima inventaria o valor"
}

Test-Case "10. Isencao devolve 501 enquanto nao houver catalogo de codigos" {
    foreach ($c in @("ISE", "NS")) {
        $code = Get-StatusCode { Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$c&taxPointDate=2026-03-15" -Headers $adminHeaders }
        if ($code -ne 501) { throw "esperado 501 para $c, obtido $code" }
    }
    "ISE e NS: capacidade adiada (ADR-036), nao defeito do pedido"
}

Test-Case "11. Introduzir versao de taxa e auditado (ADR-011 par.5)" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='fiscal.tax_rate.introduced' and entity_id='$($script:scheduleId)'"
    if ([int]$n -lt 2) { throw "esperados >=2 registos de auditoria, obtidos $n" }
    $actor = Invoke-Sql "select count(*) from audit.audit_event where action='fiscal.tax_rate.introduced' and entity_id='$($script:scheduleId)' and actor_id is null"
    if ($actor -ne "0") { throw "$actor registos sem actor" }
    "$n registos, todos com actor"
}

Test-Case "12. Serie por imposto e codigo e unica na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select kind, code from fiscal.tax_rate_schedule group by kind, code having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup pares (kind, code) repetidos" }
    "indice unico e a segunda linha de defesa"
}

Test-Case "13. Semear INSS do trabalhador (3%) e recusar Sales a faze-lo" {
    $body = @{ code = "SALES"; description = "Tentativa" } | ConvertTo-Json
    # Ja verificado no caso 2 para IVA; aqui confirma-se que a mesma politica
    # cobre o INSS -- nao ha um segundo conjunto de permissoes por tipo de
    # imposto (fiscal.rates.write cobre todas as series).
    $existeSemPerfil = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value='fiscal.rates.write'"
    if ($existeSemPerfil -ne "0") { throw "Sales tem fiscal.rates.write" }

    $existe = (Invoke-Sql "select id from fiscal.tax_rate_schedule where kind='EmployeeSocialSecurity' and code='INSS'").Trim()
    if ($existe) {
        $script:inssTrabalhadorId = $existe
        "ja semeado por uma corrida anterior ($existe)"
    }
    else {
        # 1 = TaxKind.EmployeeSocialSecurity. O corpo JSON nao tem um
        # JsonStringEnumConverter registado (nenhum outro sitio no codigo
        # ainda precisava de o enviar por nome) -- o valor ordinal e o unico
        # que o model binding aceita aqui. A query string do caso 15 e
        # diferente: o binding de parametro de rota/query aceita o nome.
        $abrir = @{ kind = 1; code = "INSS"; description = "INSS - contribuicao do trabalhador" } | ConvertTo-Json
        $r = Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $abrir -ContentType "application/json" -Headers $adminHeaders
        $script:inssTrabalhadorId = $r.scheduleId

        # Vigencia aberta desde antes de qualquer folha que a suite de payroll
        # processe: nao e um valor de teste, e o que `payroll` consome de
        # facto. legalInstrument documenta a origem -- confirmado pelo
        # utilizador em resposta directa, nao levantamento secundario
        # (`.claude/docs/rivo-fiscal-regras-angola-v1.md` nao e fonte).
        $versao = @{
            percentage      = 3
            effectiveFrom   = "2020-01-01"
            legalInstrument = "Confirmado pelo utilizador em 2026-08-30 (sem tecto, 3% sobre o bruto inteiro); nao fonte fiscal profissional"
        } | ConvertTo-Json
        Invoke-RestMethod "$base/fiscal/tax-rates/$($script:inssTrabalhadorId)/versions" -Method Post -Body $versao -ContentType "application/json" -Headers $adminHeaders | Out-Null
        "aberto e semeado agora ($($script:inssTrabalhadorId))"
    }
}

Test-Case "14. Semear INSS patronal (8%)" {
    $existe = (Invoke-Sql "select id from fiscal.tax_rate_schedule where kind='EmployerSocialSecurity' and code='INSS'").Trim()
    if ($existe) {
        $script:inssPatronalId = $existe
        "ja semeado por uma corrida anterior ($existe)"
    }
    else {
        # 2 = TaxKind.EmployerSocialSecurity -- ver a nota no caso 13.
        $abrir = @{ kind = 2; code = "INSS"; description = "INSS - contribuicao patronal" } | ConvertTo-Json
        $r = Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $abrir -ContentType "application/json" -Headers $adminHeaders
        $script:inssPatronalId = $r.scheduleId

        $versao = @{
            percentage      = 8
            effectiveFrom   = "2020-01-01"
            legalInstrument = "Confirmado pelo utilizador em 2026-08-30 (sem tecto, 8% sobre o bruto inteiro); nao fonte fiscal profissional"
        } | ConvertTo-Json
        Invoke-RestMethod "$base/fiscal/tax-rates/$($script:inssPatronalId)/versions" -Method Post -Body $versao -ContentType "application/json" -Headers $adminHeaders | Out-Null
        "aberto e semeado agora ($($script:inssPatronalId))"
    }
}

Test-Case "15. Determinacao de INSS devolve 3% (trabalhador) e 8% (patronal)" {
    $trabalhador = Invoke-RestMethod "$base/fiscal/tax-rates/determination?kind=EmployeeSocialSecurity&taxCode=INSS&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($trabalhador.percentage -ne 3) { throw "esperado 3%, obtido $($trabalhador.percentage)" }

    $patronal = Invoke-RestMethod "$base/fiscal/tax-rates/determination?kind=EmployerSocialSecurity&taxCode=INSS&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($patronal.percentage -ne 8) { throw "esperado 8%, obtido $($patronal.percentage)" }
    "trabalhador=3%, patronal=8%"
}

Test-Case "16. Semear a tabela de escalões de IRT (Tabela B, Lei n.o 14/25)" {
    $existe = (Invoke-Sql "select id from fiscal.income_tax_schedule").Trim()
    if ($existe) {
        $script:irtScheduleId = $existe
        "ja semeada por uma corrida anterior ($existe)"
    }
    else {
        # Os 11 escalões da Tabela B. Os dois que estavam por confirmar em
        # `docs/rivo-fiscal-regras-angola-v1.md` §1.4 (escalao 2: 12.500;
        # escalao 7: 292.250) vieram do utilizador directamente, nao do
        # levantamento -- e e por isso que se pode semear em producao-like,
        # nao so em teste.
        $escaloes = @(
            @{ lowerBound = 0; fixedPortion = 0; rate = 0 }
            @{ lowerBound = 150000; fixedPortion = 12500; rate = 16.0 }
            @{ lowerBound = 200000; fixedPortion = 31250; rate = 18.0 }
            @{ lowerBound = 300000; fixedPortion = 49250; rate = 19.0 }
            @{ lowerBound = 500000; fixedPortion = 87250; rate = 20.0 }
            @{ lowerBound = 1000000; fixedPortion = 187250; rate = 21.0 }
            @{ lowerBound = 1500000; fixedPortion = 292250; rate = 22.0 }
            @{ lowerBound = 2000000; fixedPortion = 402250; rate = 23.0 }
            @{ lowerBound = 2500000; fixedPortion = 517250; rate = 24.0 }
            @{ lowerBound = 5000000; fixedPortion = 1117250; rate = 24.5 }
            @{ lowerBound = 10000000; fixedPortion = 2342250; rate = 25.0 }
        )
        $body = @{
            brackets        = $escaloes
            effectiveFrom   = "2020-01-01"
            legalInstrument = "Lei n.o 14/25 (Tabela B); parcelas dos escaloes 2 e 7 confirmadas pelo utilizador em 2026-08-30, nao fonte fiscal profissional"
        } | ConvertTo-Json -Depth 5

        $r = Invoke-RestMethod "$base/fiscal/income-tax-schedule/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
        $script:irtScheduleId = $r.versionId
        "semeada agora, 11 escaloes"
    }
}

Test-Case "17. Escalao de isencao: ate 150.000 nao paga IRT" {
    $r = Invoke-RestMethod "$base/fiscal/income-tax-schedule/determination?taxableIncome=150000&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($r.amount -ne 0) { throw "esperado 0, obtido $($r.amount)" }
    "150.000 exacto ainda no escalao de isencao"
}

Test-Case "18. Um kwanza acima da isencao ja paga (salto documentado, 12.500)" {
    $r = Invoke-RestMethod "$base/fiscal/income-tax-schedule/determination?taxableIncome=150001&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($r.amount -ne 12500.16) { throw "esperado 12500.16, obtido $($r.amount)" }
    if ($r.fixedPortion -ne 12500) { throw "parcela fixa errada: $($r.fixedPortion)" }
    "150.001: IRT=12500.16, confirmando o salto (docs/rivo-fiscal-regras-angola-v1.md 1.5)"
}

Test-Case "19. Exemplo documentado: bruto 250.000, materia colectavel 242.500, IRT 38.900" {
    $r = Invoke-RestMethod "$base/fiscal/income-tax-schedule/determination?taxableIncome=242500&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($r.amount -ne 38900) { throw "esperado 38900, obtido $($r.amount)" }
    if ($r.rate -ne 18) { throw "taxa errada: $($r.rate)" }
    if ($r.bracketLowerBound -ne 200000) { throw "escalao errado, excesso de $($r.bracketLowerBound)" }
    "242.500 -> escalao 200.001-300.000, IRT=38.900 (docs/rivo-fiscal-regras-angola-v1.md 1.6)"
}

Test-Case "20. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $r = Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$codigo&taxPointDate=2026-03-15" -Headers $adminHeaders
    if ($r.percentage -ne 5) { throw "taxa perdida ou alterada: $($r.percentage)" }

    $irt = Invoke-RestMethod "$base/fiscal/income-tax-schedule/determination?taxableIncome=242500&taxPointDate=2026-08-31" -Headers $adminHeaders
    if ($irt.amount -ne 38900) { throw "IRT perdido ou alterado: $($irt.amount)" }
    "determinacao de IVA e de IRT intactas apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
