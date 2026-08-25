# Verificação de Contabilidade & Fecho e Planeamento.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-ledger.ps1
#
# São os dois contextos que faltavam a `finance`, e é com Planeamento que
# **BR-8 deixa de ser uma recusa e passa a ser uma regra**: até aqui uma
# política com `RequiresBudgetCheck` recusava a submissão porque não havia
# orçamento nenhum contra que verificar.
#
# O plano de contas usado é inventado para a suite. O Rivo fixa a estrutura que
# o XSD do SAF-T fixa, e **não o PGC angolano** — esse não está em fonte
# primária neste projecto, e inventá-lo seria pior do que não o ter.
#
# Re-executável: cada corrida usa códigos derivados do carimbo temporal.

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
$curto = "$stamp".Substring("$stamp".Length - 6)

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

function New-PerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo@rivo.ao"
    $body = @{ email = $email; password = $script:pass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $body = @{ profile = $Perfil } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body $body -ContentType "application/json" -Headers $script:adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $script:pass) }
}

# `Manager` elabora o orçamento; `Finance` aprova-o e lança. Nenhum faz os dois.
$managerHeaders = New-PerfilHeaders "Manager" "chefe-l-$stamp"
$financeHeaders = New-PerfilHeaders "Finance" "conta-l-$stamp"

# Códigos próprios desta corrida. O plano de contas do SAF-T admite letras.
$raiz = "R$curto"
$agregada = "A$curto"
$custo = "C$curto"
$fornecedor = "F$curto"
$diario = "D$curto"
$centroCodigo = "CC$curto"

# Contas para a postagem automatica: cliente, proveito e IVA liquidado.
$cliente2 = "K$curto"
$proveito = "P$curto"
$ivaLiq = "I$curto"

# Taxa propria desta corrida, como faz a suite de finance.
$codigoTaxa = "T" + $curto

# Ano fiscal próprio de cada corrida para os **períodos contabilísticos**: são
# únicos por (ano, número), e reutilizar o ano corrente faria a segunda corrida
# colidir no fecho.
$ano = 2030 + ([int]$curto % 900)

# **O orçamento é outra coisa, e tem de ser do ano corrente.**
#
# BR-8 verifica contra a data de hoje — é essa que escolhe o mês do tecto. Um
# orçamento num ano sintético daria `NoBudget` em vez de `Exceeded`, e a suite
# passaria pela razão errada. Foi o que aconteceu a 2026-08-25, e só apareceu
# quando a asserção deixou de aceitar "409 ou 501".
$anoOrcamento = (Get-Date).Year

$responsavel = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ fullName = "Responsavel CC $curto" } | ConvertTo-Json)).employeeId

$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ code = $codigoTaxa; description = "IVA - suite ledger" } | ConvertTo-Json)).scheduleId

Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ percentage = 14; effectiveFrom = "2026-01-01"; legalInstrument = "Lei 20/26" } | ConvertTo-Json) | Out-Null

Write-Host "`n=== Contabilidade & Fecho e Planeamento ===`n"

Test-Case "1. Schema finance ganhou as tabelas de contabilidade e planeamento" {
    foreach ($t in @("ledger_account", "journal", "journal_entry", "journal_entry_line",
            "accounting_period", "cost_centre", "budget", "budget_line", "cost_forecast")) {
        $existe = Invoke-Sql "select count(*) from information_schema.tables where table_schema='finance' and table_name='$t'"
        if ($existe -ne "1") { throw "tabela $t em falta" }
    }
    "9 tabelas novas no schema finance"
}

Test-Case "2. O Rivo nao traz plano de contas semeado" {
    # A estrutura e do XSD; o conteudo nao esta em fonte primaria, e inventa-lo
    # seria pior do que nao o ter.
    $n = Invoke-Sql "select count(*) from finance.ledger_account"
    if ([int]$n -gt 0 -and $script:primeiraCorrida) { throw "plano semeado sem fonte" }
    "o plano carrega-se; a estrutura e que e imposta"
}

Test-Case "3. Carregar plano de contas de cima para baixo" {
    $r = Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $raiz; name = "Custos"; category = "GR" } | ConvertTo-Json)
    if (-not $r.accountId) { throw "sem accountId" }

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $agregada; name = "Fornecimentos"; category = "GA"; parentCode = $raiz } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $custo; name = "Combustiveis"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $fornecedor; name = "Fornecedores"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $cliente2; name = "Clientes c/c"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $proveito; name = "Prestacao de servicos"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $ivaLiq; name = "IVA liquidado"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) | Out-Null

    "GR -> GA -> GM, com GroupingCode a apontar ao grau acima"
}

Test-Case "4. Conta que nao e de 1.o grau exige agregadora" {
    # O XSD e explicito: "excepto para as contas do 1.o grau, deve ser indicada
    # a conta agregadora respectiva".
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ code = "X$curto"; name = "Orfa"; category = "GM" } | ConvertTo-Json) }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    # E a de 1.o grau nao tem agregadora acima.
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ code = "Y$curto"; name = "Raiz com pai"; category = "GR"; parentCode = $raiz } | ConvertTo-Json) }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    "GroupingCode obrigatorio fora do 1.o grau, e proibido nele"
}

