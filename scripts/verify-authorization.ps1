# Verificação da autorização por perfis.
#
# Pressupõe a stack a correr:  docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#
# Cobre os seis cenários obrigatórios desta etapa. Falha com código de saída
# diferente de zero se algum não passar, para poder correr em CI.

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
        # Um 401 sem corpo faz o PowerShell perder a resposta; distingue-se pela mensagem.
        if ($_.Exception.Message -match "401|Unauthorized") { return 401 }
        throw
    }
}

function Get-Token {
    param([string]$Email, [string]$Password)

    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

Write-Host "`n=== Autorização por perfis ===`n"

$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$adminEmail = "admin-$stamp@rivo.ao"
$plainEmail = "comum-$stamp@rivo.ao"

# As duas contas desta suite nascem de um convite, dado pelo Admin do bootstrap
# (ADR-059). Antes nasciam de `POST /identity/register` e o perfil da primeira
# era enxertado por SQL directo — o registo não sabia atribuir perfis, e sem
# alguém com `identity.roles.assign` não havia por onde começar. O convite sabe,
# e o bootstrap já semeia esse alguém (ADR-016): o `insert` saiu daqui.
$dotenv = Get-RivoCredentials
$bootstrapHeaders = @{
    Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"])
}

$adminId = New-RivoConta -Email $adminEmail -Password $pass `
    -AdminHeaders $bootstrapHeaders -Perfil "Admin"

# `Cliente` e não "sem perfil": o convite exige perfil, e este é o mais estreito
# do catálogo — tem `documents.write` e mais nada, por isso continua a ser a
# conta certa para provar o 403 dos casos 2 e 7.
New-RivoConta -Email $plainEmail -Password $pass `
    -AdminHeaders $bootstrapHeaders -Perfil "Cliente" | Out-Null

$adminToken = Get-Token $adminEmail $pass
$plainToken = Get-Token $plainEmail $pass
$adminHeaders = @{ Authorization = "Bearer $adminToken" }
$plainHeaders = @{ Authorization = "Bearer $plainToken" }

Test-Case "1. Nao autenticado -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/roles" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Autenticado sem perfil adequado -> 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/roles" -Headers $plainHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403"
}

Test-Case "3. Autenticado com perfil adequado -> 200" {
    $roles = Invoke-RestMethod "$base/identity/roles" -Headers $adminHeaders
    if ($roles.Count -ne 8) { throw "esperados 8 perfis, obtidos $($roles.Count)" }
    "8 perfis devolvidos"
}

Test-Case "4. Seed nao cria perfis fora do catalogo" {
    # SuperAdmin (ADR-058) e semeado como os outros, mas nao e atribuivel em
    # runtime — por isso esta aqui e nao aparece em GET /identity/roles. A
    # assimetria e verificada pelo caso 4b.
    $expected = @("Admin", "AssetManager", "Cliente", "Finance", "HR", "Manager", "ProjectManager", "Sales", "SuperAdmin")
    $actual = (Invoke-RivoSql "select name from [identity].app_role order by name") -split "`n" | Where-Object { $_ }
    $diff = Compare-Object $expected $actual
    # `-join` e nao `Join-String`: este ultimo so existe a partir do PowerShell
    # 6.2, e esta linha vive no caminho de erro — falhava exactamente quando
    # havia algo a reportar, escondendo a divergencia atras de um erro de
    # cmdlet inexistente.
    if ($diff) { throw "divergencia: " + (($diff | ForEach-Object { $_.InputObject }) -join ",") }
    "exactamente os 9 esperados"
}

Test-Case "4b. SuperAdmin nao e atribuivel nem visivel (ADR-058)" {
    # A propriedade que o ADR-058 existe para dar: um Admin da empresa nao
    # consegue ver nem conceder a si proprio a permissao que contorna BR-20.
    $perfis = (Invoke-RestMethod "$base/identity/roles" -Headers $adminHeaders).name
    if ($perfis -contains "SuperAdmin") { throw "SuperAdmin listado em /identity/roles" }

    $qualquerConta = (Invoke-RestMethod "$base/identity/users" -Headers $adminHeaders)[0].userId
    $corpo = @{ profile = "SuperAdmin" } | ConvertTo-Json
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/identity/users/$qualquerConta/roles" -Method Post -Body $corpo `
            -ContentType "application/json" -Headers $adminHeaders
    }

    if ($code -ne 400) { throw "atribuir SuperAdmin devolveu $code, esperado 400" }
    "invisivel no catalogo e recusado com 400, mesmo a um Admin"
}

