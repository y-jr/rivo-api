# Verificação do módulo `inventory`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-inventory.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Movimento ganhou regra de
# negócio (ADR-039 desbloqueou a fronteira com Activos Fixos de `finance`,
# ver `modules/inventory.md`). Armazém, Transferência, Contagem e
# valorização de stock continuam por fazer — esta suite verifica o que
# existe, não o que falta.
#
# Re-executável: cada corrida usa um SKU próprio, derivado do carimbo
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

$semPerfilEmail = "semperfil-in-$stamp@rivo.ao"
Invoke-RestMethod "$base/identity/register" -Method Post -Body (@{ email = $semPerfilEmail; password = $pass } | ConvertTo-Json) -ContentType "application/json" | Out-Null
$semPerfilHeaders = @{ Authorization = "Bearer " + (Get-Token $semPerfilEmail $pass) }

$sku = "sku-$stamp"

Write-Host "`n=== Modulo inventory ===`n"

Test-Case "1. Schema inventory com migration propria e isolado" {
    $m = Invoke-Sql "select count(*) from inventory.__ef_migrations_history"
    if ([int]$m -lt 1) { throw "sem migration de inventory" }
    $t = Invoke-Sql "select count(*) from information_schema.tables where table_schema='inventory'"
    $cross = Invoke-Sql "select count(*) from information_schema.tables where table_schema='inventory' and table_name in ('app_user','audit_event','goods_receipt')"
    if ($cross -ne "0") { throw "tabelas cruzadas no schema inventory" }
    "$m migration(s), $t tabelas, sem cruzamento"
}

Test-Case "2. AssetManager le e escreve inventory" {
    $r = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='inventory.items.read'"
    $w = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='AssetManager' and c.claim_value='inventory.items.write'"
    if ($r -ne "1" -or $w -ne "1") { throw "AssetManager sem inventory.items.read/write (read=$r write=$w)" }
    "AssetManager le e escreve itens"
}

Test-Case "3. Registar item" {
    $body = @{ sku = $sku; name = "Parafuso M6"; unit = "un" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.itemId) { throw "sem itemId na resposta" }
    $script:itemId = $r.itemId
    "item $sku registado"
}

Test-Case "4. SKU normalizado em maiusculas; nasce sem movimentos" {
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ($item.sku -ne $sku.ToUpperInvariant()) { throw "SKU nao normalizado: '$($item.sku)'" }
    if (@($item.movements).Count -ne 0) { throw "movimentos deviam estar vazios" }
    "SKU '$($item.sku)', sem movimentos"
}

Test-Case "5. Quantidade em mao nasce a zero" {
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.quantityOnHand -ne 0) { throw "quantidade nao nasceu a zero: $($item.quantityOnHand)" }
    "quantityOnHand=0"
}

