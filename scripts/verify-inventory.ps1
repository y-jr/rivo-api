# Verificação do módulo `inventory`.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-inventory.ps1
#
# Deixou de ser esqueleto puro a 2026-08-30: Movimento ganhou regra de
# negócio (ADR-039 desbloqueou a fronteira com Activos Fixos de `finance`,
# ver `modules/inventory.md`). Armazém e Transferência entre armazéns desde
# 2026-08-31 — retrofit: todo o movimento passou a exigir armazém, e
# QuantityOnHand ganhou uma leitura por armazém além do total agregado.
# Contagem (inventariação periódica, com geração de Ajuste no fecho) e
# Valorização (custo médio ponderado, decisão de negócio do utilizador)
# também desde 2026-08-31.
#
# Re-executável: cada corrida usa um SKU e códigos de armazém próprios,
# derivados do carimbo temporal.

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

# Era uma conta sem perfil nenhum ate o ADR-059 tirar o registo publico: nao ha
# forma de criar uma agora, e o perfil e obrigatorio no convite. `Cliente` e o
# mais estreito que existe -- tem `documents.write` e mais nada --, por isso
# continua a provar o mesmo 403 por falta de permissao de inventory.
$semPermissaoEmail = "sempermissao-in-$stamp@rivo.ao"
New-RivoConta -Email $semPermissaoEmail -Password $pass -AdminHeaders $adminHeaders -Perfil "Cliente" | Out-Null
$semPermissaoHeaders = @{ Authorization = "Bearer " + (Get-Token $semPermissaoEmail $pass) }

$sku = "sku-$stamp"
$codigoA = "a-$stamp"
$codigoB = "b-$stamp"

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

Test-Case "5. Quantidade em mao e custo nascem a zero, sem armazens ainda" {
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.quantityOnHand -ne 0) { throw "quantidade nao nasceu a zero: $($item.quantityOnHand)" }
    if ([decimal]$item.averageCost -ne 0) { throw "averageCost nao nasceu a zero: $($item.averageCost)" }
    if ([decimal]$item.totalValue -ne 0) { throw "totalValue nao nasceu a zero: $($item.totalValue)" }
    if (@($item.quantitiesByWarehouse).Count -ne 0) { throw "quantitiesByWarehouse devia estar vazio" }
    "quantityOnHand=0, averageCost=0, sem repartição por armazém"
}

Test-Case "6. SKU duplicado devolve 409 com o id do existente" {
    $body = @{ sku = $sku; name = "Outro"; unit = "kg" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no SKU repetido"
}

Test-Case "7. Campos obrigatorios do item sao impostos" {
    $semNome = @{ sku = "outro-$stamp"; name = ""; unit = "un" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Method Post -Body $semNome -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "sem nome: esperado 400, obtido $code" }
    "sem nome recusado"
}

# --- Armazém -------------------------------------------------------------

Test-Case "8. Registar armazem A" {
    $body = @{ code = $codigoA; name = "Armazem A" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/warehouses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.warehouseId) { throw "sem warehouseId na resposta" }
    $script:warehouseAId = $r.warehouseId
    "armazem $codigoA registado"
}

Test-Case "9. Registar armazem B" {
    $body = @{ code = $codigoB; name = "Armazem B" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/warehouses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.warehouseId) { throw "sem warehouseId na resposta" }
    $script:warehouseBId = $r.warehouseId
    "armazem $codigoB registado"
}

Test-Case "10. Codigo de armazem duplicado devolve 409 com o id do existente" {
    $body = @{ code = $codigoA; name = "Outro nome" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/warehouses" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no codigo repetido"
}

Test-Case "11. Campos obrigatorios do armazem sao impostos" {
    $semNome = @{ code = "outro-$stamp"; name = "" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/warehouses" -Method Post -Body $semNome -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "sem nome: esperado 400, obtido $code" }
    "sem nome recusado"
}

Test-Case "12. Listar e obter armazem" {
    $todos = Invoke-RestMethod "$base/inventory/warehouses" -Headers $adminHeaders
    if ($todos.warehouseId -notcontains $script:warehouseAId) { throw "armazem A nao aparece na listagem" }

    $armazem = Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseAId)" -Headers $adminHeaders
    if ($armazem.code -ne $codigoA.ToUpperInvariant()) { throw "codigo nao normalizado: '$($armazem.code)'" }
    if ($armazem.status -ne "Active") { throw "armazem devia nascer Active: $($armazem.status)" }
    "listagem e leitura individual correctas"
}

# --- Movimento (agora exige armazem e, na Recepção, custo unitario) --------

Test-Case "13. Registar recepcao no armazem A" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 20; unitCost = 100; reason = "Compra inicial"; occurredOn = "2026-09-01" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.movementId) { throw "sem movementId na resposta" }
    if ([decimal]$r.quantityOnHand -ne 20) { throw "quantityOnHand esperado 20, obtido $($r.quantityOnHand)" }
    if ([decimal]$r.quantityAtWarehouse -ne 20) { throw "quantityAtWarehouse esperado 20, obtido $($r.quantityAtWarehouse)" }
    if ([decimal]$r.averageCost -ne 100) { throw "averageCost esperado 100 (primeira recepcao), obtido $($r.averageCost)" }
    "recepcao de 20 a 100/un no armazem A, quantityOnHand=20, averageCost=100"
}

