# Verificação do módulo `projects`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-projects.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Marco e Tarefa ganharam regra de
# negócio (ver `modules/projects.md` §Possui e a nota "Estado"). Orçamento de
# Projecto e Alocação de Recursos continuam por fazer — esta suite verifica o
# que existe, não o que falta.
#
# Re-executável: cada corrida abre um projecto com nome próprio, derivado do
# carimbo temporal.

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

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

$semPerfilEmail = "semperfil-pr-$stamp@rivo.ao"
Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

$colaborador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Colaborador PR $stamp" } | ConvertTo-Json)).employeeId
$colaborador2 = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Segundo Colaborador PR $stamp" } | ConvertTo-Json)).employeeId

$nome = "Piloto Angola $stamp"

Write-Host "`n=== Modulo projects ===`n"

Test-Case "1. Schema projects com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from projects.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de projects" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='projects'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='projects' and table_name in ('app_user','audit_event','approval_request')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema projects" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. ProjectManager deixou de ser perfil vazio" {
    $r = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='ProjectManager' and c.claim_value='projects.projects.read'"
    $w = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='ProjectManager' and c.claim_value='projects.projects.write'"
    if ($r -ne "1" -or $w -ne "1") { throw "ProjectManager sem projects.projects.read/write (read=$r write=$w)" }
    "ProjectManager le e escreve projectos"
}

Test-Case "3. Abrir projecto" {
    $body = @{ name = $nome; startDate = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.projectId) { throw "sem projectId na resposta" }
    $script:projectId = $r.projectId
    "projecto '$nome' aberto"
}

Test-Case "4. Nome vazio e recusado" {
    $body = @{ name = ""; startDate = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "sem nome recusado"
}

Test-Case "5. Consultar projecto -- nasce sem marcos nem tarefas" {
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.name -ne $nome) { throw "nome nao bate: '$($p.name)'" }
    if ($p.status -ne "Active") { throw "estado inesperado: $($p.status)" }
    if (@($p.milestones).Count -ne 0) { throw "marcos deviam estar vazios" }
    if (@($p.tasks).Count -ne 0) { throw "tarefas deviam estar vazias" }
    "estado Active, nome correcto, sem marcos nem tarefas"
}

Test-Case "6. Listagem por omissao mostra activos" {
    $lista = Invoke-RestMethod "$base/projects" -Headers $adminHeaders
    if ($lista.projectId -notcontains $script:projectId) { throw "projecto nao aparece na listagem" }
    "projecto na listagem por omissao"
}

