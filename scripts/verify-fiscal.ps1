# Verificação do módulo `fiscal`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-fiscal.ps1
#
# Âmbito reduzido pelo ADR-036: taxa com vigência e determinação à data do
# facto gerador. Não há motor fiscal, exportação SAF-T nem declarações — e o
# que esta suite verifica é exactamente a fatia que existe.
#
# Re-executável: cada corrida usa um código de taxa próprio, derivado do
# carimbo temporal. Duas corridas seguidas passam as duas.

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

Test-Case "13. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $r = Invoke-RestMethod "$base/fiscal/tax-rates/determination?taxCode=$codigo&taxPointDate=2026-03-15" -Headers $adminHeaders
    if ($r.percentage -ne 5) { throw "taxa perdida ou alterada: $($r.percentage)" }
    "determinacao intacta apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