Test-Case "5. Agregadora inexistente distingue-se de codigo repetido" {
    $c1 = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ code = "Z$curto"; name = "Sem pai"; category = "GM"; parentCode = "NAO-EXISTE" } | ConvertTo-Json) }
    if ($c1 -ne 404) { throw "agregadora inexistente: esperado 404, obtido $c1" }

    $c2 = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ code = $custo; name = "Outra"; category = "GM"; parentCode = $agregada } | ConvertTo-Json) }
    if ($c2 -ne 409) { throw "codigo repetido: esperado 409, obtido $c2" }

    "404 para o que nao existe, 409 para o que colide"
}

Test-Case "6. Abrir diario e periodo" {
    Invoke-RestMethod "$base/finance/ledger/journals" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ code = $diario; name = "Diversos" } | ConvertTo-Json) | Out-Null

    foreach ($p in @(7, 8)) {
        Invoke-RestMethod "$base/finance/ledger/periods" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ fiscalYear = $ano; number = $p } | ConvertTo-Json) | Out-Null
    }

    $n = Invoke-Sql "select count(*) from finance.accounting_period where fiscal_year=$ano"
    if ($n -ne "2") { throw "esperados 2 periodos, obtidos $n" }
    "diario $diario e periodos $ano/7 e $ano/8"
}

Test-Case "7. Lancamento equilibrado entra nos livros" {
    $body = @{
        journalCode     = $diario
        archivalNumber  = "ARQ-$curto"
        transactionDate = "$ano-08-15"
        fiscalYear      = $ano
        period          = 8
        description     = "Combustivel de Agosto"
        type            = "N"
        lines           = @(
            @{ accountCode = $custo; side = "Debit"; amount = 100000; description = "Custo" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = 100000; description = "Divida" }
        )
    } | ConvertTo-Json -Depth 5

    $r = Invoke-RestMethod "$base/finance/ledger/entries" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders
    $script:lancamentoId = $r.entryId

    # O XSD fixa a composicao: data, diario e numero de arquivo, por espacos.
    if ($r.transactionId -ne "$ano-08-15 $diario ARQ-$curto") { throw "TransactionID errado: $($r.transactionId)" }
    "$($r.transactionId)"
}

Test-Case "8. Lancamento que nao equilibra e recusado com razao propria" {
    $body = @{
        journalCode = $diario; archivalNumber = "ARQ2-$curto"; transactionDate = "$ano-08-16"
        fiscalYear = $ano; period = 8; description = "Nao bate"
        lines       = @(
            @{ accountCode = $custo; side = "Debit"; amount = 100000; description = "Custo" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = 90000; description = "Divida" }
        )
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod "$base/finance/ledger/entries" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders | Out-Null
        throw "esperado 400, o lancamento passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 400) { throw "esperado 400, obtido $([int]$_.Exception.Response.StatusCode)" }
        # A razao importa: nao e "pedido invalido", e a partida dobrada.
        if ($_.ErrorDetails.Message -notmatch "partidaDobrada") { throw "razao errada: $($_.ErrorDetails.Message)" }
    }
    "400 com chave `partidaDobrada`, nao um erro de validacao qualquer"
}

Test-Case "9. Nao se lanca em conta agregadora" {
    $body = @{
        journalCode = $diario; archivalNumber = "ARQ3-$curto"; transactionDate = "$ano-08-17"
        fiscalYear = $ano; period = 8; description = "Na agregadora"
        lines       = @(
            @{ accountCode = $agregada; side = "Debit"; amount = 100; description = "A" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = 100; description = "B" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/entries" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "o total de uma agregadora tem de continuar a ser a soma das filhas"
}

Test-Case "10. Chave do SAF-T repetida e recusada" {
    $body = @{
        journalCode = $diario; archivalNumber = "ARQ-$curto"; transactionDate = "$ano-08-15"
        fiscalYear = $ano; period = 8; description = "Mesma chave"
        lines       = @(
            @{ accountCode = $custo; side = "Debit"; amount = 1; description = "A" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = 1; description = "B" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/entries" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "TransactionID e unico no ficheiro SAF-T"
}

Test-Case "11. Balancete equilibra e soma por conta" {
    $b = Invoke-RestMethod "$base/finance/ledger/trial-balance?fiscalYear=$ano" -Headers $financeHeaders

    if (-not $b.isBalanced) { throw "balancete nao equilibra" }
    if ([decimal]$b.totalDebit -ne 100000) { throw "debito total $($b.totalDebit)" }
    if ([decimal]$b.totalDebit -ne [decimal]$b.totalCredit) { throw "debito != credito" }

    $linha = $b.lines | Where-Object { $_.accountCode -eq $custo }
    if ([decimal]$linha.closingDebit -ne 100000) { throw "conta de custo com $($linha.closingDebit)" }

    "100000 a debito e a credito; so contas de movimento aparecem"
}

Test-Case "12. Fechar o periodo para de aceitar lancamentos" {
    Invoke-RestMethod "$base/finance/ledger/periods/$ano/8/closure" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ closedByEmployeeId = $responsavel } | ConvertTo-Json) | Out-Null

    $body = @{
        journalCode = $diario; archivalNumber = "TARDE-$curto"; transactionDate = "$ano-08-31"
        fiscalYear = $ano; period = 8; description = "Depois do fecho"
        lines       = @(
            @{ accountCode = $custo; side = "Debit"; amount = 1; description = "A" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = 1; description = "B" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/entries" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    # 409 e nao 400: o lancamento esta bem formado, e noutro periodo entrava.
    "o periodo fechado recusa escrita, e e isso que torna o balancete um facto"
}

Test-Case "13. Anular um lancamento de periodo fechado e recusado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/entries/$($script:lancamentoId)/void" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ reason = "Engano" } | ConvertTo-Json) }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "corrige-se por lancamento de regularizacao, que fica visivel"
}

Test-Case "14. Fechar e reabrir sao mais restritos que lancar" {
    # `Finance` lanca mas nao fecha — reabrir faz numeros ja reportados voltarem
    # a mexer-se, e isso e do mesmo calibre que abrir uma serie de documento.
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/periods/$ano/7/closure" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ closedByEmployeeId = $responsavel } | ConvertTo-Json) }
    if ($code -ne 403) { throw "Finance fechou periodo: esperado 403, obtido $code" }

    $temFechar = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.ledger.close'"
    $temLancar = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.ledger.write'"
    if ($temFechar -ne "0") { throw "Finance tem finance.ledger.close" }
    if ($temLancar -ne "1") { throw "Finance nao lanca" }

    "Finance lanca; so Admin fecha e reabre"
}

Test-Case "15. Reabrir exige motivo e fica na trilha com accao propria" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/periods/$ano/8/reopening" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ reason = "   " } | ConvertTo-Json) }
    if ($code -ne 409) { throw "sem motivo: esperado 409, obtido $code" }

    Invoke-RestMethod "$base/finance/ledger/periods/$ano/8/reopening" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ reason = "Factura de fornecedor chegou depois do fecho" } | ConvertTo-Json) | Out-Null

    $n = Invoke-Sql "select count(*) from audit.audit_event where action='finance.accounting_period.reopened'"
    if ([int]$n -lt 1) { throw "reabertura nao ficou na trilha" }

    $estado = Invoke-Sql "select status from finance.accounting_period where fiscal_year=$ano and number=8"
    if ($estado -ne "Open") { throw "periodo nao reabriu: $estado" }

    "accao propria na trilha — quem audita tem de a encontrar sem a procurar entre os fechos"
}

