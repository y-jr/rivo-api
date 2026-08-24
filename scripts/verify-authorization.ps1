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

function New-User {
    param([string]$Email, [string]$Password)

    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
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

$adminId = New-User $adminEmail $pass
New-User $plainEmail $pass | Out-Null

# O primeiro Admin é atribuído fora de banda: nenhum utilizador é semeado, por
# isso não há ninguém com permissão para conceder o primeiro perfil.
Invoke-RivoSql @"
insert into [identity].app_user_role (user_id, role_id)
select '$adminId', r.id from [identity].app_role r
where r.name = 'Admin'
  and not exists (
    select 1 from [identity].app_user_role ur
    where ur.user_id = '$adminId' and ur.role_id = r.id);
"@ | Out-Null

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
    if ($roles.Count -ne 7) { throw "esperados 7 perfis, obtidos $($roles.Count)" }
    "7 perfis devolvidos"
}

Test-Case "4. Seed nao cria perfis fora do catalogo" {
    $expected = @("Admin", "AssetManager", "Finance", "HR", "Manager", "ProjectManager", "Sales")
    $actual = (Invoke-RivoSql "select name from [identity].app_role order by name") -split "`n" | Where-Object { $_ }
    $diff = Compare-Object $expected $actual
    # `-join` e nao `Join-String`: este ultimo so existe a partir do PowerShell
    # 6.2, e esta linha vive no caminho de erro — falhava exactamente quando
    # havia algo a reportar, escondendo a divergencia atras de um erro de
    # cmdlet inexistente.
    if ($diff) { throw "divergencia: " + (($diff | ForEach-Object { $_.InputObject }) -join ",") }
    "exactamente os 7 esperados"
}

Test-Case "5. Seed repetido nao duplica" {
    # O seed corre a cada arranque; reiniciar a API executa-o segunda vez.
    Restart-RivoStack -ApiOnly
    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $roleCount = (Invoke-RivoSql "select count(*) from [identity].app_role")
    if ($roleCount -ne "7") { throw "perfis duplicados: $roleCount" }

    # Duplicacao verificada directamente. O total de permissoes cresce a cada
    # modulo novo, por isso nao serve de asercao.
    $dupClaims = (Invoke-RivoSql "select count(*) from (select role_id, claim_type, claim_value from [identity].app_role_claim group by role_id, claim_type, claim_value having count(*)>1) d")
    if ($dupClaims -ne "0") { throw "$dupClaims permissoes duplicadas" }
    "7 perfis, sem permissoes duplicadas apos segunda execucao"
}

Test-Case "6. Permissoes sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(180)
    do {
        Start-Sleep -Seconds 3
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    # O token anterior morreu com a sessao? Nao: a sessao esta em base de dados
    # e o volume persiste, por isso continua valida.
    $roles = Invoke-RestMethod "$base/identity/roles" -Headers $adminHeaders
    if ($roles.Count -ne 7) { throw "esperados 7 perfis, obtidos $($roles.Count)" }
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

Write-Host ""
if ($failures -gt 0) {
    Write-Host "$failures teste(s) falharam." -ForegroundColor Red
    exit 1
}

Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
