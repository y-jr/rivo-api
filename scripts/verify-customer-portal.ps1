# Verificação da camada de composição `Rivo.CustomerPortal` (Portal do
# Cliente, identidade externa — ADR-043).
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-customer-portal.ps1

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
$pass = "Rivo!Password2026"
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$de = "2020-01-01"
$ate = "2030-12-31"

function Get-Token {
    param([string]$Email, [string]$Password)
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json
    return (Invoke-RestMethod "$base/identity/login" -Method Post -Body $body -ContentType "application/json").accessToken
}

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

$salesEmail = "cportal-vendas-$stamp@rivo.ao"
$b = @{ email = $salesEmail; password = $pass } | ConvertTo-Json
$salesUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
$b = @{ profile = "Sales" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$salesUserId/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
$salesHeaders = @{ Authorization = "Bearer " + (Get-Token $salesEmail $pass) }

$financeEmail = "cportal-fin-$stamp@rivo.ao"
$b = @{ email = $financeEmail; password = $pass } | ConvertTo-Json
$financeUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
$b = @{ profile = "Finance" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$financeUserId/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
$financeHeaders = @{ Authorization = "Bearer " + (Get-Token $financeEmail $pass) }

# Taxa fiscal aberta, efectiva desde sempre — a factura da suite precisa dela.
$codigoTaxa = "P" + ("$stamp".Substring("$stamp".Length - 6))
$b = @{ code = $codigoTaxa; description = "IVA - suite customer-portal" } | ConvertTo-Json
$scheduleId = (Invoke-RestMethod "$base/fiscal/tax-rates" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).scheduleId
$b = @{ percentage = 14; effectiveFrom = "2000-01-01"; legalInstrument = "Suite customer-portal" } | ConvertTo-Json
Invoke-RestMethod "$base/fiscal/tax-rates/$scheduleId/versions" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

# Upload de comprovativo (ADR-044), mesmo mecanismo de verify-fleet.ps1 -- ver
# ali o porque da portabilidade Windows/Linux (curl.exe vs curl).
$temp = [System.IO.Path]::GetTempPath()
$curl = if (Get-Command curl.exe -ErrorAction SilentlyContinue) { "curl.exe" } else { "curl" }
$tempFile = Join-Path $temp "rivo-comprovativo-$stamp.txt"
Set-Content -Path $tempFile -Value "Comprovativo de transferencia bancaria de teste - $stamp" -NoNewline -Encoding UTF8

function Invoke-Upload {
    param([string]$FilePath, [string]$Category, [string]$Token)

    return (& $curl -s -X POST "$base/documents" `
        -H "Authorization: Bearer $Token" `
        -F "file=@$FilePath" `
        -F "category=$Category" 2>$null)
}

Write-Host "`n=== Camada de composicao customer-portal ===`n"

Test-Case "1. Sem autenticacao -> 401" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" }
    if ($code -ne 401) { throw "esperado 401, obtido $code" }
    "HTTP 401"
}

Test-Case "2. Admin autenticado sem cliente ligado -> 403 -- o portal nao contorna a falta de vinculo" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $adminHeaders }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- Admin nao tem commercial.Customer ligado, e o portal recusa em vez de adivinhar"
}

$script:customerId = $null
$script:ownEmail = "cliente-$stamp@rivo-teste.local"
Test-Case "3. Registar cliente, registar conta e ligar (ADR-043)" {
    $b = @{
        name = "Kianda Lda $stamp"; taxId = "57$stamp"
        addressDetail = "Rua Rainha Ginga 12"; city = "Luanda"; country = "AO"
    } | ConvertTo-Json
    $script:customerId = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId

    $b = @{ email = $script:ownEmail; password = $pass } | ConvertTo-Json
    $script:ownUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId

    $b = @{ userId = $script:ownUserId } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/account" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    "cliente $($script:customerId) e conta ligados"
}

$script:base0 = $null
Test-Case "4. Cliente novo ve o proprio perfil, sem facturas nem divida" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $script:base0 = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders

    if ($script:base0.customerId -ne $script:customerId) { throw "customerId nao e o proprio" }
    if ($script:base0.customerName -notlike "Kianda Lda*") { throw "nome errado: $($script:base0.customerName)" }
    if ($script:base0.invoices.Count -ne 0) { throw "cliente novo com facturas: $($script:base0.invoices.Count)" }
    "customerId=$($script:customerId), receita=$($script:base0.netRevenue), em-aberto=$($script:base0.outstanding), 0 facturas"
}

Test-Case "5. Facturar ao cliente faz a receita e o em-aberto subirem, e a factura aparece na lista" {
    $b = @{
        customerId = $script:customerId; issuedOn = "2026-08-15"; taxPointDate = "2026-08-15"
        lines = @(@{ description = "Servico"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $script:factura = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $b -ContentType "application/json" -Headers $salesHeaders

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders

    if (($vista.netRevenue - $script:base0.netRevenue) -ne 100000) { throw "receita devia subir 100000, subiu $($vista.netRevenue - $script:base0.netRevenue)" }
    if (($vista.outstanding - $script:base0.outstanding) -ne 114000) { throw "em-aberto devia subir 114000 (bruto), subiu $($vista.outstanding - $script:base0.outstanding)" }
    if ($vista.invoices.Count -ne 1) { throw "esperada 1 factura, obtidas $($vista.invoices.Count)" }
    if ($vista.invoices[0].number -ne $script:factura.number) { throw "numero da factura nao bate: $($vista.invoices[0].number)" }
    "receita +100000, em-aberto +114000, factura $($script:factura.number) na lista"
}

Test-Case "6. Extracto de conta corrente: factura, nota de credito e recibo, com saldo corrido" {
    $b = @{
        salesInvoiceId = $script:factura.invoiceId; reason = "Desconto acordado"
        lines = @(@{ description = "Desconto"; quantity = 1; unitPrice = 14000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/credit-notes" -Method Post -Body $b -ContentType "application/json" -Headers $financeHeaders | Out-Null

    $b = @{ method = "MB"; settlements = @(@{ salesInvoiceId = $script:factura.invoiceId; amount = 50000 }) } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/receipts" -Method Post -Body $b -ContentType "application/json" -Headers $financeHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $extracto = Invoke-RestMethod "$base/customer-portal/me/statement?from=$de&to=$ate" -Headers $ownHeaders

    if ($extracto.openingBalance -ne 0) { throw "abertura devia ser 0, veio $($extracto.openingBalance)" }
    if ($extracto.lines.Count -ne 3) { throw "esperadas 3 linhas (factura, nota, recibo), obtidas $($extracto.lines.Count)" }
    if ($extracto.lines[0].documentType -ne "Factura" -or $extracto.lines[0].balanceAfter -ne 114000) { throw "linha 1 errada: $($extracto.lines[0] | ConvertTo-Json -Compress)" }
    if ($extracto.lines[1].documentType -ne "NotaCredito" -or $extracto.lines[1].balanceAfter -ne 98040) { throw "linha 2 errada: $($extracto.lines[1] | ConvertTo-Json -Compress)" }
    if ($extracto.lines[2].documentType -ne "Recibo" -or $extracto.lines[2].balanceAfter -ne 48040) { throw "linha 3 errada: $($extracto.lines[2] | ConvertTo-Json -Compress)" }
    if ($extracto.closingBalance -ne 48040) { throw "fecho devia ser 48040, veio $($extracto.closingBalance)" }
    "abertura=0, 3 movimentos, fecho=48040"
}

Test-Case "7. Janela invertida e recusada com 400" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$ate&to=$de" -Headers $ownHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    $code2 = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me/statement?from=$ate&to=$de" -Headers $ownHeaders }
    if ($code2 -ne 400) { throw "esperado 400 no extracto, obtido $code2" }
    "HTTP 400 -- data inicial depois da final, nas duas rotas"
}

Test-Case "8. Moeda tem omissao AOA" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders
    if ($vista.currency -ne "AOA") { throw "moeda por omissao devia ser AOA, veio '$($vista.currency)'" }
    "moeda='AOA' por omissao"
}

Test-Case "9. Outro utilizador sem cliente ligado -> 403, nunca ve o cliente de outro" {
    $e2 = "semvinculo-c-$stamp@rivo.ao"
    $b = @{ email = $e2; password = $pass } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json" | Out-Null
    $h2 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }

    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $h2 }
    if ($code -ne 403) { throw "esperado 403, obtido $code" }
    "HTTP 403 -- so ve o proprio, e o proprio nao existe para esta conta"
}

# A ligacao da conta (caso 3) so poe Customer.UserId -- nao atribui perfil
# nenhum, mesma distincao que ADR-043 faz ("so depois disso o perfil Cliente
# e atribuido"). Sem isto, o upload do comprovativo (documents.write) falhava
# com 403 antes de chegar a nenhuma regra de ADR-044.
$b = @{ profile = "Cliente" } | ConvertTo-Json
Invoke-RestMethod "$base/identity/users/$($script:ownUserId)/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

Test-Case "10. Cliente submete comprovativo de pagamento -- fica Pending" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $upload = Invoke-Upload $tempFile "comprovativo-pagamento" $ownHeaders.Authorization.Split(" ")[1] | ConvertFrom-Json
    $script:documentoId = $upload.documentId

    # Caso 6 deixou a factura original com 48040 em aberto (114000 - 15960 -
    # 50000). E o valor que se vai confirmar no caso seguinte.
    $b = @{
        salesInvoiceId = $script:factura.invoiceId; amount = 48040; paidOn = "2026-08-20"
        documentId = $script:documentoId
    } | ConvertTo-Json
    $script:pedidoId = (Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders).claimId

    $meus = Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Headers $ownHeaders
    if ($meus.Count -ne 1) { throw "esperado 1 pedido, obtidos $($meus.Count)" }
    if ($meus[0].status -ne "Pending") { throw "estado errado: $($meus[0].status)" }
    "pedido $($script:pedidoId) submetido, Pending, 48040"
}

Test-Case "11. Finance confirma o pedido -- gera o recibo e o extracto fecha a zero" {
    $pendentes = Invoke-RestMethod "$base/finance/payment-claims?customerId=$($script:customerId)&status=Pending" -Headers $financeHeaders
    if (-not ($pendentes | Where-Object { $_.id -eq $script:pedidoId })) { throw "pedido nao aparece na fila do finance" }

    $confirmacao = Invoke-RestMethod "$base/finance/payment-claims/$($script:pedidoId)/confirmation" -Method Post -Headers $financeHeaders
    if (-not $confirmacao.receiptId) { throw "confirmacao sem receiptId" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $meus = Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Headers $ownHeaders
    if (($meus | Where-Object { $_.id -eq $script:pedidoId }).status -ne "Confirmed") { throw "pedido nao ficou Confirmed" }

    $extracto = Invoke-RestMethod "$base/customer-portal/me/statement?from=$de&to=$ate" -Headers $ownHeaders
    if ($extracto.closingBalance -ne 0) { throw "fecho devia ser 0 apos confirmar o resto, veio $($extracto.closingBalance)" }
    "recibo $($confirmacao.receiptId), pedido Confirmed, fecho=0"
}

Test-Case "12. Pedido acima do em aberto -- 409, sem gateway nenhum a esconder o erro" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $upload = Invoke-Upload $tempFile "comprovativo-pagamento" $ownHeaders.Authorization.Split(" ")[1] | ConvertFrom-Json

    # A factura original ja nao deve nada (caso 11) -- qualquer valor excede.
    $b = @{
        salesInvoiceId = $script:factura.invoiceId; amount = 1000; paidOn = "2026-08-21"
        documentId = $upload.documentId
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "HTTP 409 -- factura ja liquidada"
}

Test-Case "13. Finance rejeita um pedido -- fica Rejected com motivo, sem apagar nada (BR-14)" {
    $b = @{
        customerId = $script:customerId; issuedOn = "2026-08-22"; taxPointDate = "2026-08-22"
        lines = @(@{ description = "Segundo servico"; quantity = 1; unitPrice = 100000; taxCode = $codigoTaxa })
    } | ConvertTo-Json
    $script:factura2 = Invoke-RestMethod "$base/finance/sales-invoices" -Method Post -Body $b -ContentType "application/json" -Headers $salesHeaders

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $upload = Invoke-Upload $tempFile "comprovativo-pagamento" $ownHeaders.Authorization.Split(" ")[1] | ConvertFrom-Json
    $b = @{
        salesInvoiceId = $script:factura2.invoiceId; amount = 114000; paidOn = "2026-08-23"
        documentId = $upload.documentId
    } | ConvertTo-Json
    $script:pedidoRejeitadoId = (Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders).claimId

    $b = @{ reason = "Comprovativo ilegivel." } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/payment-claims/$($script:pedidoRejeitadoId)/rejection" -Method Post -Body $b -ContentType "application/json" -Headers $financeHeaders | Out-Null

    $meus = Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Headers $ownHeaders
    $rejeitado = $meus | Where-Object { $_.id -eq $script:pedidoRejeitadoId }
    if ($rejeitado.status -ne "Rejected") { throw "estado errado: $($rejeitado.status)" }
    if ($rejeitado.rejectionReason -ne "Comprovativo ilegivel.") { throw "motivo perdido: $($rejeitado.rejectionReason)" }
    "pedido $($script:pedidoRejeitadoId) Rejected, factura2 continua em aberto (114000)"
}

Test-Case "14. Comprovativo de factura de outro cliente -- 404, nao revela a outrem" {
    $e2 = "cliente2-cp-$stamp@rivo-teste.local"
    $b = @{ name = "Segundo Cliente CP $stamp"; taxId = "58$stamp"; addressDetail = "Rua Z"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $cliente2Id = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId
    $b = @{ email = $e2; password = $pass } | ConvertTo-Json
    $user2Id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
    $b = @{ userId = $user2Id } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$cliente2Id/account" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $b = @{ profile = "Cliente" } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$user2Id/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $h2 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }
    $upload = Invoke-Upload $tempFile "comprovativo-pagamento" $h2.Authorization.Split(" ")[1] | ConvertFrom-Json

    $b = @{
        salesInvoiceId = $script:factura2.invoiceId; amount = 114000; paidOn = "2026-08-23"
        documentId = $upload.documentId
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Method Post -Body $b -ContentType "application/json" -Headers $h2 }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404 -- factura2 nao e do segundo cliente"
}

Test-Case "15. Cliente sem vendedor responsavel envia mensagem -- abre conversa Open" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $r = Invoke-RestMethod "$base/customer-portal/me/messages" -Method Post -Body (@{ body = "Preciso de ajuda com a factura." } | ConvertTo-Json) -ContentType "application/json" -Headers $ownHeaders
    $script:conversationId = $r.conversationId
    if (-not $script:conversationId) { throw "sem conversationId na resposta" }

    $minhas = Invoke-RestMethod "$base/customer-portal/me/messages" -Headers $ownHeaders
    if ($minhas.Count -ne 1) { throw "esperada 1 conversa, obtidas $($minhas.Count)" }
    if ($minhas[0].status -ne "Open") { throw "estado errado: $($minhas[0].status)" }
    if ($minhas[0].messages.Count -ne 1 -or $minhas[0].messages[0].sender -ne "Customer") { throw "mensagem inicial errada" }
    "conversa $($script:conversationId) aberta, Open, 1 mensagem do cliente"
}

Test-Case "16. Atribuir vendedor responsavel; a proxima mensagem notifica-o (ADR-045)" {
    $vendedorEmail = "cportal-vendedor-$stamp@rivo.ao"
    $b = @{ email = $vendedorEmail; password = $pass } | ConvertTo-Json
    $script:vendedorUserId = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
    $b = @{ profile = "Sales" } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$($script:vendedorUserId)/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $emp = Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ fullName = "Vendedor CP $stamp" } | ConvertTo-Json)
    $script:vendedorEmployeeId = $emp.employeeId

    # Passo proprio desde o ADR-054: a admissao ja nao aceita userId.
    Invoke-RestMethod "$base/hr/employees/$($script:vendedorEmployeeId)/account" -Method Post `
        -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ userId = $script:vendedorUserId } | ConvertTo-Json) | Out-Null

    $b = @{ employeeId = $script:vendedorEmployeeId } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$($script:customerId)/owner" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    Invoke-RestMethod "$base/customer-portal/me/messages" -Method Post -Body (@{ body = "Alguem pode responder?" } | ConvertTo-Json) -ContentType "application/json" -Headers $ownHeaders | Out-Null

    $aviso = Invoke-Sql "select count(*) from notifications.notification where recipient_user_id='$($script:vendedorUserId)' and type='messaging.conversation.message_received'"
    if ($aviso -ne "1") { throw "vendedor nao foi notificado (contagem=$aviso)" }

    $minhas = Invoke-RestMethod "$base/customer-portal/me/messages" -Headers $ownHeaders
    if ($minhas.Count -ne 1 -or $minhas[0].messages.Count -ne 2) { throw "segunda mensagem nao entrou na mesma conversa" }
    "vendedor $($script:vendedorEmployeeId) atribuido, notificado 1 vez, mesma conversa"
}

Test-Case "17. Outro Sales (nao o vendedor atribuido) responde -- caixa partilhada, nao controlo de acesso" {
    $fila = Invoke-RestMethod "$base/messaging/conversations?status=Open" -Headers $salesHeaders
    $entrada = $fila | Where-Object { $_.conversationId -eq $script:conversationId }
    if (-not $entrada) { throw "conversa nao aparece na fila de outro Sales" }
    if ($entrada.assignedToEmployeeId -ne $script:vendedorEmployeeId) { throw "assignedToEmployeeId nao bate: $($entrada.assignedToEmployeeId)" }

    Invoke-RestMethod "$base/messaging/conversations/$($script:conversationId)/messages" -Method Post -Body (@{ body = "Já estou a tratar disso." } | ConvertTo-Json) -ContentType "application/json" -Headers $salesHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $minhas = Invoke-RestMethod "$base/customer-portal/me/messages" -Headers $ownHeaders
    $ultima = $minhas[0].messages[-1]
    if ($ultima.sender -ne "Employee") { throw "resposta nao apareceu como Employee" }
    "Sales sem atribuicao respondeu -- ADR-045: atribuicao so decide quem e notificado"
}

Test-Case "18. Responder com corpo vazio -- 400" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/messaging/conversations/$($script:conversationId)/messages" -Method Post -Body (@{ body = "   " } | ConvertTo-Json) -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "HTTP 400 -- corpo vazio"
}