# ---------- Planeamento ----------

Test-Case "16. Centro de custo nao e departamento (D4)" {
    $r = Invoke-RestMethod "$base/finance/planning/cost-centres" -Method Post -ContentType "application/json" -Headers $managerHeaders `
        -Body (@{ code = $centroCodigo; name = "Operacoes"; responsibleEmployeeId = $responsavel } | ConvertTo-Json)
    $script:centroId = $r.costCentreId

    # Sem departamento e um estado normal, nao dado em falta.
    $dep = Invoke-Sql "select count(*) from finance.cost_centre where id='$($script:centroId)' and department_id is null"
    if ($dep -ne "1") { throw "centro de custo nasceu com departamento" }

    # E sem chave estrangeira para hr: sao schemas de modulos distintos.
    $fk = Invoke-Sql @"
select count(*) from sys.foreign_keys fk
join sys.tables t on t.object_id = fk.parent_object_id
join sys.tables rt on rt.object_id = fk.referenced_object_id
where t.name = 'cost_centre' and schema_name(rt.schema_id) = 'hr'
"@
    if ($fk -ne "0") { throw "cost_centre referencia hr por FK" }

    "mapeamento opcional por desenho, e sem FK entre schemas"
}

Test-Case "17. Orcamento em rascunho nao controla nada" {
    $meses = @{}
    1..12 | ForEach-Object { $meses["$_"] = 500000 }

    $r = Invoke-RestMethod "$base/finance/planning/budgets" -Method Post -ContentType "application/json" -Headers $managerHeaders `
        -Body (@{ costCentreId = $script:centroId; fiscalYear = $anoOrcamento; currency = "AOA"; monthlyCeilings = $meses } | ConvertTo-Json -Depth 5)
    $script:orcamentoId = $r.budgetId

    if ($r.estado -ne "Draft") { throw "orcamento nasceu $($r.estado)" }

    $total = Invoke-Sql "select annual_total from finance.budget where id='$($script:orcamentoId)'"
    if ([decimal]$total -ne 6000000) { throw "total anual $total" }

    "6.000.000 em 12 meses, e ainda sem forca"
}

Test-Case "18. Quem elabora o orcamento nao o aprova (BR-8 no catalogo)" {
    # Se fosse a mesma pessoa, bastava subir o tecto para o proprio pedido
    # passar a caber — e a verificacao orcamental deixaria de verificar nada.
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/planning/budgets/$($script:orcamentoId)/approval" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ approvedByEmployeeId = $responsavel } | ConvertTo-Json) }
    if ($code -ne 403) { throw "Manager aprovou orcamento: esperado 403, obtido $code" }

    $escreve = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='finance.planning.write'"
    $aprova = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='finance.budgets.approve'"
    if ($escreve -ne "1") { throw "Manager nao escreve orcamentos" }
    if ($aprova -ne "0") { throw "Manager pode aprovar orcamentos" }

    "Manager escreve, Finance aprova; as duas listas nao se sobrepoem"
}

