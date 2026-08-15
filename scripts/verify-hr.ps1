# Verificação do módulo `hr`.
#
#   docker compose up -d --build
#   pwsh -File scripts/verify-hr.ps1

$ErrorActionPreference = "Stop"
$base = "http://localhost:5080"
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

function Invoke-Sql { param([string]$q) return (docker exec rivo-postgres psql -U rivo -d rivo -t -A -c $q).Trim() }

$dotenv = @{}
Get-Content ".env" | Where-Object { $_ -match "=" -and $_ -notmatch "^\s*#" } | ForEach-Object {
    $p = $_ -split "=", 2; $dotenv[$p[0].Trim()] = $p[1].Trim()
}

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

# Admin do bootstrap
$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

# Utilizador com perfil HR, para testar a separacao do ADR-015
$hrEmail = "rh-$stamp@rivo.ao"
$body = @{ email = $hrEmail; password = $pass } | ConvertTo-Json
$hrUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
$body = @{ profile = "HR" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$hrUserId/roles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
$hrHeaders = @{ Authorization = "Bearer " + (Get-Token $hrEmail $pass) }

Write-Host "`n=== Modulo hr ===`n"

Test-Case "1. Schema hr com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from hr.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de hr" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='hr'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='hr' and table_name in ('app_user','audit_event')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema hr" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. Perfil HR nao recebe hr.positions.write (ADR-015)" {
    $has = Invoke-Sql "select count(*) from identity.app_role_claim c join identity.app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='hr.positions.write'"
    if ($has -ne "0") { throw "HR tem hr.positions.write" }
    $assign = Invoke-Sql "select count(*) from identity.app_role_claim c join identity.app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='hr.positions.assign'"
    if ($assign -ne "1") { throw "HR nao tem hr.positions.assign" }
    "HR atribui cargos mas nao gere o catalogo"
}

$script:deptId = $null
Test-Case "3. HR cria departamento" {
    $b = @{ name = "Financeiro-$stamp" } | ConvertTo-Json
    $script:deptId = (Invoke-RestMethod "$base/hr/departments" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).departmentId
    if (-not $script:deptId) { throw "sem id" }
    "departamento criado"
}

Test-Case "4. HR NAO pode criar cargo no catalogo -> 403" {
    $b = @{ name = "Tecnico-$stamp"; hierarchyLevel = 5; grantsApprovalAuthority = $false } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 sem hr.positions.write"
}

$script:plainPositionId = $null
$script:authorityPositionId = $null
Test-Case "5. Admin cria cargos, com e sem autoridade" {
    $b = @{ name = "Tecnico-$stamp"; hierarchyLevel = 5; grantsApprovalAuthority = $false } | ConvertTo-Json
    $script:plainPositionId = (Invoke-RestMethod "$base/hr/positions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).positionId
    $b = @{ name = "DirectorFinanceiro-$stamp"; hierarchyLevel = 1; grantsApprovalAuthority = $true } | ConvertTo-Json
    $script:authorityPositionId = (Invoke-RestMethod "$base/hr/positions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).positionId
    "dois cargos criados"
}

$script:employeeId = $null
Test-Case "6. HR admite colaborador" {
    $b = @{ fullName = "Ana Teste"; departmentId = $script:deptId } | ConvertTo-Json
    $script:employeeId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId
    if (-not $script:employeeId) { throw "sem id" }
    "colaborador criado"
}

Test-Case "7. Departamento inexistente e recusado" {
    $b = @{ fullName = "Fantasma"; departmentId = [Guid]::NewGuid().ToString() } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404"
}

Test-Case "8. Cargo sem autoridade e atribuido de imediato" {
    $b = @{ positionId = $script:plainPositionId } | ConvertTo-Json
    Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders | Out-Null
    $status = Invoke-Sql "select status from hr.position_assignment where employee_id='$($script:employeeId)'"
    if ($status -ne "Effective") { throw "estado '$status', esperado Effective" }
    "atribuicao efectiva"
}

Test-Case "9. Contrato resolve o cargo actual (ADR-010)" {
    $ref = Invoke-RestMethod "$base/hr/employees/$($script:employeeId)" -Headers $hrHeaders
    if ($ref.currentPosition -eq $null) { throw "cargo nao resolvido" }
    if ($ref.currentPosition.grantsApprovalAuthority -ne $false) { throw "marca de autoridade errada" }
    if ($ref.displayName -ne "Ana Teste") { throw "nome errado" }
    "EmployeeReference com cargo, estado e departamento"
}

Test-Case "10. ⚠ Cargo COM autoridade e recusado ate existir approval (BR-20)" {
    $b = @{ positionId = $script:authorityPositionId } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 501) { throw "esperado 501, obtido $code" }

    # Nada foi gravado: a escalada continua fechada.
    $count = Invoke-Sql "select count(*) from hr.position_assignment where position_id='$($script:authorityPositionId)'"
    if ($count -ne "0") { throw "atribuicao gravada apesar da recusa" }
    "HTTP 501 e nenhuma atribuicao gravada"
}

Test-Case "11. Acoes de hr auditadas" {
    $hired = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.hired' and entity_id='$($script:employeeId)'"
    if ($hired -ne "1") { throw "admissao nao auditada" }
    $assigned = Invoke-Sql "select count(*) from audit.audit_event where action='hr.position.assigned' and entity_id='$($script:employeeId)'"
    if ($assigned -ne "1") { throw "atribuicao nao auditada" }
    # A criacao de cargo com autoridade tem de registar a marca (BR-21).
    $marked = Invoke-Sql "select new_value from audit.audit_event where action='hr.position.created' and entity_id='$($script:authorityPositionId)'"
    if ($marked -notmatch "true") { throw "marca de autoridade nao registada: '$marked'" }
    "admissao, atribuicao e criacao de cargo com marca"
}

Test-Case "12. Sem autenticacao -> 401; sem permissao -> 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }

    # Utilizador sem perfil nenhum.
    $e = "semperfil-$stamp@rivo.ao"
    $b = @{ email = $e; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
    $h = @{ Authorization = "Bearer " + (Get-Token $e $pass) }
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees" -Headers $h }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "13. Dados sobrevivem ao reinicio da stack" {
    docker compose restart | Out-Null
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }
    $emp = Invoke-Sql "select count(*) from hr.employee"
    $asg = Invoke-Sql "select count(*) from hr.position_assignment"
    if ([int]$emp -lt 1 -or [int]$asg -lt 1) { throw "dados perdidos: emp=$emp asg=$asg" }
    "colaboradores=$emp atribuicoes=$asg"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
