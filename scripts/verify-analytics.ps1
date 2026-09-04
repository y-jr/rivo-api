# Verificação da camada de composição `Rivo.Analytics` (Analytics & IA,
# Fase 8, ADR-047).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-analytics.ps1
#
# Moeda ZZY de propósito -- distinta de ZZZ (verify-dashboard.ps1) para as
# duas suites poderem correr sem se contaminarem uma à outra.

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
$moeda = "ZZY"
$de = "2020-01-01"
$ate = "2030-12-31"

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

function New-PerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo-$stamp@rivo.ao"
    $body = @{ email = $email; password = $pass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $body = @{ profile = $Perfil } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body $body -ContentType "application/json" -Headers $script:adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $pass) }
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }
$managerHeaders = New-PerfilHeaders "Manager" "analytics-manager"
$salesHeaders = New-PerfilHeaders "Sales" "analytics-vendas"

# --- Pré-requisitos: taxa fiscal aberta e um cliente, mesmo padrão de
# verify-dashboard.ps1.
$codigoTaxa = "Y" + ("$stamp".Substring("$stamp".Length - 6))
$body = @{ code = $codigoTaxa; description = "IVA - suite analytics" } | ConvertTo-Json
$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).scheduleId
$body = @{ percentage = 14; effectiveFrom = "2000-01-01"; legalInstrument = "Suite analytics" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

$body = @{ name = "Analytics Cliente $stamp"; taxId = "58$stamp"; addressDetail = "Rua A"; city = "Luanda"; country = "AO" } | ConvertTo-Json
$cliente = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).customerId

Write-Host "`n=== Camada de composicao analytics ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Autenticado sem a permissao (Sales) -> 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" -Headers $salesHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Sales nao tem analytics.overview.read"
}

Test-Case "3. Manager consegue ver a vista, com os tres domInios presentes" {
    $vista = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    if ($null -eq $vista.monthlyRevenue) { throw "monthlyRevenue em falta" }
    if ($null -eq $vista.monthlyExpenses) { throw "monthlyExpenses em falta" }
    if ($null -eq $vista.fleetPeriodExpenses) { throw "fleetPeriodExpenses em falta" }
    if ($null -eq $vista.fleetPeriodDistanceKm) { throw "fleetPeriodDistanceKm em falta" }
    if ($null -eq $vista.fleetPeriodMaintenanceCost) { throw "fleetPeriodMaintenanceCost em falta" }
    if ($null -eq $vista.inventoryCurrentValue) { throw "inventoryCurrentValue em falta" }
    if ($null -eq $vista.inventoryPeriodValuation) { throw "inventoryPeriodValuation em falta" }
    "Finance (mensal), Frota e Inventario, todos presentes"
}

Test-Case "4. Facturar num mes cria (ou soma a) o ponto mensal desse mes, sem afectar outros meses" {
    $baseline = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    $antesAgosto = ($baseline.monthlyRevenue | Where-Object { $_.year -eq 2026 -and $_.month -eq 8 }).amount
    if (-not $antesAgosto) { $antesAgosto = 0 }
    $antesSetembro = ($baseline.monthlyRevenue | Where-Object { $_.year -eq 2026 -and $_.month -eq 9 }).amount
    if (-not $antesSetembro) { $antesSetembro = 0 }

    $body = @{
        customerId = $cliente; issuedOn = "2026-08-20"; taxPointDate = "2026-08-20"; currency = $moeda
        lines = @(@{ description = "Servico Analytics"; quantity = 1; unitPrice = 75000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders | Out-Null

    $vista = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    $depoisAgosto = ($vista.monthlyRevenue | Where-Object { $_.year -eq 2026 -and $_.month -eq 8 }).amount
    $depoisSetembro = ($vista.monthlyRevenue | Where-Object { $_.year -eq 2026 -and $_.month -eq 9 }).amount
    if (-not $depoisSetembro) { $depoisSetembro = 0 }

    if (($depoisAgosto - $antesAgosto) -ne 75000) { throw "ponto de Agosto devia subir 75000, subiu $($depoisAgosto - $antesAgosto)" }
    if ($depoisSetembro -ne $antesSetembro) { throw "Setembro nao devia mudar, mudou de $antesSetembro para $depoisSetembro" }
    "Agosto/2026 +75000; Setembro/2026 inalterado"
}

Test-Case "5. Janela invertida e recusada com 400" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/analytics/overview?from=$ate&to=$de&currency=$moeda" -Headers $managerHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "HTTP 400 -- data inicial depois da final"
}

Test-Case "6. Moeda tem omissao -- AOA" {
    $vista = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate" -Headers $managerHeaders
    if ($vista.currency -ne "AOA") { throw "omissao de moeda devia ser AOA, veio '$($vista.currency)'" }
    "moeda='AOA' por omissao"
}

Test-Case "7. Fechar manutencao com custo (ADR-048) soma ao FleetPeriodMaintenanceCost" {
    $assetHeaders = New-PerfilHeaders "AssetManager" "analytics-frota"

    $baseline = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=AOA" -Headers $managerHeaders
    $antes = $baseline.fleetPeriodMaintenanceCost

    $placa = "AN" + ("$stamp".Substring("$stamp".Length - 6))
    $viatura = Invoke-RestMethod "$base/fleet/vehicles" -Method Post -Body (@{ plateNumber = $placa; model = "Hilux" } | ConvertTo-Json) -ContentType "application/json" -Headers $assetHeaders
    $manut = Invoke-RestMethod "$base/fleet/vehicles/$($viatura.vehicleId)/maintenance" -Method Post -Body (@{ type = "Corrective"; description = "Suite analytics"; startedOn = "2026-08-10" } | ConvertTo-Json) -ContentType "application/json" -Headers $assetHeaders
    Invoke-RestMethod "$base/fleet/vehicles/$($viatura.vehicleId)/maintenance/$($manut.maintenanceId)/closure" -Method Post -Body (@{ endedOn = "2026-08-11"; cost = 33000 } | ConvertTo-Json) -ContentType "application/json" -Headers $assetHeaders | Out-Null

    $vista = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=AOA" -Headers $managerHeaders
    if (($vista.fleetPeriodMaintenanceCost - $antes) -ne 33000) {
        throw "custo de manutencao devia subir 33000, subiu $($vista.fleetPeriodMaintenanceCost - $antes)"
    }
    "custo de manutencao +33000 (moeda AOA -- fleet nao tem moeda propria, ver modules/fleet.md)"
}

Test-Case "8. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $managerHeaders2 = @{ Authorization = "Bearer " + (Get-Token ("analytics-manager-$stamp@rivo.ao") $pass) }
    $vista = Invoke-RestMethod "$base/analytics/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders2
    $agosto = ($vista.monthlyRevenue | Where-Object { $_.year -eq 2026 -and $_.month -eq 8 }).amount
    if ($agosto -lt 75000) { throw "ponto de Agosto nao sobreviveu: $agosto" }
    "ponto mensal de Agosto/2026 intacto apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
