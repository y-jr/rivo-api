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
    # Com conta propria: desde o ADR-050, quem decide resolve-se do token e o
    # identificador deixou de vir no corpo do pedido.
    $script:aprovadorConta = New-RivoColaboradorComConta `
        -Email "apr-hr-$stamp@rivo.ao" -Nome "Aprovador Verify" `
        -AdminHeaders $adminHeaders -HeadersDeAdmissao $hrHeaders -Perfil "Admin"
    $aprovador = $script:aprovadorConta.EmployeeId

    Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ positionId = $script:plainPositionId } | ConvertTo-Json) | Out-Null

    # Politica so se ja nao existir: duas politicas igualmente especificas sao
    # empate, e o empate recusa a submissao (ADR-034). Sem esta guarda, a suite
    # so passava a primeira vez.
    # Pela base de dados e nao pela rota, de proposito. `Invoke-RestMethod`
    # entrega um array JSON de forma inconsistente — desembrulha-o quando tem um
    # so elemento, e nos outros casos passa-o ao pipeline como **um so item**,
    # onde `$_.campo -eq $valor` compara uma lista com um escalar e devolve o
    # subconjunto correspondente, que sendo nao-vazio e verdadeiro. O
    # `Where-Object` deixaria passar todas as politicas.
    $cargoDaPolitica = Invoke-Sql @"
select top 1 cast(s.approver_position_id as varchar(36))
from approval.policy p join approval.policy_step s on s.policy_id = p.id
where p.process_type = 'hr.position_assignment' and p.is_active = 1
order by s.[order]
"@

    if (-not $cargoDaPolitica) {
        Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
            -Body (@{ processType = "hr.position_assignment"; steps = @(@{ approverPositionId = $script:plainPositionId }) } | ConvertTo-Json -Depth 5) | Out-Null
    }
    else {
        # A politica ja existe de uma execucao anterior e aprova por outro
        # cargo. O aprovador tem de ocupar *esse*, senao nao esta atribuido ao
        # passo. Nao se mexe em $plainPositionId: os casos 8 e 9 dependem dele.
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
    # Pela base de dados: filtrar do lado do PowerShell nao e de confiar sobre
    # o que `Invoke-RestMethod` devolve — ver a nota no caso 11.
    $requestId = Invoke-Sql "select cast(id as varchar(36)) from approval.request where source_reference='$($script:pendingAssignmentId)'"
    if (-not $requestId) { throw "pedido de aprovacao nao encontrado para a atribuicao pendente" }
    $script:pendingRequestId = $requestId

    Invoke-RestMethod "$base/approval/requests/$requestId/decisions" -Method Post -ContentType "application/json" -Headers $script:aprovadorConta.Headers `
        -Body (@{ action = "Approved" } | ConvertTo-Json) | Out-Null

    Invoke-RestMethod "$base/hr/position-assignments/$($script:pendingAssignmentId)/approval-outcome" -Method Post -Headers $hrHeaders | Out-Null

    $estado = Invoke-Sql "select status from hr.position_assignment where id='$($script:pendingAssignmentId)'"
    if ($estado -ne "Effective") { throw "estado '$estado', esperado Effective" }

    $ref = Invoke-RestMethod "$base/hr/employees/$($script:pendingTarget)" -Headers $hrHeaders
    if ($null -eq $ref.currentPosition) { throw "cargo nao conferido apos aprovacao" }
    if ($ref.currentPosition.grantsApprovalAuthority -ne $true) { throw "marca de autoridade errada" }
    "decisao aplicada; cargo com autoridade conferido"
}

Test-Case "13. Historico do pedido mostra a submissao e a decisao completas" {
    # **Difere do GET simples**: esse devolve so quem falta decidir agora
    # (PendingAssignments), util para quem espera pela sua vez. Aqui vem tudo —
    # incluidas as atribuicoes ja decididas — para quem reconstroi o que
    # aconteceu.
    $h = Invoke-RestMethod "$base/approval/requests/$($script:pendingRequestId)/history" -Headers $adminHeaders

    if ($h.status -ne "Approved") { throw "estado '$($h.status)', esperado Approved" }
    if ($h.requestedByEmployeeId -ne $script:pendingTarget) { throw "requisitante errado no historico" }
    if (@($h.decisions).Count -ne 1) { throw "esperada 1 decisao, obtidas $(@($h.decisions).Count)" }
    if ($h.decisions[0].decidedByEmployeeId -ne $script:pendingApprover) { throw "decisor errado no historico" }
    if ($h.decisions[0].action -ne "Approved") { throw "accao '$($h.decisions[0].action)', esperada Approved" }

    # A atribuicao continua no historico depois de decidida — e o que o
    # separa do GET simples, que so mostra quem falta.
    #
    # **Nao assumir posicao no array:** o cargo pode ter mais do que um
    # ocupante, e todos ficam congelados como atribuicoes do mesmo passo
    # (AnyApprover) — a ordem entre eles nao e garantida. O que importa e que
    # o aprovador que decidiu esteja algures na lista.
    if (@($h.assignments).Count -eq 0) { throw "atribuicoes ausentes do historico" }
    if ($h.assignments.approverEmployeeId -notcontains $script:pendingApprover) { throw "aprovador ausente das atribuicoes" }

    "submissao, atribuicao congelada e decisao, todas presentes"
}

Test-Case "14. Historico de pedido inexistente -> 404" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/approval/requests/$([Guid]::NewGuid())/history" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404"
}

Test-Case "15. BR-2: quem submete nao decide sobre o proprio pedido" {
    # O alvo precisa de conta propria: desde o ADR-050, a unica forma de
    # tentar decidir e autenticado como ele. Antes bastava declarar o
    # identificador dele no corpo -- e era precisamente essa a falha.
    $alvoConta = New-RivoColaboradorComConta `
        -Email "alvo-br2-$stamp@rivo.ao" -Nome "Alvo BR2" `
        -AdminHeaders $adminHeaders -HeadersDeAdmissao $hrHeaders -Perfil "Admin"
    $alvo = $alvoConta.EmployeeId

    $resposta = Invoke-RestMethod "$base/hr/employees/$alvo/positions" -Method Post -ContentType "application/json" -Headers $hrHeaders `
        -Body (@{ positionId = $script:authorityPositionId } | ConvertTo-Json)

    $requestId = Invoke-Sql "select cast(id as varchar(36)) from approval.request where source_reference='$($resposta.assignmentId)'"
    if (-not $requestId) { throw "pedido de aprovacao nao encontrado para a atribuicao" }

    # O requisitante e o proprio alvo. Decidir sobre si mesmo e 403, nao 409:
    # nao e o estado que impede, e a pessoa.
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/approval/requests/$requestId/decisions" -Method Post -ContentType "application/json" -Headers $alvoConta.Headers `
            -Body (@{ action = "Approved" } | ConvertTo-Json)
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }

    $estado = Invoke-Sql "select status from hr.position_assignment where id='$($resposta.assignmentId)'"
    if ($estado -ne "Pending") { throw "estado '$estado' — a recusa nao travou nada" }
    "HTTP 403 e atribuicao continua Pending"
}