Test-Case "19. Fechar a conversa; responder depois de fechada -- 409" {
    Invoke-RestMethod "$base/messaging/conversations/$($script:conversationId)/closure" -Method Post -Headers $salesHeaders | Out-Null

    $code = Get-StatusCode { Invoke-RestMethod "$base/messaging/conversations/$($script:conversationId)/messages" -Method Post -Body (@{ body = "Ainda aqui?" } | ConvertTo-Json) -ContentType "application/json" -Headers $salesHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "HTTP 409 -- conversa fechada nao aceita resposta"
}

Test-Case "20. Nova mensagem do cliente depois de fechada -- abre outra conversa, nao reabre a anterior" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $r = Invoke-RestMethod "$base/customer-portal/me/messages" -Method Post -Body (@{ body = "Preciso de outra coisa agora." } | ConvertTo-Json) -ContentType "application/json" -Headers $ownHeaders
    if ($r.conversationId -eq $script:conversationId) { throw "reaproveitou a conversa fechada em vez de abrir outra" }
    $script:segundaConversationId = $r.conversationId

    $minhas = Invoke-RestMethod "$base/customer-portal/me/messages" -Headers $ownHeaders
    if ($minhas.Count -ne 2) { throw "esperadas 2 conversas, obtidas $($minhas.Count)" }
    $fechada = $minhas | Where-Object { $_.conversationId -eq $script:conversationId }
    $aberta = $minhas | Where-Object { $_.conversationId -eq $script:segundaConversationId }
    if ($fechada.status -ne "Closed") { throw "primeira conversa devia continuar Closed" }
    if ($aberta.status -ne "Open") { throw "segunda conversa devia ser Open" }
    "2 conversas: $($script:conversationId) Closed, $($script:segundaConversationId) Open"
}

