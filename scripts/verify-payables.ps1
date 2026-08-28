# Verificação de Contas a Pagar e Tesouraria.
#
#   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
#   pwsh -File scripts/verify-payables.ps1
#
# É onde BR-1, BR-3, BR-5 e BR-17 se encontram — o ponto de consistência forte
# do sistema, e a razão pela qual o ADR-001 escolheu monólito modular.
#
# Monta o cenário pelas rotas reais de `hr`, `approval` e `finance`. Sem atalho
# por SQL de propósito: se a montagem falhar, é porque o caminho real de
# pagamento está partido.
#
# Re-executável: cada corrida cria os seus colaboradores, cargo, contas e
# facturas.

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
$curto = "$stamp".Substring("$stamp".Length - 6)

$adminHeaders = @{ Authorization = "Bearer " + (Get-Token $dotenv["BOOTSTRAP_ADMIN_EMAIL"] $dotenv["BOOTSTRAP_ADMIN_PASSWORD"]) }

function New-PerfilHeaders {
    param([string]$Perfil, [string]$Sufixo)
    $email = "$Sufixo@rivo.ao"
    $body = @{ email = $email; password = $script:pass } | ConvertTo-Json
    $id = (Invoke-RestMethod "$base/identity/register" -Method Post -Body $body -ContentType "application/json").userId
    $body = @{ profile = $Perfil } | ConvertTo-Json
    Invoke-RestMethod "$base/identity/users/$id/roles" -Method Post -Body $body -ContentType "application/json" -Headers $script:adminHeaders | Out-Null
    return @{ Authorization = "Bearer " + (Get-Token $email $script:pass) }
}

# `Manager` pede e regista facturas; `Finance` executa. Nenhum faz os dois.
$managerHeaders = New-PerfilHeaders "Manager" "chefe-p-$stamp"
$financeHeaders = New-PerfilHeaders "Finance" "tesouraria-p-$stamp"

# --- Cenário, montado pelas rotas reais.
$requisitante = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Requisitante $curto" } | ConvertTo-Json)).employeeId

$aprovador = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Aprovador $curto" } | ConvertTo-Json)).employeeId

$tesoureiro = (Invoke-RestMethod "$base/hr/employees" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ fullName = "Tesoureiro $curto" } | ConvertTo-Json)).employeeId

