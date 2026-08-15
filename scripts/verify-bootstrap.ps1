# Verificação do bootstrap de autoridade a partir de uma base limpa.
#
#   docker compose down -v
#   docker compose up -d --build
#   pwsh -File scripts/verify-bootstrap.ps1
#
# Falha com código de saída diferente de zero se algum ponto não passar.

$ErrorActionPreference = "Stop"
$base = "http://localhost:5080"
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
    return (docker exec rivo-postgres psql -U rivo -d rivo -t -A -c $Query).Trim()
}

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

# Credenciais lidas do .env — nunca escritas neste ficheiro.
$env:PATH | Out-Null
$dotenv = @{}
Get-Content ".env" | Where-Object { $_ -match "=" -and $_ -notmatch "^\s*#" } | ForEach-Object {
    $parts = $_ -split "=", 2
    $dotenv[$parts[0].Trim()] = $parts[1].Trim()
}

$adminEmail = $dotenv["BOOTSTRAP_ADMIN_EMAIL"]
$adminPass = $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]
$deciderEmail = $dotenv["BOOTSTRAP_DECIDER_EMAIL"]
$deciderPass = $dotenv["BOOTSTRAP_DECIDER_PASSWORD"]

Write-Host "`n=== Bootstrap de autoridade ===`n"

Test-Case "1. Migrations aplicadas" {
    $count = Invoke-Sql "select count(*) from identity.__ef_migrations_history"
    if ([int]$count -lt 1) { throw "nenhuma migration registada" }
    $tables = Invoke-Sql "select count(*) from information_schema.tables where table_schema='identity'"
    "$count migration(s), $tables tabelas"
}

Test-Case "2. Seed criou os perfis" {
    $roles = Invoke-Sql "select count(*) from identity.app_role"
    if ($roles -ne "7") { throw "esperados 7 perfis, obtidos $roles" }

    # Sem contagem absoluta de permissoes: cresce legitimamente a cada modulo
    # novo. Verifica-se que o Admin tem as que deve e que nenhuma se repete.
    $adminPerms = Invoke-Sql @"
select count(*) from identity.app_role_claim c
join identity.app_role r on r.id = c.role_id
where r.name = 'Admin' and c.claim_type = 'permission'
"@
    if ([int]$adminPerms -lt 1) { throw "Admin sem permissoes" }

    # Verifica-se a regra, nao um total: perfis ganham permissoes a cada
    # modulo novo, e um numero fixo tornaria este teste falsamente vermelho.
    #
    # A regra que interessa e a de ADR-015: RH atribui cargos mas nao gere o
    # catalogo, porque quem controla a marca de autoridade controla quem pode
    # vir a aprovar pagamentos.
    $hrHasCatalogue = Invoke-Sql @"
select count(*) from identity.app_role_claim c
join identity.app_role r on r.id = c.role_id
where r.name = 'HR' and c.claim_value = 'hr.positions.write'
"@
    if ($hrHasCatalogue -ne "0") { throw "HR tem hr.positions.write, contra ADR-015" }

    # Perfis ainda sem modulos de negocio que os justifiquem.
    $shouldBeEmpty = Invoke-Sql @"
select count(*) from identity.app_role_claim c
join identity.app_role r on r.id = c.role_id
where r.name in ('Manager','Finance','Sales','AssetManager','ProjectManager')
"@
    if ($shouldBeEmpty -ne "0") { throw "perfis sem modulo atribuido tem $shouldBeEmpty permissoes" }

    "7 perfis; Admin com $adminPerms permissoes; HR sem catalogo de cargos"
}

Test-Case "3. Seed criou o Admin" {
    $exists = Invoke-Sql "select count(*) from identity.app_user where email = '$adminEmail'"
    if ($exists -ne "1") { throw "Admin nao encontrado" }
    "conta $adminEmail criada"
}

Test-Case "4. Seed criou o decisor" {
    $exists = Invoke-Sql "select count(*) from identity.app_user where email = '$deciderEmail'"
    if ($exists -ne "1") { throw "decisor nao encontrado" }
    "conta $deciderEmail criada"
}