Test-Case "5. Seed repetido nao duplica" {
    # O seed corre a cada arranque; reiniciar a API executa-o segunda vez.
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $roleCount = (Invoke-RivoSql "select count(*) from [identity].app_role")
    if ($roleCount -ne "9") { throw "perfis duplicados: $roleCount" }

    # Duplicacao verificada directamente. O total de permissoes cresce a cada
    # modulo novo, por isso nao serve de asercao.
    $dupClaims = (Invoke-RivoSql "select count(*) from (select role_id, claim_type, claim_value from [identity].app_role_claim group by role_id, claim_type, claim_value having count(*)>1) d")
    if ($dupClaims -ne "0") { throw "$dupClaims permissoes duplicadas" }
    "9 perfis, sem permissoes duplicadas apos segunda execucao"
}

Test-Case "6. Permissoes sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    # O token anterior morreu com a sessao? Nao: a sessao esta em base de dados
    # e o volume persiste, por isso continua valida.
    $roles = Invoke-RestMethod "$base/identity/roles" -Headers $adminHeaders
    if ($roles.Count -ne 8) { throw "esperados 8 perfis, obtidos $($roles.Count)" }
    "autorizacao intacta apos restart"
}

Test-Case "7. Atribuicao de perfil exige permissao" {
    $body = @{ profile = "Finance" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/users/$adminId/roles" -Method Post -Body $body -ContentType "application/json" -Headers $plainHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 sem identity.roles.assign"
}

Test-Case "8. Perfil inexistente e recusado" {
    $body = @{ profile = "NaoExiste" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/users/$adminId/roles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }

    # 400 e nao 404: o perfil vem do corpo, e o utilizador do URI existe. Um
    # 404 aqui manda procurar o defeito no userId, que e o sitio errado.
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "HTTP 400"
}

Test-Case "9. Utilizador inexistente distingue-se de perfil invalido" {
    $body = @{ profile = "Finance" } | ConvertTo-Json
    $inexistente = "11111111-1111-1111-1111-111111111111"
    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/users/$inexistente/roles" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404 para utilizador inexistente, obtido $code" }
    "404 para o URI, 400 para o corpo"
}

# ── O convite, e a porta que se fechou (ADR-059) ─────────────────────────────
#
# Sao os casos que sustentam a barreira que o utilizador pediu a 2026-09-13,
# depois de criar conta em producao e entrar de imediato. Verificam-se aqui, e
# nao em `verify-bootstrap`, porque e isto a autorizacao a decidir quem existe.

Test-Case "10. O registo publico deixou de existir" {
    $corpo = @{ email = "naodevianascer-$stamp@rivo.ao"; password = $pass } | ConvertTo-Json
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/identity/register" -Method Post -Body $corpo -ContentType "application/json"
    }

    # 404 e nao 405 nem 403: a rota nao esta escondida atras de uma permissao,
    # nem aceita o verbo e recusa. Nao existe.
    if ($code -ne 404) { throw "esperado 404, obtido $code" }

    $criada = Invoke-RivoSql "select count(*) from [identity].app_user where email='naodevianascer-$stamp@rivo.ao'"
    if ($criada -ne "0") { throw "a conta foi criada apesar do $code" }
    "404, e nenhuma conta criada"
}

Test-Case "11. Convidar exige a permissao de quem administra contas" {
    $corpo = @{ email = "convidado-por-quem-nao-pode-$stamp@rivo.ao"; profile = "Cliente" } | ConvertTo-Json
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/identity/invitations" -Method Post -Body $corpo `
            -ContentType "application/json" -Headers $plainHeaders
    }
    if ($code -ne 403) { throw "esperado 403 sem identity.users.write, obtido $code" }

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/identity/invitations" -Method Post -Body $corpo -ContentType "application/json"
    }
    if ($code -ne 401) { throw "esperado 401 sem autenticacao, obtido $code" }
    "403 sem permissao, 401 sem autenticacao"
}