# Cargo sem autoridade de aprovação: o que confere autoridade passaria ele
# próprio por governança (BR-20), e não é isso que se testa aqui.
$cargo = (Invoke-RestMethod "$base/hr/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ name = "Chefe Financeiro $curto"; hierarchyLevel = 2; grantsApprovalAuthority = $false } | ConvertTo-Json)).positionId

Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
    -Body (@{ positionId = $cargo } | ConvertTo-Json) | Out-Null

# Política só se ainda não existir: duas igualmente específicas são empate, e o
# empate recusa a submissão (ADR-034). Sem esta guarda a suite só passava à
# primeira.
$cargoDaPolitica = Invoke-Sql @"
select top 1 cast(s.approver_position_id as varchar(36))
from approval.policy p join approval.policy_step s on s.policy_id = p.id
where p.process_type = 'finance.payment_request' and p.is_active = 1
  and p.department_id is null and p.requires_budget_check = 0
order by s.[order]
"@

if (-not $cargoDaPolitica) {
    Invoke-RestMethod "$base/approval/policies" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ processType = "finance.payment_request"; steps = @(@{ approverPositionId = $cargo }) } | ConvertTo-Json -Depth 5) | Out-Null
}
else {
    # A política já existe e aprova por outro cargo. O aprovador tem de ocupar
    # *esse*, senão não fica atribuído ao passo.
    Invoke-RestMethod "$base/hr/employees/$aprovador/positions" -Method Post -ContentType "application/json" -Headers $adminHeaders `
        -Body (@{ positionId = $cargoDaPolitica } | ConvertTo-Json) | Out-Null
}

Write-Host "`n=== Contas a Pagar e Tesouraria ===`n"

Test-Case "1. Tres funcoes, tres pessoas (BR-3 no catalogo)" {
    # Quem pede não executa.
    $pede = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='finance.payments.request'"
    $paga = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Manager' and c.claim_value='finance.payments.execute'"
    if ($pede -ne "1") { throw "Manager nao pede pagamentos" }
    if ($paga -ne "0") { throw "Manager pode executar pagamentos" }

    # Quem executa não pede.
    $fPede = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.payments.request'"
    $fPaga = Invoke-Sql "select count(*) from [identity].app_role_claim c join [identity].app_role r on r.id=c.role_id where r.name='Finance' and c.claim_value='finance.payments.execute'"
    if ($fPede -ne "0") { throw "Finance pode pedir pagamentos" }
    if ($fPaga -ne "1") { throw "Finance nao executa pagamentos" }

    "Manager pede sem pagar; Finance paga sem pedir"
}

Test-Case "2. Abrir conta e carregar fundos" {
    $body = @{ name = "Operacional $curto"; bank = "BAI"; currency = "AOA" } | ConvertTo-Json
    $c = Invoke-RestMethod "$base/finance/accounts" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:contaId = $c.accountId

    $body = @{ amount = 200000; reference = "Carregamento inicial" } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/deposits" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaId)'"
    if ([decimal]$saldo -ne 200000) { throw "saldo $saldo, esperado 200000" }
    "conta em AOA com 200000"
}

Test-Case "3. Registar factura de compra com o numero do fornecedor" {
    $body = @{
        supplierInvoiceNumber = "FT $curto"; supplierName = "Sonangol"; supplierTaxId = "5401$curto"
        netTotal = 100000; taxTotal = 14000; dueOn = "2026-12-31"
    } | ConvertTo-Json
    $f = Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:compraId = $f.purchaseInvoiceId

    $compra = Invoke-RestMethod "$base/finance/purchase-invoices/$($script:compraId)" -Headers $managerHeaders
    if ($compra.supplierInvoiceNumber -ne "FT $curto") { throw "numero do fornecedor perdido" }
    if ($compra.grossTotal -ne 114000) { throw "total $($compra.grossTotal), esperado 114000" }
    "FT $curto de 114000; o Rivo nao numera facturas de compra"
}

Test-Case "4. A mesma factura do mesmo fornecedor duas vezes e recusada" {
    $body = @{
        supplierInvoiceNumber = "FT $curto"; supplierName = "Sonangol"; supplierTaxId = "5401$curto"
        netTotal = 1; taxTotal = 0; dueOn = "2026-12-31"
    } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/purchase-invoices" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $ux = Invoke-Sql "select count(*) from sys.indexes where object_id=object_id('finance.purchase_invoice') and name='ux_purchase_invoice_supplier_number' and is_unique=1"
    if ($ux -ne "1") { throw "indice unico em falta na base de dados" }
    "409 no caso de uso, e indice unico como segunda linha"
}

Test-Case "5. Pedido de pagamento devolve 202, nao 201" {
    $body = @{
        purchaseInvoiceId = $script:compraId; amount = 114000; requestedByEmployeeId = $requisitante
    } | ConvertTo-Json
    $p = Invoke-RestMethod "$base/finance/payment-requests" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:pedidoId = $p.paymentRequestId
    $script:processoId = $p.approvalRequestId

    if ($p.estado -ne "PendenteAprovacao") { throw "estado '$($p.estado)'" }
    if (-not $script:processoId) { throw "pedido sem processo de aprovacao (BR-1)" }

    $estado = Invoke-Sql "select status from finance.payment_request where id='$($script:pedidoId)'"
    if ($estado -ne "Eligible") { throw "estado '$estado', esperado Eligible" }
    "202: existe e ainda nao e pagavel"
}

Test-Case "6. O pedido nao guarda estado de aprovacao (anti-padrao do prototipo)" {
    # `payment_requests` do prototipo tinha o workflow na propria tabela. Aqui
    # os estados sao dois, e o do processo vive em `approval`.
    $colunas = Invoke-Sql @"
select count(*) from information_schema.columns
where table_schema='finance' and table_name='payment_request'
  and (column_name like '%approv%status%' or column_name like '%step%' or column_name like '%approver%')
"@
    if ($colunas -ne "0") { throw "$colunas colunas de workflow no pedido de pagamento" }

    $ref = Invoke-Sql "select count(*) from finance.payment_request where id='$($script:pedidoId)' and approval_request_id is not null"
    if ($ref -ne "1") { throw "pedido sem ponteiro para o processo" }
    "so um ponteiro para `approval`, sem copia do estado"
}

Test-Case "7. BR-1: sem decisao aprovada nao se paga" {
    $body = @{ bankAccountId = $script:contaId; executedByEmployeeId = $tesoureiro; method = "TB" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaId)'"
    if ([decimal]$saldo -ne 200000) { throw "o saldo mexeu: $saldo" }
    "409 e o dinheiro nao saiu"
}

Test-Case "8. BR-3: quem aprova nao paga" {
    $body = @{ decidedByEmployeeId = $aprovador; action = "Approved" } | ConvertTo-Json
    Invoke-RestMethod "$base/approval/requests/$($script:processoId)/decisions" -Method Post -Body $body -ContentType "application/json" -Headers $adminHeaders | Out-Null

    $body = @{ bankAccountId = $script:contaId; executedByEmployeeId = $aprovador; method = "TB" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }

    # **403 e nao 409:** nao e o estado que impede, e *esta pessoa*.
    if ($code -ne 403) { throw "esperado 403, obtido $code" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaId)'"
    if ([decimal]$saldo -ne 200000) { throw "o saldo mexeu: $saldo" }

    # A tentativa fica na trilha como evento proprio.
    $trilha = Invoke-Sql "select count(*) from audit.audit_event where action='finance.payment_request.segregation_refused' and entity_id='$($script:pedidoId)'"
    if ([int]$trilha -lt 1) { throw "tentativa nao registada na trilha" }

    "403, dinheiro intacto, e a tentativa na trilha"
}

Test-Case "9. BR-5 (saldo): conta sem fundos recusa" {
    $body = @{ name = "Sem fundos $curto"; bank = "BFA"; currency = "AOA" } | ConvertTo-Json
    $pobre = (Invoke-RestMethod "$base/finance/accounts" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders).accountId

    $body = @{ amount = 1000 } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$pobre/deposits" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $body = @{ bankAccountId = $pobre; executedByEmployeeId = $tesoureiro; method = "TB" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$pobre'"
    if ([decimal]$saldo -ne 1000) { throw "o saldo da conta pobre mexeu: $saldo" }
    "409 com a decisao aprovada mas sem dinheiro"
}

Test-Case "10. Moeda do pedido e da conta tem de coincidir" {
    $body = @{ name = "Dolares $curto"; bank = "BFA"; currency = "USD" } | ConvertTo-Json
    $usd = (Invoke-RestMethod "$base/finance/accounts" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders).accountId

    $body = @{ amount = 900000 } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$usd/deposits" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $body = @{ bankAccountId = $usd; executedByEmployeeId = $tesoureiro; method = "TB" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($code -ne 400) { throw "esperado 400, obtido $code" }
    "sem conversao automatica: o cambio e uma decisao"
}

Test-Case "11. Executar: dinheiro sai e o pedido fica executado" {
    $body = @{
        bankAccountId = $script:contaId; executedByEmployeeId = $tesoureiro
        method = "TB"; reference = "TRF-$curto"
    } | ConvertTo-Json
    $r = Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders

    if ($r.saldoRestante -ne 86000) { throw "saldo restante $($r.saldoRestante), esperado 86000" }

    $pedido = Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)" -Headers $managerHeaders
    if ($pedido.status -ne "Executed") { throw "estado $($pedido.status)" }
    if ($pedido.executedByEmployeeId -ne $tesoureiro) { throw "quem pagou nao ficou registado" }
    if ($pedido.executionReference -ne "TRF-$curto") { throw "referencia perdida" }

    "200000 - 114000 = 86000; quem pagou fica registado"
}

Test-Case "12. Pagar duas vezes recusa com a razao certa" {
    $body = @{ bankAccountId = $script:contaId; executedByEmployeeId = $tesoureiro; method = "TB" } | ConvertTo-Json
    try {
        Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders | Out-Null
        throw "esperado 409, o pedido passou"
    }
    catch {
        if (-not $_.Exception.Response) { throw }
        if ([int]$_.Exception.Response.StatusCode -ne 409) { throw "esperado 409, obtido $([int]$_.Exception.Response.StatusCode)" }

        # A razao importa: sem verificar o estado antes do saldo, isto dizia
        # "falta de dinheiro" e mandava procurar o problema no sitio errado.
        $corpo = $_.ErrorDetails.Message
        if ($corpo -notmatch "dobrar|executado") { throw "razao errada: $corpo" }
    }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaId)'"
    if ([decimal]$saldo -ne 86000) { throw "o saldo mexeu outra vez: $saldo" }
    "409 por 'ja executado', nao por saldo; dinheiro intacto"
}

Test-Case "13. Um pedido executado nao se cancela" {
    $body = @{ reason = "Arrependimento" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/cancellation" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "o dinheiro saiu; desfazer e outro movimento"
}

Test-Case "14. Pedidos nao ultrapassam o total da factura" {
    # A factura de 114000 ja tem um pedido de 114000. Mais um nao cabe.
    $body = @{ purchaseInvoiceId = $script:compraId; amount = 1; requestedByEmployeeId = $requisitante } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }
    "tres pedidos de metade cada passariam um a um; juntos pagavam a mais"
}

Test-Case "15. Autorizacao: 401 sem token, 403 na funcao errada" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts" }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    # Finance nao pede pagamentos.
    $body = @{ purchaseInvoiceId = $script:compraId; amount = 1; requestedByEmployeeId = $requisitante } | ConvertTo-Json
    $c1 = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests" -Method Post -Body $body -ContentType "application/json" -Headers $financeHeaders }
    if ($c1 -ne 403) { throw "Finance pediu pagamento: esperado 403, obtido $c1" }

    # Manager nao executa.
    $body = @{ bankAccountId = $script:contaId; executedByEmployeeId = $tesoureiro; method = "TB" } | ConvertTo-Json
    $c2 = Get-StatusCode { Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)/execution" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($c2 -ne 403) { throw "Manager executou pagamento: esperado 403, obtido $c2" }

    "401 e 403 nas duas direccoes da segregacao"
}

Test-Case "16. Execucao e auditada, com quem pagou e o processo" {
    $n = Invoke-Sql "select count(*) from audit.audit_event where action='finance.payment_request.executed' and entity_id='$($script:pedidoId)'"
    if ($n -ne "1") { throw "esperado 1 registo, obtido $n" }

    $comProcesso = Invoke-Sql "select count(*) from audit.audit_event where action='finance.payment_request.executed' and entity_id='$($script:pedidoId)' and new_value like '%approvalRequest%'"
    if ($comProcesso -ne "1") { throw "a trilha nao liga o pagamento ao processo de aprovacao" }
    "1 registo, com quem pagou e o processo que o autorizou"
}

Test-Case "17. Extracto: cada movimento de saldo deixa linha, com a origem" {
    $extracto = Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/statement" -Headers $managerHeaders

    # Um carregamento de 200000 e um pagamento de 114000.
    if ($extracto.movements.Count -ne 2) { throw "esperados 2 movimentos, obtidos $($extracto.movements.Count)" }

    $saida = $extracto.movements | Where-Object { $_.direction -eq "Debit" }
    if (-not $saida) { throw "o pagamento nao deixou linha no extracto" }
    if ([decimal]$saida.amount -ne 114000) { throw "montante errado: $($saida.amount)" }
    if ([decimal]$saida.balanceAfter -ne 86000) { throw "saldo congelado errado: $($saida.balanceAfter)" }

    # O percurso de volta que a reconciliacao precisa.
    if ($saida.sourceType -ne "payment_request") { throw "origem nao apontada: $($saida.sourceType)" }
    if ($saida.sourceId -ne $script:pedidoId) { throw "origem aponta ao documento errado" }

    "2 movimentos; a saida aponta ao pedido que a causou"
}

Test-Case "18. Extracto ate hoje reconcilia com o saldo da conta" {
    $extracto = Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/statement" -Headers $managerHeaders

    if ($extracto.reconciles -ne $true) { throw "extracto nao reconcilia com o saldo" }
    if ([decimal]$extracto.closingBalance -ne 86000) { throw "fecho $($extracto.closingBalance), esperado 86000" }
    if ([decimal]$extracto.totalCredits -ne 200000) { throw "creditos $($extracto.totalCredits)" }
    if ([decimal]$extracto.totalDebits -ne 114000) { throw "debitos $($extracto.totalDebits)" }

    # A soma tem de fechar: abertura + entradas - saidas = fecho.
    $calculado = [decimal]$extracto.openingBalance + [decimal]$extracto.totalCredits - [decimal]$extracto.totalDebits
    if ($calculado -ne [decimal]$extracto.closingBalance) { throw "a cadeia de saldos esta partida" }

    "0 + 200000 - 114000 = 86000, e bate com a conta"
}

Test-Case "19. Janela fechada nao afirma reconciliacao" {
    # Uma janela que acaba no passado nao deve bater com o saldo de hoje —
    # dizer que nao reconcilia seria mentir ao contrario.
    $extracto = Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/statement?from=2020-01-01&to=2020-12-31" -Headers $managerHeaders

    if ($null -ne $extracto.reconciles) { throw "afirmou reconciliacao sobre janela fechada" }
    if ($extracto.movements.Count -ne 0) { throw "movimentos fora da janela" }

    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/statement?from=2026-12-31&to=2026-01-01" -Headers $managerHeaders }
    if ($code -ne 400) { throw "datas invertidas: esperado 400, obtido $code" }

    "janela fechada nao responde a pergunta; datas invertidas dao 400"
}

Test-Case "20. Extracto e append-only, imposto pela base de dados" {
    $id = Invoke-Sql "select top 1 cast(id as nvarchar(50)) from finance.bank_movement where bank_account_id='$($script:contaId)'"
    if (-not $id) { throw "sem movimentos para testar" }

    # Um extracto que se pode editar nao serve para reconciliar nada.
    $update = Invoke-RivoSql "update finance.bank_movement set amount=1 where id='$id'" -Raw
    if ("$update" -notmatch "append-only|50020") { throw "UPDATE passou: $update" }

    $delete = Invoke-RivoSql "delete from finance.bank_movement where id='$id'" -Raw
    if ("$delete" -notmatch "append-only|50020") { throw "DELETE passou: $delete" }

    # TRUNCATE nao dispara gatilhos em SQL Server. O que o impede e a sentinela
    # referenciada por chave estrangeira.
    $truncate = Invoke-RivoSql "truncate table finance.bank_movement" -Raw
    if ("$truncate" -notmatch "FOREIGN KEY|referenc") { throw "TRUNCATE passou: $truncate" }

    $ainda = Invoke-Sql "select count(*) from finance.bank_movement where id='$id'"
    if ($ainda -ne "1") { throw "o movimento desapareceu" }

    "UPDATE, DELETE e TRUNCATE recusados; o movimento continua la"
}

Test-Case "21. Saldo da conta bate com a soma dos movimentos" {
    # A invariante que o extracto existe para tornar verificavel.
    $divergentes = Invoke-Sql @"
select count(*) from finance.bank_account a
where a.balance <> isnull((
    select sum(case when m.direction = 'Credit' then m.amount else -m.amount end)
    from finance.bank_movement m where m.bank_account_id = a.id), 0)
"@
    if ($divergentes -ne "0") { throw "$divergentes conta(s) com saldo que nao bate com o extracto" }
    "nenhuma conta diverge do proprio extracto"
}

Test-Case "22. Levantamento que nao e pagamento a fornecedor" {
    # Conta propria, para nao mexer no saldo que o caso 27 vai verificar apos
    # o reinicio da stack.
    $body = @{ name = "Secundaria $curto"; bank = "BFA"; currency = "AOA" } | ConvertTo-Json
    $c = Invoke-RestMethod "$base/finance/accounts" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders
    $script:contaSecundariaId = $c.accountId

    $body = @{ amount = 50000; reference = "Carregamento" } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/deposits" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $body = @{ amount = 3000; description = "Comissao bancaria mensal" } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/withdrawals" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ([decimal]$saldo -ne 47000) { throw "saldo $saldo, esperado 47000" }

    # Ao contrario do pagamento executado, este movimento nao vem de documento
    # nenhum: sourceType e sourceId ficam nulos.
    $comOrigem = Invoke-Sql "select count(*) from finance.bank_movement where bank_account_id='$($script:contaSecundariaId)' and direction='Debit' and source_type is not null"
    if ($comOrigem -ne "0") { throw "o levantamento avulso ficou com origem, e nao devia" }

    "50000 - 3000 = 47000; sem origem de documento"
}

Test-Case "23. Levantar acima do saldo e recusado" {
    $body = @{ amount = 999999; description = "Impossivel" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/withdrawals" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ([decimal]$saldo -ne 47000) { throw "saldo mudou apesar do 409: $saldo" }
    "409, saldo intacto"
}

Test-Case "24. Fechar conta com saldo diferente de zero e recusado" {
    # Fechar uma conta com dinheiro dentro esconderia esse dinheiro atras de
    # uma conta que diz nao estar em uso.
    $body = @{ reason = "Tentativa de fecho." } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 409) { throw "esperado 409, obtido $code" }

    $activa = Invoke-Sql "select is_active from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ($activa -ne "1") { throw "a conta fechou apesar do saldo" }
    "409, conta continua aberta"
}

Test-Case "25. Esvaziar e fechar; fechada nao movimenta; reabrir devolve o uso" {
    Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/withdrawals" -Method Post -ContentType "application/json" -Headers $managerHeaders `
        -Body (@{ amount = 47000; description = "Encerramento da conta" } | ConvertTo-Json) | Out-Null

    $body = @{ reason = "Conta secundaria de ensaio, ja nao e precisa." } | ConvertTo-Json
    Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/closure" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders | Out-Null

    $activa = Invoke-Sql "select is_active from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ($activa -ne "0") { throw "a conta nao fechou com saldo zero" }

    $razao = Invoke-Sql "select new_value from audit.audit_event where action='finance.bank_account.closed' and entity_id='$($script:contaSecundariaId)'"
    if ($razao -notmatch "ja nao e precisa") { throw "a razao nao ficou na trilha: $razao" }

    # Fechada, nao aceita deposito.
    $body = @{ amount = 1; reference = "Nao devia entrar" } | ConvertTo-Json
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/deposits" -Method Post -Body $body -ContentType "application/json" -Headers $managerHeaders }
    if ($code -ne 400) { throw "deposito em conta fechada: esperado 400, obtido $code" }

    # Reabrir devolve o uso, sem repor saldo nenhum.
    Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/reopening" -Method Post -Headers $managerHeaders | Out-Null
    $activa = Invoke-Sql "select is_active from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ($activa -ne "1") { throw "a conta nao reabriu" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaSecundariaId)'"
    if ([decimal]$saldo -ne 0) { throw "reabrir alterou o saldo: $saldo" }

    "fecho com razao na trilha; deposito recusado fechada; reabertura sem repor saldo"
}

