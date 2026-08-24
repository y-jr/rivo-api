# Verificação do módulo `finance` — Contas a Receber.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-finance.ps1
#
# Âmbito reduzido pelo ADR-036: só a factura de venda. Contas a Pagar,
# Tesouraria, Contabilidade e Planeamento não existem — e com eles BR-1, BR-3,
# BR-5 e o disponível orçamental de BR-8, que esta suite não procura.
#
# É a suite que exercita os três módulos juntos: `commercial` dá o cliente,
# `fiscal` dá a taxa à data do facto gerador, `finance` possui o documento.
#
# Re-executável: cada corrida abre a sua própria série de numeração, o seu
# código de taxa e o seu cliente.
#
# ⚠ As facturas emitidas **não são documentos fiscais válidos em Angola** —
# falta a certificação da AGT e a cadeia Hash/HashControl (ADR-036).

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

function New-PerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo@rivo.ao"
    $body = @{ email = $email; password = $script:pass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $body = @{ profile = $Perfil } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body $body -ContentType "application/json" -Headers $script:adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $script:pass) }
}

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

$salesHeaders = New-PerfilHeaders "Sales" "vendas-f-$stamp"
$financeHeaders = New-PerfilHeaders "Finance" "financas-f-$stamp"

$serie = "S$stamp"
$codigoTaxa = "F" + ("$stamp".Substring("$stamp".Length - 6))
$nif = "55$stamp"

# --- Pre-requisitos, montados pelas rotas publicas dos outros dois modulos.
# Nao ha atalho por SQL de proposito: se a montagem falhar, e porque o caminho
# real de emissao esta partido, e e isso que interessa saber.

$body = @{ code = $codigoTaxa; description = "IVA - suite finance" } | ConvertTo-Json
$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).scheduleId

