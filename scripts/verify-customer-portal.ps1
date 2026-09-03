# Verificação da camada de composição `Rivo.CustomerPortal` (Portal do
# Cliente, identidade externa — ADR-043).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-customer-portal.ps1

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

$dotenv = Get-RivoCredentials
$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$de = "2020-01-01"
$ate = "2030-12-31"

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

$salesEmail = "cportal-vendas-$stamp@rivo.ao"
$b = @{ email = $salesEmail; password = $pass } | ConvertTo-Json
$salesUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
$b = @{ profile = "Sales" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$salesUserId/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
$salesHeaders = @{ Authorization = "Bearer " + (Get-Token $salesEmail $pass) }

# Taxa fiscal aberta, efectiva desde sempre — a factura da suite precisa dela.
$codigoTaxa = "P" + ("$stamp".Substring("$stamp".Length - 6))
$b = @{ code = $codigoTaxa; description = "IVA - suite customer-portal" } | ConvertTo-Json
$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).scheduleId
$b = @{ percentage = 14; effectiveFrom = "2000-01-01"; legalInstrument = "Suite customer-portal" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

Write-Host "`n=== Camada de composicao customer-portal ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Admin autenticado sem cliente ligado -> 403 -- o portal nao contorna a falta de vinculo" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $adminHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Admin nao tem commercial.Customer ligado, e o portal recusa em vez de adivinhar"
}

$script:customerId = $null
$script:ownEmail = "cliente-$stamp@rivo-teste.local"
Test-Case "3. Registar cliente, registar conta e ligar (ADR-043)" {
    $b = @{
        name = "Kianda Lda $stamp"; taxId = "57$stamp"
        addressDetail = "Rua Rainha Ginga 12"; city = "Luanda"; country = "AO"
    } | ConvertTo-Json
    $script:customerId = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId

    $b = @{ email = $script:ownEmail; password = $pass } | ConvertTo-Json
    $script:ownUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $b = @{ userId = $script:ownUserId } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "cliente $($script:customerId) e conta ligados"
}

$script:base0 = $null
Test-Case "4. Cliente novo ve o proprio perfil, sem facturas nem divida" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $script:base0 = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders

    if ($script:base0.customerId -ne $script:customerId) { throw "customerId nao e o proprio" }
    if ($script:base0.customerName -notlike "Kianda Lda*") { throw "nome errado: $($script:base0.customerName)" }
    if ($script:base0.invoices.Count -ne 0) { throw "cliente novo com facturas: $($script:base0.invoices.Count)" }
    "customerId=$($script:customerId), receita=$($script:base0.netRevenue), em-aberto=$($script:base0.outstanding), 0 facturas"
}

Test-Case "5. Facturar ao cliente faz a receita e o em-aberto subirem, e a factura aparece na lista" {
    $b = @{
        customerId = $script:customerId; issuedOn = "2026-08-15"; taxPointDate = "2026-08-15"
        lines = @(@{ description = "Servico"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $factura = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $b -ContentType "application/json" -Headers $salesHeaders

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders

    if (($vista.netRevenue - $script:base0.netRevenue) -ne 100000) { throw "receita devia subir 100000, subiu $($vista.netRevenue - $script:base0.netRevenue)" }
    if (($vista.outstanding - $script:base0.outstanding) -ne 114000) { throw "em-aberto devia subir 114000 (bruto), subiu $($vista.outstanding - $script:base0.outstanding)" }
    if ($vista.invoices.Count -ne 1) { throw "esperada 1 factura, obtidas $($vista.invoices.Count)" }
    if ($vista.invoices[0].number -ne $factura.number) { throw "numero da factura nao bate: $($vista.invoices[0].number)" }
    "receita +100000, em-aberto +114000, factura $($factura.number) na lista"
}

Test-Case "6. Janela invertida e recusada com 400" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$ate&to=$de" -Headers $ownHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "HTTP 400 -- data inicial depois da final"
}

Test-Case "7. Moeda tem omissao AOA" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders
    if ($vista.currency -ne "AOA") { throw "moeda por omissao devia ser AOA, veio '$($vista.currency)'" }
    "moeda='AOA' por omissao"
}

Test-Case "8. Outro utilizador sem cliente ligado -> 403, nunca ve o cliente de outro" {
    $e2 = "semvinculo-c-$stamp@rivo.ao"
    $b = @{ email = $e2; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
    $h2 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }

    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $h2 }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- so ve o proprio, e o proprio nao existe para esta conta"
}

Test-Case "9. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 4
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders
    if ($vista.customerId -ne $script:customerId) { throw "vinculo nao sobreviveu ao reinicio" }
    if ($vista.invoices.Count -ne 1) { throw "factura perdida apos restart" }
    "customerId=$($script:customerId) e factura intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