Test-Case "19. Aprovar poe em vigor, e depois o tecto nao se altera" {
    Invoke-RestMethod "$base/finance/planning/budgets/$($script:orcamentoId)/approval" -Method Post -ContentType "application/json" -Headers $financeHeaders `
        -Body (@{ approvedByEmployeeId = $responsavel } | ConvertTo-Json) | Out-Null

    $estado = Invoke-Sql "select status from finance.budget where id='$($script:orcamentoId)'"
    if ($estado -ne "Approved") { throw "estado $estado" }

    # Subir o tecto depois de aprovado esvaziaria a aprovacao, e com ela BR-8.
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/planning/budgets/$($script:orcamentoId)/revision" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ monthlyCeilings = @{ "8" = 9000000 } } | ConvertTo-Json -Depth 5) }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $n = Invoke-Sql "select count(*) from audit.audit_event where action='finance.budget.approved' and entity_id='$($script:orcamentoId)'"
    if ($n -ne "1") { throw "aprovacao nao ficou na trilha" }

    "aprovado controla e deixa de se mexer; a aprovacao fica na trilha"
}

Test-Case "20. Previsao de custos e entidade distinta do orcamento (D3)" {
    $departamento = (Invoke-RestMethod "$base/hr/departments" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ name = "Operacoes $curto" } | ConvertTo-Json)).departmentId
    $script:departamentoId = $departamento

    Invoke-RestMethod "$base/finance/planning/cost-forecasts" -Method Post -ContentType "application/json" -Headers $managerHeaders `
        -Body (@{ departmentId = $departamento; fiscalYear = $ano; month = 8; currency = "AOA"
        operationalCosts = 300000; fixedCosts = 200000; submit = $true } | ConvertTo-Json) | Out-Null

    # Duas tabelas distintas: uma do departamento, outra do centro de custo.
    $previsao = Invoke-Sql "select operational_costs from finance.cost_forecast where department_id='$departamento' and fiscal_year=$ano and month=8"
    if ([decimal]$previsao -ne 300000) { throw "previsao $previsao" }

    $temCentro = Invoke-Sql "select count(*) from information_schema.columns where table_schema='finance' and table_name='cost_forecast' and column_name='cost_centre_id'"
    if ($temCentro -ne "0") { throw "a previsao tem centro de custo — foi fundida com o orcamento" }

    $temDep = Invoke-Sql "select count(*) from information_schema.columns where table_schema='finance' and table_name='budget' and column_name='department_id'"
    if ($temDep -ne "0") { throw "o orcamento tem departamento — foi fundido com a previsao" }

    "previsao e do departamento, orcamento e do centro de custo; nunca se fundem"
}

Test-Case "21. Uma previsao por departamento e mes" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/planning/cost-forecasts" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ departmentId = $script:departamentoId; fiscalYear = $ano; month = 8; currency = "AOA"
            operationalCosts = 1; fixedCosts = 1 } | ConvertTo-Json) }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "duas seriam dois numeros a dizer coisas diferentes sobre o mesmo carregamento de caixa"
}

# ---------- BR-8 ----------

Test-Case "22. BR-8: politica com verificacao orcamental deixou de recusar sempre" {
    # Ate hoje uma politica com `RequiresBudgetCheck` recusava a submissao
    # porque nao havia orcamento nenhum contra que verificar.
    $cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ name = "Aprovador BR8 $curto"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

    $aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ fullName = "Aprovador BR8 $curto" } | ConvertTo-Json)).employeeId

    Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

    $script:cargoBR8 = $cargo
    $script:aprovadorBR8 = $aprovador

    $has = Invoke-Sql "select count(*) from finance.budget where cost_centre_id='$($script:centroId)' and status='Approved' and fiscal_year=$anoOrcamento"
    if ($has -ne "1") { throw "sem orcamento aprovado para verificar" }

    "cenario montado: cargo, aprovador e orcamento de 500000/mes"
}