Test-Case "7. Acrescentar marco" {
    $body = @{ name = "Fundacoes prontas"; targetDate = "2026-09-15" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/milestones" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.milestoneId) { throw "sem milestoneId na resposta" }
    $script:milestoneId = $r.milestoneId
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $marco = $p.milestones | Where-Object { $_.milestoneId -eq $script:milestoneId }
    if (-not $marco) { throw "marco nao aparece no projecto" }
    if ($marco.status -ne "Pending") { throw "estado inicial devia ser Pending: $($marco.status)" }
    "marco acrescentado, Pending"
}

Test-Case "8. Marco com data anterior ao inicio do projecto e recusado" {
    $body = @{ name = "Marco impossivel"; targetDate = "2026-08-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/milestones" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "data anterior ao inicio recusada"
}

Test-Case "9. Alcancar marco" {
    $body = @{ reachedOn = "2026-09-16" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/milestones/$($script:milestoneId)/reached" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $marco = $p.milestones | Where-Object { $_.milestoneId -eq $script:milestoneId }
    if ($marco.status -ne "Reached") { throw "estado nao mudou para Reached: $($marco.status)" }
    if ($marco.reachedOn -notmatch "2026-09-16") { throw "reachedOn nao gravado: $($marco.reachedOn)" }
    "marco alcancado em 2026-09-16"
}

Test-Case "10. Alcancar o mesmo marco outra vez e recusado" {
    $body = @{ reachedOn = "2026-09-17" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/milestones/$($script:milestoneId)/reached" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 ao alcancar duas vezes"
}

Test-Case "11. Marco inexistente devolve 404" {
    $body = @{ reachedOn = "2026-09-17" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/milestones/$([guid]::NewGuid())/reached" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "marco inexistente: 404"
}

Test-Case "12. Acrescentar tarefa sem atribuicao" {
    $body = @{ title = "Pedir orcamento ao fornecedor"; dueDate = $null; assignedEmployeeId = $null } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.taskId) { throw "sem taskId na resposta" }
    $script:taskId = $r.taskId
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $tarefa = $p.tasks | Where-Object { $_.taskId -eq $script:taskId }
    if ($tarefa.status -ne "Pending") { throw "estado inicial devia ser Pending: $($tarefa.status)" }
    if ($tarefa.assignedEmployeeId) { throw "nao devia ter atribuicao" }
    "tarefa acrescentada, sem atribuicao"
}

Test-Case "13. Acrescentar tarefa atribuida a colaborador existente" {
    $body = @{ title = "Rever planta"; dueDate = "2026-09-20"; assignedEmployeeId = $colaborador } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:taskAtribuidaId = $r.taskId
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $tarefa = $p.tasks | Where-Object { $_.taskId -eq $script:taskAtribuidaId }
    if ($tarefa.assignedEmployeeId -ne $colaborador) { throw "atribuicao nao gravada" }
    "tarefa atribuida a colaborador existente"
}

Test-Case "14. Acrescentar tarefa atribuida a colaborador inexistente e recusado" {
    $body = @{ title = "Tarefa orfa"; dueDate = $null; assignedEmployeeId = [guid]::NewGuid() } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "colaborador inexistente: 404, sem copiar atributos que nao existem (BR-18)"
}

Test-Case "15. Atribuir tarefa a outro colaborador" {
    $body = @{ employeeId = $colaborador2 } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/tasks/$($script:taskAtribuidaId)/assignment" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $tarefa = $p.tasks | Where-Object { $_.taskId -eq $script:taskAtribuidaId }
    if ($tarefa.assignedEmployeeId -ne $colaborador2) { throw "reatribuicao nao gravada" }
    "reatribuida ao segundo colaborador"
}

Test-Case "16. Concluir tarefa" {
    Invoke-RestMethod "$base/projects/$($script:projectId)/tasks/$($script:taskId)/completion" -Method Post -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $tarefa = $p.tasks | Where-Object { $_.taskId -eq $script:taskId }
    if ($tarefa.status -ne "Done") { throw "estado nao mudou para Done: $($tarefa.status)" }
    "tarefa concluida"
}

Test-Case "17. Concluir a mesma tarefa outra vez e recusado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/tasks/$($script:taskId)/completion" -Method Post -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 ao concluir duas vezes"
}

Test-Case "18. Cancelar tarefa nunca elimina" {
    $body = @{ title = "Tarefa a cancelar"; dueDate = $null; assignedEmployeeId = $null } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $tarefaId = $r.taskId

    Invoke-RestMethod "$base/projects/$($script:projectId)/tasks/$tarefaId/cancellation" -Method Post -Headers $adminHeaders | Out-Null

    $existe = Invoke-Sql "select status from projects.project_task where id='$tarefaId'"
    if ($existe.Trim() -ne "Cancelled") { throw "estado na base de dados nao e Cancelled: '$existe'" }
    "tarefa cancelada; a linha continua na base de dados (BR-14)"
}

Test-Case "19. Marcos e tarefas ficam na trilha, com actor" {
    $acoes = @(
        "projects.milestone.added", "projects.milestone.reached",
        "projects.task.added", "projects.task.assigned",
        "projects.task.completed", "projects.task.cancelled")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "seis tipos de evento de marco/tarefa auditados, todos com actor"
}

Test-Case "20. Fechar com data anterior ao inicio e recusado" {
    $body = @{ endDate = "2026-08-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "data de fecho anterior ao inicio recusada"
}

Test-Case "21. Fechar projecto" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.status -ne "Closed") { throw "estado nao mudou para Closed: $($p.status)" }
    "projecto fechado"
}

Test-Case "22. Fechar outra vez e recusado (nao ha reabertura)" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo fecho"
}

Test-Case "23. Projecto fechado nao aceita marco nem tarefa novos" {
    $bodyMarco = @{ name = "Marco tardio"; targetDate = "2026-12-01" } | ConvertTo-Json
    $codeMarco = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/milestones" -Method Post -Body $bodyMarco -ContentType "application/json" -Headers $adminHeaders }
    if ($codeMarco -ne 400) { throw "marco em projecto fechado: esperado 400, obtido $codeMarco" }

    $bodyTarefa = @{ title = "Tarefa tardia"; dueDate = $null; assignedEmployeeId = $null } | ConvertTo-Json
    $codeTarefa = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $bodyTarefa -ContentType "application/json" -Headers $adminHeaders }
    if ($codeTarefa -ne 400) { throw "tarefa em projecto fechado: esperado 400, obtido $codeTarefa" }
    "projecto fechado e facto historico -- nem marco nem tarefa se acrescentam"
}

Test-Case "24. Projecto fechado sai da listagem por omissao, includeClosed traz de volta" {
    $activos = Invoke-RestMethod "$base/projects" -Headers $adminHeaders
    if ($activos.projectId -contains $script:projectId) { throw "projecto fechado ainda aparece nos activos" }

    $todos = Invoke-RestMethod "$base/projects?includeClosed=true" -Headers $adminHeaders
    if ($todos.projectId -notcontains $script:projectId) { throw "projecto fechado nao aparece com includeClosed" }
    "filtra por omissao; includeClosed mostra tudo"
}

Test-Case "25. Nao ha eliminacao de projecto" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from projects.project where id='$($script:projectId)'"
    if ($existe -ne "1") { throw "projecto desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "26. Abrir e fechar ficam na trilha, com actor" {
    $abrir = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.opened' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($abrir -ne "1") { throw "abertura nao auditada com actor" }
    $fechar = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.closed' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($fechar -ne "1") { throw "fecho nao auditado com actor" }
    "abertura e fecho na trilha, ambos com actor"
}

Test-Case "27. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "28. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.status -ne "Closed") { throw "estado perdido apos restart: $($p.status)" }
    $marco = $p.milestones | Where-Object { $_.milestoneId -eq $script:milestoneId }
    if ($marco.status -ne "Reached") { throw "estado do marco perdido apos restart: $($marco.status)" }
    $tarefa = $p.tasks | Where-Object { $_.taskId -eq $script:taskId }
    if ($tarefa.status -ne "Done") { throw "estado da tarefa perdido apos restart: $($tarefa.status)" }
    "projecto '$nome', marco e tarefa intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
