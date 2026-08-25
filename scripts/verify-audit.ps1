# Verificação do módulo `audit` e do registo de acções por `identity`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-audit.ps1

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

function Invoke-Sql {
    param([string]$Query)
    return (Invoke-RivoSql $Query)
}

$dotenv = Get-RivoCredentials
$adminEmail = $dotenv["BOOTSTRAP_ADMIN_EMAIL"]
$adminPass = $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

Write-Host "`n=== Modulo audit ===`n"

Test-Case "1. Schema audit criado com migration propria" {
    $migrations = Invoke-Sql "select count(*) from audit.__ef_migrations_history"
    if ([int]$migrations -lt 1) { throw "nenhuma migration de audit" }
    $table = Invoke-Sql "select count(*) from information_schema.tables where table_schema='audit' and table_name='audit_event'"
    if ($table -ne "1") { throw "tabela audit_event nao existe" }
    "schema audit com $migrations migration(s), tabela audit_event"
}

Test-Case "2. Schemas isolados por modulo" {
    $identityInAudit = Invoke-Sql "select count(*) from information_schema.tables where table_schema='audit' and table_name like 'app_%'"
    $auditInIdentity = Invoke-Sql "select count(*) from information_schema.tables where table_schema='identity' and table_name='audit_event'"
    if ($identityInAudit -ne "0" -or $auditInIdentity -ne "0") { throw "tabelas cruzadas entre schemas" }
    "sem tabelas cruzadas"
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$newEmail = "auditado-$stamp@rivo.ao"
$newPass = "Rivo!Auditado2026"

Test-Case "3. Registo de conta e auditado" {
    $body = @{ email = $newEmail; password = $newPass } | ConvertTo-Json
    $userId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $count = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.registered' and entity_id='$userId'"
    if ($count -ne "1") { throw "esperado 1 registo, obtido $count" }
    "identity.user.registered"
}

Test-Case "4. Login falhado e auditado (BR-12)" {
    $body = @{ email = $newEmail; password = "PasswordErrada!2026" } | ConvertTo-Json
    try { Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json" | Out-Null } catch { }
    $count = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.login_failed' and entity_id='$newEmail'"
    if ([int]$count -lt 1) { throw "tentativa falhada nao auditada" }
    "identity.user.login_failed"
}

Test-Case "5. Login e logout auditados, com IP e correlation" {
    $token = Get-Token $newEmail $newPass
    $headers = @{ Authorization = "Bearer $token" }
    Invoke-RestMethod "$base/identity/logout" -Method Post -Headers $headers | Out-Null

    $login = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.logged_in'"
    $logout = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.logged_out'"
    if ([int]$login -lt 1) { throw "login nao auditado" }
    if ([int]$logout -lt 1) { throw "logout nao auditado" }

    $withContext = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.logged_in' and ip_address is not null and correlation_id is not null"
    if ([int]$withContext -lt 1) { throw "login auditado sem IP ou correlation_id" }
    "login=$login logout=$logout, com IP e correlation_id"
}

Test-Case "6. Atribuicao de perfil auditada (BR-13)" {
    $adminHeaders = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $userId = Invoke-Sql "select id from [identity].app_user where email='$newEmail'"
    $body = @{ profile = "Finance" } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$userId/roles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $row = Invoke-Sql "select new_value from audit.audit_event where action='identity.user.profile_assigned' and entity_id='$userId'"
    if ($row -notmatch "Finance") { throw "novo valor nao regista o perfil: '$row'" }

    # O actor tem de ser o Admin, nao o alvo: e isso que torna a trilha util.
    $actor = Invoke-Sql "select actor_id from audit.audit_event where action='identity.user.profile_assigned' and entity_id='$userId'"
    $adminId = Invoke-Sql "select id from [identity].app_user where email='$adminEmail'"
    if ($actor -ne $adminId) { throw "actor registado '$actor' nao e o Admin '$adminId'" }
    "actor=Admin, new_value contem o perfil"
}

Test-Case "7. Consulta da trilha exige permissao de audit" {
    $token = Get-Token $newEmail $newPass
    $code = Get-StatusCode { Invoke-RestMethod "$base/audit/entries" -Headers @{ Authorization = "Bearer $token" } }
    if ($code -ne 403) { throw "esperado 403 sem audit.trail.read, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/audit/entries" }
    if ($code -ne 401) { throw "esperado 401 sem autenticacao, obtido $code" }
    "403 sem permissao, 401 sem autenticacao"
}

Test-Case "8. Admin consulta a trilha; permissao de outro modulo funciona" {
    $adminHeaders = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $entries = Invoke-RestMethod "$base/audit/entries?limit=100" -Headers $adminHeaders
    if ($entries.Count -lt 4) { throw "esperadas pelo menos 4 entradas, obtidas $($entries.Count)" }
    # A vista nao expoe valores antes/depois (BR-16).
    if ($entries[0].PSObject.Properties.Name -contains "previousValue") { throw "vista expoe previousValue" }
    "$($entries.Count) entradas; vista sem valores sensiveis"
}

Test-Case "9. Filtro por entidade" {
    $adminHeaders = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $userId = Invoke-Sql "select id from [identity].app_user where email='$newEmail'"
    $entries = Invoke-RestMethod "$base/audit/entries?entityType=identity.user&entityId=$userId" -Headers $adminHeaders
    if ($entries.Count -lt 1) { throw "filtro nao devolveu nada" }
    $wrong = $entries | Where-Object { $_.entityId -ne $userId }
    if ($wrong) { throw "filtro devolveu entidades erradas" }
    "$($entries.Count) entradas do utilizador"
}

Test-Case "10. Trilha sobrevive ao reinicio da stack" {
    $before = Invoke-Sql "select count(*) from audit.audit_event"
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }
    $after = Invoke-Sql "select count(*) from audit.audit_event"
    if ([int]$after -lt [int]$before) { throw "trilha perdeu registos: $before -> $after" }
    "$after entradas preservadas"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