Test-Case "23. BR-8: pedido que cabe no tecto passa" {
    # O centro de custo passa a apontar ao departamento, para que a politica o
    # apanhe pela faixa de departamento.
    $compra = (Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ supplierInvoiceNumber = "FT BR8-$curto"; supplierName = "Fornecedor $curto"
            supplierTaxId = "5401$curto"; currency = "AOA"; netTotal = 100000; taxTotal = 0 } | ConvertTo-Json)).purchaseInvoiceId
    $script:compraBR8 = $compra

    $r = Invoke-RestMethod "$base/finance/payment-requests" -Method Post -ContentType "application/json" -Headers $managerHeaders `
        -Body (@{ purchaseInvoiceId = $compra; amount = 100000; requestedByEmployeeId = $script:aprovadorBR8
        costCentreId = $script:centroId } | ConvertTo-Json)

    if (-not $r.paymentRequestId) { throw "pedido nao criado" }

    # A imputacao ficou gravada — e o que faz o pedido consumir orcamento.
    $cc = Invoke-Sql "select count(*) from finance.payment_request where id='$($r.paymentRequestId)' and cost_centre_id='$($script:centroId)'"
    if ($cc -ne "1") { throw "o pedido nao ficou imputado ao centro de custo" }

    "100000 de 500000; a imputacao fica gravada"
}

Test-Case "24. BR-8: pedido que excede o tecto e recusado antes da decisao" {
    # Politica com verificacao orcamental, so para este departamento.
    $politica = Get-StatusCode { Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ processType = "finance.payment_request"; departmentId = $script:departamentoId
            requiresBudgetCheck = $true; steps = @(@{ approverPositionId = $script:cargoBR8 }) } | ConvertTo-Json -Depth 5) }
    if ($politica -ne 200 -and $politica -ne 201) { throw "politica nao criada: $politica" }

    # O centro de custo passa a mapear o departamento, para que BR-8 encontre o
    # orcamento a partir do que `approval` conhece.
    Invoke-Sql "update finance.cost_centre set department_id='$($script:departamentoId)' where id='$($script:centroId)'" | Out-Null

    $compra = (Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ supplierInvoiceNumber = "FT EXC-$curto"; supplierName = "Fornecedor $curto"
            supplierTaxId = "5401$curto"; currency = "AOA"; netTotal = 900000; taxTotal = 0 } | ConvertTo-Json)).purchaseInvoiceId

    try {
        Invoke-RestMethod "$base/finance/payment-requests" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ purchaseInvoiceId = $compra; amount = 900000; requestedByEmployeeId = $script:aprovadorBR8
            costCentreId = $script:centroId } | ConvertTo-Json) | Out-Null
        throw "esperada recusa, o pedido passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        $sc = [int]$_.Exception.Response.StatusCode

        # **409 e nao 501**: a capacidade existe e funcionou. E o estado do
        # orcamento que impede, nao a ausencia de verificacao.
        if ($sc -ne 409) { throw "esperado 409, obtido $sc" }

        # E a razao diz o tecto, o comprometido e o pedido.
        if ($_.ErrorDetails.Message -notmatch "tecto|500000") { throw "razao errada: $($_.ErrorDetails.Message)" }
    }

    # **E o que interessa: nada foi criado.** A verificacao corre antes da
    # decisao, e um processo que nao passa nao chega a existir.
    $n = Invoke-Sql "select count(*) from finance.payment_request where purchase_invoice_id='$compra'"
    if ($n -ne "0") { throw "$n pedido(s) criados apesar da recusa" }

    "900000 nao cabe em 500000; nem pedido nem processo foram criados"
}

Test-Case "25. BR-8: o comprometido conta contra o tecto" {
    # Ja ha 100000 comprometidos no mes. Um pedido de 450000 cabia no tecto
    # sozinho, mas nao no que resta.
    $compra = (Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ supplierInvoiceNumber = "FT COM-$curto"; supplierName = "Fornecedor $curto"
            supplierTaxId = "5401$curto"; currency = "AOA"; netTotal = 450000; taxTotal = 0 } | ConvertTo-Json)).purchaseInvoiceId

    $comprometido = Invoke-Sql @"
select isnull(sum(amount), 0) from finance.payment_request
where cost_centre_id = '$($script:centroId)' and status <> 'Cancelled'
"@

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ purchaseInvoiceId = $compra; amount = 450000; requestedByEmployeeId = $script:aprovadorBR8
            costCentreId = $script:centroId } | ConvertTo-Json) }

    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "ja comprometidos $comprometido; 450000 nao cabe no que resta"
}

Test-Case "26. Autorizacao: 401 sem token, 403 no perfil errado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    # Manager nao lanca na contabilidade.
    $c1 = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/accounts" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ code = "M$curto"; name = "Tentativa"; category = "GR" } | ConvertTo-Json) }
    if ($c1 -ne 403) { throw "Manager lancou no plano: esperado 403, obtido $c1" }

    # Finance nao escreve orcamentos.
    $c2 = Get-StatusCode { Invoke-RestMethod "$base/finance/planning/budgets" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ costCentreId = $script:centroId; fiscalYear = 2099; currency = "AOA"; monthlyCeilings = @{ "1" = 1 } } | ConvertTo-Json -Depth 5) }
    if ($c2 -ne 403) { throw "Finance escreveu orcamento: esperado 403, obtido $c2" }

    "403 nas duas direccoes da segregacao"
}

Test-Case "27. Um orcamento por centro de custo e ano" {
    $meses = @{ "1" = 1 }
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/planning/budgets" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ costCentreId = $script:centroId; fiscalYear = $anoOrcamento; currency = "AOA"; monthlyCeilings = $meses } | ConvertTo-Json -Depth 5) }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $dup = Invoke-Sql "select count(*) from (select cost_centre_id, fiscal_year from finance.budget group by cost_centre_id, fiscal_year having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup pares repetidos na base de dados" }

    "dois tectos para o mesmo ano tornariam BR-8 ambigua"
}

Test-Case "28. Lancamentos e periodos sao auditados" {
    $lancado = Invoke-Sql "select count(*) from audit.audit_event where action='finance.journal_entry.posted' and entity_id='$($script:lancamentoId)'"
    if ($lancado -ne "1") { throw "lancamento nao auditado" }

    $fechado = Invoke-Sql "select count(*) from audit.audit_event where action='finance.accounting_period.closed'"
    if ([int]$fechado -lt 1) { throw "fecho nao auditado" }

    $semActor = Invoke-Sql "select count(*) from audit.audit_event where action like 'finance.journal_entry%' and actor_id is null"
    if ($semActor -ne "0") { throw "$semActor registos sem actor" }

    "lancar, fechar e reabrir na trilha, todos com actor"
}

# ---------- Postagem automatica ----------

Test-Case "29. Sem regra de postagem, o ciclo de venda nao muda" {
    # **E o estado por omissao, e tem de ser inofensivo.** O ciclo de venda
    # funcionou meses sem contabilidade nenhuma.
    $regras = Invoke-Sql "select count(*) from finance.posting_rule where is_active=1 and event='SalesInvoiceIssued'"
    if ($regras -ne "0") { throw "ja existe regra activa de venda: $regras" }

    $antes = Invoke-Sql "select count(*) from finance.journal_entry"

    $cliente = (Invoke-RestMethod "$base/commercial/customers" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ name = "Cliente Postagem $curto"; taxId = "5417$curto"
            addressDetail = "Rua 1"; city = "Luanda"; country = "AO" } | ConvertTo-Json)).customerId
    $script:clientePost = $cliente

    try {
        $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ customerId = $cliente; issuedOn = "$anoOrcamento-08-10"
            lines = @(@{ description = "Servico"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa }) } | ConvertTo-Json -Depth 5)
    }
    catch { throw "emissao falhou: $($_.ErrorDetails.Message)" }

    if (-not $r.invoiceId) { throw "factura nao emitida" }

    $depois = Invoke-Sql "select count(*) from finance.journal_entry"
    if ($antes -ne $depois) { throw "lancou sem regra configurada" }

    "factura emitida, zero lancamentos — postar e opt-in"
}

Test-Case "30. Regra que nao equilibra e recusada na configuracao" {
    # Debitar o total e creditar so o liquido esquece o imposto: equilibraria
    # numa factura isenta e falharia em todas as outras.
    $body = @{
        event = "SalesInvoiceIssued"; journalCode = $diario; description = "Torta"
        lines = @(
            @{ accountCode = $cliente2; side = "Debit"; amount = "Gross"; description = "Divida" },
            @{ accountCode = $proveito; side = "Credit"; amount = "Net"; description = "Proveito" }
        )
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
        throw "esperado 400, a regra passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 400) { throw "esperado 400, obtido $([int]$_.Exception.Response.StatusCode)" }
        if ($_.ErrorDetails.Message -notmatch "equilibrio") { throw "razao errada: $($_.ErrorDetails.Message)" }
    }

    "recusada antes de haver documento nenhum — nao para os numeros de um caso"
}

Test-Case "31. Regra que lanca em conta agregadora e recusada" {
    $body = @{
        event = "SalesInvoiceIssued"; journalCode = $diario; description = "Na agregadora"
        lines = @(
            @{ accountCode = $agregada; side = "Debit"; amount = "Gross"; description = "Divida" },
            @{ accountCode = $proveito; side = "Credit"; amount = "Gross"; description = "Proveito" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "so se descobriria a primeira emissao se nao fosse verificado aqui"
}

Test-Case "32. Definir a regra de venda" {
    $body = @{
        event = "SalesInvoiceIssued"; journalCode = $diario; description = "Facturacao"
        lines = @(
            @{ accountCode = $cliente2; side = "Debit"; amount = "Gross"; description = "Divida do cliente" },
            @{ accountCode = $proveito; side = "Credit"; amount = "Net"; description = "Proveito" },
            @{ accountCode = $ivaLiq; side = "Credit"; amount = "Tax"; description = "IVA liquidado" }
        )
    } | ConvertTo-Json -Depth 5

    $r = Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.ruleId) { throw "sem ruleId" }
    $script:regraId = $r.ruleId

    "total a debito, liquido e imposto a credito"
}

Test-Case "33. Uma regra activa por acontecimento" {
    $body = @{
        event = "SalesInvoiceIssued"; journalCode = $diario; description = "Segunda"
        lines = @(
            @{ accountCode = $cliente2; side = "Debit"; amount = "Gross"; description = "A" },
            @{ accountCode = $proveito; side = "Credit"; amount = "Gross"; description = "B" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $dup = Invoke-Sql "select count(*) from (select event from finance.posting_rule where is_active=1 group by event having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup acontecimentos com duas regras activas" }

    "duas tornariam a traducao documento -> contas ambigua"
}

Test-Case "34. Emitir passa a lancar, na mesma transaccao" {
    $antes = Invoke-Sql "select count(*) from finance.journal_entry"

    $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ customerId = $script:clientePost; issuedOn = "$anoOrcamento-08-11"
        lines = @(@{ description = "Servico"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa }) } | ConvertTo-Json -Depth 5)

    $numero = $r.number
    $script:facturaPostada = $numero

    $depois = Invoke-Sql "select count(*) from finance.journal_entry"
    if ([int]$depois -ne [int]$antes + 1) { throw "esperado 1 lancamento novo, obtidos $([int]$depois - [int]$antes)" }

    # O numero de arquivo deriva do numero do documento — e o que liga o
    # lancamento ao papel.
    $arquivo = $numero.Replace(" ", "-").Replace("/", "-")
    $existe = Invoke-Sql "select count(*) from finance.journal_entry where archival_number='$arquivo'"
    if ($existe -ne "1") { throw "lancamento nao encontrado por '$arquivo'" }

    "$numero -> lancamento $arquivo"
}

Test-Case "35. O lancamento reparte liquido, imposto e total" {
    $arquivo = $script:facturaPostada.Replace(" ", "-").Replace("/", "-")

    $divida = Invoke-Sql @"
select l.amount from finance.journal_entry_line l
join finance.journal_entry e on e.id = l.journal_entry_id
where e.archival_number = '$arquivo' and l.account_code = '$cliente2'
"@
    $proveitoValor = Invoke-Sql @"
select l.amount from finance.journal_entry_line l
join finance.journal_entry e on e.id = l.journal_entry_id
where e.archival_number = '$arquivo' and l.account_code = '$proveito'
"@
    $ivaValor = Invoke-Sql @"
select l.amount from finance.journal_entry_line l
join finance.journal_entry e on e.id = l.journal_entry_id
where e.archival_number = '$arquivo' and l.account_code = '$ivaLiq'
"@

    # 14% e a taxa desta corrida; aqui o que interessa e que
    # total = liquido + imposto, seja qual for.
    if ([decimal]$divida -ne [decimal]$proveitoValor + [decimal]$ivaValor) {
        throw "total $divida != liquido $proveitoValor + imposto $ivaValor"
    }
    if ([decimal]$proveitoValor -ne 100000) { throw "liquido $proveitoValor" }

    "$divida = $proveitoValor + $ivaValor"
}

Test-Case "36. O balancete do documento equilibra" {
    $b = Invoke-RestMethod "$base/finance/ledger/trial-balance?fiscalYear=$anoOrcamento" -Headers $financeHeaders

    if (-not $b.isBalanced) { throw "balancete nao equilibra" }

    $linhaCliente = $b.lines | Where-Object { $_.accountCode -eq $cliente2 }
    if (-not $linhaCliente) { throw "conta de cliente sem movimento" }

    "debito total = credito total, com o documento la dentro"
}

Test-Case "37. Postar o mesmo documento duas vezes colide na chave do SAF-T" {
    # Idempotencia por construcao: o numero de arquivo deriva do documento, e a
    # chave (data, diario, arquivo) e unica.
    $arquivo = $script:facturaPostada.Replace(" ", "-").Replace("/", "-")
    $n = Invoke-Sql "select count(*) from finance.journal_entry where archival_number='$arquivo'"
    if ($n -ne "1") { throw "$n lancamentos com a mesma chave" }

    $dup = Invoke-Sql @"
select count(*) from (
  select transaction_date, journal_code, archival_number
  from finance.journal_entry group by transaction_date, journal_code, archival_number
  having count(*) > 1) d
"@
    if ($dup -ne "0") { throw "$dup chaves repetidas" }
    "a chave unica e a garantia, nao uma verificacao que alguem pode esquecer"
}

Test-Case "38. A factura de compra lanca pelo seu proprio acontecimento" {
    # A factura de venda ja provou o caminho. Este caso prova que o **ponto de
    # ligacao e outro**: um `PostingEvent` trocado por engano faria a regra
    # nunca disparar, e ninguem daria por isso.
    #
    # Auto-contido: define a regra, usa-a, e desactiva-a. Deixa-la activa faria
    # `verify-payables` — que tambem regista facturas de compra — passar a
    # lancar contra o plano desta corrida.
    $body = @{
        event = "PurchaseInvoiceRegistered"; journalCode = $diario; description = "Compras"
        lines = @(
            @{ accountCode = $custo; side = "Debit"; amount = "Net"; description = "Custo" },
            @{ accountCode = $ivaLiq; side = "Debit"; amount = "Tax"; description = "IVA dedutivel" },
            @{ accountCode = $fornecedor; side = "Credit"; amount = "Gross"; description = "Divida" }
        )
    } | ConvertTo-Json -Depth 5

    $regra = (Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).ruleId

    try {
        $numero = "FC-$curto"

        Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -ContentType "application/json" -Headers $managerHeaders `
            -Body (@{ supplierInvoiceNumber = $numero; supplierName = "Fornecedor $curto"
            supplierTaxId = "5402$curto"; netTotal = 50000; taxTotal = 7000
            issuedOn = "$anoOrcamento-08-12"; dueOn = "$anoOrcamento-12-31" } | ConvertTo-Json) | Out-Null

        # **A chave de arquivo nao e o numero do fornecedor** — esse nao e
        # unico. Procura-se pelo `SourceDocumentID` das linhas, que e onde o
        # numero fica.
        $lancado = Invoke-Sql "select count(distinct journal_entry_id) from finance.journal_entry_line where source_document_id='$numero'"
        if ($lancado -ne "1") { throw "a factura de compra nao lancou" }

        $chave = Invoke-Sql "select top 1 e.archival_number from finance.journal_entry e join finance.journal_entry_line l on l.journal_entry_id = e.id where l.source_document_id = '$numero'"
        if ($chave -eq $numero) { throw "a chave de arquivo e o numero do fornecedor, e esse nao e unico" }

        # O espelho da venda: custo e imposto a debito, divida a credito.
        $debito = Invoke-Sql "select sum(amount) from finance.journal_entry_line where source_document_id='$numero' and side='Debit'"
        $credito = Invoke-Sql "select sum(amount) from finance.journal_entry_line where source_document_id='$numero' and side='Credit'"
        if ([decimal]$debito -ne 57000) { throw "debito $debito, esperado 57000" }
        if ([decimal]$debito -ne [decimal]$credito) { throw "debito $debito != credito $credito" }
    }
    finally {
        Invoke-RestMethod "$base/finance/ledger/posting-rules/$regra/deactivation" -Method Post -ContentType "application/json" -Headers $adminHeaders | Out-Null
    }

    "50000 + 7000 a debito, 57000 a credito; regra desactivada no fim"
}