Test-Case "16. Acoes de hr auditadas" {
    $hired = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.hired' and entity_id='$($script:employeeId)'"
    if ($hired -ne "1") { throw "admissao nao auditada" }
    $assigned = Invoke-Sql "select count(*) from audit.audit_event where action='hr.position.assigned' and entity_id='$($script:employeeId)'"
    if ($assigned -ne "1") { throw "atribuicao nao auditada" }
    # A criacao de cargo com autoridade tem de registar a marca (BR-21).
    $marked = Invoke-Sql "select new_value from audit.audit_event where action='hr.position.created' and entity_id='$($script:authorityPositionId)'"
    if ($marked -notmatch "true") { throw "marca de autoridade nao registada: '$marked'" }
    "admissao, atribuicao e criacao de cargo com marca"
}

Test-Case "17. Sem autenticacao -> 401; sem permissao -> 403" {
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

$script:linkedUserId = $null
Test-Case "18. Contratar com conta ja ligada a outro colaborador e recusado (ADR-042)" {
    $e = "portal-$stamp@rivo.ao"
    $b = @{ email = $e; password = $pass } | ConvertTo-Json
    $script:linkedUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $b = @{ fullName = "Primeiro Colaborador"; userId = $script:linkedUserId } | ConvertTo-Json
    $primeiro = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId
    if (-not $primeiro) { throw "primeiro colaborador nao foi criado" }

    $b = @{ fullName = "Segundo Colaborador"; userId = $script:linkedUserId } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- a conta ja tem um colaborador, nao se liga a um segundo"
}

Test-Case "19. UserId e unico na base de dados" {
    $dup = Invoke-Sql "select count(*) from hr.employee where user_id='$($script:linkedUserId)'"
    if ($dup -ne "1") { throw "esperado exactamente 1 colaborador ligado, encontrados $dup -- indice unico nao impediu" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

# --- Ligacao de conta a colaborador ja admitido (ADR-051) -------------------
#
# Ate 2026-09-05 o vinculo so se estabelecia na admissao. Passou a fazer falta
# com o ADR-050: quem decide uma aprovacao tem de ter conta ligada, e quem ja
# estava admitido sem conta nao podia ser ligado sem ser readmitido.

Test-Case "20. Perfil HR nao recebe hr.employees.link_account (ADR-051)" {
    $has = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='HR' and c.claim_value='hr.employees.link_account'"
    if ($has -ne "0") { throw "HR tem hr.employees.link_account" }
    $admin = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Admin' and c.claim_value='hr.employees.link_account'"
    if ($admin -ne "1") { throw "Admin nao tem hr.employees.link_account" }
    "RH admite pessoas mas nao decide que conta age por quem"
}

Test-Case "21. Ligar conta a colaborador ja admitido devolve 204" {
    # Admitido sem conta -- o caso que antes nao tinha saida.
    $b = @{ fullName = "Ligado Depois $stamp" } | ConvertTo-Json
    $script:semContaId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId

    $email = "ligado-$stamp@rivo.ao"
    $b = @{ email = $email; password = $pass } | ConvertTo-Json
    $script:contaNovaId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $r = Invoke-WebRequest "$base/hr/employees/$($script:semContaId)/account" -Method Post `
        -Body (@{ userId = $script:contaNovaId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }

    $ligado = Invoke-Sql "select count(*) from hr.employee where id='$($script:semContaId)' and user_id='$($script:contaNovaId)'"
    if ($ligado -ne "1") { throw "vinculo nao ficou gravado" }
    "colaborador admitido sem conta passa a ter uma"
}

Test-Case "22. Repetir a mesma ligacao e repetivel sem erro" {
    $r = Invoke-WebRequest "$base/hr/employees/$($script:semContaId)/account" -Method Post `
        -Body (@{ userId = $script:contaNovaId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }
    "mesmo estado pretendido, sem segundo registo na trilha"
}

Test-Case "23. Conta ja de outro colaborador da 409" {
    $b = @{ fullName = "Outro Qualquer $stamp" } | ConvertTo-Json
    $outroId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$outroId/account" -Method Post `
            -Body (@{ userId = $script:contaNovaId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders
    }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "uma conta serve no maximo um colaborador (ADR-042, ADR-050)"
}

Test-Case "24. Colaborador que ja tem conta da 409, nao substitui" {
    $email = "outra-conta-$stamp@rivo.ao"
    $b = @{ email = $email; password = $pass } | ConvertTo-Json
    $terceira = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$($script:semContaId)/account" -Method Post `
            -Body (@{ userId = $terceira } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders
    }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    # O vinculo original tem de continuar intacto -- religar por cima
    # transferiria a identidade com que se aprova.
    $intacto = Invoke-Sql "select count(*) from hr.employee where id='$($script:semContaId)' and user_id='$($script:contaNovaId)'"
    if ($intacto -ne "1") { throw "o vinculo original foi substituido" }
    "recusa em vez de sobrepor, ao contrario de LinkCustomerAccount"
}

Test-Case "25. Auto-ligacao recusada com 403" {
    # O Admin do bootstrap nao e colaborador; tenta ligar a propria conta.
    $adminUserId = Invoke-Sql "select id from [identity].app_user where email='$($dotenv["BOOTSTRAP_ADMIN_EMAIL"])'"
    $b = @{ fullName = "Alvo De Auto Ligacao $stamp" } | ConvertTo-Json
    $alvoId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$alvoId/account" -Method Post `
            -Body (@{ userId = $adminUserId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "403 e nao 409: nao e o estado que impede, e quem pede"
}

Test-Case "26. Perfil HR nao consegue ligar contas" {
    $b = @{ fullName = "Fora Do Alcance $stamp" } | ConvertTo-Json
    $alvoId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId
    $email = "recusado-$stamp@rivo.ao"
    $b = @{ email = $email; password = $pass } | ConvertTo-Json
    $conta = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$alvoId/account" -Method Post `
            -Body (@{ userId = $conta } | ConvertTo-Json) -ContentType "application/json" -Headers $hrHeaders
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "a permissao esta fora do perfil HR de proposito"
}

Test-Case "27. A ligacao fica na trilha, com a conta e o autor" {
    $reg = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.account_linked' and entity_id='$($script:semContaId)'"
    if ($reg -ne "1") { throw "esperado exactamente 1 registo, encontrados $reg" }
    $comConta = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.account_linked' and entity_id='$($script:semContaId)' and new_value like '%$($script:contaNovaId)%' and actor_id is not null"
    if ($comConta -ne "1") { throw "registo sem a conta ligada ou sem autor" }
    "quem investiga uma decisao sabe quando a conta passou a agir por aquela pessoa"
}

# --- Desligar (ADR-052) -----------------------------------------------------
#
# As decisoes de aprovacao ja tomadas continuam validas: ApprovalDecision
# guarda DecidedByEmployeeId, e o facto gravado e "o colaborador X decidiu",
# nunca "a conta A decidiu". Desligar so remove a capacidade de agir daqui
# para a frente.

Test-Case "28. Desligar a conta devolve 204 e liberta o colaborador" {
    $r = Invoke-WebRequest "$base/hr/employees/$($script:semContaId)/account" -Method Delete `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }

    $vinculo = Invoke-Sql "select count(*) from hr.employee where id='$($script:semContaId)' and user_id is null"
    if ($vinculo -ne "1") { throw "o vinculo nao foi removido" }
    "a conta deixa de agir por esta pessoa"
}

Test-Case "29. Desligar de novo e repetivel sem erro" {
    $r = Invoke-WebRequest "$base/hr/employees/$($script:semContaId)/account" -Method Delete `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }
    "mesmo estado pretendido, sem segundo registo"
}

Test-Case "30. A conta libertada pode ligar-se a outro colaborador" {
    # E a sequencia que corrige um vinculo errado, e a unica que existe.
    $b = @{ fullName = "Pessoa Certa $stamp" } | ConvertTo-Json
    $script:certoId = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId

    $r = Invoke-WebRequest "$base/hr/employees/$($script:certoId)/account" -Method Post `
        -Body (@{ userId = $script:contaNovaId } | ConvertTo-Json) -ContentType "application/json" `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }
    "desligar e voltar a ligar e o caminho de correccao"
}

Test-Case "31. A transferencia fica legivel na trilha" {
    # O par desligar+ligar nomeia a mesma conta dos dois lados: PreviousValue
    # no primeiro, NewValue no segundo. E o que torna a transferencia
    # investigavel -- o preco assumido por o 409 ser contornavel em dois passos.
    $saiu = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.account_unlinked' and entity_id='$($script:semContaId)' and previous_value like '%$($script:contaNovaId)%'"
    if ($saiu -ne "1") { throw "o desligar nao nomeia a conta removida" }
    $entrou = Invoke-Sql "select count(*) from audit.audit_event where action='hr.employee.account_linked' and entity_id='$($script:certoId)' and new_value like '%$($script:contaNovaId)%'"
    if ($entrou -ne "1") { throw "o ligar nao nomeia a conta recebida" }
    "a conta saiu de um colaborador e entrou noutro, com registo dos dois lados"
}

Test-Case "32. Perfil HR nao consegue desligar contas" {
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$($script:certoId)/account" -Method Delete -Headers $hrHeaders
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "mesma permissao de ligar, e continua fora do perfil HR"
}

Test-Case "33. Desligar colaborador inexistente -> 404" {
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$([Guid]::NewGuid())/account" -Method Delete -Headers $adminHeaders
    }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "404 e nao 204: nao ha estado pretendido para um colaborador que nao existe"
}

# --- Historico do vinculo (ADR-053) -----------------------------------------
#
# O campo employee.user_id continua a ser o vinculo activo e o caminho de
# decisao. Esta tabela e historia: responde a "que conta podia agir por esta
# pessoa no dia D" sem depender de LIKE sobre o JSON da trilha.

Test-Case "34. Campo e historico nunca divergem" {
    # A invariante que torna o historico confiavel. Se falhar, uma investigacao
    # passa a ter duas respostas e nenhuma forma de saber qual vale.
    $orfaos = Invoke-Sql "select count(*) from hr.employee e where e.user_id is not null and not exists (select 1 from hr.employee_account_link l where l.employee_id=e.id and l.user_id=e.user_id and l.unlinked_on is null)"
    if ($orfaos -ne "0") { throw "$orfaos vinculo(s) activo(s) sem episodio aberto" }

    $fantasmas = Invoke-Sql "select count(*) from hr.employee_account_link l where l.unlinked_on is null and not exists (select 1 from hr.employee e where e.id=l.employee_id and e.user_id=l.user_id)"
    if ($fantasmas -ne "0") { throw "$fantasmas episodio(s) aberto(s) sem vinculo correspondente" }
    "nenhum vinculo sem episodio, nenhum episodio sem vinculo"
}

Test-Case "35. No maximo um episodio aberto por colaborador" {
    # Garantido pelo indice unico filtrado; verificado na mesma porque e a
    # invariante de que FindOpenAccountLinkAsync depende.
    $duplicados = Invoke-Sql "select count(*) from (select employee_id from hr.employee_account_link where unlinked_on is null group by employee_id having count(*) > 1) x"
    if ($duplicados -ne "0") { throw "$duplicados colaborador(es) com mais de um episodio aberto" }
    "indice unico filtrado em unlinked_on is null"
}

Test-Case "36. O historico mostra a sequencia completa do vinculo" {
    # semContaId foi ligado (caso 21), desligado (caso 28) e nunca religado.
    $h = Invoke-RestMethod "$base/hr/employees/$($script:semContaId)/account-history" -Headers $adminHeaders
    $episodios = @($h)
    if ($episodios.Count -lt 1) { throw "historico vazio para um colaborador que ja teve conta" }

    $fechado = $episodios | Where-Object { $_.userId -eq $script:contaNovaId -and $_.unlinkedOn }
    if (-not $fechado) { throw "o episodio da conta ligada e depois desligada nao aparece fechado" }
    if (-not $fechado.unlinkedByUserId) { throw "o episodio fechado nao diz quem desligou" }
    "$($episodios.Count) episodio(s), com quem ligou, quem desligou e quando"
}

Test-Case "37. Colaborador sem conta da lista vazia, inexistente da 404" {
    $b = @{ fullName = "Nunca Teve Conta $stamp" } | ConvertTo-Json
    $nunca = (Invoke-RestMethod "$base/hr/employees" -Method Post -Body $b -ContentType "application/json" -Headers $hrHeaders).employeeId

    # Le-se o corpo cru: Invoke-RestMethod devolve $null para um array JSON
    # vazio, e @($null) tem um elemento -- o teste dava falso positivo sobre a
    # propria API que estava a verificar.
    $corpo = (Invoke-WebRequest "$base/hr/employees/$nunca/account-history" -Headers $adminHeaders).Content
    if ($corpo.Trim() -ne "[]") { throw "esperado corpo '[]', obtido '$corpo'" }

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$([Guid]::NewGuid())/account-history" -Headers $adminHeaders
    }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "lista vazia e 404 dizem coisas diferentes"
}

Test-Case "38. Perfil HR nao ve o historico de contas" {
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/hr/employees/$($script:certoId)/account-history" -Headers $hrHeaders
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "o mapa conta<->pessoa e informacao de seguranca, nao de organograma"
}

# Nao ha caso para "o retroactivo correu".
#
# Houve um, e estava errado: afirmava que existiam episodios sem autor, o que
# so e verdade numa base que ja existia antes da migracao. Em CI a base nasce
# vazia -- nao ha nada para retroagir, e todos os episodios tem autor porque
# foram criados pelo codigo. O caso afirmava o estado de uma maquina, nao uma
# propriedade do sistema, e falhou no primeiro ambiente limpo.
#
# O que importa do retroactivo ja esta no caso 34: nenhum vinculo activo sem
# episodio aberto. Essa invariante vale nas duas situacoes, e e ela que diz se
# a migracao fez o que devia.

Test-Case "39. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
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
