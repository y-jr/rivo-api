# Verificação da camada de composição `Rivo.Settings` (Configurações &
# Administração, ADR-041).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-settings.ps1

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
$adminEmail = $dotenv["BOOTSTRAP_ADMIN_EMAIL"]
$adminPass = $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

Write-Host "`n=== Camada de composicao settings ===`n"

Test-Case "1. Vista mostra os oito Perfis de Acesso, cada um com as suas permissoes" {
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    if ($overview.accessProfiles.Count -ne 8) { throw "esperados 8 perfis, obtidos $($overview.accessProfiles.Count)" }
    $admin = $overview.accessProfiles | Where-Object { $_.name -eq "Admin" }
    if (-not $admin) { throw "perfil Admin nao apareceu" }
    if ($admin.permissions -notcontains "identity.roles.read") { throw "Admin sem identity.roles.read" }
    "8 perfis; Admin com $($admin.permissions.Count) permissoes"
}

Test-Case "2. Perfis vem ordenados por nome" {
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $nomes = $overview.accessProfiles | ForEach-Object { $_.name }
    $ordenado = $nomes | Sort-Object
    if (($nomes -join ",") -ne ($ordenado -join ",")) { throw "nao vem ordenado: $($nomes -join ',')" }
    "ordem alfabetica confirmada"
}

$processType = "settings.verify_probe_$stamp"

Test-Case "3. Regra de aprovacao nova aparece agrupada pelo seu modulo" {
    $body = @{
        processType = $processType
        requiresBudgetCheck = $false
        steps = @(@{ approverPositionId = [Guid]::NewGuid().ToString() })
    } | ConvertTo-Json -Depth 5
    $script:policyId = (Invoke-RestMethod "$base/approval/policies" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders).policyId
    if (-not $script:policyId) { throw "politica nao criada" }

    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    if (-not $grupo) { throw "grupo 'settings' nao apareceu na vista" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra) { throw "politica nova nao apareceu no grupo" }
    if (-not $regra.isActive) { throw "politica nova devia nascer activa" }
    if ($regra.stepCount -ne 1) { throw "esperado 1 passo, obtido $($regra.stepCount)" }
    "grupo 'settings', policyId=$($script:policyId), 1 passo, activa"
}

Test-Case "4. Desactivar a politica nao a esconde da vista - mostra o estado, nao filtra" {
    # Clear-RivoApprovalPolicies (_ambiente.ps1) repete ate confirmar por SQL
    # (K20, known-issues.md) - a mesma robustez que fecha esta suite sem
    # deixar nada activo para tras.
    Clear-RivoApprovalPolicies -ProcessType $processType -Headers $adminHeaders

    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra) { throw "politica desactivada desapareceu da vista" }
    if ($regra.isActive) { throw "devia mostrar isActive=false apos desactivar" }
    "continua na vista, isActive=false"
}

Test-Case "5. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/settings/overview" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "6. Autenticado sem as duas permissoes -> 403" {
    $email = "settings-verify-$stamp@rivo.ao"
    $pass = "Rivo!Settings2026"
    $body = @{ email = $email; password = $pass } | ConvertTo-Json
    $userId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    Invoke-RestMethod "$base/identity/users/$userId/roles" -Method Post -Body (@{ profile = "HR" } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $token = Get-Token $email $pass
    $code = Get-StatusCode { Invoke-RestMethod "$base/settings/overview" -Headers @{ Authorization = "Bearer $token" } }
    if ($code -ne 403) { throw "esperado 403 (HR nao tem approval.policies.read), obtido $code" }
    "HTTP 403 -- HR nao tem as duas permissoes que a vista soma"
}

Test-Case "7. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $adminHeaders2 = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $overview = Invoke-RestMethod "$base/settings/overview" -Headers $adminHeaders2
    if ($overview.accessProfiles.Count -ne 8) { throw "perfis nao sobreviveram: $($overview.accessProfiles.Count)" }
    $grupo = $overview.approvalRulesByModule | Where-Object { $_.module -eq "settings" }
    $regra = $grupo.rules | Where-Object { $_.policyId -eq $script:policyId }
    if (-not $regra -or $regra.isActive) { throw "estado da politica nao sobreviveu ao reinicio" }
    "8 perfis, politica desactivada intacta apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