Test-Case "39. Periodo fechado trava a emissao, e nada e gravado" {
    # Um documento com data dentro de um periodo fechado nao devia existir.
    # Re-executavel: numa segunda corrida o periodo ja existe e ja esta
    # fechado, e as duas chamadas devolvem 409. E o estado que interessa.
    Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/periods" -Method Post -ContentType "application/json" -Headers $financeHeaders `
            -Body (@{ fiscalYear = $anoOrcamento; number = 7 } | ConvertTo-Json) } | Out-Null

    Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/periods/$anoOrcamento/7/closure" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ closedByEmployeeId = $responsavel } | ConvertTo-Json) } | Out-Null

    $estado = Invoke-Sql "select status from finance.accounting_period where fiscal_year=$anoOrcamento and number=7"
    if ($estado -ne "Closed") { throw "o periodo 7 nao ficou fechado: $estado" }

    $facturasAntes = Invoke-Sql "select count(*) from finance.sales_invoice"
    $lancamentosAntes = Invoke-Sql "select count(*) from finance.journal_entry"

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ customerId = $script:clientePost; issuedOn = "$anoOrcamento-07-15"
            lines = @(@{ description = "Tardia"; quantity = 1; unitPrice = 1000; taxCode = $codigoTaxa }) } | ConvertTo-Json -Depth 5) }

    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    # **O que interessa: nem factura nem lancamento.** A postagem falha antes do
    # SaveChanges, e a transaccao leva os dois.
    $facturasDepois = Invoke-Sql "select count(*) from finance.sales_invoice"
    $lancamentosDepois = Invoke-Sql "select count(*) from finance.journal_entry"

    if ($facturasAntes -ne $facturasDepois) { throw "a factura foi gravada apesar da recusa" }
    if ($lancamentosAntes -ne $lancamentosDepois) { throw "o lancamento foi gravado apesar da recusa" }

    "409, e nem documento nem lancamento — a transaccao leva os dois"
}