Test-Case "21. Cliente abre um ticket de suporte -- fica Open, com assunto, e notifica o vendedor" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }

    # Baseline em vez de numero fixo: os casos 16 e 20 ja geraram avisos ao
    # mesmo vendedor por mensagens directas -- o que este caso verifica e
    # que abrir um ticket soma mais um, mesma NotifyAssignedOwner (ADR-046).
    $antes = [int](Invoke-Sql "select count(*) from notifications.notification where recipient_user_id='$($script:vendedorUserId)' and type='messaging.conversation.message_received'")

    $b = @{ subject = "Problema com login"; body = "Nao consigo entrar no portal." } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/customer-portal/me/tickets" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders
    $script:primeiroTicketId = $r.conversationId
    if (-not $script:primeiroTicketId) { throw "sem conversationId na resposta" }

    $meusTickets = Invoke-RestMethod "$base/customer-portal/me/tickets" -Headers $ownHeaders
    if ($meusTickets.Count -ne 1) { throw "esperado 1 ticket, obtidos $($meusTickets.Count)" }
    if ($meusTickets[0].kind -ne "Ticket" -or $meusTickets[0].subject -ne "Problema com login") { throw "ticket com forma errada: $($meusTickets[0] | ConvertTo-Json -Compress)" }
    if ($meusTickets[0].status -ne "Open") { throw "estado errado: $($meusTickets[0].status)" }

    $depois = [int](Invoke-Sql "select count(*) from notifications.notification where recipient_user_id='$($script:vendedorUserId)' and type='messaging.conversation.message_received'")
    if (($depois - $antes) -ne 1) { throw "esperado +1 aviso ao vendedor, subiu $($depois - $antes)" }

    "ticket $($script:primeiroTicketId) aberto, Open, assunto certo, vendedor notificado (+1)"
}

