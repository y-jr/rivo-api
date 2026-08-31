# Verificação do módulo `projects`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-projects.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Marco, Tarefa e Orçamento
# ganharam regra de negócio (ver `modules/projects.md` §Possui e a nota
# "Estado"). Alocação de Recursos (Colaborador via `hr`, Viatura via
# `fleet`) desde 2026-08-31 — ver os casos 24-33. Custos ficam de fora de
# propósito (postagem em `finance` é decisão em aberto).
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

Test-Case "19. Definir orcamento" {
    $body = @{ amount = 500000; currency = "aoa" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if (-not $p.budget) { throw "orcamento nao aparece no projecto" }
    if ([decimal]$p.budget.amount -ne 500000) { throw "amount esperado 500000, obtido $($p.budget.amount)" }
    if ($p.budget.currency -ne "AOA") { throw "moeda nao normalizada: $($p.budget.currency)" }
    "orcamento definido: 500000 AOA"
}

Test-Case "20. Orcamento com valor nao positivo e recusado" {
    $body = @{ amount = 0; currency = "AOA" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "valor zero recusado"
}

Test-Case "21. Orcamento com moeda invalida e recusado" {
    $body = @{ amount = 1000; currency = "KWANZA" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "moeda malformada recusada"
}

Test-Case "22. Rever orcamento mantendo a moeda" {
    $body = @{ amount = 650000; currency = "AOA" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ([decimal]$p.budget.amount -ne 650000) { throw "amount esperado 650000, obtido $($p.budget.amount)" }
    "orcamento revisto para 650000 AOA"
}

Test-Case "23. Rever orcamento com moeda diferente e recusado" {
    $body = @{ amount = 1000; currency = "USD" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.budget.currency -ne "AOA") { throw "moeda mudou apesar da recusa: $($p.budget.currency)" }
    if ([decimal]$p.budget.amount -ne 650000) { throw "amount mudou apesar da recusa: $($p.budget.amount)" }
    "409 -- a moeda fixa-se na primeira vez"
}

Test-Case "24. Alocar colaborador ao projecto" {
    $body = @{ kind = 0; resourceId = $colaborador; startsOn = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.allocationId) { throw "sem allocationId na resposta" }
    $script:alocacaoColaborador = $r.allocationId

    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $alocacao = $p.allocations | Where-Object { $_.allocationId -eq $script:alocacaoColaborador }
    if ($alocacao.kind -ne "Employee") { throw "kind errado: $($alocacao.kind)" }
    if ($alocacao.resourceId -ne $colaborador) { throw "resourceId errado" }
    if ($null -ne $alocacao.endsOn) { throw "endsOn deveria ser nulo, veio $($alocacao.endsOn)" }
    "alocacao $($script:alocacaoColaborador), Employee, aberta"
}

Test-Case "25. Alocar colaborador com data anterior ao inicio do projecto e recusado" {
    $body = @{ kind = 0; resourceId = $colaborador2; startsOn = "2026-08-31" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "data anterior ao inicio do projecto -- 400"
}

Test-Case "26. Alocar o mesmo colaborador outra vez, ainda aberto, e recusado" {
    $body = @{ kind = 0; resourceId = $colaborador; startsOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- o mesmo recurso nao se aloca duas vezes em aberto"
}

Test-Case "27. Alocar colaborador inexistente devolve 404" {
    $body = @{ kind = 0; resourceId = [Guid]::NewGuid().ToString(); startsOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "colaborador inexistente -- 404, verificado pelo contrato publicado de hr"
}

Test-Case "28. Registar viatura em fleet e aloca-la ao projecto" {
    $placa = "PR-$stamp"
    $bodyViatura = @{ plateNumber = $placa; model = "Hilux" } | ConvertTo-Json
    $viatura = Invoke-RestMethod "$base/fleet/vehicles" -Method Post -Body $bodyViatura -ContentType "application/json" -Headers $adminHeaders
    $script:vehicleId = $viatura.vehicleId
    if (-not $script:vehicleId) { throw "sem vehicleId na resposta de fleet" }

    $body = @{ kind = 1; resourceId = $script:vehicleId; startsOn = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:alocacaoViatura = $r.allocationId

    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $alocacao = $p.allocations | Where-Object { $_.allocationId -eq $script:alocacaoViatura }
    if ($alocacao.kind -ne "Vehicle") { throw "kind errado: $($alocacao.kind)" }
    "viatura $placa alocada, alocacao $($script:alocacaoViatura)"
}

Test-Case "29. Alocar viatura inexistente devolve 404" {
    $body = @{ kind = 1; resourceId = [Guid]::NewGuid().ToString(); startsOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "viatura inexistente -- 404, verificado pelo contrato publicado de fleet"
}

Test-Case "30. Terminar alocacao" {
    $body = @{ endsOn = "2026-09-15" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/allocations/$($script:alocacaoColaborador)/end" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    $alocacao = $p.allocations | Where-Object { $_.allocationId -eq $script:alocacaoColaborador }
    if ($alocacao.endsOn -ne "2026-09-15") { throw "endsOn nao gravado: $($alocacao.endsOn)" }
    "alocacao terminada em 2026-09-15"
}

Test-Case "31. Terminar a mesma alocacao outra vez e recusado" {
    $body = @{ endsOn = "2026-09-20" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations/$($script:alocacaoColaborador)/end" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- ja tinha terminado"
}

Test-Case "32. Terminar alocacao com data anterior ao inicio e recusado" {
    $body = @{ endsOn = "2026-07-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations/$($script:alocacaoViatura)/end" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "data de fim anterior ao inicio -- 400"
}

Test-Case "33. Alocacoes ficam na trilha, com actor" {
    # So as duas com sucesso (colaborador e viatura) auditam -- as
    # tentativas recusadas nos casos 25/26/27/29 nao tem efeito, e por isso
    # nao deixam registo.
    $alocado = Invoke-Sql "select count(*) from audit.audit_event where action='projects.resource_allocation.allocated' and actor_id is not null"
    if ([int]$alocado -lt 2) { throw "esperados >=2 registos de alocacao, obtidos $alocado" }
    $terminado = Invoke-Sql "select count(*) from audit.audit_event where action='projects.resource_allocation.ended' and actor_id is not null"
    if ([int]$terminado -lt 1) { throw "sem registo de fim de alocacao" }
    "alocar e terminar auditados, ambos com actor"
}

Test-Case "34. Marcos, tarefas e orcamento ficam na trilha, com actor" {
    $acoes = @(
        "projects.milestone.added", "projects.milestone.reached",
        "projects.task.added", "projects.task.assigned",
        "projects.task.completed", "projects.task.cancelled",
        "projects.budget.set")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "sete tipos de evento de marco/tarefa/orcamento auditados, todos com actor"
}

Test-Case "35. Fechar com data anterior ao inicio e recusado" {
    $body = @{ endDate = "2026-08-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "data de fecho anterior ao inicio recusada"
}

Test-Case "36. Fechar projecto" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.status -ne "Closed") { throw "estado nao mudou para Closed: $($p.status)" }
    "projecto fechado"
}

Test-Case "37. Fechar outra vez e recusado (nao ha reabertura)" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo fecho"
}

Test-Case "38. Projecto fechado nao aceita marco, tarefa, orcamento nem alocacao novos" {
    # Conflito com o estado do projecto, nao pedido malformado -- 409, nao 400.
    $bodyMarco = @{ name = "Marco tardio"; targetDate = "2026-12-01" } | ConvertTo-Json
    $codeMarco = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/milestones" -Method Post -Body $bodyMarco -ContentType "application/json" -Headers $adminHeaders }
    if ($codeMarco -ne 409) { throw "marco em projecto fechado: esperado 409, obtido $codeMarco" }

    $bodyTarefa = @{ title = "Tarefa tardia"; dueDate = $null; assignedEmployeeId = $null } | ConvertTo-Json
    $codeTarefa = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/tasks" -Method Post -Body $bodyTarefa -ContentType "application/json" -Headers $adminHeaders }
    if ($codeTarefa -ne 409) { throw "tarefa em projecto fechado: esperado 409, obtido $codeTarefa" }

    $bodyOrcamento = @{ amount = 1000; currency = "AOA" } | ConvertTo-Json
    $codeOrcamento = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/budget" -Method Post -Body $bodyOrcamento -ContentType "application/json" -Headers $adminHeaders }
    if ($codeOrcamento -ne 409) { throw "orcamento em projecto fechado: esperado 409, obtido $codeOrcamento" }

    $bodyAlocacao = @{ kind = 0; resourceId = $colaborador2; startsOn = "2026-09-01" } | ConvertTo-Json
    $codeAlocacao = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/allocations" -Method Post -Body $bodyAlocacao -ContentType "application/json" -Headers $adminHeaders }
    if ($codeAlocacao -ne 409) { throw "alocacao em projecto fechado: esperado 409, obtido $codeAlocacao" }

    "projecto fechado e facto historico -- nem marco, nem tarefa, nem orcamento, nem alocacao se alteram"
}

Test-Case "39. Projecto fechado sai da listagem por omissao, includeClosed traz de volta" {
    $activos = Invoke-RestMethod "$base/projects" -Headers $adminHeaders
    if ($activos.projectId -contains $script:projectId) { throw "projecto fechado ainda aparece nos activos" }

    $todos = Invoke-RestMethod "$base/projects?includeClosed=true" -Headers $adminHeaders
    if ($todos.projectId -notcontains $script:projectId) { throw "projecto fechado nao aparece com includeClosed" }
    "filtra por omissao; includeClosed mostra tudo"
}

Test-Case "40. Nao ha eliminacao de projecto" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from projects.project where id='$($script:projectId)'"
    if ($existe -ne "1") { throw "projecto desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "41. Abrir e fechar ficam na trilha, com actor" {
    $abrir = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.opened' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($abrir -ne "1") { throw "abertura nao auditada com actor" }
    $fechar = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.closed' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($fechar -ne "1") { throw "fecho nao auditado com actor" }
    "abertura e fecho na trilha, ambos com actor"
}

Test-Case "42. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "43. Dados sobrevivem ao reinicio da stack" {
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
    if ([decimal]$p.budget.amount -ne 650000) { throw "orcamento perdido apos restart: $($p.budget.amount)" }
    $alocacaoColaborador = $p.allocations | Where-Object { $_.allocationId -eq $script:alocacaoColaborador }
    if ($alocacaoColaborador.endsOn -ne "2026-09-15") { throw "fim da alocacao perdido apos restart: $($alocacaoColaborador.endsOn)" }
    $alocacaoViatura = $p.allocations | Where-Object { $_.allocationId -eq $script:alocacaoViatura }
    if ($alocacaoViatura.kind -ne "Vehicle") { throw "alocacao de viatura perdida apos restart" }
    "projecto '$nome', marco, tarefa, orcamento e alocacoes intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