Test-Case "40. Um periodo que ninguem abriu aceita lancamentos" {
    # Exigir a linha faria a facturacao parar no dia 1 de cada mes por
    # arrumacao contabilistica por fazer.
    $existia = Invoke-Sql "select count(*) from finance.accounting_period where fiscal_year=$anoOrcamento and number=9"

    $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ customerId = $script:clientePost; issuedOn = "$anoOrcamento-09-05"
        lines = @(@{ description = "Setembro"; quantity = 1; unitPrice = 5000; taxCode = $codigoTaxa }) } | ConvertTo-Json -Depth 5)

    if (-not $r.invoiceId) { throw "factura nao emitida" }

    $agora = Invoke-Sql "select status from finance.accounting_period where fiscal_year=$anoOrcamento and number=9"
    if ($agora -ne "Open") { throw "periodo nao ficou aberto: $agora" }

    "a linha regista um *fecho*, nao da licenca — e ninguem fechou nada"
}

Test-Case "41. Desactivar a regra pára a postagem e mantem os lancamentos" {
    $lancamentos = Invoke-Sql "select count(*) from finance.journal_entry"

    Invoke-RestMethod "$base/finance/ledger/posting-rules/$($script:regraId)/deactivation" -Method Post -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ customerId = $script:clientePost; issuedOn = "$anoOrcamento-08-20"
        lines = @(@{ description = "Sem regra"; quantity = 1; unitPrice = 2000; taxCode = $codigoTaxa }) } | ConvertTo-Json -Depth 5)

    if (-not $r.invoiceId) { throw "factura nao emitida" }

    $depois = Invoke-Sql "select count(*) from finance.journal_entry"
    if ($lancamentos -ne $depois) { throw "lancou com a regra desactivada" }

    "os lancamentos que ela produziu ficam — sao factos, nao configuracao"
}