Test-Case "22. Segundo ticket fica aberto ao mesmo tempo que o primeiro (ao contrario de mensagens directas)" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $b = @{ subject = "Duvida sobre factura"; body = "Porque e que a factura tem este valor?" } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/customer-portal/me/tickets" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders
    $script:segundoTicketId = $r.conversationId
    if ($script:segundoTicketId -eq $script:primeiroTicketId) { throw "reaproveitou o primeiro ticket em vez de abrir outro" }

    $meusTickets = Invoke-RestMethod "$base/customer-portal/me/tickets" -Headers $ownHeaders
    if ($meusTickets.Count -ne 2) { throw "esperados 2 tickets abertos ao mesmo tempo, obtidos $($meusTickets.Count)" }
    if (($meusTickets | Where-Object { $_.status -eq "Open" }).Count -ne 2) { throw "os dois tickets deviam continuar Open" }

    "2 tickets abertos ao mesmo tempo: $($script:primeiroTicketId) e $($script:segundoTicketId)"
}

Test-Case "23. Cliente responde a UM dos seus tickets -- o outro fica intacto" {
    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $b = @{ body = "Continua sem funcionar, tentei de novo." } | ConvertTo-Json
    Invoke-RestMethod "$base/customer-portal/me/tickets/$($script:primeiroTicketId)/messages" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders | Out-Null

    $meusTickets = Invoke-RestMethod "$base/customer-portal/me/tickets" -Headers $ownHeaders
    $primeiro = $meusTickets | Where-Object { $_.conversationId -eq $script:primeiroTicketId }
    $segundo = $meusTickets | Where-Object { $_.conversationId -eq $script:segundoTicketId }
    if ($primeiro.messages.Count -ne 2) { throw "primeiro ticket devia ter 2 mensagens, tem $($primeiro.messages.Count)" }
    if ($segundo.messages.Count -ne 1) { throw "segundo ticket devia continuar com 1 mensagem, tem $($segundo.messages.Count)" }

    "primeiro ticket com 2 mensagens, segundo continua com 1"
}

