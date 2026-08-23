# Verificação do módulo `hr`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-hr.ps1

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
    $has = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='hr.positions.write'"
    if ($has -ne "0") { throw "HR tem hr.positions.write" }
    $assign = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='hr.positions.assign'"
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

Test-Case "10. Cargo COM autoridade nunca fica efectivo directamente (BR-20)" {
    # A invariante e esta, e vale **independentemente de haver politica**: sem
    # politica a submissao e recusada (409); com politica fica pendente (202).
    # Efectiva de imediato, nunca.
    #
    # Escrito assim de proposito para a suite ser re-executavel: assertar "409"
    # obrigaria a base de dados a nao ter politica nenhuma, e o caso 11 cria uma.
    $b = @{ positionId = $script:authorityPositionId } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees/$($script:employeeId)/positions" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders }
    if ($code -notin @(200, 409)) { throw "esperado 202 ou 409, obtido $code" }

    $efectivas = Invoke-Sql "select count(*) from hr.position_assignment where position_id='$($script:authorityPositionId)' and status='Effective'"
    if ($efectivas -ne "0") { throw "cargo com autoridade ficou efectivo sem aprovacao" }
    "HTTP $code e nenhuma atribuicao efectiva"
}

Test-Case "11. ⚠ Cargo COM autoridade fica PENDENTE e nao confere nada (BR-20)" {
    # Politica de um passo, aprovada por quem ocupa o cargo simples.
    $aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ fullName = "Aprovador Verify" } | ConvertTo-Json)).employeeId
    Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ positionId = $script:plainPositionId } | ConvertTo-Json) | Out-Null

    # Politica so se ja nao existir: duas politicas igualmente especificas sao
    # empate, e o empate recusa a submissao (ADR-034). Sem esta guarda, a suite
    # so passava a primeira vez.
    $existentes = Invoke-RestMethod "$base/approval/policies" -Headers $adminHeaders |
        Where-Object { $_.processType -eq "hr.position_assignment" -and $_.isActive }

    if (-not $existentes) {
        Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ processType = "hr.position_assignment"; steps = @(@{ approverPositionId = $script:plainPositionId }) } | ConvertTo-Json -Depth 5) | Out-Null
    }
    else {
        # A politica ja existe de uma execucao anterior e aprova por outro
        # cargo. O aprovador tem de ocupar *esse*, senao nao esta atribuido ao
        # passo. Nao se mexe em $plainPositionId: os casos 8 e 9 dependem dele.
        $cargoDaPolitica = $existentes[0].steps[0].approverPositionId
        Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
            -Body (@{ positionId = $cargoDaPolitica } | ConvertTo-Json) | Out-Null
    }

    $alvo = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ fullName = "Alvo Verify" } | ConvertTo-Json)).employeeId

    $resposta = Invoke-RestMethod "$base/hr/employees/$alvo/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ positionId = $script:authorityPositionId } | ConvertTo-Json)

    $estado = Invoke-Sql "select status from hr.position_assignment where id='$($resposta.assignmentId)'"
    if ($estado -ne "Pending") { throw "estado '$estado', esperado Pending" }

    # **O ponto todo de BR-20:** pendente nao confere o cargo.
    $ref = Invoke-RestMethod "$base/hr/employees/$alvo" -Headers $hrHeaders
    if ($null -ne $ref.currentPosition) { throw "cargo conferido antes da aprovacao" }

    $script:pendingAssignmentId = $resposta.assignmentId
    $script:pendingApprover = $aprovador
    $script:pendingTarget = $alvo
    "atribuicao Pending; cargo nao resolvido"
}

Test-Case "12. Aprovado, o cargo passa a ser conferido" {
    $pedido = (Invoke-RestMethod "$base/approval/requests?processType=hr.position_assignment" -Headers $adminHeaders |
        Where-Object { $_.sourceReference -eq $script:pendingAssignmentId })[0]

    Invoke-RestMethod "$base/approval/requests/$($pedido.requestId)/decisions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ decidedByEmployeeId = $script:pendingApprover; action = "Approved" } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/hr/position-assignments/$($script:pendingAssignmentId)/approval-outcome" -Method Post -Headers $hrHeaders | Out-Null

    $estado = Invoke-Sql "select status from hr.position_assignment where id='$($script:pendingAssignmentId)'"
    if ($estado -ne "Effective") { throw "estado '$estado', esperado Effective" }

    $ref = Invoke-RestMethod "$base/hr/employees/$($script:pendingTarget)" -Headers $hrHeaders
    if ($null -eq $ref.currentPosition) { throw "cargo nao conferido apos aprovacao" }
    if ($ref.currentPosition.grantsApprovalAuthority -ne $true) { throw "marca de autoridade errada" }
    "decisao aplicada; cargo com autoridade conferido"
}

Test-Case "13. BR-2: quem submete nao decide sobre o proprio pedido" {
    $alvo = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ fullName = "Alvo BR2" } | ConvertTo-Json)).employeeId

    $resposta = Invoke-RestMethod "$base/hr/employees/$alvo/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ positionId = $script:authorityPositionId } | ConvertTo-Json)

    $pedido = (Invoke-RestMethod "$base/approval/requests?processType=hr.position_assignment" -Headers $adminHeaders |
        Where-Object { $_.sourceReference -eq $resposta.assignmentId })[0]

    # O requisitante e o proprio alvo. Decidir sobre si mesmo e 403, nao 409:
    # nao e o estado que impede, e a pessoa.
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/approval/requests/$($pedido.requestId)/decisions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ decidedByEmployeeId = $alvo; action = "Approved" } | ConvertTo-Json)
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }

    $estado = Invoke-Sql "select status from hr.position_assignment where id='$($resposta.assignmentId)'"
    if ($estado -ne "Pending") { throw "estado '$estado' — a recusa nao travou nada" }
    "HTTP 403 e atribuicao continua Pending"
}

Test-Case "14. Acoes de hr auditadas" {
    $hired = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.hired' and entity_id='$($script:employeeId)'"
    if ($hired -ne "1") { throw "admissao nao auditada" }
    $assigned = Invoke-Sql "select count(*) from audit.audit_event where action='hr.position.assigned' and entity_id='$($script:employeeId)'"
    if ($assigned -ne "1") { throw "atribuicao nao auditada" }
    # A criacao de cargo com autoridade tem de registar a marca (BR-21).
    $marked = Invoke-Sql "select new_value from audit.audit_event where action='hr.position.created' and entity_id='$($script:authorityPositionId)'"
    if ($marked -notmatch "true") { throw "marca de autoridade nao registada: '$marked'" }
    "admissao, atribuicao e criacao de cargo com marca"
}

Test-Case "15. Sem autenticacao -> 401; sem permissao -> 403" {
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

Test-Case "16. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
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
