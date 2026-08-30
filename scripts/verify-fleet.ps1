# Verificação do módulo `fleet`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-fleet.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Manutenção e Atribuição ganharam
# regra de negócio (ver `modules/fleet.md` §Possui e a nota "Estado"). Plano
# de Manutenção, Registo de Viagem, Despesa de Frota e Seguros continuam por
# fazer — esta suite verifica o que existe, não o que falta.
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

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

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

Test-Case "4. Matricula normalizada em maiusculas; nasce sem manutencoes nem atribuicoes" {
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.plateNumber -ne $placa.ToUpperInvariant()) { throw "matricula nao normalizada: '$($v.plateNumber)'" }
    if ($v.status -ne "Active") { throw "estado inesperado: $($v.status)" }
    if (@($v.maintenances).Count -ne 0) { throw "manutencoes deviam estar vazias" }
    if (@($v.assignments).Count -ne 0) { throw "atribuicoes deviam estar vazias" }
    "matricula '$($v.plateNumber)', estado Active, sem manutencoes nem atribuicoes"
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

Test-Case "10. Fechar manutencao" {
    $body = @{ endedOn = "2026-09-03" } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance/$($script:maintenanceId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Active") { throw "estado nao voltou a Active: $($v.status)" }
    $registo = $v.maintenances | Where-Object { $_.maintenanceId -eq $script:maintenanceId }
    if ($registo.endedOn -notmatch "2026-09-03") { throw "endedOn nao gravado: $($registo.endedOn)" }
    "manutencao fechada, de volta a Active"
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

Test-Case "20. Desactivar esconde da listagem, includeInactive traz de volta" {
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/deactivation" -Method Post -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/fleet/vehicles" -Headers $adminHeaders
    if ($activos.vehicleId -contains $script:vehicleId) { throw "viatura desactivada ainda aparece na listagem por omissao" }

    $todas = Invoke-RestMethod "$base/fleet/vehicles?includeInactive=true" -Headers $adminHeaders
    if ($todas.vehicleId -notcontains $script:vehicleId) { throw "viatura desactivada nao aparece com includeInactive" }
    "Inactive sai da listagem por omissao; InMaintenance nao saia (caso 9)"
}

Test-Case "21. Viatura inactiva nao aceita manutencao nem atribuicao novas" {
    # Conflito com o estado da viatura, nao pedido malformado -- 409, nao 400.
    $bodyManut = @{ type = "Preventive"; description = "Revisao tardia"; startedOn = "2026-09-12" } | ConvertTo-Json
    $codeManut = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $bodyManut -ContentType "application/json" -Headers $adminHeaders }
    if ($codeManut -ne 409) { throw "manutencao em viatura inactiva: esperado 409, obtido $codeManut" }

    $bodyAtrib = @{ employeeId = $motorista; startedOn = "2026-09-12" } | ConvertTo-Json
    $codeAtrib = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/assignments" -Method Post -Body $bodyAtrib -ContentType "application/json" -Headers $adminHeaders }
    if ($codeAtrib -ne 409) { throw "atribuicao em viatura inactiva: esperado 409, obtido $codeAtrib" }
    "viatura inactiva recusa nova manutencao e nova atribuicao"
}

Test-Case "22. Nao ha eliminacao de viatura" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from fleet.vehicle where id='$($script:vehicleId)'"
    if ($existe -ne "1") { throw "viatura desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "23. Registo e desactivacao ficam na trilha, com actor" {
    $reg = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.registered' and entity_id='$($script:vehicleId)' and actor_id is not null"
    if ($reg -ne "1") { throw "registo nao auditado com actor" }
    $desact = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.deactivated' and entity_id='$($script:vehicleId)' and actor_id is not null"
    if ($desact -ne "1") { throw "desactivacao nao auditada com actor" }
    "registo e desactivacao na trilha, ambos com actor"
}

Test-Case "24. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "25. Matricula e unica na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select plate_number from fleet.vehicle group by plate_number having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup matriculas repetidas" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "26. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Inactive") { throw "estado perdido apos restart: $($v.status)" }
    $registo = $v.maintenances | Where-Object { $_.maintenanceId -eq $script:maintenanceId }
    if ($registo.endedOn -notmatch "2026-09-03") { throw "manutencao perdida apos restart" }
    $segunda = $v.assignments | Where-Object { $_.assignmentId -eq $script:segundaAtribuicaoId }
    if (-not $segunda -or $segunda.endedOn) { throw "segunda atribuicao (ainda aberta) perdida apos restart" }
    "viatura $placa, manutencao e atribuicoes intactas apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
