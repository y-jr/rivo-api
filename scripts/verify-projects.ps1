# Verificação do módulo `projects`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-projects.ps1
#
# Esqueleto — 2026-08-29. CRUD sem regra de negócio: sem Marco, Tarefa,
# Orçamento de Projecto, Alocação de Recursos. Esta suite verifica o que
# existe, e não o que `modules/projects.md` descreve como por fazer.
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

$nome = "Piloto Angola $stamp"

Write-Host "`n=== Modulo projects (esqueleto) ===`n"

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

Test-Case "5. Consultar projecto" {
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.name -ne $nome) { throw "nome nao bate: '$($p.name)'" }
    if ($p.status -ne "Active") { throw "estado inesperado: $($p.status)" }
    "estado Active, nome correcto"
}

Test-Case "6. Listagem por omissao mostra activos" {
    $lista = Invoke-RestMethod "$base/projects" -Headers $adminHeaders
    if ($lista.projectId -notcontains $script:projectId) { throw "projecto nao aparece na listagem" }
    "projecto na listagem por omissao"
}

Test-Case "7. Fechar com data anterior ao inicio e recusado" {
    $body = @{ endDate = "2026-08-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "data de fecho anterior ao inicio recusada"
}

Test-Case "8. Fechar projecto" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.status -ne "Closed") { throw "estado nao mudou para Closed: $($p.status)" }
    "projecto fechado"
}

Test-Case "9. Fechar outra vez e recusado (nao ha reabertura)" {
    $body = @{ endDate = "2026-12-31" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo fecho"
}

Test-Case "10. Projecto fechado sai da listagem por omissao, includeClosed traz de volta" {
    $activos = Invoke-RestMethod "$base/projects" -Headers $adminHeaders
    if ($activos.projectId -contains $script:projectId) { throw "projecto fechado ainda aparece nos activos" }

    $todos = Invoke-RestMethod "$base/projects?includeClosed=true" -Headers $adminHeaders
    if ($todos.projectId -notcontains $script:projectId) { throw "projecto fechado nao aparece com includeClosed" }
    "filtra por omissao; includeClosed mostra tudo"
}

Test-Case "11. Nao ha eliminacao de projecto" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects/$($script:projectId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from projects.project where id='$($script:projectId)'"
    if ($existe -ne "1") { throw "projecto desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "12. Abrir e fechar ficam na trilha, com actor" {
    $abrir = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.opened' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($abrir -ne "1") { throw "abertura nao auditada com actor" }
    $fechar = Invoke-Sql "select count(*) from audit.audit_event where action='projects.project.closed' and entity_id='$($script:projectId)' and actor_id is not null"
    if ($fechar -ne "1") { throw "fecho nao auditado com actor" }
    "abertura e fecho na trilha, ambos com actor"
}

Test-Case "13. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/projects" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "14. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $p = Invoke-RestMethod "$base/projects/$($script:projectId)" -Headers $adminHeaders
    if ($p.status -ne "Closed") { throw "estado perdido apos restart: $($p.status)" }
    "projecto '$nome' intacto apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