Test-Case "24. Responder a ticket de outro cliente -- 404, nao revela a outrem" {
    $e2 = "cliente-ticket-$stamp@rivo-teste.local"
    $b = @{ name = "Terceiro Cliente CP $stamp"; taxId = "59$stamp"; addressDetail = "Rua W"; city = "Luanda"; country = "AO" } | ConvertTo-Json
    $cliente3Id = (Invoke-RestMethod "$base/commercial/customers" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders).customerId
    $b = @{ email = $e2; password = $pass } | ConvertTo-Json
    $user3Id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $b -ContentType "application/json").userId
    $b = @{ userId = $user3Id } | ConvertTo-Json
    Invoke-RestMethod "$base/commercial/customers/$cliente3Id/account" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null
    $b = @{ profile = "Cliente" } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$user3Id/roles" -Method Post -Body $b -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $h3 = @{ Authorization = "Bearer " + (Get-Token $e2 $pass) }
    $b = @{ body = "Sou de outro cliente, isto devia falhar." } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me/tickets/$($script:primeiroTicketId)/messages" -Method Post -Body $b -ContentType "application/json" -Headers $h3 }
    if ($code -ne 404) { throw "esperado 404, obtido $code" }
    "HTTP 404 -- ticket nao e do terceiro cliente"
}