Test-Case "14. Recepcao com quantidade nao positiva e recusada" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 0; unitCost = 100; reason = $null; occurredOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade zero recusada"
}

Test-Case "15. Recepcao com custo unitario negativo e recusada" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 1; unitCost = -1; reason = $null; occurredOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "custo unitario negativo recusado"
}

Test-Case "16. Recepcao num armazem inexistente devolve 404" {
    $body = @{ warehouseId = [guid]::NewGuid().ToString(); quantity = 1; unitCost = 100; reason = $null; occurredOn = "2026-09-01" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "armazem inexistente -- 404"
}

Test-Case "17. Registar saida no armazem A" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 5; reason = "Consumo interno"; occurredOn = "2026-09-02" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 15) { throw "quantityOnHand esperado 15, obtido $($r.quantityOnHand)" }
    if ([decimal]$r.averageCost -ne 100) { throw "averageCost nao devia mudar numa saida, obtido $($r.averageCost)" }
    "saida de 5, quantityOnHand=15, averageCost continua 100"
}

Test-Case "18. Saida maior que a quantidade nesse armazem e recusada" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 100; reason = $null; occurredOn = "2026-09-02" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.quantityOnHand -ne 15) { throw "quantidade mudou apesar da recusa: $($item.quantityOnHand)" }
    "409 -- sem quantidade suficiente; quantityOnHand nao mudou"
}

Test-Case "19. Saida com quantidade nao positiva e recusada" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = -1; reason = $null; occurredOn = "2026-09-02" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade negativa recusada"
}

Test-Case "20. Registar ajuste positivo no armazem A" {
    $body = @{ warehouseId = $script:warehouseAId; quantityDelta = 3; reason = "Contagem fisica encontrou mais 3"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 18) { throw "quantityOnHand esperado 18, obtido $($r.quantityOnHand)" }
    "ajuste +3, quantityOnHand=18"
}

Test-Case "21. Registar ajuste negativo no armazem A" {
    $body = @{ warehouseId = $script:warehouseAId; quantityDelta = -4; reason = "Contagem fisica encontrou menos 4"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 14) { throw "quantityOnHand esperado 14, obtido $($r.quantityOnHand)" }
    "ajuste -4, quantityOnHand=14"
}