$body = @{ percentage = 5; effectiveFrom = "2026-01-01"; effectiveTo = "2026-06-30"; legalInstrument = "Lei 14/23" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

$body = @{ percentage = 7; effectiveFrom = "2026-07-01"; legalInstrument = "Lei 20/26" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

$body = @{
    name = "Kianda Lda"; taxId = $nif
    addressDetail = "Rua Rainha Ginga 12"; city = "Luanda"; country = "AO"
} | ConvertTo-Json
$customerId = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).customerId

Write-Host "`n=== Modulo finance (AR) ===`n"

Test-Case "1. Schema finance com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from finance.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de finance" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='finance'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='finance' and table_name in ('app_user','audit_event','customer','tax_rate_schedule')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema finance" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. Quem emite nao anula, e quem anula nao emite (BR-3)" {
    $emite = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value='finance.invoices.write'"
    $anula = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value='finance.invoices.cancel'"
    if ($emite -ne "1") { throw "Sales nao emite" }
    if ($anula -ne "0") { throw "Sales pode anular" }

    $fEmite = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.invoices.write'"
    $fAnula = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.invoices.cancel'"
    if ($fEmite -ne "0") { throw "Finance pode emitir" }
    if ($fAnula -ne "1") { throw "Finance nao anula" }

    "Sales emite sem anular; Finance anula sem emitir"
}

Test-Case "3. Abrir series e so de Admin; duplicada e recusada" {
    $body = @{ code = $serie } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/series" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 403) { throw "Sales abriu serie: esperado 403, obtido $code" }

    Invoke-RestMethod "$base/finance/series" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/series" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "serie duplicada: esperado 409, obtido $code" }

    "uma serie paralela emitiria fora da sequencia auditavel"
}

Test-Case "4. Emitir factura, numerada FT serie/1" {
    $body = @{
        customerId = $customerId; series = $serie
        issuedOn = "2026-08-24"; taxPointDate = "2026-03-15"
        lines = @(
            @{ description = "Consultoria"; quantity = 2; unitPrice = 50000; taxCode = $codigoTaxa },
            @{ description = "Deslocacao"; quantity = 1; unitPrice = 10000; taxCode = $codigoTaxa }
        )
    } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders
    if ($r.number -ne "FT $serie/1") { throw "numero errado: $($r.number)" }
    $script:invoiceId = $r.invoiceId
    "emitida $($r.number) por um utilizador Sales"
}

Test-Case "5. Taxa aplicada e a da data do facto gerador (ADR-011 par.3)" {
    $f = Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Headers $adminHeaders

    # Facto gerador em Marco/2026: a versao de 5%, nao a de 7% que vigora hoje.
    foreach ($l in $f.lines) {
        if ($l.taxPercentage -ne 5) { throw "linha $($l.lineNumber) com taxa $($l.taxPercentage), esperada 5" }
    }
    if ($f.netTotal -ne 110000) { throw "netTotal $($f.netTotal), esperado 110000" }
    if ($f.taxTotal -ne 5500) { throw "taxTotal $($f.taxTotal), esperado 5500" }
    if ($f.grossTotal -ne 115500) { throw "grossTotal $($f.grossTotal), esperado 115500" }

    "5% de Marco aplicada; totais 110000 + 5500 = 115500"
}

Test-Case "6. Cliente fica congelado na factura" {
    $f = Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Headers $adminHeaders
    if ($f.customerTaxId -ne $nif) { throw "NIF nao congelado: $($f.customerTaxId)" }
    if ($f.customerName -ne "Kianda Lda") { throw "nome nao congelado: $($f.customerName)" }
    if ($f.customerId -ne $customerId) { throw "identificador do cliente perdido" }

    # Renomear o cliente nao pode reescrever a factura ja emitida.
    $body = @{ name = "Kianda, S.A." } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$customerId/details" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $depois = Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Headers $adminHeaders
    if ($depois.customerName -ne "Kianda Lda") { throw "renomear o cliente alterou a factura: $($depois.customerName)" }

    "cliente renomeado; a factura mantem o nome da emissao"
}

Test-Case "7. Numeracao avanca; facto gerador de Setembro leva 7%" {
    $body = @{
        customerId = $customerId; series = $serie
        issuedOn = "2026-09-30"; taxPointDate = "2026-09-01"
        lines = @(@{ description = "Servico"; quantity = 1; unitPrice = 1000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders
    if ($r.number -ne "FT $serie/2") { throw "numero errado: $($r.number)" }

    $f = Invoke-RestMethod "$base/finance/sales-invoices/$($r.invoiceId)" -Headers $adminHeaders
    if ($f.lines[0].taxPercentage -ne 7) { throw "esperada taxa 7, obtida $($f.lines[0].taxPercentage)" }
    if ($f.grossTotal -ne 1070) { throw "grossTotal $($f.grossTotal), esperado 1070" }

    "FT $serie/2 com 7%; a mesma serie, taxa diferente"
}

Test-Case "8. Emissao recusada nao queima numero" {
    # Pela base de dados e nao pela rota, de proposito. `Invoke-RestMethod`
    # entrega um array JSON ao pipeline como **um so item**, e nesse caso
    # `$_.code -eq $serie` compara uma lista com um escalar: devolve o
    # subconjunto correspondente, que sendo nao-vazio e verdadeiro. O
    # `Where-Object` deixaria passar todas as series.
    $antes = [int](Invoke-Sql "select next_sequence from finance.document_series where code='$serie'")

    $body = @{
        customerId = $customerId; series = $serie
        lines = @(@{ description = "X"; quantity = 1; unitPrice = 10; taxCode = "NAOEXISTE" })
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 400) { throw "taxa inexistente: esperado 400, obtido $code" }

    $depois = [int](Invoke-Sql "select next_sequence from finance.document_series where code='$serie'")
    if ($antes -ne $depois) { throw "sequencia avancou de $antes para $depois numa emissao recusada" }

    "sequencia em $depois antes e depois da recusa"
}

Test-Case "9. Cliente desactivado nao se factura" {
    $body = @{ active = $false } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$customerId/status" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $body = @{
        customerId = $customerId; series = $serie
        lines = @(@{ description = "X"; quantity = 1; unitPrice = 10; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }

    $body = @{ active = $true } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$customerId/status" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "foi desactivado justamente para deixar de aparecer aqui"
}

Test-Case "10. Emitir com isencao devolve 501 (ADR-036)" {
    $body = @{
        customerId = $customerId; series = $serie
        lines = @(@{ description = "Servico isento"; quantity = 1; unitPrice = 10; taxCode = "ISE" })
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 501) { throw "esperado 501, obtido $code" }
    "sem catalogo de codigos de isencao nao se inventa codigo"
}

Test-Case "11. Serie ou cliente inexistentes devolvem 404" {
    $b1 = @{
        customerId = $customerId; series = "NAOEXISTE"
        lines = @(@{ description = "X"; quantity = 1; unitPrice = 10; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $c1 = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $b1 -ContentType "application/json" -Headers $salesHeaders }
    if ($c1 -ne 404) { throw "serie inexistente: esperado 404, obtido $c1" }

    $b2 = @{
        customerId = "11111111-1111-1111-1111-111111111111"; series = $serie
        lines = @(@{ description = "X"; quantity = 1; unitPrice = 10; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $c2 = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $b2 -ContentType "application/json" -Headers $salesHeaders }
    if ($c2 -ne 404) { throw "cliente inexistente: esperado 404, obtido $c2" }

    "404 para o que nao existe, 400 para o que esta mal"
}

Test-Case "12. Anular mantem linhas e totais (BR-14)" {
    $body = @{ reason = "Emitida ao cliente errado" } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders | Out-Null

    $f = Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Headers $adminHeaders
    if ($f.status -ne "Cancelled") { throw "estado $($f.status), esperado Cancelled" }
    if ($f.cancellationReason -ne "Emitida ao cliente errado") { throw "motivo nao guardado" }
    if ($f.lines.Count -ne 2) { throw "linhas perdidas: $($f.lines.Count)" }
    if ($f.grossTotal -ne 115500) { throw "totais alterados: $($f.grossTotal)" }

    "anulada por um utilizador Finance; linhas e totais intactos"
}

Test-Case "13. Anular duas vezes e recusado com 409" {
    $body = @{ reason = "Outra vez" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "o segundo motivo apagaria o primeiro sem rasto"
}

Test-Case "14. Nao ha eliminacao de factura (BR-14)" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    $existe = Invoke-Sql "select count(*) from finance.sales_invoice where id='$($script:invoiceId)'"
    if ($existe -ne "1") { throw "factura desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "15. Numeracao e unica na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select number_type, number_series, number_sequence from finance.sales_invoice group by number_type, number_series, number_sequence having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup numeros repetidos" }
    "indice unico; o contador de concorrencia da serie e a primeira linha"
}

Test-Case "16. Emissao e anulacao sao auditadas" {
    $emitidas = Invoke-Sql "select count(*) from audit.audit_event where action='finance.sales_invoice.issued' and entity_id='$($script:invoiceId)'"
    if ($emitidas -ne "1") { throw "esperado 1 registo de emissao, obtido $emitidas" }

    $anuladas = Invoke-Sql "select count(*) from audit.audit_event where action='finance.sales_invoice.cancelled' and entity_id='$($script:invoiceId)'"
    if ($anuladas -ne "1") { throw "esperado 1 registo de anulacao, obtido $anuladas" }

    $motivo = Invoke-Sql "select count(*) from audit.audit_event where action='finance.sales_invoice.cancelled' and entity_id='$($script:invoiceId)' and new_value like '%Emitida ao cliente errado%'"
    if ($motivo -ne "1") { throw "o motivo da anulacao nao ficou na trilha" }

    "emissao e anulacao na trilha, com o motivo"
}

Test-Case "17. Autorizacao: sem token 401, sem a permissao 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    # Finance le mas nao emite.
    $body = @{
        customerId = $customerId; series = $serie
        lines = @(@{ description = "X"; quantity = 1; unitPrice = 10; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 403) { throw "Finance emitiu: esperado 403, obtido $code" }

    # Sales emite mas nao anula.
    $body = @{ reason = "X" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 403) { throw "Sales anulou: esperado 403, obtido $code" }

    "401 sem token; 403 nas duas direccoes da segregacao"
}

Test-Case "18. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $f = Invoke-RestMethod "$base/finance/sales-invoices/$($script:invoiceId)" -Headers $adminHeaders
    if ($f.number -ne "FT $serie/1") { throw "factura perdida" }
    if ($f.status -ne "Cancelled") { throw "estado perdido: $($f.status)" }
    if ($f.grossTotal -ne 115500) { throw "totais perdidos: $($f.grossTotal)" }
    "FT $serie/1 intacta e anulada apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
