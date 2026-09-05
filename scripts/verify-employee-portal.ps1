# Verificação da camada de composição `Rivo.EmployeePortal` (Portal do
# Colaborador, ADR-042).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-employee-portal.ps1

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

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

# Utilizador com perfil HR, para admitir o colaborador desta suite.
$hrEmail = "portal-rh-$stamp@rivo.ao"
$b = @{ email = $hrEmail; password = $pass } | ConvertTo-Json
$hrUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
$b = @{ profile = "HR" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$hrUserId/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
$hrHeaders = @{ Authorization = "Bearer " + (Get-Token $hrEmail $pass) }

Write-Host "`n=== Camada de composicao employee-portal ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Admin autenticado sem colaborador ligado -> 403 -- o portal nao contorna a falta de vinculo" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" -Headers $adminHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Admin nao tem hr.Employee ligado, e o portal recusa em vez de adivinhar"
}

$script:deptId = $null
Test-Case "3. Abrir departamento para o colaborador da suite" {
    $b = @{ name = "PortalDept-$stamp" } | ConvertTo-Json
    $script:deptId = (Invoke-RestMethod "$base/hr/departments" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).departmentId
    if (-not $script:deptId) { throw "sem id" }
    "departamento criado"
}

$script:ownEmail = "colaborador-$stamp@rivo.ao"
$script:ownUserId = $null
$script:ownEmployeeId = $null
Test-Case "4. Colaborador com conta ligada ve o seu proprio perfil" {
    $b = @{ email = $script:ownEmail; password = $pass } | ConvertTo-Json
    $script:ownUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $b = @{ fullName = "Colaborador Portal $stamp"; departmentId = $script:deptId } | ConvertTo-Json
    $script:ownEmployeeId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId
    if (-not $script:ownEmployeeId) { throw "colaborador nao foi criado" }

    # Ligacao em passo proprio desde o ADR-054, e com cabecalhos de Admin: RH
    # admite mas nao liga -- a permissao esta fora do perfil de proposito.
    Invoke-RestMethod "$base/hr/employees/$($script:ownEmployeeId)/account" -Method Post `
        -Body (@{ userId = $script:ownUserId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $adminHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.employeeId -ne $script:ownEmployeeId) { throw "employeeId '$($perfil.employeeId)' nao e o proprio '$($script:ownEmployeeId)'" }
    if ($perfil.displayName -ne "Colaborador Portal $stamp") { throw "nome errado: '$($perfil.displayName)'" }
    if ($perfil.departmentId -ne $script:deptId) { throw "departamento errado" }
    if ($perfil.status -ne "Active") { throw "estado esperado Active, obtido '$($perfil.status)'" }
    "employeeId=$($script:ownEmployeeId), nome e departamento correctos"
}

Test-Case "5. Sem cargo atribuido, currentPosition vem nulo" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($null -ne $perfil.currentPosition) { throw "currentPosition devia ser nulo, veio $($perfil.currentPosition | ConvertTo-Json -Compress)" }
    "currentPosition nulo"
}

$script:positionId = $null
Test-Case "6. Com cargo atribuido, currentPosition aparece no proprio perfil" {
    $b = @{ name = "Analista-$stamp"; hierarchyLevel = 5; grantsApprovalAuthority = $false } | ConvertTo-Json
    $script:positionId = (Invoke-RestMethod "$base/hr/positions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).positionId
    $b = @{ positionId = $script:positionId } | ConvertTo-Json
    Invoke-RestMethod "$base/hr/employees/$($script:ownEmployeeId)/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.currentPosition.name -ne "Analista-$stamp") { throw "cargo actual nao apareceu" }
    if ($perfil.currentPosition.grantsApprovalAuthority) { throw "este cargo nao confere autoridade" }
    "currentPosition.name='Analista-$stamp'"
}

Test-Case "7. Outro utilizador sem colaborador ligado -> 403, nunca ve o colaborador de outro" {
    $e2 = "semvinculo-$stamp@rivo.ao"
    $b = @{ email = $e2; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
    $h2 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }

    $code = Get-StatusCode { Invoke-RestMethod "$base/portal/me" -Headers $h2 }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- so ve o proprio, e o proprio nao existe para esta conta"
}

Test-Case "8. Vista sobrevive ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $perfil = Invoke-RestMethod "$base/portal/me" -Headers $ownHeaders
    if ($perfil.employeeId -ne $script:ownEmployeeId) { throw "vinculo nao sobreviveu ao reinicio" }
    "employeeId=$($script:ownEmployeeId) intacto apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