Test-Case "42. Definir regras e mais restrito que lancar" {
    # Definir uma regra decide como **todos** os documentos futuros lancam.
    $body = @{
        event = "PaymentExecuted"; journalCode = $diario; description = "Tentativa"
        lines = @(
            @{ accountCode = $cliente2; side = "Debit"; amount = "Gross"; description = "A" },
            @{ accountCode = $proveito; side = "Credit"; amount = "Gross"; description = "B" }
        )
    } | ConvertTo-Json -Depth 5

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/ledger/posting-rules" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 403) { throw "Finance definiu regra: esperado 403, obtido $code" }
    "Finance lanca um a um; so quem fecha periodos decide como todos lancam"
}

Test-Case "43. Regras de postagem sao auditadas" {
    $definida = Invoke-Sql "select count(*) from audit.audit_event where action='finance.posting_rule.defined' and entity_id='$($script:regraId)'"
    if ($definida -ne "1") { throw "definicao nao auditada" }

    $desactivada = Invoke-Sql "select count(*) from audit.audit_event where action='finance.posting_rule.deactivated' and entity_id='$($script:regraId)'"
    if ($desactivada -ne "1") { throw "desactivacao nao auditada" }

    "definir e desactivar na trilha — decidem como tudo lanca"
}

Test-Case "44. A suite nao deixa politica de BR-8 activa atras de si" {
    # **Independencia entre suites.** A politica desta corrida e especifica de um
    # departamento novo, e cada corrida criaria mais uma: a tabela cresceria sem
    # limite e a guarda de `verify-payables` — que procura a politica generica —
    # passaria a ter de a distinguir de dezenas.
    #
    # Nao ha rota para desactivar politicas, e por isso e SQL. Enquanto nao
    # houver, e a unica forma de a suite se limpar.
    Invoke-Sql @"
update approval.policy set is_active = 0
where process_type = 'finance.payment_request' and department_id = '$($script:departamentoId)'
"@ | Out-Null

    $activas = Invoke-Sql "select count(*) from approval.policy where process_type='finance.payment_request' and is_active=1 and department_id='$($script:departamentoId)'"
    if ($activas -ne "0") { throw "$activas politicas ficaram activas" }

    $generica = Invoke-Sql "select count(*) from approval.policy where process_type='finance.payment_request' and is_active=1 and department_id is null and requires_budget_check=0"
    if ($generica -ne "1") { throw "a politica generica de verify-payables desapareceu: $generica" }

    "a generica fica; a desta corrida sai"
}

Test-Case "45. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $b = Invoke-RestMethod "$base/finance/ledger/trial-balance?fiscalYear=$ano" -Headers $financeHeaders
    if (-not $b.isBalanced) { throw "balancete deixou de equilibrar" }
    if ([decimal]$b.totalDebit -ne 100000) { throw "balancete perdido: $($b.totalDebit)" }

    $estado = Invoke-Sql "select status from finance.budget where id='$($script:orcamentoId)'"
    if ($estado -ne "Approved") { throw "orcamento perdido: $estado" }

    "balancete e orcamento intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