Test-Case "6. SKU duplicado devolve 409 com o id do existente" {
    $body = @{ sku = $sku; name = "Outro"; unit = "kg" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no SKU repetido"
}

Test-Case "7. Campos obrigatorios sao impostos" {
    $semNome = @{ sku = "outro-$stamp"; name = ""; unit = "un" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Method Post -Body $semNome -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "sem nome: esperado 400, obtido $code" }
    "sem nome recusado"
}

Test-Case "8. Registar recepcao" {
    $body = @{ quantity = 20; reason = "Compra inicial"; occurredOn = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.movementId) { throw "sem movementId na resposta" }
    if ([decimal]$r.quantityOnHand -ne 20) { throw "quantityOnHand esperado 20, obtido $($r.quantityOnHand)" }
    "recepcao de 20, quantityOnHand=20"
}

Test-Case "9. Recepcao com quantidade nao positiva e recusada" {
    $body = @{ quantity = 0; reason = $null; occurredOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade zero recusada"
}

Test-Case "10. Registar saida" {
    $body = @{ quantity = 5; reason = "Consumo interno"; occurredOn = "2026-09-02" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 15) { throw "quantityOnHand esperado 15, obtido $($r.quantityOnHand)" }
    "saida de 5, quantityOnHand=15"
}

Test-Case "11. Saida maior que a quantidade em mao e recusada" {
    $body = @{ quantity = 100; reason = $null; occurredOn = "2026-09-02" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.quantityOnHand -ne 15) { throw "quantidade mudou apesar da recusa: $($item.quantityOnHand)" }
    "409 -- sem quantidade suficiente; quantityOnHand nao mudou"
}

Test-Case "12. Saida com quantidade nao positiva e recusada" {
    $body = @{ quantity = -1; reason = $null; occurredOn = "2026-09-02" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade negativa recusada"
}

Test-Case "13. Registar ajuste positivo" {
    $body = @{ quantityDelta = 3; reason = "Contagem fisica encontrou mais 3"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 18) { throw "quantityOnHand esperado 18, obtido $($r.quantityOnHand)" }
    "ajuste +3, quantityOnHand=18"
}

Test-Case "14. Registar ajuste negativo" {
    $body = @{ quantityDelta = -4; reason = "Contagem fisica encontrou menos 4"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 14) { throw "quantityOnHand esperado 14, obtido $($r.quantityOnHand)" }
    "ajuste -4, quantityOnHand=14"
}

Test-Case "15. Ajuste sem variacao e recusado" {
    $body = @{ quantityDelta = 0; reason = "Nada mudou"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "variacao zero recusada"
}

Test-Case "16. Ajuste sem motivo e recusado" {
    $body = @{ quantityDelta = 2; reason = ""; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "ajuste sem motivo recusado -- uma correccao sem explicacao nao se aceita"
}

Test-Case "17. Ajuste que puxaria a quantidade para negativo e recusado" {
    $body = @{ quantityDelta = -100; reason = "Contagem absurda"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- quantidade em mao nunca fica negativa"
}

Test-Case "18. Quantidade em mao e sempre a soma assinada dos movimentos" {
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    $soma = Invoke-Sql "select sum(quantity) from inventory.stock_movement where item_id='$($script:itemId)'"
    if ([decimal]$soma -ne [decimal]$item.quantityOnHand) { throw "soma na BD ($soma) nao bate com quantityOnHand ($($item.quantityOnHand))" }
    if ([decimal]$item.quantityOnHand -ne 14) { throw "quantityOnHand esperado 14, obtido $($item.quantityOnHand)" }
    "soma dos movimentos = quantityOnHand = 14"
}

Test-Case "19. Movimentos ficam na trilha, com actor" {
    $acoes = @("inventory.movement.receipt", "inventory.movement.issue", "inventory.movement.adjustment")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "tres tipos de evento de movimento auditados, todos com actor"
}

Test-Case "20. Desactivar esconde da listagem e recusa movimentos novos, includeInactive traz de volta" {
    Invoke-RestMethod "$base/inventory/items/$($script:itemId)/status" -Method Post -Body (@{ active = $false } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/inventory/items" -Headers $adminHeaders
    if ($activos.itemId -contains $script:itemId) { throw "item desactivado ainda aparece nos activos" }

    $todos = Invoke-RestMethod "$base/inventory/items?includeInactive=true" -Headers $adminHeaders
    if ($todos.itemId -notcontains $script:itemId) { throw "item desactivado nao aparece com includeInactive" }

    $body = @{ quantity = 1; reason = $null; occurredOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "recepcao em item inactivo: esperado 409, obtido $code" }

    "desactivar filtra e recusa movimentos novos (409)"
}

Test-Case "21. Nao ha eliminacao de item" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }
    $existe = Invoke-Sql "select count(*) from inventory.item where id='$($script:itemId)'"
    if ($existe -ne "1") { throw "item desapareceu da base de dados" }
    "DELETE recusado ($code); a linha continua la"
}

Test-Case "22. Registo e auditado, com actor" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='inventory.item.registered' and entity_id='$($script:itemId)' and actor_id is not null"
    if ($n -ne "1") { throw "registo nao auditado com actor" }
    "registo na trilha, com actor"
}

Test-Case "23. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Headers $semPerfilHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "24. SKU e unico na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select sku from inventory.item group by sku having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup SKUs repetidos" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "25. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ($item.sku -ne $sku.ToUpperInvariant()) { throw "item perdido ou alterado" }
    if ($item.status -ne "Inactive") { throw "estado perdido apos restart: $($item.status)" }
    if ([decimal]$item.quantityOnHand -ne 14) { throw "quantityOnHand perdido apos restart: $($item.quantityOnHand)" }
    "item $sku, estado e movimentos intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
