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

Test-Case "12. Ligar uma conta a um cliente (ADR-043)" {
    $contaEmail = "cliente-conta-$stamp@rivo-teste.local"
    $regBody = @{ email = $contaEmail; password = $pass } | ConvertTo-Json
    $reg = Invoke-RestMethod "$base/identity/register" -Method Post -Body $regBody -ContentType "application/json"
    $script:contaUserId = $reg.userId

    $ligarBody = @{ userId = $script:contaUserId } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account" -Method Post -Body $ligarBody -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $ligado = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and user_id='$($script:contaUserId)'"
    if ($ligado -ne "1") { throw "user_id nao ficou gravado no cliente" }
    "conta ligada ao cliente $nif"
}

Test-Case "13. Ligar cliente inexistente devolve 404" {
    $ligarBody = @{ userId = [Guid]::NewGuid().ToString() } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$([Guid]::NewGuid())/account" -Method Post -Body $ligarBody -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "cliente inexistente -- 404"
}

Test-Case "14. Ligar uma conta ja ligada a outro cliente e recusado" {
    $body2 = @{ name = "Segundo Cliente"; taxId = "55$stamp"; addressDetail = "Rua Y"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $r2 = Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $body2 -ContentType "application/json" -Headers $adminHeaders

    $ligarBody = @{ userId = $script:contaUserId } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($r2.customerId)/account" -Method Post -Body $ligarBody -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- a conta ja tem um cliente, nao se liga a um segundo"
}

Test-Case "15. UserId e unico na base de dados (commercial.customer)" {
    $dup = Invoke-Sql "select count(*) from (select user_id from commercial.customer where user_id is not null group by user_id having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup contas ligadas a mais de um cliente" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "16. Ligar conta exige a mesma permissao de escrever no cliente" {
    $ligarBody = @{ userId = [Guid]::NewGuid().ToString() } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account" -Method Post -Body $ligarBody -ContentType "application/json" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "403 sem commercial.customers.write"
}

# --- Religar, desligar e historico (ADR-055) --------------------------------
#
# Ate 2026-09-05, ligar uma conta a um cliente que ja tinha outra substituia-a
# EM SILENCIO: o portal mudava de dono sem registo, e a conta anterior perdia
# o acesso sem explicacao.

Test-Case "16b. Religar por cima e recusado, nao substitui (ADR-055)" {
    $outraEmail = "cliente-outra-$stamp@rivo-teste.local"
    $reg = Invoke-RestMethod "$base/identity/register" -Method Post `
        -Body (@{ email = $outraEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json"
    $script:outraContaId = $reg.userId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account" -Method Post `
            -Body (@{ userId = $script:outraContaId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders
    }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    # O vinculo original tem de continuar intacto -- e o que antes nao
    # acontecia.
    $intacto = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and user_id='$($script:contaUserId)'"
    if ($intacto -ne "1") { throw "o vinculo original foi substituido" }
    "409 e o dono do portal nao mudou"
}

Test-Case "16c. Auto-ligacao recusada com 403 (ADR-055)" {
    # Quem ligue a propria conta a um cliente passa a poder submeter
    # comprovativos de pagamento como esse cliente (ADR-044).
    $adminUserId = Invoke-Sql "select id from [identity].app_user where email='$($dotenv["BOOTSTRAP_ADMIN_EMAIL"])'"
    $b = @{ name = "Cliente Auto $stamp"; taxId = "77$stamp"; addressDetail = "Rua Z"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $alvo = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/commercial/customers/$alvo/account" -Method Post `
            -Body (@{ userId = $adminUserId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders
    }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "403 e nao 409: nao e o estado que impede, e quem pede"
}

Test-Case "16d. Desligar liberta o cliente e fecha o episodio" {
    $r = Invoke-WebRequest "$base/commercial/customers/$($script:customerId)/account" -Method Delete `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }

    $livre = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and user_id is null"
    if ($livre -ne "1") { throw "o vinculo nao foi removido" }

    $fechado = Invoke-Sql "select count(*) from commercial.customer_account_link where customer_id='$($script:customerId)' and user_id='$($script:contaUserId)' and unlinked_on is not null"
    if ($fechado -ne "1") { throw "o episodio nao foi fechado" }
    "conta desligada e episodio fechado"
}

Test-Case "16e. Desligar de novo e repetivel; inexistente da 404" {
    $r = Invoke-WebRequest "$base/commercial/customers/$($script:customerId)/account" -Method Delete `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($r.StatusCode -ne 204) { throw "esperado 204, obtido $($r.StatusCode)" }

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/commercial/customers/$([Guid]::NewGuid())/account" -Method Delete -Headers $adminHeaders
    }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "204 repetivel; 404 para cliente inexistente"
}

Test-Case "16f. A conta libertada liga-se a outro cliente" {
    # A sequencia que corrige um vinculo errado, e a unica que existe desde que
    # religar por cima passou a ser recusado.
    $b = @{ name = "Cliente Certo $stamp"; taxId = "88$stamp"; addressDetail = "Rua W"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $script:clienteCertoId = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId

    Invoke-RestMethod "$base/commercial/customers/$($script:clienteCertoId)/account" -Method Post `
        -Body (@{ userId = $script:contaUserId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $ligado = Invoke-Sql "select count(*) from commercial.customer where id='$($script:clienteCertoId)' and user_id='$($script:contaUserId)'"
    if ($ligado -ne "1") { throw "a conta libertada nao ligou ao cliente novo" }
    "desligar e voltar a ligar e o caminho de correccao"
}

Test-Case "16g. Campo e historico nunca divergem" {
    $orfaos = Invoke-Sql "select count(*) from commercial.customer c where c.user_id is not null and not exists (select 1 from commercial.customer_account_link l where l.customer_id=c.id and l.user_id=c.user_id and l.unlinked_on is null)"
    if ($orfaos -ne "0") { throw "$orfaos vinculo(s) activo(s) sem episodio aberto" }

    $fantasmas = Invoke-Sql "select count(*) from commercial.customer_account_link l where l.unlinked_on is null and not exists (select 1 from commercial.customer c where c.id=l.customer_id and c.user_id=l.user_id)"
    if ($fantasmas -ne "0") { throw "$fantasmas episodio(s) aberto(s) sem vinculo correspondente" }
    "nenhum vinculo sem episodio, nenhum episodio sem vinculo"
}

Test-Case "16h. O historico mostra a transferencia dos dois lados" {
    $h = @(Invoke-RestMethod "$base/commercial/customers/$($script:clienteCertoId)/account-history" -Headers $adminHeaders)
    if ($h.Count -lt 1) { throw "historico vazio para um cliente com conta" }
    if ($h[0].userId -ne $script:contaUserId) { throw "o episodio aberto nao e o da conta ligada" }
    if ($h[0].unlinkedOn) { throw "o episodio deveria estar aberto" }

    # E o cliente anterior mantem o episodio fechado -- a conta saiu de um e
    # entrou noutro, com registo dos dois lados.
    $anterior = @(Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account-history" -Headers $adminHeaders)
    $fechado = $anterior | Where-Object { $_.userId -eq $script:contaUserId -and $_.unlinkedOn }
    if (-not $fechado) { throw "o cliente anterior nao mostra o episodio fechado" }
    "a mesma conta, fechada num cliente e aberta noutro"
}

Test-Case "16i. Cliente sem conta da lista vazia; inexistente da 404" {
    $b = @{ name = "Nunca Teve Conta $stamp"; taxId = "99$stamp"; addressDetail = "Rua V"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $nunca = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId

    # Corpo cru: Invoke-RestMethod devolve $null para array vazio, e @($null)
    # tem um elemento -- o mesmo engano que ja deu falso positivo no verify-hr.
    $corpo = (Invoke-WebRequest "$base/commercial/customers/$nunca/account-history" -Headers $adminHeaders).Content
    if ($corpo.Trim() -ne "[]") { throw "esperado '[]', obtido '$corpo'" }

    $code = Get-StatusCode {
        Invoke-RestMethod "$base/commercial/customers/$([Guid]::NewGuid())/account-history" -Headers $adminHeaders
    }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "lista vazia e 404 dizem coisas diferentes"
}

Test-Case "17. Atribuir vendedor responsavel a um cliente (ADR-045)" {
    $vendedor = Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ fullName = "Vendedor CO $stamp" } | ConvertTo-Json)
    $script:vendedorId = $vendedor.employeeId

    $body = @{ employeeId = $script:vendedorId } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $atribuido = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and assigned_to_employee_id='$($script:vendedorId)'"
    if ($atribuido -ne "1") { throw "assigned_to_employee_id nao ficou gravado" }
    "vendedor $($script:vendedorId) atribuido ao cliente $nif"
}

Test-Case "18. Atribuir a cliente inexistente devolve 404" {
    $body = @{ employeeId = $script:vendedorId } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$([Guid]::NewGuid())/owner" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "cliente inexistente -- 404"
}

Test-Case "19. Atribuir colaborador inexistente devolve 404, sem gravar nada" {
    $body = @{ employeeId = [Guid]::NewGuid().ToString() } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }

    $atribuido = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and assigned_to_employee_id='$($script:vendedorId)'"
    if ($atribuido -ne "1") { throw "atribuicao valida anterior foi apagada por um pedido invalido" }
    "colaborador inexistente -- 404, atribuicao anterior intacta"
}

Test-Case "20. Atribuir null remove a atribuicao; exige a mesma permissao de escrever no cliente" {
    $codeSemPerfil = Get-StatusCode { Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body (@{ employeeId = $script:vendedorId } | ConvertTo-Json) -ContentType "application/json" -Headers $semPerfilHeaders }
    if ($codeSemPerfil -ne 403) { throw "esperado 403 sem commercial.customers.write, obtido $codeSemPerfil" }

    $body = @{ employeeId = $null } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $atribuido = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and assigned_to_employee_id is null"
    if ($atribuido -ne "1") { throw "atribuicao nao foi removida" }

    # Repoe para o caso 21 verificar a sobrevivencia ao reinicio.
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body (@{ employeeId = $script:vendedorId } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "403 sem permissao; null remove a atribuicao"
}

Test-Case "21. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $c = Invoke-RestMethod "$base/commercial/customers/$($script:customerId)" -Headers $adminHeaders
    if ($c.taxId -ne $nif) { throw "cliente perdido ou alterado" }

    # A conta ja nao esta neste cliente: o caso 16d desligou-a e o 16f ligou-a
    # ao cliente certo. E onde ela esta agora que tem de sobreviver.
    $ligado = Invoke-Sql "select count(*) from commercial.customer where id='$($script:clienteCertoId)' and user_id='$($script:contaUserId)'"
    if ($ligado -ne "1") { throw "ligacao da conta perdida apos restart" }

    # E o historico tambem: e o registo de quem pode agir como quem, e
    # perde-lo no reinicio seria pior do que nao o ter.
    $episodios = Invoke-Sql "select count(*) from commercial.customer_account_link where user_id='$($script:contaUserId)'"
    if ([int]$episodios -lt 2) { throw "historico do vinculo perdido: esperados >=2 episodios, encontrados $episodios" }

    $atribuido = Invoke-Sql "select count(*) from commercial.customer where id='$($script:customerId)' and assigned_to_employee_id='$($script:vendedorId)'"
    if ($atribuido -ne "1") { throw "vendedor responsavel perdido apos restart" }

    "cliente $nif, vendedor responsavel, e $episodios episodios de vinculo intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