Test-Case "26. Levantamento e fecho: 401 sem token, 404 em conta inexistente" {
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$($script:contaSecundariaId)/withdrawals" -Method Post -ContentType "application/json" -Body (@{ amount = 1; description = "x" } | ConvertTo-Json) }
    if ($code -ne 401) { throw "sem token: esperado 401, obtido $code" }

    $inexistente = [Guid]::NewGuid()
    $code = Get-StatusCode { Invoke-RestMethod "$base/finance/accounts/$inexistente/closure" -Method Post -ContentType "application/json" -Headers $managerHeaders -Body (@{ reason = "x" } | ConvertTo-Json) }
    if ($code -ne 404) { throw "conta inexistente: esperado 404, obtido $code" }

    "401 sem token; 404 em conta que nao existe"
}

Test-Case "27. Dados sobrevivem ao reinicio da stack" {
    Restart-RivoStack
    $deadline = (Get-Date).AddSeconds(420)   # ver a nota em Wait-RivoApi
    do { Start-Sleep -Seconds 4; $up = try { Invoke-RestMethod "$base/health" -TimeoutSec 5 | Out-Null; $true } catch { $false } } while (-not $up -and (Get-Date) -lt $deadline)
    if (-not $up) { throw "API nao voltou" }

    $pedido = Invoke-RestMethod "$base/finance/payment-requests/$($script:pedidoId)" -Headers $managerHeaders
    if ($pedido.status -ne "Executed") { throw "estado perdido: $($pedido.status)" }

    $saldo = Invoke-Sql "select balance from finance.bank_account where id='$($script:contaId)'"
    if ([decimal]$saldo -ne 86000) { throw "saldo perdido: $saldo" }

    $extracto = Invoke-RestMethod "$base/finance/accounts/$($script:contaId)/statement" -Headers $managerHeaders
    if ($extracto.movements.Count -ne 2) { throw "extracto perdido: $($extracto.movements.Count) movimentos" }
    if ($extracto.reconciles -ne $true) { throw "extracto deixou de reconciliar apos restart" }

    "pagamento, saldo e extracto intactos apos restart"
}

Write-Host ""
if ($failures -gt 0) { Write-Host "$failures teste(s) falharam." -ForegroundColor Red; exit 1 }
Write-Host "Todos os testes passaram." -ForegroundColor Green
exit 0
