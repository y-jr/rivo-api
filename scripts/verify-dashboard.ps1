# Verificação da camada de composição `Rivo.Dashboard` (Dashboard
# Executivo, Fase 8).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-dashboard.ps1
#
# Moeda ZZZ de propósito: nenhuma outra suite factura nela, por isso os
# totais são exactos em vez de "pelo menos" — sem ZZZ, qualquer outra suite
# a correr antes desta contaminaria a soma em AOA.

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
$moeda = "ZZZ"
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
$managerHeaders = New-PerfilHeaders "Manager" "dash-manager"
$salesHeaders = New-PerfilHeaders "Sales" "dash-vendas"

# --- Pré-requisitos: taxa fiscal aberta (efectiva desde sempre, para não
# depender de nenhuma data concreta) e dois clientes.
$codigoTaxa = "Z" + ("$stamp".Substring("$stamp".Length - 6))
$body = @{ code = $codigoTaxa; description = "IVA - suite dashboard" } | ConvertTo-Json
$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).scheduleId
$body = @{ percentage = 14; effectiveFrom = "2000-01-01"; legalInstrument = "Suite dashboard" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

$body = @{ name = "Kianda Lda $stamp"; taxId = "55$stamp"; addressDetail = "Rua A"; city = "Luanda"; country = "AO" } | ConvertTo-Json
$clienteA = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).customerId

$body = @{ name = "Refriango $stamp"; taxId = "56$stamp"; addressDetail = "Rua B"; city = "Luanda"; country = "AO" } | ConvertTo-Json
$clienteB = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).customerId

Write-Host "`n=== Camada de composicao dashboard ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Autenticado sem a permissao (Sales) -> 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $salesHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Sales nao tem dashboard.overview.read"
}


# Baseline em vez de zero absoluto: a suite tem de ser re-executavel (mesma
# convencao de verify-finance.ps1), e uma corrida anterior pode ter deixado
# dados na mesma moeda ZZZ. Mede-se o que MUDA, nao um estado inicial que
# ninguem garante.
$script:base0 = $null
Test-Case "3. Manager (perfil que docs/rivo-suite-descricao-modulos.md nomeia) consegue ver a vista" {
    $script:base0 = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    "HTTP 200 -- receita=$($script:base0.revenue), a-receber=$($script:base0.receivables)"
}

Test-Case "4. Facturar ao cliente A faz a receita e o em-falta subirem exactamente o esperado" {
    $body = @{
        customerId = $clienteA; issuedOn = "2026-08-15"; taxPointDate = "2026-08-15"; currency = $moeda
        lines = @(@{ description = "Servico A"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders | Out-Null

    $vista = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    if (($vista.revenue - $script:base0.revenue) -ne 100000) { throw "receita devia subir 100000, subiu $($vista.revenue - $script:base0.revenue)" }
    if (($vista.receivables - $script:base0.receivables) -ne 114000) { throw "a receber devia subir 114000 (bruto, com imposto), subiu $($vista.receivables - $script:base0.receivables)" }
    "receita +100000 (liquido), a receber +114000 (bruto)"
}

Test-Case "5. Segundo cliente com valor menor nao ultrapassa o primeiro no topo" {
    $body = @{
        customerId = $clienteB; issuedOn = "2026-08-16"; taxPointDate = "2026-08-16"; currency = $moeda
        lines = @(@{ description = "Servico B"; quantity = 1; unitPrice = 40000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $salesHeaders | Out-Null

    # topCustomers generoso -- nao se assume posicao absoluta quando pode
    # haver clientes doutra corrida na mesma janela/moeda; procura-se pelos
    # dois identificadores conhecidos, e verifica-se a ordem relativa entre
    # eles, nao o indice no array inteiro.
    $vista = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda&topCustomers=50" -Headers $managerHeaders
    if (($vista.revenue - $script:base0.revenue) -ne 140000) { throw "receita devia subir 140000, subiu $($vista.revenue - $script:base0.revenue)" }

    $indiceA = [array]::IndexOf($vista.topCustomers.customerId, $clienteA)
    $indiceB = [array]::IndexOf($vista.topCustomers.customerId, $clienteB)
    if ($indiceA -lt 0) { throw "cliente A nao apareceu no topo" }
    if ($indiceB -lt 0) { throw "cliente B nao apareceu no topo" }
    if ($vista.topCustomers[$indiceA].netRevenue -ne 100000) { throw "netRevenue de A errado: $($vista.topCustomers[$indiceA].netRevenue)" }
    if ($vista.topCustomers[$indiceB].netRevenue -ne 40000) { throw "netRevenue de B errado: $($vista.topCustomers[$indiceB].netRevenue)" }
    if ($indiceA -ge $indiceB) { throw "A (100000) devia vir antes de B (40000) no topo" }
    "A antes de B no topo, ambos com o valor certo"
}

$script:base1 = $null
Test-Case "6. Registar despesa faz a despesa e o a-pagar subirem, e o lucro reflecte os dois lados" {
    $script:base1 = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders

    $body = @{
        supplierInvoiceNumber = "FZ-$stamp"; supplierName = "Fornecedor Z"; supplierTaxId = "57$stamp"
        issuedOn = "2026-08-17"; currency = $moeda; netTotal = 60000; taxTotal = 8400
    } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $vista = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders
    if (($vista.expenses - $script:base1.expenses) -ne 60000) { throw "despesa devia subir 60000, subiu $($vista.expenses - $script:base1.expenses)" }
    if (($vista.payables - $script:base1.payables) -ne 68400) { throw "a pagar devia subir 68400 (bruto), subiu $($vista.payables - $script:base1.payables)" }
    # So a despesa mudou nesta janela -- a receita ficou como estava (caso
    # 5). Lucro = Receita - Despesa, por isso desce exactamente o que a
    # despesa subiu.
    if (($vista.profit - $script:base1.profit) -ne -60000) { throw "lucro devia descer 60000 (despesa subiu, receita nao mudou), moveu-se $($vista.profit - $script:base1.profit)" }
    $script:base1 = $vista
    "despesa +60000, a pagar +68400, lucro -60000"
}

Test-Case "7. Janela invertida e recusada com 400" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/dashboard/overview?from=$ate&to=$de&currency=$moeda" -Headers $managerHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "HTTP 400 -- data inicial depois da final"
}

Test-Case "8. Moeda e topCustomers tem omissao -- AOA e 5" {
    $vista = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate" -Headers $managerHeaders
    if ($vista.currency -ne "AOA") { throw "omissao de moeda devia ser AOA, veio '$($vista.currency)'" }
    if ($vista.topCustomers.Count -gt 5) { throw "omissao de topCustomers devia limitar a 5, vieram $($vista.topCustomers.Count)" }
    "moeda='AOA', topCustomers<=5 por omissao"
}

Test-Case "9. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $managerHeaders2 = @{ Authorization = "Bearer " + (Get-Token ("dash-manager-$stamp@rivo.ao") $pass) }
    $vista = Invoke-RestMethod "$base/dashboard/overview?from=$de&to=$ate&currency=$moeda" -Headers $managerHeaders2
    if ($vista.revenue -ne $script:base1.revenue) { throw "receita nao sobreviveu: era $($script:base1.revenue), veio $($vista.revenue)" }
    if ($vista.payables -ne $script:base1.payables) { throw "a pagar nao sobreviveu: era $($script:base1.payables), veio $($vista.payables)" }
    "receita e a-pagar intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
