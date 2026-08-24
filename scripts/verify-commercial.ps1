# Verificação do módulo `commercial`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-commercial.ps1
#
# Âmbito reduzido pelo ADR-036: só o Cliente. Lead, Oportunidade, Proposta,
# Contrato Comercial e Acção de Cobrança não existem, e esta suite não os
# procura.
#
# Re-executável: cada corrida usa um NIF próprio, derivado do carimbo temporal.

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

# Utilizador sem perfil nenhum, para a fronteira de autorizacao.
$semPerfilEmail = "semperfil-c-$stamp@rivo.ao"
$body = @{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json
Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

$nif = "54$stamp"

Write-Host "`n=== Modulo commercial ===`n"

Test-Case "1. Schema commercial com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from commercial.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de commercial" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='commercial'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='commercial' and table_name in ('app_user','audit_event','sales_invoice','tax_rate_schedule')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema commercial" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. Sales deixou de ser perfil vazio (ADR-036)" {
    $r = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value='commercial.customers.read'"
    $w = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Sales' and c.claim_value='commercial.customers.write'"
    if ($r -ne "1" -or $w -ne "1") { throw "Sales sem commercial.customers.read/write (read=$r write=$w)" }
    "Sales le e escreve clientes"
}

Test-Case "3. Registar cliente" {
    $body = @{
        name = "Kianda Lda"; taxId = $nif
        addressDetail = "Rua Rainha Ginga 12"; city = "Luanda"; country = "AO"
        email = "geral-$stamp@kianda.ao"
    } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.customerId) { throw "sem customerId na resposta" }
    $script:customerId = $r.customerId
    "cliente $nif registado"
}

Test-Case "4. NIF duplicado devolve 409 com o id do existente" {
    # Com espacos a volta: o NIF e normalizado antes de comparar, senao dois
    # clientes que so diferem no espacamento passariam como distintos.
    $body = @{ name = "Outro"; taxId = " $nif "; addressDetail = "Rua X"; city = "Benguela" } | ConvertTo-Json
    try {
        Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null
        throw "esperado 409, o pedido passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        $code = [int]$_.Exception.Response.StatusCode
        if ($code -ne 409) { throw "esperado 409, obtido $code" }
        $corpo = $_.ErrorDetails.Message | ConvertFrom-Json
        if ($corpo.customerId -ne $script:customerId) { throw "409 sem o id do cliente existente" }
    }
    "409 com o id do existente, e o NIF normalizado"
}

Test-Case "5. Campos obrigatorios do SAF-T sao impostos" {
    # Sem NIF: nao ha como identificar o cliente no documento fiscal.
    $semNif = @{ name = "X"; taxId = ""; addressDetail = "Rua X"; city = "Luanda" } | ConvertTo-Json
    $c1 = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $semNif -ContentType "application/json" -Headers $adminHeaders }
    if ($c1 -ne 400) { throw "sem NIF: esperado 400, obtido $c1" }

    # Pais tem de ser ISO 3166-1 alpha-2. "Angola" falha, "AO" passa.
    $paisMau = @{ name = "X"; taxId = "9$stamp"; addressDetail = "Rua X"; city = "Luanda"; country = "Angola" } | ConvertTo-Json
    $c2 = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $paisMau -ContentType "application/json" -Headers $adminHeaders }
    if ($c2 -ne 400) { throw "pais invalido: esperado 400, obtido $c2" }

    "sem NIF e pais nao-alpha2 recusados"
}

Test-Case "6. Morada substitui-se inteira ou nao se toca" {
    $parcial = @{ city = "Benguela" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/details" -Method Post -Body $parcial -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "morada parcial: esperado 400, obtido $code" }

    $inteira = @{ addressDetail = "Rua Nova 3"; city = "Benguela"; country = "AO" } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/details" -Method Post -Body $inteira -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $c = Invoke-RestMethod "$base/commercial/customers/$($script:customerId)" -Headers $adminHeaders
    if ($c.billingAddress.city -ne "Benguela") { throw "morada nao foi alterada" }
    "objecto de valor: parcial recusada, inteira aceite"
}

Test-Case "7. Desactivar esconde da listagem, includeInactive traz de volta" {
    $body = @{ active = $false } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/status" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/commercial/customers" -Headers $adminHeaders
    if ($activos.customerId -contains $script:customerId) { throw "cliente desactivado ainda aparece na lista de activos" }

    $todos = Invoke-RestMethod "$base/commercial/customers?includeInactive=true" -Headers $adminHeaders
    if ($todos.customerId -notcontains $script:customerId) { throw "cliente desactivado nao aparece com includeInactive" }

    # E volta a activo, para nao deixar lixo desactivado atras.
    $body = @{ active = $true } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/status" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "desactivar filtra; reactivar repoe"
}

Test-Case "8. Nao ha eliminacao de cliente (BR-14)" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($script:customerId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    $existe = Invoke-Sql "select count(*) from commercial.customer where tax_id='$nif'"
    if ($existe -ne "1") { throw "cliente desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "9. Registo de cliente e auditado" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='commercial.customer.registered' and entity_id='$($script:customerId)'"
    if ($n -ne "1") { throw "esperado 1 registo de auditoria, obtido $n" }
    $actor = Invoke-Sql "select count(*) from audit.audit_event where action='commercial.customer.registered' and entity_id='$($script:customerId)' and actor_id is null"
    if ($actor -ne "0") { throw "registo sem actor" }
    "1 registo, com actor"
}

Test-Case "10. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "11. NIF e unico na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select tax_id from commercial.customer group by tax_id having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup NIFs repetidos" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "12. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(180)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $c = Invoke-RestMethod "$base/commercial/customers/$($script:customerId)" -Headers $adminHeaders
    if ($c.taxId -ne $nif) { throw "cliente perdido ou alterado" }
    "cliente $nif intacto apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
