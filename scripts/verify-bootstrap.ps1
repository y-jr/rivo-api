# Verificação do bootstrap de autoridade a partir de uma base limpa.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-bootstrap.ps1
#
# Falha com código de saída diferente de zero se algum ponto não passar.

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

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

# Credenciais lidas do .env — nunca escritas neste ficheiro.
$env:PATH | Out-Null
$dotenv = Get-RivoCredentials

$adminEmail = $dotenv["BOOTSTRAP_ADMIN_EMAIL"]
$adminPass = $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]
$deciderEmail = $dotenv["BOOTSTRAP_DECIDER_EMAIL"]
$deciderPass = $dotenv["BOOTSTRAP_DECIDER_PASSWORD"]

Write-Host "`n=== Bootstrap de autoridade ===`n"

Test-Case "1. Migrations aplicadas" {
    $count = Invoke-Sql "select count(*) from [identity].__ef_migrations_history"
    if ([int]$count -lt 1) { throw "nenhuma migration registada" }
    $tables = Invoke-Sql "select count(*) from information_schema.tables where table_schema='identity'"
    "$count migration(s), $tables tabelas"
}

Test-Case "2. Seed criou os perfis" {
    $roles = Invoke-Sql "select count(*) from [identity].app_role"
    if ($roles -ne "8") { throw "esperados 8 perfis, obtidos $roles" }

    # Sem contagem absoluta de permissoes: cresce legitimamente a cada modulo
    # novo. Verifica-se que o Admin tem as que deve e que nenhuma se repete.
    $adminPerms = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
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
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name = 'HR' and c.claim_value = 'hr.positions.write'
"@
    if ($hrHasCatalogue -ne "0") { throw "HR tem hr.positions.write, contra ADR-015" }

    # A lista de perfis vazios encolheu quatro vezes, e cada saida foi um
    # modulo a nascer: `Manager` e `Finance` em 2026-08-23 (ADR-034), quando
    # decidir sobre pedidos passou a existir; `Sales` em 2026-08-24 (ADR-036),
    # com clientes e emissao de facturas; `AssetManager` em 2026-08-27, com a
    # recepcao de mercadoria; `ProjectManager` em 2026-08-29, com o esqueleto
    # de `projects`. **Nenhum dos sete perfis continua vazio.** Um oitavo,
    # `Cliente`, nasceu vazio a 2026-09-03 (ADR-043) — espera pelo Portal do
    # Cliente, mesma situacao em que os outros ja estiveram.
    #
    # A saida do `AssetManager` nao e adivinhacao: a recepcao e a porta de
    # entrada do stock, e `modules/procurement.md` diz que `procurement` publica
    # o facto da recepcao para `inventory` o consumir. Quem gere activos e
    # existencias e quem conta o que chega.
    #
    # `ProjectManager` fica com exactamente as permissoes de `projects` — o
    # modulo que este perfil nomeia, e nenhum outro: e esqueleto sem regra de
    # segregacao ainda, por isso nao ha mais nada para verificar aqui alem de
    # que a atribuicao aconteceu.
    $projectManagerPerms = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name = 'ProjectManager' and c.claim_value like 'projects.%'
"@
    if ($projectManagerPerms -ne "2") { throw "ProjectManager esperava 2 permissoes de projects, tem $projectManagerPerms" }

    # **`AssetManager` recebe e nao encomenda.** E a segregacao que da valor ao
    # 3-way match: se quem encomenda registasse a chegada, uma entrega a menos
    # podia ser dada como completa sem que mais ninguem visse.
    $recebe = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name = 'AssetManager' and c.claim_value = 'procurement.receipts.write'
"@
    $encomenda = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name = 'AssetManager' and c.claim_value = 'procurement.orders.write'
"@
    if ($recebe -ne "1") { throw "AssetManager nao recebe mercadoria" }
    if ($encomenda -ne "0") { throw "AssetManager encomenda e recebe - a segregacao caiu" }

    # `Sales` vende e emite, mas **nao fixa a taxa** que a sua propria venda
    # vai liquidar, e **nao anula** o que emitiu (ADR-036).
    $salesFiscal = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name = 'Sales' and (c.claim_value like 'fiscal.%' or c.claim_value = 'finance.invoices.cancel')
"@
    if ($salesFiscal -ne "0") { throw "Sales fixa taxas ou anula facturas" }

    # `Manager` e `Finance` decidem, mas **nao configuram as alcadas**: quem
    # configura decidiria indirectamente o que pode aprovar sozinho, que e a
    # mesma escalada que o ADR-015 fecha em `hr`.
    $decidem = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name in ('Manager','Finance') and c.claim_value = 'approval.requests.decide'
"@
    if ($decidem -ne "2") { throw "Manager e Finance deviam decidir sobre pedidos; tem $decidem" }

    $configuram = Invoke-Sql @"
select count(*) from [identity].app_role_claim c
join [identity].app_role r on r.id = c.role_id
where r.name in ('Manager','Finance') and c.claim_value = 'approval.policies.write'
"@
    if ($configuram -ne "0") { throw "Manager ou Finance gerem politicas de aprovacao" }

    "8 perfis; Admin com $adminPerms permissoes; HR sem catalogo; Manager/Finance decidem sem configurar"
}