Test-Case "12. O convite recusa perfil ausente, invalido e SuperAdmin" {
    foreach ($perfil in @($null, "", "NaoExiste", "SuperAdmin")) {
        $c = @{ email = "convite-recusado-$stamp@rivo.ao" }
        if ($null -ne $perfil) { $c.profile = $perfil }

        $code = Get-StatusCode {
            Invoke-RestMethod "$base/identity/invitations" -Method Post -Body ($c | ConvertTo-Json) `
                -ContentType "application/json" -Headers $adminHeaders
        }
        if ($code -ne 400) { throw "perfil '$perfil' devolveu $code, esperado 400" }
    }

    # A recusa do SuperAdmin e a que importa: sem ela, convidar era o desvio
    # por onde um Admin da empresa se dava a permissao que contorna BR-20.
    $criada = Invoke-RivoSql "select count(*) from [identity].app_user where email='convite-recusado-$stamp@rivo.ao'"
    if ($criada -ne "0") { throw "conta criada por um convite recusado" }
    "quatro recusas com 400, e nenhuma conta criada"
}

$script:convidadoId = $null
$script:convidadoEmail = "convidado-$stamp@rivo.ao"
Test-Case "13. A conta convidada existe e ninguem lhe entra" {
    $script:convidadoId = (Invoke-RestMethod "$base/identity/invitations" -Method Post `
        -Body (@{ email = $script:convidadoEmail; profile = "Finance" } | ConvertTo-Json) `
        -ContentType "application/json" -Headers $adminHeaders).userId

    $comPassword = Invoke-RivoSql "select case when password_hash is null then 'f' else 't' end from [identity].app_user where id='$($script:convidadoId)'"
    if ($comPassword -ne "f") { throw "a conta nasceu com password ('$comPassword')" }

    # Ja tem o perfil: convidar e um acto so, e nao dois que alguem se esquece
    # de completar -- era esse o defeito das contas que o registo deixava.
    $perfil = Invoke-RivoSql @"
select count(*) from [identity].app_user_role ur
join [identity].app_role r on r.id = ur.role_id
where ur.user_id = '$($script:convidadoId)' and r.name = 'Finance'
"@
    if ($perfil -ne "1") { throw "o convite nao atribuiu o perfil" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/identity/login" -Method Post `
        -Body (@{ email = $script:convidadoEmail; password = $pass } | ConvertTo-Json) `
        -ContentType "application/json" }
    if ($code -ne 401) { throw "entrou numa conta sem password: $code" }
    "existe, com perfil, sem password, e o login da 401"
}

Test-Case "14. O testemunho vai no destino da notificacao, e nao na resposta" {
    $onde = "where recipient_user_id='$($script:convidadoId)' and type='identity.user_invited'"

    # Desde 15-09 o destino e dado estruturado, e nao texto dentro do corpo: o
    # `message` e o mesmo que a aplicacao mostra na lista de notificacoes, e e o
    # canal de correio que o desenha como botao.
    $destino = Invoke-RivoSql "select action_url from notifications.notification $onde"
    if (-not $destino) { throw "convidar nao enfileirou notificacao com destino" }
    if ($destino -notmatch "/convite\?u=$($script:convidadoId)&t=") { throw "destino sem a ligacao do convite: $destino" }

    $etiqueta = Invoke-RivoSql "select action_label from notifications.notification $onde"
    if (-not $etiqueta) { throw "destino sem etiqueta -- o botao ficaria por legendar" }

    # E o testemunho nao fica no corpo, que e o que se le em texto simples e o
    # que fica visivel dentro da aplicacao.
    $mensagem = Invoke-RivoSql "select message from notifications.notification $onde"
    if ($mensagem -match "&t=") { throw "o testemunho continua no corpo da mensagem" }

    # E pedida para sair mesmo da aplicacao. `SendEmail` tem por omissao
    # `false`, e uma notificacao assim nasce `NotRequired`: o worker nunca lhe
    # toca e o convite fica na caixa de quem ainda nao consegue entrar para o ler.
    $estado = Invoke-RivoSql "select delivery_status from notifications.notification $onde"
    if ($estado -eq "NotRequired") { throw "o convite nasceu NotRequired -- nunca sera enviado" }
    "destino legendado, corpo sem testemunho, entrega pedida (estado=$estado)"
}

Test-Case "15. Aceitar o convite abre a conta -- uma vez so" {
    $ligacao = Invoke-RivoSql "select action_url from notifications.notification where recipient_user_id='$($script:convidadoId)' and type='identity.user_invited'"
    if ($ligacao -notmatch "t=([A-Za-z0-9_-]+)") { throw "nao se extraiu o testemunho do destino" }
    $testemunho = $Matches[1]

    $novaPass = "Rivo!Convidado2026"
    $corpo = @{ userId = $script:convidadoId; token = $testemunho; password = $novaPass } | ConvertTo-Json

    # `Invoke-WebRequest` e nao `Get-StatusCode`: num sucesso o `Invoke-RestMethod`
    # nao devolve o codigo, e o auxiliar responderia 200 a um 204.
    $r = Invoke-WebRequest "$base/identity/invitations/acceptance" -Method Post -Body $corpo `
        -ContentType "application/json" -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "aceitar devolveu $($r.StatusCode), esperado 204" }

    # Agora entra, e com as permissoes do perfil que o convite lhe deu.
    $token = Get-Token $script:convidadoEmail $novaPass
    $eu = Invoke-RestMethod "$base/identity/me" -Headers @{ Authorization = "Bearer $token" }
    if ($eu.roles -notcontains "Finance") { throw "entrou sem o perfil do convite: $($eu.roles -join ',')" }

    # De uso unico. Sem isto, um convite antigo por consumir era uma segunda via
    # de reposicao de password para uma conta ja em uso.
    $code = Get-StatusCode {
        Invoke-RestMethod "$base/identity/invitations/acceptance" -Method Post -Body $corpo -ContentType "application/json"
    }
    if ($code -ne 400) { throw "repetir o convite devolveu $code, esperado 400" }
    "204, entra com o perfil Finance, e repetir da 400"
}

Write-Host ""
if ($failures -gt 0) {
    Write-Host "$failures teste(s) falharam." -ForegroundColor Red
    exit 1
}

Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