Test-Case "25. Sales fecha um ticket; responder depois de fechado -- 409" {
    $fila = Invoke-RestMethod "$base/messaging/conversations?kind=Ticket&status=Open" -Headers $salesHeaders
    if (-not ($fila | Where-Object { $_.conversationId -eq $script:segundoTicketId })) { throw "segundo ticket nao aparece na fila filtrada por kind=Ticket" }

    Invoke-RestMethod "$base/messaging/conversations/$($script:segundoTicketId)/closure" -Method Post -Headers $salesHeaders | Out-Null

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $b = @{ body = "Ainda preciso de ajuda." } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/customer-portal/me/tickets/$($script:segundoTicketId)/messages" -Method Post -Body $b -ContentType "application/json" -Headers $ownHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $meusTickets = Invoke-RestMethod "$base/customer-portal/me/tickets" -Headers $ownHeaders
    $fechado = $meusTickets | Where-Object { $_.conversationId -eq $script:segundoTicketId }
    if ($fechado.status -ne "Closed") { throw "segundo ticket devia estar Closed" }
    $aberto = $meusTickets | Where-Object { $_.conversationId -eq $script:primeiroTicketId }
    if ($aberto.status -ne "Open") { throw "primeiro ticket nao devia ter sido afectado" }

    "segundo ticket fechado por Sales, resposta a fechado -- 409, primeiro ticket continua Open"
}

