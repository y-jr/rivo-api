# Verificação do módulo `fleet`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-fleet.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Manutenção, Atribuição e Plano
# de Manutenção ganharam regra de negócio (ver `modules/fleet.md` §Possui e a
# nota "Estado"). Registo de Viagem, Despesa de Frota e Seguros desde
# 2026-08-31 — esta suite verifica o que existe, não o que falta.
#
# Re-executável: cada corrida usa uma matrícula própria, derivada do carimbo
# temporal.

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

$adminToken = Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]
$adminHeaders = @{ Authorization = "Bearer $adminToken" }

# Upload de documento (seguro), mesmo mecanismo de verify-documents.ps1 -- ver
# ali o porque da portabilidade Windows/Linux (curl.exe vs curl, NUL vs /dev/null).
$temp = [System.IO.Path]::GetTempPath()
$curl = if (Get-Command curl.exe -ErrorAction SilentlyContinue) { "curl.exe" } else { "curl" }
$tempFile = Join-Path $temp "rivo-seguro-$stamp.txt"
Set-Content -Path $tempFile -Value "Apolice de seguro de teste - $stamp" -NoNewline -Encoding UTF8

function Invoke-Upload {
    param([string]$FilePath, [string]$Category, [string]$Token)

    return (& $curl -s -X POST "$base/documents" `
        -H "Authorization: Bearer $Token" `
        -F "file=@$FilePath" `
        -F "category=$Category" 2>$null)
}

$semPerfilEmail = "semperfil-fl-$stamp@rivo.ao"
Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

$motorista = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Motorista FL $stamp" } | ConvertTo-Json)).employeeId
$motorista2 = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Segundo Motorista FL $stamp" } | ConvertTo-Json)).employeeId

$placa = "ld-$stamp-ao"

Write-Host "`n=== Modulo fleet ===`n"

Test-Case "1. Schema fleet com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from fleet.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de fleet" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='fleet'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='fleet' and table_name in ('app_user','audit_event','employee')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema fleet" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. AssetManager le e escreve fleet" {
    $r = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='fleet.vehicles.read'"
    $w = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='fleet.vehicles.write'"
    if ($r -ne "1" -or $w -ne "1") { throw "AssetManager sem fleet.vehicles.read/write (read=$r write=$w)" }
    "AssetManager le e escreve viaturas"
}

Test-Case "3. Registar viatura" {
    $body = @{ plateNumber = $placa; model = "Hilux" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.vehicleId) { throw "sem vehicleId na resposta" }
    $script:vehicleId = $r.vehicleId
    "viatura $placa registada"
}

Test-Case "4. Matricula normalizada em maiusculas; nasce sem manutencoes, atribuicoes nem planos" {
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.plateNumber -ne $placa.ToUpperInvariant()) { throw "matricula nao normalizada: '$($v.plateNumber)'" }
    if ($v.status -ne "Active") { throw "estado inesperado: $($v.status)" }
    if (@($v.maintenances).Count -ne 0) { throw "manutencoes deviam estar vazias" }
    if (@($v.assignments).Count -ne 0) { throw "atribuicoes deviam estar vazias" }
    if (@($v.plans).Count -ne 0) { throw "planos deviam estar vazios" }
    "matricula '$($v.plateNumber)', estado Active, sem manutencoes, atribuicoes nem planos"
}

Test-Case "5. Matricula duplicada devolve 409" {
    $body = @{ plateNumber = $placa; model = "Outro" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 na matricula repetida"
}

Test-Case "6. Abrir manutencao" {
    $body = @{ type = "Preventive"; description = "Revisao dos 20.000 km"; startedOn = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.maintenanceId) { throw "sem maintenanceId na resposta" }
    $script:maintenanceId = $r.maintenanceId
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "InMaintenance") { throw "estado nao mudou: $($v.status)" }
    "manutencao aberta, estado InMaintenance"
}

Test-Case "7. Tipo de manutencao invalido e recusado" {
    $body = @{ type = "Cosmetica"; description = "Polimento"; startedOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "tipo desconhecido recusado"
}

Test-Case "8. Viatura ja em manutencao nao abre outra" {
    $body = @{ type = "Corrective"; description = "Outra avaria"; startedOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- so uma manutencao aberta de cada vez"
}

Test-Case "9. Viatura em manutencao continua na listagem por omissao" {
    # So Inactive sai por omissao -- InMaintenance nao e a mesma coisa que
    # indisponivel para a listagem.
    $lista = Invoke-RestMethod "$base/fleet/vehicles" -Headers $adminHeaders
    if ($lista.vehicleId -notcontains $script:vehicleId) { throw "viatura em manutencao saiu da listagem" }
    "InMaintenance visivel na listagem por omissao"
}

Test-Case "10. Fechar manutencao, com custo (ADR-048)" {
    $body = @{ endedOn = "2026-09-03"; cost = 45000 } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($script:maintenanceId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Active") { throw "estado nao voltou a Active: $($v.status)" }
    $registo = $v.maintenances | Where-Object { $_.maintenanceId -eq $script:maintenanceId }
    if ($registo.endedOn -notmatch "2026-09-03") { throw "endedOn nao gravado: $($registo.endedOn)" }
    if ($registo.cost -ne 45000) { throw "custo nao gravado: $($registo.cost)" }
    "manutencao fechada, de volta a Active, custo=45000"
}

Test-Case "11. Fechar a mesma manutencao outra vez e recusado" {
    $body = @{ endedOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($script:maintenanceId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 ao fechar duas vezes"
}

Test-Case "12. Manutencao inexistente devolve 404" {
    $body = @{ endedOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$([guid]::NewGuid())/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "manutencao inexistente: 404"
}

Test-Case "13. Atribuir viatura a colaborador existente" {
    $body = @{ employeeId = $motorista; startedOn = "2026-09-05" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.assignmentId) { throw "sem assignmentId na resposta" }
    $script:assignmentId = $r.assignmentId
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $atribuicao = $v.assignments | Where-Object { $_.assignmentId -eq $script:assignmentId }
    if ($atribuicao.employeeId -ne $motorista) { throw "atribuicao nao gravada" }
    "viatura atribuida ao motorista"
}

Test-Case "14. Atribuir viatura a colaborador inexistente e recusado" {
    $body = @{ employeeId = [guid]::NewGuid(); startedOn = "2026-09-05" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "colaborador inexistente: 404, sem copiar atributos que nao existem (BR-18)"
}

Test-Case "15. Atribuir viatura ja atribuida e recusado" {
    $body = @{ employeeId = $motorista2; startedOn = "2026-09-06" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- so uma atribuicao aberta de cada vez"
}

Test-Case "16. Terminar atribuicao" {
    $body = @{ endedOn = "2026-09-10" } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments/$($script:assignmentId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $atribuicao = $v.assignments | Where-Object { $_.assignmentId -eq $script:assignmentId }
    if ($atribuicao.endedOn -notmatch "2026-09-10") { throw "endedOn nao gravado: $($atribuicao.endedOn)" }
    "atribuicao terminada"
}

Test-Case "17. Terminar a mesma atribuicao outra vez e recusado" {
    $body = @{ endedOn = "2026-09-11" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments/$($script:assignmentId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 ao terminar duas vezes"
}

Test-Case "18. Reatribuir a outro motorista depois de terminar" {
    $body = @{ employeeId = $motorista2; startedOn = "2026-09-11" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:segundaAtribuicaoId = $r.assignmentId
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if (@($v.assignments).Count -ne 2) { throw "esperavam-se 2 atribuicoes, ha $(@($v.assignments).Count)" }
    "segunda atribuicao aberta ao segundo motorista"
}

Test-Case "19. Manutencao e atribuicao ficam na trilha, com actor" {
    $acoes = @(
        "fleet.maintenance.opened", "fleet.maintenance.closed",
        "fleet.assignment.opened", "fleet.assignment.ended")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "quatro tipos de evento de manutencao/atribuicao auditados, todos com actor"
}

Test-Case "20. Agendar plano de manutencao" {
    $body = @{ description = "Mudanca de oleo"; intervalDays = 90; firstDueOn = "2026-08-25" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.planId) { throw "sem planId na resposta" }
    $script:planOleoId = $r.planId
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $plano = $v.plans | Where-Object { $_.planId -eq $script:planOleoId }
    if (-not $plano.isActive) { throw "plano devia nascer activo" }
    if (-not $plano.isOverdue) { throw "plano com data devida no passado devia estar atrasado" }
    "plano agendado, atrasado (data devida 2026-08-25)"
}

Test-Case "21. Plano com intervalo nao positivo e recusado" {
    $body = @{ description = "Invalido"; intervalDays = 0; firstDueOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "intervalo zero recusado"
}

Test-Case "22. Plano sem descricao e recusado" {
    $body = @{ description = ""; intervalDays = 90; firstDueOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "sem descricao recusado"
}

Test-Case "23. Varios planos activos na mesma viatura sao normais" {
    $body = @{ description = "Rodagem dos pneus"; intervalDays = 180; firstDueOn = "2027-01-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:planPneusId = $r.planId
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if (@($v.plans).Count -ne 2) { throw "esperavam-se 2 planos, ha $(@($v.plans).Count)" }
    "segundo plano aberto -- sem exclusao mutua"
}

Test-Case "24. Concluir ciclo do plano reagenda a partir da data de conclusao" {
    $body = @{ completedOn = "2026-08-30" } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans/$($script:planOleoId)/cycles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $plano = $v.plans | Where-Object { $_.planId -eq $script:planOleoId }
    if ($plano.nextDueOn -notmatch "2026-11-28") { throw "nextDueOn esperado 2026-11-28 (2026-08-30 + 90 dias), obtido $($plano.nextDueOn)" }
    if ($plano.isOverdue) { throw "plano nao devia estar atrasado depois de reagendado para o futuro" }
    "reagendado para 2026-11-28, ja nao atrasado"
}

Test-Case "25. Concluir ciclo de plano inexistente devolve 404" {
    $body = @{ completedOn = "2026-08-30" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans/$([guid]::NewGuid())/cycles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "plano inexistente: 404"
}

Test-Case "26. Cancelar plano" {
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans/$($script:planPneusId)/cancellation" -Method Post -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $plano = $v.plans | Where-Object { $_.planId -eq $script:planPneusId }
    if ($plano.isActive) { throw "plano devia estar cancelado" }
    "plano cancelado"
}

Test-Case "27. Cancelar o mesmo plano outra vez e recusado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans/$($script:planPneusId)/cancellation" -Method Post -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 ao cancelar duas vezes"
}

Test-Case "28. Agendar plano atrasado para alimentar o alerta" {
    $body = @{ description = "Filtro de ar"; intervalDays = 30; firstDueOn = "2026-08-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:planFiltroId = $r.planId
    "plano do filtro de ar agendado, atrasado desde 2026-08-01"
}

Test-Case "29. Alerta -- viaturas com plano devido aparecem, as outras nao" {
    $devidos = Invoke-RestMethod "$base/fleet/maintenance-plans/due?withinDays=0" -Headers $adminHeaders
    if ($devidos.planId -notcontains $script:planFiltroId) { throw "plano atrasado nao aparece no alerta" }
    if (-not (($devidos | Where-Object { $_.planId -eq $script:planFiltroId }).isOverdue)) { throw "plano do alerta devia estar marcado atrasado" }
    if ($devidos.planId -contains $script:planOleoId) { throw "plano reagendado para o futuro nao devia aparecer no alerta" }
    if ($devidos.planId -contains $script:planPneusId) { throw "plano cancelado nao devia aparecer no alerta" }
    "so o plano atrasado e activo aparece no alerta"
}

Test-Case "30. Planos ficam na trilha, com actor" {
    $acoes = @(
        "fleet.maintenance_plan.scheduled", "fleet.maintenance_plan.cycle_completed",
        "fleet.maintenance_plan.cancelled")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "tres tipos de evento de plano auditados, todos com actor"
}

# --- Registo de Viagem ------------------------------------------------------

Test-Case "31. Registar viagem com motorista" {
    $body = @{ driverId = $motorista; startedOn = "2026-09-12"; endedOn = "2026-09-12"; startOdometer = 10000; endOdometer = 10120; purpose = "Entrega em Viana" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.tripId) { throw "sem tripId na resposta" }
    if ([decimal]$r.distance -ne 120) { throw "distancia esperada 120, obtida $($r.distance)" }
    "viagem de 120 km, com motorista"
}

Test-Case "32. Registar viagem sem motorista (opcional)" {
    $body = @{ startedOn = "2026-09-13"; endedOn = "2026-09-13"; startOdometer = 10120; endOdometer = 10200 } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.distance -ne 80) { throw "distancia esperada 80, obtida $($r.distance)" }
    "viagem de 80 km, sem motorista"
}

Test-Case "33. Viagem com motorista inexistente devolve 404" {
    $body = @{ driverId = [guid]::NewGuid(); startedOn = "2026-09-13"; endedOn = "2026-09-13"; startOdometer = 10200; endOdometer = 10210 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "motorista inexistente: 404"
}

Test-Case "34. Viagem com data de fim anterior ao inicio e recusada" {
    $body = @{ startedOn = "2026-09-13"; endedOn = "2026-09-12"; startOdometer = 10200; endOdometer = 10210 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "data de fim anterior ao inicio recusada"
}

Test-Case "35. Viagem com odometro final menor que o inicial e recusada" {
    $body = @{ startedOn = "2026-09-13"; endedOn = "2026-09-13"; startOdometer = 10200; endOdometer = 10100 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "odometro final menor que o inicial recusado"
}

Test-Case "36. Viagens ficam na trilha, com actor" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.trip.registered' and actor_id is not null"
    if ([int]$n -lt 2) { throw "esperavam-se pelo menos 2 viagens auditadas, obtidas $n" }
    "viagens auditadas, com actor"
}

# --- Despesa de Frota --------------------------------------------------------

Test-Case "37. Registar despesa de combustivel" {
    $body = @{ category = "Fuel"; amount = 15000; occurredOn = "2026-09-12"; description = "Posto Sonangol" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/expenses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.expenseId) { throw "sem expenseId na resposta" }
    "despesa de combustivel registada"
}

Test-Case "38. Despesa com categoria desconhecida e recusada" {
    $body = @{ category = "Multa"; amount = 5000; occurredOn = "2026-09-12" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/expenses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "categoria desconhecida recusada -- so Fuel, Toll ou Parking"
}

Test-Case "39. Despesa com valor nao positivo e recusada" {
    $body = @{ category = "Toll"; amount = 0; occurredOn = "2026-09-12" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/expenses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "valor zero recusado"
}

Test-Case "40. Despesas ficam na trilha, com actor" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.expense.registered' and actor_id is not null"
    if ([int]$n -lt 1) { throw "sem evento auditado para despesa" }
    "despesa auditada, com actor"
}

# --- Seguros e documentacao legal -------------------------------------------

Test-Case "41. Anexar documento (seguro) a viatura, listagem mostra metadados" {
    $upload = Invoke-Upload $tempFile "seguro" $adminToken | ConvertFrom-Json
    $script:seguroDocumentId = $upload.documentId
    if (-not $script:seguroDocumentId) { throw "upload sem documentId" }

    $body = @{ documentId = $script:seguroDocumentId; category = "seguro" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/documents" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.linkId) { throw "sem linkId na resposta" }

    $lista = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/documents" -Headers $adminHeaders
    $ligacao = $lista | Where-Object { $_.documentId -eq $script:seguroDocumentId }
    if (-not $ligacao) { throw "documento nao aparece na listagem" }
    if ($ligacao.category -ne "seguro") { throw "categoria nao gravada: $($ligacao.category)" }
    if (-not $ligacao.fileName) { throw "metadados de documents em falta (fileName)" }
    "documento anexado, categoria 'seguro', metadados de documents presentes"
}

Test-Case "42. Anexar documento inexistente devolve 404" {
    $body = @{ documentId = [guid]::NewGuid(); category = "seguro" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/documents" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "documento inexistente -- 404, verificado pelo contrato publicado de documents"
}

Test-Case "43. Fechar manutencao sem custo e aceite; custo negativo e recusado (ADR-048)" {
    # abre e fecha uma segunda manutencao sem indicar custo -- fica nulo,
    # nao zero.
    $bodyAbrir = @{ type = "Corrective"; description = "Troca de pastilhas"; startedOn = "2026-09-05" } | ConvertTo-Json
    $aberta = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $bodyAbrir -ContentType "application/json" -Headers $adminHeaders
    $bodyFechar = @{ endedOn = "2026-09-06" } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($aberta.maintenanceId)/closure" -Method Post -Body $bodyFechar -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $registoSemCusto = $v.maintenances | Where-Object { $_.maintenanceId -eq $aberta.maintenanceId }
    if ($null -ne $registoSemCusto.cost) { throw "custo devia ficar nulo, veio $($registoSemCusto.cost)" }

    # abre uma terceira -- custo negativo e recusado (409), e a manutencao
    # continua aberta; fecha-a a seguir com um custo valido, para a viatura
    # voltar a Active antes do caso seguinte a desactivar.
    $bodyAbrir2 = @{ type = "Preventive"; description = "Revisao"; startedOn = "2026-09-07" } | ConvertTo-Json
    $aberta2 = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $bodyAbrir2 -ContentType "application/json" -Headers $adminHeaders

    $bodyNegativo = @{ endedOn = "2026-09-08"; cost = -100 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($aberta2.maintenanceId)/closure" -Method Post -Body $bodyNegativo -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "custo negativo devia ser recusado (409), obtido $code" }

    $bodyValido = @{ endedOn = "2026-09-08"; cost = 12000 } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($aberta2.maintenanceId)/closure" -Method Post -Body $bodyValido -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "sem custo -> null; custo negativo -> 409; corrigido e fechado com custo valido"
}

Test-Case "44. Desactivar esconde da listagem, includeInactive traz de volta" {
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/deactivation" -Method Post -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/fleet/vehicles" -Headers $adminHeaders
    if ($activos.vehicleId -contains $script:vehicleId) { throw "viatura desactivada ainda aparece na listagem por omissao" }

    $todas = Invoke-RestMethod "$base/fleet/vehicles?includeInactive=true" -Headers $adminHeaders
    if ($todas.vehicleId -notcontains $script:vehicleId) { throw "viatura desactivada nao aparece com includeInactive" }
    "Inactive sai da listagem por omissao; InMaintenance nao saia (caso 9)"
}

Test-Case "45. Viatura inactiva nao aceita manutencao, atribuicao, plano, viagem nem despesa novos" {
    # Conflito com o estado da viatura, nao pedido malformado -- 409, nao 400.
    $bodyManut = @{ type = "Preventive"; description = "Revisao tardia"; startedOn = "2026-09-12" } | ConvertTo-Json
    $codeManut = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $bodyManut -ContentType "application/json" -Headers $adminHeaders }
    if ($codeManut -ne 409) { throw "manutencao em viatura inactiva: esperado 409, obtido $codeManut" }

    $bodyAtrib = @{ employeeId = $motorista; startedOn = "2026-09-12" } | ConvertTo-Json
    $codeAtrib = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $bodyAtrib -ContentType "application/json" -Headers $adminHeaders }
    if ($codeAtrib -ne 409) { throw "atribuicao em viatura inactiva: esperado 409, obtido $codeAtrib" }

    $bodyPlano = @{ description = "Tardio"; intervalDays = 30; firstDueOn = "2026-09-12" } | ConvertTo-Json
    $codePlano = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans" -Method Post -Body $bodyPlano -ContentType "application/json" -Headers $adminHeaders }
    if ($codePlano -ne 409) { throw "plano em viatura inactiva: esperado 409, obtido $codePlano" }

    $bodyViagem = @{ startedOn = "2026-09-14"; endedOn = "2026-09-14"; startOdometer = 10200; endOdometer = 10210 } | ConvertTo-Json
    $codeViagem = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/trips" -Method Post -Body $bodyViagem -ContentType "application/json" -Headers $adminHeaders }
    if ($codeViagem -ne 409) { throw "viagem em viatura inactiva: esperado 409, obtido $codeViagem" }

    $bodyDespesa = @{ category = "Parking"; amount = 500; occurredOn = "2026-09-14" } | ConvertTo-Json
    $codeDespesa = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/expenses" -Method Post -Body $bodyDespesa -ContentType "application/json" -Headers $adminHeaders }
    if ($codeDespesa -ne 409) { throw "despesa em viatura inactiva: esperado 409, obtido $codeDespesa" }

    "viatura inactiva recusa nova manutencao, nova atribuicao, novo plano, nova viagem e nova despesa"
}

Test-Case "46. Cancelar plano de viatura inactiva continua permitido" {
    # Cancelar planos de uma viatura que acabou de ficar inactiva e o que se
    # espera -- nao ha guarda de Status aqui, ao contrario dos outros tres.
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance-plans/$($script:planFiltroId)/cancellation" -Method Post -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    $plano = $v.plans | Where-Object { $_.planId -eq $script:planFiltroId }
    if ($plano.isActive) { throw "plano devia estar cancelado" }
    "cancelamento permitido mesmo com a viatura inactiva"
}

Test-Case "47. Nao ha eliminacao de viatura" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from fleet.vehicle where id='$($script:vehicleId)'"
    if ($existe -ne "1") { throw "viatura desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "48. Registo e desactivacao ficam na trilha, com actor" {
    $reg = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.registered' and entity_id='$($script:vehicleId)' and actor_id is not null"
    if ($reg -ne "1") { throw "registo nao auditado com actor" }
    $desact = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.deactivated' and entity_id='$($script:vehicleId)' and actor_id is not null"
    if ($desact -ne "1") { throw "desactivacao nao auditada com actor" }
    "registo e desactivacao na trilha, ambos com actor"
}

Test-Case "49. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "50. Matricula e unica na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select plate_number from fleet.vehicle group by plate_number having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup matriculas repetidas" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "51. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Inactive") { throw "estado perdido apos restart: $($v.status)" }
    $registo = $v.maintenances | Where-Object { $_.maintenanceId -eq $script:maintenanceId }
    if ($registo.endedOn -notmatch "2026-09-03") { throw "manutencao perdida apos restart" }
    if ($registo.cost -ne 45000) { throw "custo de manutencao perdido apos restart: $($registo.cost)" }
    $segunda = $v.assignments | Where-Object { $_.assignmentId -eq $script:segundaAtribuicaoId }
    if (-not $segunda -or $segunda.endedOn) { throw "segunda atribuicao (ainda aberta) perdida apos restart" }
    $oleo = $v.plans | Where-Object { $_.planId -eq $script:planOleoId }
    if ($oleo.nextDueOn -notmatch "2026-11-28") { throw "plano de oleo perdido apos restart" }
    if (@($v.trips).Count -lt 2) { throw "viagens perdidas apos restart" }
    if (@($v.expenses).Count -lt 1) { throw "despesas perdidas apos restart" }

    $documentos = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/documents" -Headers $adminHeaders
    if ($documentos.documentId -notcontains $script:seguroDocumentId) { throw "documento de seguro perdido apos restart" }

    "viatura $placa, manutencao, atribuicoes, planos, viagens, despesas e documentos intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
