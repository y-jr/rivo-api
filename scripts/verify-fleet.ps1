# Verificação do módulo `fleet`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-fleet.ps1
#
# Esqueleto — 2026-08-29. Catálogo de viaturas: sem Manutenção (a tabela),
# Plano de Manutenção, Atribuição, Registo de Viagem, Despesa de Frota,
# Seguros. Esta suite verifica o que existe, e não o que `modules/fleet.md`
# descreve como por fazer.
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

$placa = "ld-$stamp-ao"

Write-Host "`n=== Modulo fleet (esqueleto) ===`n"

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

Test-Case "4. Matricula e normalizada em maiusculas" {
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.plateNumber -ne $placa.ToUpperInvariant()) { throw "matricula nao normalizada: '$($v.plateNumber)'" }
    if ($v.status -ne "Active") { throw "estado inesperado: $($v.status)" }
    "matricula '$($v.plateNumber)', estado Active"
}

Test-Case "5. Matricula duplicada devolve 409" {
    $body = @{ plateNumber = $placa; model = "Outro" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 na matricula repetida"
}

Test-Case "6. Enviar para manutencao" {
    $body = @{ inMaintenance = $true } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "InMaintenance") { throw "estado nao mudou: $($v.status)" }
    "estado InMaintenance"
}

Test-Case "7. Viatura em manutencao continua na listagem por omissao" {
    # So Inactive sai por omissao -- InMaintenance nao e a mesma coisa que
    # indisponivel para a listagem, e o esqueleto nao distingue os dois.
    $lista = Invoke-RestMethod "$base/fleet/vehicles" -Headers $adminHeaders
    if ($lista.vehicleId -notcontains $script:vehicleId) { throw "viatura em manutencao saiu da listagem" }
    "InMaintenance visivel na listagem por omissao"
}

Test-Case "8. Devolver da manutencao" {
    $body = @{ inMaintenance = $false } | ConvertTo-Json
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Active") { throw "estado nao voltou a Active: $($v.status)" }
    "de volta a Active"
}

Test-Case "9. Devolver da manutencao uma viatura que nao esta em manutencao e recusado" {
    $body = @{ inMaintenance = $false } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/maintenance" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- ja estava Active"
}

Test-Case "10. Desactivar esconde da listagem, includeInactive traz de volta" {
    Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)/deactivation" -Method Post -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/fleet/vehicles" -Headers $adminHeaders
    if ($activos.vehicleId -contains $script:vehicleId) { throw "viatura desactivada ainda aparece na listagem por omissao" }

    $todas = Invoke-RestMethod "$base/fleet/vehicles?includeInactive=true" -Headers $adminHeaders
    if ($todas.vehicleId -notcontains $script:vehicleId) { throw "viatura desactivada nao aparece com includeInactive" }
    "Inactive sai da listagem por omissao; InMaintenance nao saia (caso 7)"
}

Test-Case "11. Nao ha eliminacao de viatura" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from fleet.vehicle where id='$($script:vehicleId)'"
    if ($existe -ne "1") { throw "viatura desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "12. Registo, manutencao e desactivacao ficam na trilha, com actor" {
    $reg = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.registered' and entity_id='$($script:vehicleId)' and actor_id is not null"
    if ($reg -ne "1") { throw "registo nao auditado com actor" }
    $manut = Invoke-Sql "select count(*) from audit.audit_event where action in ('fleet.vehicle.sent_to_maintenance','fleet.vehicle.returned_from_maintenance') and entity_id='$($script:vehicleId)'"
    if ([int]$manut -lt 2) { throw "manutencao nao totalmente auditada: $manut eventos" }
    $desact = Invoke-Sql "select count(*) from audit.audit_event where action='fleet.vehicle.deactivated' and entity_id='$($script:vehicleId)'"
    if ($desact -ne "1") { throw "desactivacao nao auditada" }
    "registo, 2 eventos de manutencao e desactivacao na trilha"
}

Test-Case "13. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/fleet/vehicles" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "14. Matricula e unica na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select plate_number from fleet.vehicle group by plate_number having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup matriculas repetidas" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "15. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $v = Invoke-RestMethod "$base/fleet/vehicles/$($script:vehicleId)" -Headers $adminHeaders
    if ($v.status -ne "Inactive") { throw "estado perdido apos restart: $($v.status)" }
    "viatura $placa intacta apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