Test-Case "26. Vista, extracto, pedidos, mensagens e tickets sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)
    do {
        Start-Sleep -Seconds 4
        $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false }
    } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou a responder" }

    $ownHeaders = @{ Authorization = "Bearer " + (Get-Token $script:ownEmail $pass) }
    $vista = Invoke-RestMethod "$base/customer-portal/me?from=$de&to=$ate" -Headers $ownHeaders
    if ($vista.customerId -ne $script:customerId) { throw "vinculo nao sobreviveu ao reinicio" }
    if ($vista.invoices.Count -ne 2) { throw "facturas perdidas apos restart: $($vista.invoices.Count)" }

    # factura original liquidada (caso 11) + factura2 rejeitada e por isso
    # ainda em aberto (caso 13) -- fecho = 0 + 114000.
    $extracto = Invoke-RestMethod "$base/customer-portal/me/statement?from=$de&to=$ate" -Headers $ownHeaders
    if ($extracto.closingBalance -ne 114000) { throw "fecho do extracto perdido apos restart: $($extracto.closingBalance)" }

    $meus = Invoke-RestMethod "$base/customer-portal/me/payment-claims" -Headers $ownHeaders
    if ($meus.Count -ne 2) { throw "pedidos de confirmacao perdidos apos restart: $($meus.Count)" }
    if (-not ($meus | Where-Object { $_.status -eq "Confirmed" })) { throw "pedido confirmado perdido" }
    if (-not ($meus | Where-Object { $_.status -eq "Rejected" })) { throw "pedido rejeitado perdido" }

    $mensagens = Invoke-RestMethod "$base/customer-portal/me/messages" -Headers $ownHeaders
    if ($mensagens.Count -ne 2) { throw "conversas perdidas apos restart: $($mensagens.Count)" }
    $fechadaApos = $mensagens | Where-Object { $_.conversationId -eq $script:conversationId }
    if ($fechadaApos.status -ne "Closed" -or $fechadaApos.messages.Count -ne 3) { throw "primeira conversa alterada apos restart" }

    $tickets = Invoke-RestMethod "$base/customer-portal/me/tickets" -Headers $ownHeaders
    if ($tickets.Count -ne 2) { throw "tickets perdidos apos restart: $($tickets.Count)" }
    $primeiroApos = $tickets | Where-Object { $_.conversationId -eq $script:primeiroTicketId }
    if ($primeiroApos.status -ne "Open" -or $primeiroApos.subject -ne "Problema com login" -or $primeiroApos.messages.Count -ne 2) { throw "primeiro ticket alterado apos restart" }
    $segundoApos = $tickets | Where-Object { $_.conversationId -eq $script:segundoTicketId }
    if ($segundoApos.status -ne "Closed") { throw "segundo ticket devia continuar Closed apos restart" }

    "customerId=$($script:customerId), 2 facturas, fecho=114000, 2 pedidos, 2 conversas e 2 tickets intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
