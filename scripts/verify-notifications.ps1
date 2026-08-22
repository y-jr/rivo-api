# Verificação do módulo `notifications`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-notifications.ps1

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

# Dois utilizadores: um recebe a notificacao, o outro serve para provar que
# nao lhe chega nem a consegue marcar.
$alvoEmail = "alvo-$stamp@rivo.ao"
$outroEmail = "outro-$stamp@rivo.ao"
foreach ($e in @($alvoEmail, $outroEmail)) {
    $b = @{ email = $e; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
}
$alvoId = Invoke-Sql "select id from [identity].app_user where email='$alvoEmail'"

Write-Host "`n=== Modulo notifications ===`n"

Test-Case "1. Schema notifications com migration propria" {
    $m = Invoke-Sql "select count(*) from notifications.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='notifications'"
    "$m migration(s), $t tabelas"
}

Test-Case "2. notifications nao referencia outros schemas" {
    $out = Invoke-Sql @"
-- INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE nao serve aqui: em SQL Server
-- devolve as colunas da tabela que *tem* a restricao, e nao a tabela
-- referida. Quem sabe o outro lado da FK e sys.foreign_keys.
select count(*) from sys.foreign_keys fk
join sys.tables ot on ot.object_id = fk.parent_object_id
join sys.schemas os on os.schema_id = ot.schema_id
join sys.tables dt on dt.object_id = fk.referenced_object_id
join sys.schemas ds on ds.schema_id = dt.schema_id
where os.name = 'notifications' and ds.name <> 'notifications'
"@
    if ($out -ne "0") { throw "notifications tem $out FK para fora" }
    "sem chaves estrangeiras para fora"
}

Test-Case "3. Atribuir perfil enfileira notificacao" {
    $b = @{ profile = "Finance" } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$alvoId/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $c = Invoke-Sql "select count(*) from notifications.notification where recipient_user_id='$alvoId' and type='identity.access_profile_assigned'"
    if ($c -ne "1") { throw "esperada 1 notificacao, obtidas $c" }
    "identity.access_profile_assigned"
}

Test-Case "4. Destinatario le a sua notificacao" {
    $h = @{ Authorization = "Bearer " + (Get-Token $alvoEmail $pass) }
    $n = Invoke-RestMethod "$base/notifications/me" -Headers $h
    if ($n.Count -ne 1) { throw "esperada 1, obtidas $($n.Count)" }
    if ($n[0].read -ne $false) { throw "deveria estar por ler" }
    if ($n[0].message -notmatch "Finance") { throw "mensagem sem o perfil: $($n[0].message)" }
    "1 notificacao por ler, com o perfil na mensagem"
}

Test-Case "5. Outro utilizador nao ve a notificacao alheia" {
    $h = @{ Authorization = "Bearer " + (Get-Token $outroEmail $pass) }
    $n = Invoke-RestMethod "$base/notifications/me" -Headers $h
    if ($n.Count -ne 0) { throw "viu $($n.Count) notificacoes alheias" }
    "lista vazia"
}

$script:notificationId = $null
Test-Case "6. Marcar como lida" {
    $h = @{ Authorization = "Bearer " + (Get-Token $alvoEmail $pass) }
    $n = Invoke-RestMethod "$base/notifications/me" -Headers $h
    $script:notificationId = $n[0].notificationId

    Invoke-RestMethod "$base/notifications/$($script:notificationId)/read" -Method Post -Headers $h | Out-Null

    $after = Invoke-RestMethod "$base/notifications/me" -Headers $h
    if ($after[0].read -ne $true) { throw "continua por ler" }

    $unread = Invoke-RestMethod "$base/notifications/me?unreadOnly=true" -Headers $h
    if ($unread.Count -ne 0) { throw "filtro de nao lidas devolveu $($unread.Count)" }
    "lida, e filtro unreadOnly correcto"
}

Test-Case "7. Nao se marca notificacao alheia -> 404" {
    $h = @{ Authorization = "Bearer " + (Get-Token $outroEmail $pass) }
    $code = Get-StatusCode { Invoke-RestMethod "$base/notifications/$($script:notificationId)/read" -Method Post -Headers $h }
    # 404 e nao 403: distinguir revelaria que a notificacao existe.
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404, sem revelar existencia"
}

Test-Case "8. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/notifications/me" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "9. Sem canal externo nasce NotRequired, nao Pending" {
    # A notificacao de atribuicao de perfil nao pede e-mail: nao deve ficar
    # eternamente pendente a espera de um worker.
    $status = Invoke-Sql "select delivery_status from notifications.notification where id='$($script:notificationId)'"
    if ($status -ne "NotRequired") { throw "estado '$status', esperado NotRequired" }
    "NotRequired"
}

Test-Case "10. Worker de entrega esta activo" {
    # Compara-se com o nome da categoria de log, que e ASCII: o Windows
    # PowerShell 5.1 le ficheiros .ps1 sem BOM na codepage ANSI, e acentos
    # neste ficheiro nao sobreviveriam a comparacao.
    $logs = docker compose -f docker-compose.yml -f docker-compose.dev.yml logs api 2>&1 | Out-String
    if ($logs -notmatch "NotificationDeliveryWorker") { throw "worker nao arrancou" }
    "BackgroundService a correr"
}

Test-Case "11. Worker entrega pendentes e faz backoff" {
    # Insere directamente uma notificacao pendente, para exercitar o worker sem
    # depender de um modulo que peca e-mail.
    $id = [Guid]::NewGuid().ToString()
    Invoke-Sql @"
insert into notifications.notification
 (id, version, recipient_user_id, type, title, message, created_at, delivery_status, delivery_attempts, next_attempt_at)
values
 ('$id', 0, '$alvoId', 'teste.entrega', 'Teste de entrega', 'corpo', SYSDATETIMEOFFSET(), 'Pending', 0, SYSDATETIMEOFFSET())
"@ | Out-Null

    # O intervalo de sondagem em dev sao 2s.
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Seconds 3
        $status = Invoke-Sql "select delivery_status from notifications.notification where id='$id'"
    } while ($status -eq "Pending" -and (Get-Date) -lt $deadline)

    if ($status -ne "Delivered") { throw "estado '$status', esperado Delivered" }

    # SQL Server nao devolve booleanos num SELECT: mapeia-se para o mesmo 't'
    # que a suite ja esperava.
    $when = Invoke-Sql "select case when delivered_at is not null then 't' else 'f' end from notifications.notification where id='$id'"
    if ($when -ne "t") { throw "delivered_at por preencher" }
    "entregue pelo worker"
}

Test-Case "12. Enfileirar nao derruba a operacao de negocio" {
    # A atribuicao de perfil gravou mesmo, e a notificacao seguiu em separado.
    $role = Invoke-Sql @"
select count(*) from [identity].app_user_role ur
join [identity].app_role r on r.id = ur.role_id
where ur.user_id = '$alvoId' and r.name = 'Finance'
"@
    if ($role -ne "1") { throw "perfil nao foi atribuido" }
    $audit = Invoke-Sql "select count(*) from audit.audit_event where action='identity.user.profile_assigned' and entity_id='$alvoId'"
    if ($audit -ne "1") { throw "atribuicao nao auditada" }
    "perfil atribuido e auditado, com notificacao a parte"
}

Test-Case "13. Notificacoes sobrevivem ao reinicio da stack" {
    $before = Invoke-Sql "select count(*) from notifications.notification"
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }
    $after = Invoke-Sql "select count(*) from notifications.notification"
    if ([int]$after -lt [int]$before) { throw "perdeu notificacoes: $before -> $after" }
    "$after preservadas"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