Test-Case "22. Ajuste sem variacao e recusado" {
    $body = @{ warehouseId = $script:warehouseAId; quantityDelta = 0; reason = "Nada mudou"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "variacao zero recusada"
}

Test-Case "23. Ajuste sem motivo e recusado" {
    $body = @{ warehouseId = $script:warehouseAId; quantityDelta = 2; reason = ""; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "ajuste sem motivo recusado -- uma correccao sem explicacao nao se aceita"
}

Test-Case "24. Ajuste que puxaria a quantidade nesse armazem para negativo e recusado" {
    $body = @{ warehouseId = $script:warehouseAId; quantityDelta = -100; reason = "Contagem absurda"; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/adjustments" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- quantidade em mao nesse armazem nunca fica negativa"
}

Test-Case "25. Saida no armazem B e recusada mesmo havendo quantidade no armazem A" {
    # 14 no armazem A, 0 no armazem B -- nao se pode "emprestar" de outro
    # armazem numa saida. E exactamente o que o retrofit de Armazem existe
    # para impedir.
    $body = @{ warehouseId = $script:warehouseBId; quantity = 1; reason = $null; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- armazem B nao tem quantidade propria, apesar do total ser 14"
}

Test-Case "26. Movimento num armazem inactivo e recusado" {
    Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseBId)/status" -Method Post -Body (@{ active = $false } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $body = @{ warehouseId = $script:warehouseBId; quantity = 1; unitCost = 100; reason = $null; occurredOn = "2026-09-03" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    # Reactiva para os casos seguintes (transferencia precisa do armazem B utilizavel).
    Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseBId)/status" -Method Post -Body (@{ active = $true } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
    "409 -- armazem inactivo recusa movimento; reactivado para os casos seguintes"
}

# --- Transferencia ---------------------------------------------------------

Test-Case "27. Transferencia atomica move quantidade entre armazens, sem alterar o total nem o custo medio" {
    $body = @{ fromWarehouseId = $script:warehouseAId; toWarehouseId = $script:warehouseBId; quantity = 6; reason = "Reorganizacao"; occurredOn = "2026-09-04" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/transfers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.outMovementId -or -not $r.inMovementId) { throw "sem outMovementId/inMovementId na resposta" }
    if ([decimal]$r.quantityAtSource -ne 8) { throw "quantityAtSource esperado 8, obtido $($r.quantityAtSource)" }
    if ([decimal]$r.quantityAtDestination -ne 6) { throw "quantityAtDestination esperado 6, obtido $($r.quantityAtDestination)" }
    if ([decimal]$r.averageCost -ne 100) { throw "averageCost nao devia mudar numa transferencia, obtido $($r.averageCost)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.quantityOnHand -ne 14) { throw "total agregado mudou com a transferencia: $($item.quantityOnHand)" }
    "A=8, B=6, total continua 14, averageCost continua 100"
}

Test-Case "28. Transferencia maior que a quantidade na origem e recusada" {
    $body = @{ fromWarehouseId = $script:warehouseAId; toWarehouseId = $script:warehouseBId; quantity = 100; reason = $null; occurredOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/transfers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- sem quantidade suficiente na origem"
}

Test-Case "29. Transferencia com armazem de origem igual ao de destino e recusada" {
    $body = @{ fromWarehouseId = $script:warehouseAId; toWarehouseId = $script:warehouseAId; quantity = 1; reason = $null; occurredOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/transfers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "400 -- origem e destino nao podem ser o mesmo armazem"
}

Test-Case "30. Transferencia com quantidade nao positiva e recusada" {
    $body = @{ fromWarehouseId = $script:warehouseAId; toWarehouseId = $script:warehouseBId; quantity = 0; reason = $null; occurredOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/transfers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade zero recusada"
}

Test-Case "31. Transferencia de/para armazem inexistente devolve 404" {
    $body = @{ fromWarehouseId = [guid]::NewGuid().ToString(); toWarehouseId = $script:warehouseBId; quantity = 1; reason = $null; occurredOn = "2026-09-04" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/transfers" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "armazem de origem inexistente -- 404"
}

# --- Invariantes -----------------------------------------------------------

Test-Case "32. Quantidade em mao e sempre a soma assinada dos movimentos" {
    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    $soma = Invoke-Sql "select sum(quantity) from inventory.stock_movement where item_id='$($script:itemId)'"
    if ([decimal]$soma -ne [decimal]$item.quantityOnHand) { throw "soma na BD ($soma) nao bate com quantityOnHand ($($item.quantityOnHand))" }
    if ([decimal]$item.quantityOnHand -ne 14) { throw "quantityOnHand esperado 14, obtido $($item.quantityOnHand)" }
    "soma dos movimentos = quantityOnHand = 14"
}

Test-Case "33. Quantidade por armazem bate com a soma dos movimentos desse armazem" {
    $somaA = Invoke-Sql "select sum(quantity) from inventory.stock_movement where item_id='$($script:itemId)' and warehouse_id='$($script:warehouseAId)'"
    $somaB = Invoke-Sql "select sum(quantity) from inventory.stock_movement where item_id='$($script:itemId)' and warehouse_id='$($script:warehouseBId)'"
    if ([decimal]$somaA -ne 8) { throw "armazem A esperado 8, obtido $somaA" }
    if ([decimal]$somaB -ne 6) { throw "armazem B esperado 6, obtido $somaB" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    $vistaA = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:warehouseAId }
    $vistaB = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:warehouseBId }
    if ([decimal]$vistaA.quantityOnHand -ne 8) { throw "vista do armazem A esperada 8, obtida $($vistaA.quantityOnHand)" }
    if ([decimal]$vistaB.quantityOnHand -ne 6) { throw "vista do armazem B esperada 6, obtida $($vistaB.quantityOnHand)" }
    if ([decimal]$vistaA.value -ne 800) { throw "valor do armazem A esperado 800, obtido $($vistaA.value)" }
    if ([decimal]$vistaB.value -ne 600) { throw "valor do armazem B esperado 600, obtido $($vistaB.value)" }
    "A=8, B=6 -- BD e vista concordam; valores 800 e 600 (a 100/un)"
}

Test-Case "34. Movimentos ficam na trilha, com actor" {
    $acoes = @("inventory.movement.receipt", "inventory.movement.issue", "inventory.movement.adjustment", "inventory.movement.transfer")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "quatro tipos de evento de movimento auditados, todos com actor"
}

Test-Case "35. Registo e (des)activacao de armazem ficam na trilha, com actor" {
    $acoes = @("inventory.warehouse.registered", "inventory.warehouse.deactivated", "inventory.warehouse.reactivated")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "registo, desactivacao e reactivacao de armazem auditados, todos com actor"
}

# --- Contagem --------------------------------------------------------------
# Estado antes desta seccao: item activo, quantityOnHandAt(A)=8, at(B)=6,
# quantityOnHand global=14, averageCost=100 (herdado da recepcao do caso 13).

Test-Case "36. Abrir contagem no armazem A" {
    $body = @{ warehouseId = $script:warehouseAId; occurredOn = "2026-09-05" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.countId) { throw "sem countId na resposta" }
    $script:countId = $r.countId
    "contagem $($script:countId) aberta no armazem A"
}

Test-Case "37. Abrir contagem num armazem inexistente devolve 404" {
    $body = @{ warehouseId = [guid]::NewGuid().ToString(); occurredOn = "2026-09-05" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "armazem inexistente -- 404"
}

Test-Case "38. Abrir contagem num armazem inactivo e recusado" {
    Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseBId)/status" -Method Post -Body (@{ active = $false } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $body = @{ warehouseId = $script:warehouseBId; occurredOn = "2026-09-05" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseBId)/status" -Method Post -Body (@{ active = $true } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null
    "409 -- armazem inactivo recusa contagem; reactivado para os casos seguintes"
}

Test-Case "39. Acrescentar linha contada" {
    $body = @{ itemId = $script:itemId; countedQuantity = 5 } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/counts/$($script:countId)/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if (-not $r.lineId) { throw "sem lineId na resposta" }
    if ([decimal]$r.expectedQuantity -ne 8) { throw "expectedQuantity esperado 8, obtido $($r.expectedQuantity)" }
    if ([decimal]$r.countedQuantity -ne 5) { throw "countedQuantity esperado 5, obtido $($r.countedQuantity)" }
    if ([decimal]$r.variance -ne -3) { throw "variance esperada -3, obtida $($r.variance)" }
    "esperado 8, contado 5, variancia -3"
}

Test-Case "40. Acrescentar o mesmo item outra vez na mesma contagem e recusado" {
    $body = @{ itemId = $script:itemId; countedQuantity = 6 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- nao se conta o mesmo item duas vezes na mesma sessao"
}

Test-Case "41. Linha com quantidade contada negativa e recusada" {
    # Reutiliza o item ja contado no caso 39 -- a validacao da quantidade
    # acontece antes da verificacao de duplicado, por isso confirma
    # exactamente o 400 esperado, e nao um 409 por outro motivo.
    $body = @{ itemId = $script:itemId; countedQuantity = -1 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "quantidade negativa recusada"
}

Test-Case "42. Acrescentar linha numa contagem inexistente devolve 404" {
    $body = @{ itemId = $script:itemId; countedQuantity = 1 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$([guid]::NewGuid())/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "contagem inexistente -- 404"
}

Test-Case "43. Acrescentar linha de item inexistente devolve 404" {
    $body = @{ itemId = [guid]::NewGuid().ToString(); countedQuantity = 1 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "item inexistente -- 404"
}

Test-Case "44. Obter contagem mostra a linha, com esperado/contado/variancia" {
    $contagem = Invoke-RestMethod "$base/inventory/counts/$($script:countId)" -Headers $adminHeaders
    if (@($contagem.lines).Count -ne 1) { throw "esperada 1 linha, obtidas $(@($contagem.lines).Count)" }
    $linha = $contagem.lines[0]
    if ($linha.itemId -ne $script:itemId) { throw "linha do item errado" }
    if ([decimal]$linha.variance -ne -3) { throw "variancia esperada -3, obtida $($linha.variance)" }
    "1 linha, item certo, variancia -3"
}

Test-Case "45. Fechar contagem sem nenhuma linha e recusado" {
    $body = @{ warehouseId = $script:warehouseAId; occurredOn = "2026-09-05" } | ConvertTo-Json
    $vazia = Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders

    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($vazia.countId)/close" -Method Post -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- uma contagem sem linhas nao tem o que confirmar"
}

Test-Case "46. Fechar contagem com variancia gera ajuste, tudo na mesma transaccao" {
    $r = Invoke-RestMethod "$base/inventory/counts/$($script:countId)/close" -Method Post -Headers $adminHeaders
    if (@($r.generatedAdjustmentIds).Count -ne 1) { throw "esperado 1 ajuste gerado, obtidos $(@($r.generatedAdjustmentIds).Count)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    $vistaA = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:warehouseAId }
    if ([decimal]$vistaA.quantityOnHand -ne 5) { throw "armazem A esperado 5 apos o ajuste, obtido $($vistaA.quantityOnHand)" }
    if ([decimal]$item.quantityOnHand -ne 11) { throw "quantityOnHand global esperado 11, obtido $($item.quantityOnHand)" }
    if ([decimal]$item.averageCost -ne 100) { throw "averageCost nao devia mudar num ajuste, obtido $($item.averageCost)" }

    $contagem = Invoke-RestMethod "$base/inventory/counts/$($script:countId)" -Headers $adminHeaders
    if ($contagem.status -ne "Closed") { throw "estado esperado Closed, obtido $($contagem.status)" }
    "1 ajuste gerado; armazem A passa a 5, total passa a 11, averageCost continua 100"
}

Test-Case "47. Fechar a mesma contagem outra vez e recusado" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/close" -Method Post -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo fecho"
}

Test-Case "48. Acrescentar linha numa contagem ja fechada e recusado" {
    $body = @{ itemId = $script:itemId; countedQuantity = 5 } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/lines" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- contagem fechada nao aceita linha nova"
}

Test-Case "49. Cancelar uma contagem aberta, com motivo" {
    $body = @{ warehouseId = $script:warehouseAId; occurredOn = "2026-09-05" } | ConvertTo-Json
    $aberta = Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    $script:cancelledCountId = $aberta.countId

    $cancelBody = @{ reason = "Aberta no armazem errado" } | ConvertTo-Json
    Invoke-RestMethod "$base/inventory/counts/$($script:cancelledCountId)/cancellation" -Method Post -Body $cancelBody -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $contagem = Invoke-RestMethod "$base/inventory/counts/$($script:cancelledCountId)" -Headers $adminHeaders
    if ($contagem.status -ne "Cancelled") { throw "estado esperado Cancelled, obtido $($contagem.status)" }
    if ($contagem.cancellationReason -ne "Aberta no armazem errado") { throw "motivo nao gravado" }
    "contagem cancelada, com motivo gravado"
}

Test-Case "50. Cancelar sem motivo e recusado" {
    $body = @{ warehouseId = $script:warehouseAId; occurredOn = "2026-09-05" } | ConvertTo-Json
    $outra = Invoke-RestMethod "$base/inventory/counts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders

    $cancelBody = @{ reason = "" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($outra.countId)/cancellation" -Method Post -Body $cancelBody -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "sem motivo recusado"
}

Test-Case "51. Cancelar uma contagem ja fechada e recusado" {
    $cancelBody = @{ reason = "Tarde demais" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)/cancellation" -Method Post -Body $cancelBody -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 -- contagem fechada e facto historico"
}

Test-Case "52. Cancelar a mesma contagem cancelada outra vez e recusado" {
    $cancelBody = @{ reason = "Segundo motivo" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:cancelledCountId)/cancellation" -Method Post -Body $cancelBody -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "409 no segundo cancelamento"
}

Test-Case "53. Listar contagens filtradas por armazem" {
    $todas = Invoke-RestMethod "$base/inventory/counts?warehouseId=$($script:warehouseAId)" -Headers $adminHeaders
    if ($todas.countId -notcontains $script:countId) { throw "contagem fechada nao aparece na listagem filtrada" }
    "listagem filtrada por armazem inclui a contagem fechada"
}

Test-Case "54. Nao ha eliminacao de contagem, e fica na trilha com actor" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/counts/$($script:countId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE devia ser recusado, obtido $code" }

    $acoes = @("inventory.count.opened", "inventory.count.line_added", "inventory.count.closed", "inventory.count.cancelled")
    foreach ($accao in $acoes) {
        $n = Invoke-Sql "select count(*) from audit.audit_event where action='$accao' and actor_id is not null"
        if ([int]$n -lt 1) { throw "sem evento auditado para '$accao'" }
    }
    "DELETE recusado ($code); quatro tipos de evento de contagem auditados, todos com actor"
}

# --- Valorização (custo médio ponderado) ------------------------------------
# Estado antes desta seccao: item activo, quantityOnHandAt(A)=5, at(B)=6,
# quantityOnHand global=11, averageCost=100 (herdado da recepcao do caso 13
# -- nem Saida, Ajuste, Transferencia nem Contagem o alteraram).

Test-Case "55. Segunda recepcao recalcula o custo medio ponderado" {
    # (11 a 100 = 1100) + (9 a 300 = 2700) = 3800 / 20 = 190.
    $body = @{ warehouseId = $script:warehouseAId; quantity = 9; unitCost = 300; reason = "Segunda compra, preco subiu"; occurredOn = "2026-09-06" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 20) { throw "quantityOnHand esperado 20, obtido $($r.quantityOnHand)" }
    if ([decimal]$r.averageCost -ne 190) { throw "averageCost esperado 190, obtido $($r.averageCost)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ([decimal]$item.totalValue -ne 3800) { throw "totalValue esperado 3800, obtido $($item.totalValue)" }
    "11 a 100 + 9 a 300 -- averageCost recalculado para 190, totalValue=3800"
}

Test-Case "56. Saida nao muda o custo medio, mas fica registada ao custo corrente" {
    $body = @{ warehouseId = $script:warehouseAId; quantity = 4; reason = "Consumo apos a segunda compra"; occurredOn = "2026-09-06" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/issues" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders
    if ([decimal]$r.quantityOnHand -ne 16) { throw "quantityOnHand esperado 16, obtido $($r.quantityOnHand)" }
    if ([decimal]$r.averageCost -ne 190) { throw "averageCost nao devia mudar, obtido $($r.averageCost)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    $ultimo = $item.movements | Sort-Object recordedAt | Select-Object -Last 1
    if ([decimal]$ultimo.unitCost -ne 190) { throw "unitCost do movimento esperado 190, obtido $($ultimo.unitCost)" }
    if ([decimal]$ultimo.value -ne -760) { throw "value do movimento esperado -760, obtido $($ultimo.value)" }
    "saida de 4 ao custo corrente (190); movimento vale -760"
}

Test-Case "57. Valorizacao por periodo soma o valor dos movimentos na janela" {
    $entradas = Invoke-RestMethod "$base/inventory/valuation?from=2026-09-06&to=2026-09-06" -Headers $adminHeaders
    $entrada = $entradas | Where-Object { $_.itemId -eq $script:itemId }
    if (-not $entrada) { throw "item nao aparece na valorizacao do periodo" }
    # Recepcao (+9 a 300 = 2700) e Saida (-4 a 190 = -760) -- 2700 - 760 = 1940.
    if ([decimal]$entrada.periodValue -ne 1940) { throw "periodValue esperado 1940, obtido $($entrada.periodValue)" }
    "periodo 2026-09-06: recepcao (+2700) e saida (-760) -- periodValue=1940"
}

Test-Case "58. Janela invertida na valorizacao e recusada" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/valuation?from=2026-09-06&to=2026-09-01" -Headers $adminHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "data inicial depois da final -- 400"
}

Test-Case "59. Valorizacao exclui itens sem movimento na janela" {
    $entradas = Invoke-RestMethod "$base/inventory/valuation?from=2027-01-01&to=2027-01-31" -Headers $adminHeaders
    if (@($entradas | Where-Object { $_.itemId -eq $script:itemId }).Count -ne 0) { throw "item apareceu numa janela sem movimento nenhum" }
    "janela sem movimento -- item nao aparece"
}

Test-Case "60. Desactivar item esconde da listagem e recusa movimentos novos, includeInactive traz de volta" {
    Invoke-RestMethod "$base/inventory/items/$($script:itemId)/status" -Method Post -Body (@{ active = $false } | ConvertTo-Json) -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $activos = Invoke-RestMethod "$base/inventory/items" -Headers $adminHeaders
    if ($activos.itemId -contains $script:itemId) { throw "item desactivado ainda aparece nos activos" }

    $todos = Invoke-RestMethod "$base/inventory/items?includeInactive=true" -Headers $adminHeaders
    if ($todos.itemId -notcontains $script:itemId) { throw "item desactivado nao aparece com includeInactive" }

    $body = @{ warehouseId = $script:warehouseAId; quantity = 1; unitCost = 100; reason = $null; occurredOn = "2026-09-07" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)/movements/receipts" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders }
    if ($code -ne 409) { throw "recepcao em item inactivo: esperado 409, obtido $code" }

    "desactivar filtra e recusa movimentos novos (409)"
}

Test-Case "61. Nao ha eliminacao de item nem de armazem" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE de item devia ser recusado, obtido $code" }
    $existeItem = Invoke-Sql "select count(*) from inventory.item where id='$($script:itemId)'"
    if ($existeItem -ne "1") { throw "item desapareceu da base de dados" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseAId)" -Method Delete -Headers $adminHeaders }
    if ($code -ne 405 -and $code -ne 404) { throw "DELETE de armazem devia ser recusado, obtido $code" }
    $existeArmazem = Invoke-Sql "select count(*) from inventory.warehouse where id='$($script:warehouseAId)'"
    if ($existeArmazem -ne "1") { throw "armazem desapareceu da base de dados" }

    "DELETE recusado em item e armazem ($code); as linhas continuam la"
}

Test-Case "62. Registo e auditado, com actor" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='inventory.item.registered' and entity_id='$($script:itemId)' and actor_id is not null"
    if ($n -ne "1") { throw "registo nao auditado com actor" }
    "registo na trilha, com actor"
}

Test-Case "63. Autorizacao: sem token 401, sem perfil 403" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/inventory/items" -Headers $semPermissaoHeaders }
    if ($code -ne 403) { throw "sem perfil: esperado 403, obtido $code" }
    "401 e 403 correctos"
}

Test-Case "64. SKU e unico na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select sku from inventory.item group by sku having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup SKUs repetidos" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

Test-Case "65. Codigo de armazem e unico na base de dados" {
    $dup = Invoke-Sql "select count(*) from (select code from inventory.warehouse group by code having count(*)>1) d"
    if ($dup -ne "0") { throw "$dup codigos repetidos" }
    "indice unico e a segunda linha; a verificacao no caso de uso e a primeira"
}

# --- Governanca das divergencias de contagem (ADR-064) ----------------------
#
# Ate 2026-09-17, fechar uma contagem corrigia o stock de imediato, fosse a
# diferenca de uma unidade ou de mil. O motivo gravado no ajuste era o
# identificador da contagem, que nao explica nada a quem o le.

$script:govItemId = $null
$script:govCountId = $null
$script:govArmazem = $null
$script:govAprovador = $null
$script:govCargo = $null

Test-Case "67. Cenario: artigo caro recebido, e uma contagem que encontra menos" {
    $script:govArmazem = (Invoke-RestMethod "$base/inventory/warehouses" -Method Post -ContentType "application/json" `
            -Headers $adminHeaders -Body (@{ code = "GOV-$stamp"; name = "Armazem Governanca $stamp" } | ConvertTo-Json)).warehouseId

    $script:govItemId = (Invoke-RestMethod "$base/inventory/items" -Method Post -ContentType "application/json" `
            -Headers $adminHeaders -Body (@{ sku = "GOV-$stamp"; name = "Artigo caro"; unit = "un" } | ConvertTo-Json)).itemId

    Invoke-RestMethod "$base/inventory/items/$($script:govItemId)/receipts" -Method Post -ContentType "application/json" `
        -Headers $adminHeaders `
        -Body (@{ warehouseId = $script:govArmazem; quantity = 100; unitCost = 250; reason = "Recepcao inicial" } | ConvertTo-Json) | Out-Null

    $script:govCountId = (Invoke-RestMethod "$base/inventory/counts" -Method Post -ContentType "application/json" `
            -Headers $adminHeaders -Body (@{ warehouseId = $script:govArmazem } | ConvertTo-Json)).countId

    Invoke-RestMethod "$base/inventory/counts/$($script:govCountId)/lines" -Method Post -ContentType "application/json" `
        -Headers $adminHeaders `
        -Body (@{ itemId = $script:govItemId; countedQuantity = 60 } | ConvertTo-Json) | Out-Null

    "100 recebidas a 250; contadas 60 -- falta de 40, que vale 10 000"
}

Test-Case "68. Com alcada configurada, fechar NAO corrige o stock -- fica pendente de decisao" {
    # Alcada: quem decide e um colaborador com cargo, como em todos os outros
    # processos (ADR-050).
    $script:govAprovador = New-RivoColaboradorComConta -Email "inv-apr-$stamp@rivo.ao" `
        -Nome "Aprovador Inventario $stamp" -AdminHeaders $adminHeaders -Perfil "Admin"

    $script:govCargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" `
            -Headers $adminHeaders `
            -Body (@{ name = "Aprovador Inventario $stamp"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

    Invoke-RestMethod "$base/hr/employees/$($script:govAprovador.EmployeeId)/positions" -Method Post `
        -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ positionId = $script:govCargo } | ConvertTo-Json) | Out-Null

    Clear-RivoApprovalPolicies -ProcessType "inventory.stock_count" -Headers $adminHeaders
    Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ processType = "inventory.stock_count"; steps = @(@{ approverPositionId = $script:govCargo }) } | ConvertTo-Json -Depth 5) | Out-Null

    # O Admin do bootstrap nao tem colaborador ligado, e quem submete tem de ter
    # (ADR-057). Quem fecha esta contagem e o aprovador -- que neste caso nao
    # pode decidir sobre o proprio pedido (BR-2), e por isso a decisao vem a
    # seguir por outra conta.
    $quemFecha = New-RivoColaboradorComConta -Email "inv-conta-$stamp@rivo.ao" `
        -Nome "Contador Inventario $stamp" -AdminHeaders $adminHeaders -Perfil "Admin"

    $resposta = Invoke-WebRequest "$base/inventory/counts/$($script:govCountId)/close" -Method Post `
        -Headers $quemFecha.Headers -SkipHttpErrorCheck
    if ($resposta.StatusCode -ne 202) { throw "esperado 202, obtido $($resposta.StatusCode): $($resposta.Content)" }

    $corpo = $resposta.Content | ConvertFrom-Json
    if (-not $corpo.approvalRequestId) { throw "202 sem processo de aprovacao" }
    if ([decimal]$corpo.varianceValue -ne 10000) { throw "valor da divergencia esperado 10000, obtido $($corpo.varianceValue)" }

    # **A propriedade central: o stock nao mudou.**
    $item = Invoke-RestMethod "$base/inventory/items/$($script:govItemId)" -Headers $adminHeaders
    $noArmazem = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:govArmazem }
    if ([decimal]$noArmazem.quantityOnHand -ne 100) { throw "o stock foi corrigido antes da decisao: $($noArmazem.quantityOnHand)" }

    $estado = (Invoke-RestMethod "$base/inventory/counts/$($script:govCountId)" -Headers $adminHeaders).status
    if ($estado -ne "PendingApproval") { throw "estado esperado PendingApproval, obtido $estado" }

    $script:govProcesso = $corpo.approvalRequestId
    "202, divergencia de 10 000 submetida; armazem continua com 100"
}

Test-Case "69. A submissao fica na trilha, e distinta do fecho" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='inventory.count.submitted' and entity_id='$($script:govCountId)'"
    if ($n -ne "1") { throw "$n registos de submissao, esperado 1" }

    $fechos = Invoke-Sql "select count(*) from audit.audit_event where action='inventory.count.closed' and entity_id='$($script:govCountId)'"
    if ($fechos -ne "0") { throw "a contagem aparece como fechada antes de ser decidida" }

    $ajustes = Invoke-Sql "select count(*) from audit.audit_event where action='inventory.movement.adjustment' and new_value like '%$($script:govCountId)%'"
    if ($ajustes -ne "0") { throw "$ajustes ajustes auditados antes da decisao" }

    "submissao registada; nem fecho nem ajustes antes de decidir"
}

Test-Case "70. Enquanto ninguem decide, pedir a decisao devolve 202 e nao corrige nada" {
    $resposta = Invoke-WebRequest "$base/inventory/counts/$($script:govCountId)/decision" -Method Post `
        -Headers $adminHeaders -SkipHttpErrorCheck
    if ($resposta.StatusCode -ne 202) { throw "esperado 202, obtido $($resposta.StatusCode)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:govItemId)" -Headers $adminHeaders
    $noArmazem = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:govArmazem }
    if ([decimal]$noArmazem.quantityOnHand -ne 100) { throw "stock alterado sem decisao: $($noArmazem.quantityOnHand)" }
    "202, e o armazem continua com 100"
}

Test-Case "71. Aprovada, a decisao aplica o ajuste que o fecho reteve" {
    Invoke-RestMethod "$base/approval/requests/$($script:govProcesso)/decisions" -Method Post `
        -ContentType "application/json" -Headers $script:govAprovador.Headers `
        -Body (@{ action = "Approved"; notes = "Quebra confirmada em armazem." } | ConvertTo-Json) | Out-Null

    $r = Invoke-RestMethod "$base/inventory/counts/$($script:govCountId)/decision" -Method Post -Headers $adminHeaders
    if (@($r.generatedAdjustmentIds).Count -ne 1) { throw "esperado 1 ajuste, obtido $(@($r.generatedAdjustmentIds).Count)" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:govItemId)" -Headers $adminHeaders
    $noArmazem = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:govArmazem }
    if ([decimal]$noArmazem.quantityOnHand -ne 60) { throw "stock esperado 60 apos a decisao, obtido $($noArmazem.quantityOnHand)" }

    $estado = (Invoke-RestMethod "$base/inventory/counts/$($script:govCountId)" -Headers $adminHeaders).status
    if ($estado -ne "Closed") { throw "estado esperado Closed, obtido $estado" }
    "aprovada; armazem passa a 60 e a contagem fecha"
}

Test-Case "72. O motivo do ajuste explica a divergencia, em vez de citar um identificador" {
    $motivo = Invoke-Sql "select top 1 reason from inventory.stock_movement where item_id='$($script:govItemId)' and type='Adjustment' order by recorded_at desc"

    # Era "Contagem 01a0b2c3-...", que cumpria a regra de exigir motivo sem
    # explicar nada a quem o lia.
    foreach ($pedaco in @("esperado 100", "contado 60", "falta 40")) {
        if ($motivo -notmatch [regex]::Escape($pedaco)) { throw "o motivo nao diz '$pedaco': '$motivo'" }
    }
    "motivo: '$motivo'"
}

Test-Case "73. O fecho resume a divergencia e o ajuste leva o esperado e o contado" {
    $fecho = Invoke-Sql "select top 1 new_value from audit.audit_event where action='inventory.count.closed' and entity_id='$($script:govCountId)' order by occurred_at desc"
    foreach ($campo in @('"linesCounted":1', '"linesWithVariance":1', '"shortfall":40', '"surplus":0', '"approvalRequired":true')) {
        if ($fecho -notmatch [regex]::Escape($campo)) { throw "o fecho nao regista $campo -- '$fecho'" }
    }

    $ajuste = Invoke-Sql "select top 1 new_value from audit.audit_event where action='inventory.movement.adjustment' and new_value like '%$($script:govCountId)%' order by occurred_at desc"
    foreach ($campo in @('"expectedQuantity":100', '"countedQuantity":60', '"quantity":-40')) {
        if ($ajuste -notmatch [regex]::Escape($campo)) { throw "o ajuste nao regista $campo -- '$ajuste'" }
    }

    "fecho com faltas e sobras; ajuste com esperado, contado e diferenca"
}

Test-Case "74. Sem alcada que cubra o valor, a contagem fecha e corrige como sempre" {
    # A politica activa exige decisao; desactiva-se, e uma divergencia nova passa
    # a aplicar-se de imediato. E o que distingue este processo dos outros: nao
    # haver politica nao e impedimento, e "isto nao precisa de aprovacao".
    Clear-RivoApprovalPolicies -ProcessType "inventory.stock_count" -Headers $adminHeaders

    $contagem = (Invoke-RestMethod "$base/inventory/counts" -Method Post -ContentType "application/json" `
            -Headers $adminHeaders -Body (@{ warehouseId = $script:govArmazem } | ConvertTo-Json)).countId

    Invoke-RestMethod "$base/inventory/counts/$contagem/lines" -Method Post -ContentType "application/json" `
        -Headers $adminHeaders -Body (@{ itemId = $script:govItemId; countedQuantity = 58 } | ConvertTo-Json) | Out-Null

    $r = Invoke-RestMethod "$base/inventory/counts/$contagem/close" -Method Post -Headers $adminHeaders
    if (@($r.generatedAdjustmentIds).Count -ne 1) { throw "esperado 1 ajuste imediato" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:govItemId)" -Headers $adminHeaders
    $noArmazem = $item.quantitiesByWarehouse | Where-Object { $_.warehouseId -eq $script:govArmazem }
    if ([decimal]$noArmazem.quantityOnHand -ne 58) { throw "stock esperado 58, obtido $($noArmazem.quantityOnHand)" }

    # E fica escrito que nao houve alcada -- a diferenca entre nao ter sido
    # preciso e ter sido contornado.
    $fecho = Invoke-Sql "select top 1 new_value from audit.audit_event where action='inventory.count.closed' and entity_id='$contagem' order by occurred_at desc"
    if ($fecho -notmatch [regex]::Escape('"approvalRequired":false')) { throw "o fecho nao regista a ausencia de alcada: '$fecho'" }

    "sem alcada: fecha, corrige para 58, e a trilha diz que nao houve governanca"
}

Test-Case "75. A suite nao deixa politica de contagem activa atras de si" {
    Clear-RivoApprovalPolicies -ProcessType "inventory.stock_count" -Headers $adminHeaders

    $activas = Invoke-Sql "select count(*) from approval.policy where process_type='inventory.stock_count' and is_active=1"
    if ($activas -ne "0") { throw "$activas politica(s) ficaram activas" }
    "nenhuma politica de contagem activa"
}

Test-Case "76. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $item = Invoke-RestMethod "$base/inventory/items/$($script:itemId)" -Headers $adminHeaders
    if ($item.sku -ne $sku.ToUpperInvariant()) { throw "item perdido ou alterado" }
    if ($item.status -ne "Inactive") { throw "estado perdido apos restart: $($item.status)" }
    if ([decimal]$item.quantityOnHand -ne 16) { throw "quantityOnHand perdido apos restart: $($item.quantityOnHand)" }
    if ([decimal]$item.averageCost -ne 190) { throw "averageCost perdido apos restart: $($item.averageCost)" }

    $armazem = Invoke-RestMethod "$base/inventory/warehouses/$($script:warehouseBId)" -Headers $adminHeaders
    if ($armazem.status -ne "Active") { throw "estado do armazem B perdido apos restart: $($armazem.status)" }

    $contagem = Invoke-RestMethod "$base/inventory/counts/$($script:countId)" -Headers $adminHeaders
    if ($contagem.status -ne "Closed") { throw "estado da contagem perdido apos restart: $($contagem.status)" }

    "item $sku, armazens, contagem e custo medio intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