Test-Case "3. Seed criou o Admin" {
    $exists = Invoke-Sql "select count(*) from [identity].app_user where email = '$adminEmail'"
    if ($exists -ne "1") { throw "Admin nao encontrado" }
    "conta $adminEmail criada"
}

Test-Case "4. Seed criou o decisor" {
    $exists = Invoke-Sql "select count(*) from [identity].app_user where email = '$deciderEmail'"
    if ($exists -ne "1") { throw "decisor nao encontrado" }
    "conta $deciderEmail criada"
}

Test-Case "5. Associacoes correctas" {
    $adminRole = Invoke-Sql @"
select r.name from [identity].app_user u
join [identity].app_user_role ur on ur.user_id = u.id
join [identity].app_role r on r.id = ur.role_id
where u.email = '$adminEmail'
"@
    if ($adminRole -ne "Admin") { throw "Admin tem perfil '$adminRole'" }

    $deciderRole = Invoke-Sql @"
select r.name from [identity].app_user u
join [identity].app_user_role ur on ur.user_id = u.id
join [identity].app_role r on r.id = ur.role_id
where u.email = '$deciderEmail'
"@
    if ($deciderRole -ne "Finance") { throw "decisor tem perfil '$deciderRole'" }
    "Admin->Admin, decisor->Finance"
}

Test-Case "6. Segunda execucao nao duplica" {
    $before = Invoke-Sql "select count(*) from [identity].app_user"
    Restart-RivoStack

    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $after = Invoke-Sql "select count(*) from [identity].app_user"
    if ($after -ne $before) { throw "utilizadores duplicados: $before -> $after" }

    # Duplicacao verificada directamente, e nao por total: outras suites criam
    # utilizadores, e um total fixo tornaria este teste falsamente vermelho.
    $dupRoles = Invoke-Sql "select count(*) from (select name from [identity].app_role group by name having count(*)>1) d"
    $dupLinks = Invoke-Sql "select count(*) from (select user_id, role_id from [identity].app_user_role group by user_id, role_id having count(*)>1) d"
    $dupClaims = Invoke-Sql "select count(*) from (select role_id, claim_type, claim_value from [identity].app_role_claim group by role_id, claim_type, claim_value having count(*)>1) d"

    if ($dupRoles -ne "0") { throw "$dupRoles perfis duplicados" }
    if ($dupLinks -ne "0") { throw "$dupLinks associacoes duplicadas" }
    if ($dupClaims -ne "0") { throw "$dupClaims permissoes duplicadas" }

    "sem duplicados em perfis, associacoes nem permissoes"
}

Test-Case "7. Admin executa operacoes administrativas" {
    $headers = @{ Authorization = "Bearer " + (Get-Token $adminEmail $adminPass) }
    $profiles = Invoke-RestMethod "$base/identity/roles" -Headers $headers
    if ($profiles.Count -ne 8) { throw "esperados 8 perfis" }
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
    # -Force porque em Linux um ficheiro comecado por ponto e oculto, e o
    # Get-Item ignora ocultos por omissao. Sem isto, `.env.example` da
    # "Could not find item" no CI e passa em Windows, onde o ponto nao marca
    # nada.
    $tracked += Get-Item -Force "docker-compose.yml", ".env.example", "Dockerfile"

    foreach ($secret in @($adminPass, $deciderPass)) {
        $hit = $tracked | Select-String -Pattern ([regex]::Escape($secret)) -SimpleMatch -ErrorAction SilentlyContinue
        if ($hit) { throw "password encontrada em " + $hit[0].Path }
    }

    # Confirma que o .env nao e versionado, senao o ponto anterior nao vale.
    #
    # Pergunta-se ao git, em vez de procurar o texto da regra no .gitignore.
    # A assercao anterior exigia uma linha literalmente `.env` e passou a
    # falhar quando a regra mudou para `.env*` com `!.env.example` — que
    # ignora o .env na mesma, e ainda o .env.vps e o .env.local, que levam
    # credenciais reais. Afirmava a forma da regra, e nao o efeito dela; o
    # ADR-022 manda o contrario.
    git check-ignore -q ".env"
    if ($LASTEXITCODE -ne 0) { throw ".env nao seria ignorado pelo git" }

    # Estar ignorado nao chega: um ficheiro que ja tenha sido versionado
    # continua a se-lo depois de entrar no .gitignore, e as credenciais
    # ficariam no historico na mesma.
    git ls-files --error-unmatch ".env" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw ".env esta versionado" }

    # O exemplo tem de continuar a viajar: e o unico sitio onde esta escrito
    # que variaveis o ambiente precisa. Um `.env*` sem a excepcao levava-o
    # atras sem ninguem dar por isso.
    git check-ignore -q ".env.example"
    if ($LASTEXITCODE -eq 0) { throw ".env.example ficou ignorado" }

    $logs = docker compose -f docker-compose.yml -f docker-compose.dev.yml logs 2>&1 | Out-String
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