Test-Case "5. Associacoes correctas" {
    $adminRole = Invoke-Sql @"
select r.name from identity.app_user u
join identity.app_user_role ur on ur.user_id = u.id
join identity.app_role r on r.id = ur.role_id
where u.email = '$adminEmail'
"@
    if ($adminRole -ne "Admin") { throw "Admin tem perfil '$adminRole'" }

    $deciderRole = Invoke-Sql @"
select r.name from identity.app_user u
join identity.app_user_role ur on ur.user_id = u.id
join identity.app_role r on r.id = ur.role_id
where u.email = '$deciderEmail'
"@
    if ($deciderRole -ne "Finance") { throw "decisor tem perfil '$deciderRole'" }
    "Admin->Admin, decisor->Finance"
}

Test-Case "6. Segunda execucao nao duplica" {
    $before = Invoke-Sql "select count(*) from identity.app_user"
    docker compose restart api | Out-Null

    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $after = Invoke-Sql "select count(*) from identity.app_user"
    if ($after -ne $before) { throw "utilizadores duplicados: $before -> $after" }

    # Duplicacao verificada directamente, e nao por total: outras suites criam
    # utilizadores, e um total fixo tornaria este teste falsamente vermelho.
    $dupRoles = Invoke-Sql "select count(*) from (select name from identity.app_role group by name having count(*)>1) d"
    $dupLinks = Invoke-Sql "select count(*) from (select user_id, role_id from identity.app_user_role group by 1,2 having count(*)>1) d"
    $dupClaims = Invoke-Sql "select count(*) from (select role_id, claim_type, claim_value from identity.app_role_claim group by 1,2,3 having count(*)>1) d"

    if ($dupRoles -ne "0") { throw "$dupRoles perfis duplicados" }
    if ($dupLinks -ne "0") { throw "$dupLinks associacoes duplicadas" }
    if ($dupClaims -ne "0") { throw "$dupClaims permissoes duplicadas" }

    "sem duplicados em perfis, associacoes nem permissoes"
}

Test-Case "7. Admin executa operacoes administrativas" {
    $headers = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $profiles = Invoke-RestMethod "$base/identity/roles" -Headers $headers
    if ($profiles.Count -ne 7) { throw "esperados 7 perfis" }
    $users = Invoke-RestMethod "$base/identity/users" -Headers $headers
    if ($users.Count -lt 2) { throw "esperados pelo menos 2 utilizadores" }
    "GET /roles e /users autorizados sem intervencao manual"
}

Test-Case "8. Decisor tem a autoridade esperada e nada mais" {
    $headers = @{ Authorization = "Bearer " + (Get-Token $deciderEmail $deciderPass) }
    $me = Invoke-RestMethod "$base/identity/me" -Headers $headers
    if ($me.roles -notcontains "Finance") { throw "decisor sem perfil Finance" }
    # Finance nao tem permissoes de identity: a separacao tem de ser efectiva.
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/roles" -Headers $headers }
    if ($code -ne 403) { throw "esperado 403 em /roles, obtido $code" }
    "perfil Finance; 403 nas operacoes de identity"
}

Test-Case "9. Passwords ausentes do codigo e dos logs" {
    # Tudo o que seria versionado: codigo, compose, exemplo de configuracao.
    # O proprio .env fica de fora — e onde as credenciais devem estar, e esta
    # no .gitignore.
    $tracked = @()
    $tracked += Get-ChildItem "src" -Recurse -File -Include *.cs,*.csproj,*.json
    $tracked += Get-Item "docker-compose.yml", ".env.example", "Dockerfile"

    foreach ($secret in @($adminPass, $deciderPass)) {
        $hit = $tracked | Select-String -Pattern ([regex]::Escape($secret)) -SimpleMatch -ErrorAction SilentlyContinue
        if ($hit) { throw "password encontrada em " + $hit[0].Path }
    }

    # Confirma que o .env nao e versionado, senao o ponto anterior nao vale.
    $ignored = Select-String -Path ".gitignore" -Pattern "^\.env$" -Quiet
    if (-not $ignored) { throw ".env nao esta no .gitignore" }

    $logs = docker compose logs 2>&1 | Out-String
    foreach ($secret in @($adminPass, $deciderPass)) {
        if ($logs -match [regex]::Escape($secret)) { throw "password presente nos logs" }
    }

    "$($tracked.Count) ficheiros versionados limpos; .env ignorado; logs limpos"
}

Write-Host ""
if ($failures -gt 0) {
    Write-Host "$failures ponto(s) falharam." -ForegroundColor Red
    exit 1
}
Write-Host "Todos os pontos verificados." -ForegroundColor Green
exit 0
