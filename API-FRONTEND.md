# Rivo API - Guia para o Frontend

Documento gerado a partir das rotas implementadas no backend. A API tem 151
endpoints: 147 protegidos por JWT e 4 públicos. A contagem inclui `/health`.

## Como ligar

Em desenvolvimento, a API responde em `http://localhost:5080`. Em produção,
usar a URL pública configurada para o deployment.

Rotas protegidas usam:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json
```

O token é obtido em `POST /identity/login` ou
`POST /identity/login/google`. Depois de login, guardar `accessToken` e
`expiresAt`. O token contém uma sessão revogável; depois de logout, o frontend
deve considerar o token inválido.

O Swagger/OpenAPI é publicado por interruptor de ambiente, `EXPOSE_OPENAPI`
(ADR-038) — e não pelo nome do ambiente:

- Interface: `/swagger`
- Documento: `/openapi/v1.json`

Em desenvolvimento estão abertos por omissão. No ambiente publicado dependem
de quem o opera: se responderem `404`, o interruptor está a `false` e este
documento é o contrato.

## Respostas e erros

- `200 OK`: leitura ou operação concluída com corpo.
- `201 Created`: recurso criado; normalmente devolve um identificador.
- `202 Accepted`: operação criada, mas ainda pendente de aprovação.
- `204 No Content`: operação concluída sem corpo.
- `400 Bad Request`: corpo ou parâmetros inválidos. Validações usam
  `ValidationProblemDetails`, com mensagens em `errors`.
- `401 Unauthorized`: token ausente, inválido, expirado ou sessão revogada.
- `403 Forbidden`: autenticado, mas sem a permissão exigida ou impedido por
  segregação de funções.
- `404 Not Found`: recurso inexistente. Em notificações também pode significar
  que o recurso pertence a outro utilizador.
- `409 Conflict`: conflito com o estado actual, duplicação ou concorrência.
- `501 Not Implemented`: a capacidade ainda não está configurada/implementada
  neste ambiente, por exemplo Google ou motor de aprovação.
- `503 Service Unavailable`: serviço dependente indisponível, especialmente
  a base de dados no health check.

Datas devem ser enviadas como `YYYY-MM-DD`. Valores monetários são decimais.
Listagens devolvem colecções; os campos exactos dos read models devem ser
confirmados no `/openapi/v1.json`, porque alguns são inferidos directamente
pelos casos de uso e não têm um DTO HTTP nomeado.

## Identidade

| Método e rota | Permissão | O que faz | Request | Sucesso |
|---|---|---|---|---|
| `POST /identity/register` | Pública | Cria uma conta | `{ email, password }` | `201 { userId }` |
| `POST /identity/login` | Pública | Abre sessão por password | `{ email, password }` | `200 { accessToken, expiresAt }` |
| `POST /identity/login/google` | Pública | Abre sessão com ID token Google | `{ idToken }` | `200 { accessToken, expiresAt }` |
| `POST /identity/logout` | JWT | Revoga a sessão actual | Sem corpo | `204` |
| `GET /identity/me` | JWT | Devolve utilizador, perfis e permissões | Sem corpo | `200 { userId, email, roles, permissions }` |
| `GET /identity/users` | `identity.users.read` | Lista contas | Sem parâmetros | `200` colecção |
| `GET /identity/roles` | `identity.roles.read` | Lista perfis de acesso e permissões | Sem parâmetros | `200` colecção |
| `POST /identity/users/{userId}/roles` | `identity.roles.assign` | Atribui perfil a utilizador | `{ profile }` | `204` |

Registo inválido devolve `400`; login falhado devolve `401` sem revelar se o
email existe. Google não configurado devolve `501`.

### Conta e sessões

| Método e rota | Permissão | O que faz | Request | Sucesso |
|---|---|---|---|---|
| `POST /identity/me/password` | JWT | Muda a própria password | `{ currentPassword, newPassword }` | `204` |
| `GET /identity/me/sessions` | JWT | Lista as sessões do próprio | Sem parâmetros | `200` colecção |
| `POST /identity/me/sessions/{sessionId}/revocation` | JWT | Termina uma sessão própria | Sem corpo | `204` |
| `POST /identity/users/{userId}/password-reset` | `identity.users.write` | Repõe a password de outra conta | `{ newPassword }` | `204` |
| `POST /identity/users/{userId}/status` | `identity.users.write` | Activa ou desactiva a conta | `{ active, reason }` | `204` |
| `POST /identity/users/{userId}/roles/{profile}/removal` | `identity.roles.assign` | Retira um perfil | Sem corpo | `204` |

**Mudar a password termina as outras sessões** e mantém a de onde se mudou.
Password actual errada devolve `401` — é a credencial que falha, não a
autorização. Password nova fraca devolve `400` com os motivos em `errors`.

**Desactivar uma conta termina todas as sessões** e fecha os dois caminhos de
entrada, password e Google. `reason` é obrigatória (`400` sem ela) e fica na
trilha. Desactivar a própria conta devolve `409`.

`GET /identity/me/sessions` devolve `sessionId`, `ipAddress`, `userAgent`,
`createdAt`, `expiresAt`, `revokedAt`, `isActive` e **`isCurrent`** — usar o
último para não oferecer «terminar» na sessão de onde se está a olhar.
Revogar a sessão de outra pessoa devolve `404`, e não `403`.

⚠ Retirar um perfil **não tem efeito imediato no token que a pessoa já tem**:
as permissões são resolvidas na autenticação. Para cortar já, desactivar a
conta.

`GET /identity/users` passou a devolver também `isActive` e `roles`.

## Comercial

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /commercial/customers` | `commercial.customers.read` | Lista clientes | `includeInactive?` (default `false`) | `200` |
| `GET /commercial/customers/{customerId}` | `commercial.customers.read` | Consulta cliente | Path `customerId` | `200` cliente |
| `POST /commercial/customers` | `commercial.customers.write` | Regista cliente | `{ name, taxId, addressDetail, city, country?, email?, phone? }` | `201 { customerId }` |
| `POST /commercial/customers/{customerId}/details` | `commercial.customers.write` | Actualiza dados e morada | `{ name?, addressDetail?, city?, country?, email?, phone? }` | `204` |
| `POST /commercial/customers/{customerId}/status` | `commercial.customers.write` | Activa/desactiva cliente | `{ active }` | `204` |

`country` assume `AO`. Não existe `DELETE`; a desactivação é lógica. NIF
duplicado devolve `409` com `customerId` do cliente existente.

## Documentos

| Método e rota | Permissão | O que faz | Request | Sucesso |
|---|---|---|---|---|
| `GET /documents` | `documents.read` | Lista o arquivo | `category?`, `from?`, `to?`, `limit` (default `50`, máximo `200`) | `200` colecção |
| `POST /documents/` | `documents.write` | Faz upload de documento | `multipart/form-data`: ficheiro `file` e campo `category` | `201 DocumentDescriptor` |
| `GET /documents/{documentId}` | `documents.read` | Descarrega ficheiro | Path `documentId` | `200` binário com content type e nome |
| `GET /documents/{documentId}/metadata` | `documents.read` | Consulta metadados | Path `documentId` | `200 { documentId, fileName, contentType, sizeInBytes, category, contentHash, uploadedBy, uploadedAt }` |

O upload aceita ficheiros até 25 MB. Ficheiro vazio ou acima do limite devolve
`400`.

**`GET /documents` lista o arquivo, e não os anexos de um registo.** Quem
procura os documentos de um colaborador pede-os a `hr`
(`GET /hr/employees/{id}/documents`), que sabe quais são. Esta rota serve quem
procura no arquivo — tipicamente um ficheiro carregado e ainda não ligado a
nada.

A janela `from`/`to` é sobre a data de carregamento e é **inclusiva nos dois
extremos**. Documentos anulados não aparecem. `limit` é sempre aplicado: um
valor acima de `200` é cortado, não recusado.

## Fiscal

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /fiscal/tax-rates` | `fiscal.rates.read` | Lista séries/taxas fiscais | Sem parâmetros | `200` |
| `POST /fiscal/tax-rates` | `fiscal.rates.write` | Abre série de taxa | `{ kind?, code, description }` | `201 { scheduleId }` |
| `POST /fiscal/tax-rates/{scheduleId}/versions` | `fiscal.rates.write` | Introduz versão com vigência | `{ percentage, effectiveFrom, effectiveTo?, legalInstrument }` | `201 { versionId }` |
| `GET /fiscal/tax-rates/determination` | `fiscal.rates.read` | Determina taxa aplicável à data | `taxCode`, `taxPointDate` obrigatórios; `kind?` | `200 { taxCode, percentage, legalInstrument }` |

`kind` assume `ValueAdded`. Vigência sobreposta devolve `409`; ausência de
taxa aplicável devolve `404`.

## Finance: facturação e recebimentos

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /finance/series` | `finance.invoices.read` | Lista séries de documentos | Sem parâmetros | `200` |
| `POST /finance/series` | `finance.series.write` | Abre série FT | `{ code }` | `201 { seriesId }` |
| `GET /finance/sales-invoices` | `finance.invoices.read` | Lista facturas de venda | `customerId?`, `from?`, `to?` | `200` |
| `GET /finance/sales-invoices/{invoiceId}` | `finance.invoices.read` | Consulta factura | Path `invoiceId` | `200` factura |
| `POST /finance/sales-invoices` | `finance.invoices.write` | Emite factura | `{ customerId?, series?, issuedOn?, taxPointDate?, currency?, lines? }`; linha `{ description, quantity, unitPrice, taxCode }` | `201 { invoiceId, number }` |
| `POST /finance/sales-invoices/{invoiceId}/cancellation` | `finance.invoices.cancel` | Anula factura | `{ reason }` | `204` |
| `GET /finance/sales-invoices/{invoiceId}/balance` | `finance.invoices.read` | Consulta saldo em aberto | Path `invoiceId` | `200` saldo |
| `GET /finance/credit-notes` | `finance.invoices.read` | Lista notas de crédito | `salesInvoiceId?` | `200` |
| `GET /finance/credit-notes/{creditNoteId}` | `finance.invoices.read` | Consulta nota de crédito | Path `creditNoteId` | `200` nota |
| `POST /finance/credit-notes` | `finance.invoices.cancel` | Emite nota de crédito | `{ salesInvoiceId, series?, issuedOn?, reason?, lines? }`; linha igual à factura | `201 { creditNoteId, number }` |
| `POST /finance/credit-notes/{creditNoteId}/cancellation` | `finance.invoices.cancel` | Anula nota de crédito | `{ reason }` | `204` |
| `GET /finance/receipts` | `finance.receipts.read` | Lista recibos | `customerId?`, `from?`, `to?` | `200` |
| `GET /finance/receipts/{receiptId}` | `finance.receipts.read` | Consulta recibo | Path `receiptId` | `200` recibo |
| `POST /finance/receipts` | `finance.receipts.write` | Regista recebimento | `{ series?, receivedOn?, method, notes?, settlements? }`; settlement `{ salesInvoiceId, amount }` | `201 { receiptId, number }` |
| `POST /finance/receipts/{receiptId}/cancellation` | `finance.invoices.cancel` | Estorna recibo | `{ reason }` | `204` |

Defaults: moeda `AOA`, datas do dia e cliente nulo significa consumidor final.
Meios SAF-T válidos: `NU`, `TB`, `CH`, `CC`, `CD`, `MB`, `PR`, `CS`, `DE`,
`OU`. Saldo excedido ou postagem bloqueada devolve `409`; isenção sem
catálogo devolve `501`.

## Finance: tesouraria e contas a pagar

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /finance/accounts` | `finance.payables.read` | Lista contas bancárias | `includeClosed?` (default `false`) | `200` |
| `POST /finance/accounts` | `finance.payables.write` | Abre conta bancária | `{ name, bank, iban?, currency? }` | `201 { accountId }` |
| `POST /finance/accounts/{accountId}/deposits` | `finance.payables.write` | Regista depósito | `{ amount, reference? }` | `204` |
| `POST /finance/accounts/{accountId}/withdrawals` | `finance.payables.write` | Saída que não é pagamento a fornecedor | `{ amount, description }` | `204` |
| `POST /finance/accounts/{accountId}/closure` | `finance.payables.write` | Fecha conta | `{ reason }` | `204` |
| `POST /finance/accounts/{accountId}/reopening` | `finance.payables.write` | Reabre conta | Sem corpo | `204` |
| `GET /finance/accounts/{accountId}/statement` | `finance.payables.read` | Consulta extracto | `from?`, `to?` | `200` extracto |
| `GET /finance/purchase-invoices` | `finance.payables.read` | Lista facturas de compra | `dueBefore?` | `200` |
| `GET /finance/purchase-invoices/{purchaseInvoiceId}` | `finance.payables.read` | Consulta factura de compra | Path `purchaseInvoiceId` | `200` factura |
| `GET /finance/purchase-invoices/{purchaseInvoiceId}/match` | `finance.payables.read` | 3-way match: encomendado, recebido e facturado | Path `purchaseInvoiceId` | `200` match, `404` se a factura não existir |
| `POST /finance/purchase-invoices` | `finance.payables.write` | Regista factura de fornecedor | `{ supplierInvoiceNumber, supplierId?, purchaseOrderId?, supplierName, supplierTaxId, issuedOn?, dueOn?, currency?, netTotal, taxTotal, description? }` | `201 { purchaseInvoiceId }` |
| `GET /finance/payment-requests` | `finance.payables.read` | Lista pedidos de pagamento | `purchaseInvoiceId?` | `200` |
| `GET /finance/payment-requests/{paymentRequestId}` | `finance.payables.read` | Consulta pedido | Path `paymentRequestId` | `200` pedido |
| `POST /finance/payment-requests` | `finance.payments.request` | Cria pedido sujeito a aprovação | `{ purchaseInvoiceId, amount, requestedByEmployeeId, requestedOn?, costCentreId?, notes? }` | `202 { paymentRequestId, approvalRequestId, estado }` |
| `POST /finance/payment-requests/{paymentRequestId}/cancellation` | `finance.payments.request` | Cancela pedido | `{ reason }` | `204` |
| `POST /finance/payment-requests/{paymentRequestId}/execution` | `finance.payments.execute` | Executa pagamento aprovado | `{ bankAccountId, executedByEmployeeId, method, reference? }` | `200 { estado, saldoRestante }` |

`202` significa `estado: "PendenteAprovacao"`. Executar sem aprovação,
sem fundos ou violando segregação devolve `409` ou `403`, respectivamente.

**`supplierId` em `purchase-invoices` é opcional.** Indicado, tem de existir
em `procurement` — senão `400`. Omitido, tenta ligar-se sozinho pelo NIF; não
encontrar não é erro, porque nem toda a despesa tem Fornecedor qualificado
(uma factura de electricidade, por exemplo). `supplierName`/`supplierTaxId`
continuam obrigatórios em ambos os casos — são o retrato congelado do
documento, não substituídos pelo que `procurement` tiver guardado.

**`purchaseOrderId` também é opcional, e mais estrito.** Indicado, tem de
existir e ser do mesmo fornecedor da factura — senão `400`. Alimenta só o
`/match`; não indicá-lo não bloqueia o registo, só deixa o match sem ordem a
comparar. O `/match` mostra a divergência de valor entre o recebido e o
facturado, mas **não a bloqueia** — é informação para quem decide o
pagamento.

**`withdrawals` não é o pagamento a fornecedor** — esse passa por
`payment-requests/{id}/execution`, com a dupla barreira de BR-5. É para o
resto do que sai de uma conta sem decisão de aprovação: comissões, transferências
entre contas. Levantar acima do saldo devolve `409`.

**Fechar só com saldo zero** (`409` caso contrário) — fechar uma conta com
dinheiro dentro escondê-lo-ia atrás de uma conta que diz não estar em uso.
Fechada, não aceita depósitos (`400`) nem levantamentos (`409` — é o mesmo
código de saldo insuficiente, porque os dois são o estado da conta a impedir,
não o corpo do pedido). Reabrir não repõe saldo nenhum: devolve apenas o uso.

## Finance: contabilidade

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /finance/ledger/accounts` | `finance.ledger.read` | Lista plano de contas | `includeInactive?` | `200` |
| `POST /finance/ledger/accounts` | `finance.ledger.write` | Abre conta contabilística | `{ code, name, category, parentCode? }`; categorias `GR`, `GA`, `GM`, `AR`, `AA`, `AM` | `201 { accountId, code }` |
| `POST /finance/ledger/accounts/{accountId}/deactivation` | `finance.ledger.write` | Desactiva conta | Sem corpo | `204` |
| `GET /finance/ledger/journals` | `finance.ledger.read` | Lista diários | `includeInactive?` | `200` |
| `POST /finance/ledger/journals` | `finance.ledger.write` | Abre diário | `{ code, name }` | `201 { journalId, code }` |
| `GET /finance/ledger/entries` | `finance.ledger.read` | Lista lançamentos | `journalId?`, `fiscalYear?`, `period?` | `200` |
| `GET /finance/ledger/entries/{entryId}` | `finance.ledger.read` | Consulta lançamento | Path `entryId` | `200` lançamento |
| `POST /finance/ledger/entries` | `finance.ledger.write` | Regista lançamento | `{ journalCode, archivalNumber, transactionDate?, fiscalYear?, period?, description, type?, lines? }`; linha `{ accountCode, side, amount, description, costCentreId?, sourceDocumentId? }` | `201 { entryId, transactionId }` |
| `POST /finance/ledger/entries/{entryId}/void` | `finance.ledger.write` | Anula lançamento | `{ reason }` | `204` |
| `GET /finance/ledger/periods` | `finance.ledger.read` | Lista períodos | `fiscalYear` obrigatório | `200` |
| `POST /finance/ledger/periods` | `finance.ledger.write` | Abre período | `{ fiscalYear, number }` | `201 { periodId }` |
| `POST /finance/ledger/periods/{fiscalYear}/{number}/closure` | `finance.ledger.close` | Fecha período | `{ closedByEmployeeId }` | `204` |
| `POST /finance/ledger/periods/{fiscalYear}/{number}/reopening` | `finance.ledger.close` | Reabre período | `{ reason }` | `204` |
| `GET /finance/ledger/trial-balance` | `finance.ledger.read` | Consulta balancete | `fiscalYear`, `period?` | `200` |
| `GET /finance/ledger/posting-rules` | `finance.ledger.read` | Lista regras de postagem | `includeInactive?` | `200` |
| `POST /finance/ledger/posting-rules` | `finance.ledger.close` | Define regra automática | `{ event, journalCode, description, lines? }`; linha `{ accountCode, side, amount, description }` | `201 { ruleId }` |
| `POST /finance/ledger/posting-rules/{ruleId}/deactivation` | `finance.ledger.close` | Desactiva regra | Sem corpo | `204` |

Tipos de lançamento: `N`, `R`, `A`, `J`. Lado: `Debit` ou `Credit`. Eventos
de postagem: `SalesInvoiceIssued`, `CreditNoteIssued`, `ReceiptRegistered`,
`PurchaseInvoiceRegistered`, `PaymentExecuted`. Parcela: `Net`, `Tax` ou
`Gross`. Partida desequilibrada devolve `400`; período fechado ou duplicação
de transacção devolve `409`.

## Finance: planeamento

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /finance/planning/cost-centres` | `finance.planning.read` | Lista centros de custo | `includeInactive?` | `200` |
| `POST /finance/planning/cost-centres` | `finance.planning.write` | Cria centro de custo | `{ code, name, departmentId?, responsibleEmployeeId }` | `201 { costCentreId }` |
| `GET /finance/planning/budgets` | `finance.planning.read` | Lista orçamentos | `costCentreId?`, `fiscalYear?` | `200` |
| `POST /finance/planning/budgets` | `finance.planning.write` | Cria orçamento em Draft | `{ costCentreId, fiscalYear, currency?, monthlyCeilings? }` | `201 { budgetId, estado: "Draft" }` |
| `POST /finance/planning/budgets/{budgetId}/revision` | `finance.planning.write` | Revê orçamento Draft | `{ monthlyCeilings? }` | `204` |
| `POST /finance/planning/budgets/{budgetId}/approval` | `finance.budgets.approve` | Aprova orçamento | `{ approvedByEmployeeId }` | `204` |
| `GET /finance/planning/cost-forecasts` | `finance.planning.read` | Lista previsões de custos | `departmentId?`, `fiscalYear?` | `200` |
| `POST /finance/planning/cost-forecasts` | `finance.planning.write` | Regista previsão | `{ departmentId, fiscalYear, month, currency?, operationalCosts, fixedCosts, submit? }` | `201 { forecastId }` |

Moeda assume `AOA`; `submit` assume `false`. Orçamento aprovado não pode ser
revisto e devolve `409`.

## Procurement

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /procurement/suppliers` | `procurement.suppliers.read` | Lista fornecedores | `includeInactive?` (default `false`) | `200` |
| `GET /procurement/suppliers/{supplierId}` | `procurement.suppliers.read` | Consulta fornecedor | Path `supplierId` | `200` fornecedor |
| `POST /procurement/suppliers` | `procurement.suppliers.write` | Qualifica fornecedor | `{ name, taxId, iban?, email?, phone? }` | `201 { supplierId }` |
| `POST /procurement/suppliers/{supplierId}/details` | `procurement.suppliers.write` | Actualiza dados | `{ name?, iban?, email?, phone? }` | `204` |
| `POST /procurement/suppliers/{supplierId}/status` | `procurement.suppliers.write` | Activa/desactiva | `{ active }` | `204` |
| `GET /procurement/requisitions` | `procurement.requisitions.read` | Lista requisições | `requestedByEmployeeId?`, `status?` | `200` |
| `GET /procurement/requisitions/{requisitionId}` | `procurement.requisitions.read` | Consulta requisição | Path `requisitionId` | `200` requisição |
| `POST /procurement/requisitions` | `procurement.requisitions.write` | Abre requisição em rascunho | `{ requestedByEmployeeId, departmentId?, justification, currency?, requestedOn?, lines? }`; linha `{ description, quantity, estimatedUnitPrice }` | `201 { requisitionId, estimatedTotal, estado: "Draft" }` |
| `POST /procurement/requisitions/{requisitionId}/submission` | `procurement.requisitions.write` | Submete a aprovação | Sem corpo | `202 { approvalRequestId, estado: "PendingApproval" }` |
| `POST /procurement/requisitions/{requisitionId}/approval-outcome` | `procurement.requisitions.read` | Aplica a decisão de `approval` | Sem corpo | `200 { estado }` ou `202` ainda pendente |
| `POST /procurement/requisitions/{requisitionId}/cancellation` | `procurement.requisitions.write` | Cancela requisição | `{ reason }` | `204` |

Estados da requisição: `Draft`, `PendingApproval`, `Approved`, `Refused`,
`Cancelled`. Moeda assume `AOA`; sem `departmentId`, usa-se o departamento do
requisitante.

**O IBAN é verificado pela norma ISO 13616.** Um dígito trocado devolve `400`
e o fornecedor não é guardado — a validação existe porque um IBAN errado paga
a outra pessoa.

Depois de submetida, a requisição **não se altera**: acrescentar ou remover
linhas devolve `409`. O frontend deve esconder a edição fora de `Draft`.

Submeter sem motor de aprovação ligado devolve `501`; sem política aplicável,
`409`. Uma requisição aprovada é o ponto de partida da Ordem de Compra,
abaixo.

### Ordens de compra

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /procurement/orders` | `procurement.orders.read` | Lista ordens de compra | `requisitionId?`, `supplierId?` | `200` |
| `GET /procurement/orders/{purchaseOrderId}` | `procurement.orders.read` | Consulta ordem | Path `purchaseOrderId` | `200` ordem |
| `POST /procurement/requisitions/{requisitionId}/orders` | `procurement.orders.write` | Emite ordem a partir da requisição | `{ supplierId, issuedOn?, expectedOn?, lines? }`; linha `{ description, quantity, unitPrice }` | `201 { purchaseOrderId, total, estado: "Issued" }` |
| `POST /procurement/orders/{purchaseOrderId}/cancellation` | `procurement.orders.write` | Cancela ordem | `{ reason }` | `204` |

**Não há `POST /procurement/orders` avulso, e é deliberado:** uma ordem nasce
sempre de uma requisição aprovada, e a rota diz a regra. Emitir contra uma
requisição que não esteja em `Approved` devolve `409`, e a mensagem nomeia o
estado em que ela está.

`unitPrice` é o preço **acordado** com o fornecedor, e não o estimado na
requisição — entre os dois houve cotação.

⚠ **O total encomendado não pode passar o aprovado.** Uma requisição pode dar
várias ordens — dividir por dois fornecedores é legítimo —, mas a soma das
ordens em vigor não ultrapassa o total estimado que foi aprovado. Excedê-lo
devolve `409` com as parcelas na mensagem: aprovado, já encomendado, restante,
e pedido. **Não há tolerância de desvio**; o caminho é uma requisição nova.
Cancelar uma ordem devolve o valor ao disponível.

Fornecedor desactivado devolve `409`. A moeda é herdada da requisição e não se
envia. A ordem **não tem número próprio** — identifica-se pelo `purchaseOrderId`.

### Recepções de mercadoria

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /procurement/receipts` | `procurement.receipts.read` | Lista recepções | `purchaseOrderId?` | `200` |
| `GET /procurement/receipts/{goodsReceiptId}` | `procurement.receipts.read` | Consulta recepção | Path `goodsReceiptId` | `200` recepção |
| `POST /procurement/orders/{purchaseOrderId}/receipts` | `procurement.receipts.write` | Regista o que chegou | `{ receivedByEmployeeId, receivedOn?, deliveryNote?, lines? }`; linha `{ purchaseOrderLineId, quantityReceived }` | `201 { goodsReceiptId, estado: "Registered" }` |
| `POST /procurement/receipts/{goodsReceiptId}/cancellation` | `procurement.receipts.write` | Anula recepção registada por engano | `{ reason }` | `204` |

**Cada linha da recepção aponta a uma linha da ordem**, por
`purchaseOrderLineId` — é por aí que o 3-way match compara. Uma linha que não
pertença à ordem devolve `409`.

**Recepções parciais são o caso normal:** somam por linha, e a ordem só fica
`fullyReceived` quando todas as linhas chegam por inteiro. O `GET` da ordem dá
`quantityReceived` por linha e `fullyReceived` na raiz.

⚠ **Receber acima do encomendado devolve `409`**, e o acumulado conta — não só
a contagem desta vez. Não há tolerância de excesso.

⚠ **Anular uma recepção é corrigir um engano de registo**, e devolve a
quantidade a "por receber". **Não é devolver mercadoria ao fornecedor** — esse
é outro facto, e não existe.

Uma ordem com mercadoria recebida **não se cancela** (`409`): anular primeiro a
recepção. A recepção não gere stock — isso é de `inventory`, que não existe.

## HR

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /hr/employees` | `hr.employees.read` | Lista colaboradores | Sem parâmetros | `200` |
| `POST /hr/employees` | `hr.employees.write` | Contrata colaborador | `{ fullName, departmentId?, userId?, hiredOn? }` | `201 { employeeId }` |
| `GET /hr/employees/{employeeId}` | `hr.employees.read` | Consulta colaborador | Path `employeeId` | `200` referência |
| `GET /hr/departments` | `hr.departments.read` | Lista departamentos | Sem parâmetros | `200` |
| `POST /hr/departments` | `hr.departments.write` | Cria departamento | `{ name, managerId? }` | `201 { departmentId }` |
| `GET /hr/positions` | `hr.positions.read` | Lista cargos | Sem parâmetros | `200` |
| `POST /hr/positions` | `hr.positions.write` | Cria cargo | `{ name, hierarchyLevel, grantsApprovalAuthority }` | `201 { positionId }` |
| `POST /hr/employees/{employeeId}/positions` | `hr.positions.assign` | Atribui cargo | `{ positionId, effectiveFrom?, effectiveTo? }` | `201 { assignmentId }` ou `202` pendente |
| `POST /hr/position-assignments/{assignmentId}/approval-outcome` | `hr.positions.assign` | Aplica decisão à atribuição | Sem corpo | `200` ou `202` |
| `POST /hr/employees/{employeeId}/documents` | `hr.employees.write` | Liga documento ao colaborador | `{ documentId, category }` | `201 { linkId }` |
| `GET /hr/employees/{employeeId}/documents` | `hr.employees.read` | Lista documentos do colaborador | Path `employeeId` | `200` |
| `GET /hr/contracts` | `hr.contracts.read` | Lista contratos | `employeeId?` | `200` |
| `POST /hr/contracts` | `hr.contracts.write` | Cria contrato | `{ employeeId, type, startsOn, endsOn?, monthlySalary, currency?, notes? }` | `201 { contractId }` |
| `POST /hr/contracts/{contractId}/termination` | `hr.contracts.write` | Termina contrato | `{ on? }` | `204` |
| `GET /hr/attendance` | `hr.attendance.read` | Lista assiduidade | `from?`, `to?`, `employeeId?`, `anomaliesOnly?` | `200` |
| `POST /hr/attendance/clock` | `hr.attendance.write` | Marca entrada ou saída | `{ employeeId, day?, late? }` | `200 { recordId, movimento, at }` |
| `POST /hr/attendance/absences` | `hr.attendance.write` | Regista ou justifica falta | `{ employeeId, day, justification? }` | `201` ou `200` |
| `GET /hr/leave` | `hr.leave.read` | Lista pedidos de ausência | `employeeId?` | `200` |
| `POST /hr/leave` | `hr.leave.write` | Solicita férias/ausência | `{ employeeId, type, startsOn, endsOn, reason? }` | `202` pendente |
| `POST /hr/leave/{leaveId}/cancellation` | `hr.leave.write` | Cancela pedido de ausência | Sem corpo | `204` |
| `POST /hr/leave/{leaveId}/approval-outcome` | `hr.leave.write` | Aplica decisão de ausência | Sem corpo | `200` ou `202` |
| `GET /hr/benefits` | `hr.benefits.read` | Lista benefícios | Sem parâmetros | `200` |
| `POST /hr/benefits` | `hr.benefits.write` | Cria benefício | `{ name, kind, monthlyValue, currency?, description? }` | `201 { benefitId }` |
| `GET /hr/benefits/enrolments` | `hr.benefits.read` | Lista adesões | `employeeId?` | `200` |
| `POST /hr/benefits/enrolments` | `hr.benefits.write` | Adere colaborador a benefício | `{ employeeId, benefitId, startsOn? }` | `201 { enrolmentId }` |
| `POST /hr/benefits/enrolments/{enrolmentId}/cancellation` | `hr.benefits.write` | Cancela adesão | `{ on? }` | `204` |
| `GET /hr/recruitment/openings` | `hr.recruitment.read` | Lista vagas | Sem parâmetros | `200` |
| `POST /hr/recruitment/openings` | `hr.recruitment.write` | Abre vaga | `{ title, departmentId?, vacancies?, description?, requirements? }` | `201 { openingId }` |
| `POST /hr/recruitment/openings/{openingId}/closure` | `hr.recruitment.write` | Fecha vaga | Sem corpo | `204` |
| `GET /hr/recruitment/candidates` | `hr.recruitment.read` | Lista candidatos | `openingId?` | `200` |
| `POST /hr/recruitment/openings/{openingId}/candidates` | `hr.recruitment.write` | Regista candidatura | `{ fullName, email?, phone?, appliedOn? }` | `201 { candidateId }` |
| `POST /hr/recruitment/candidates/{candidateId}/stage` | `hr.recruitment.write` | Avança candidato no funil | `{ stage }`: `Screening`, `Interview`, `Offer`, `Rejected` | `204` |
| `POST /hr/recruitment/candidates/{candidateId}/hire` | `hr.employees.write` | Contrata candidato | `{ departmentId? }` | `201 { employeeId }` |
| `GET /hr/lifecycle` | `hr.lifecycle.read` | Lista processos de entrada/saída | `kind?`, `employeeId?` | `200` |
| `POST /hr/lifecycle` | `hr.lifecycle.write` | Inicia processo de lifecycle | `{ employeeId, kind, lastWorkingDay?, reason?, tasks? }`; task `{ title, category, dueOn?, description? }` | `201 { processId }` |
| `POST /hr/lifecycle/{processId}/tasks/{taskId}/completion` | `hr.lifecycle.write` | Conclui tarefa | Sem corpo | `204` |
| `POST /hr/lifecycle/{processId}/completion` | `hr.lifecycle.write` | Conclui processo | Sem corpo | `204` |

Defaults relevantes: moeda `AOA`, abertura de vaga `vacancies=1`, e datas
omitidas usam a data actual. Atribuição de cargo e pedidos de ausência podem
ficar pendentes (`202`) enquanto aguardam aprovação.

## Approval

| Método e rota | Permissão | O que faz | Request/query | Sucesso |
|---|---|---|---|---|
| `GET /approval/policies` | `approval.policies.read` | Lista políticas de aprovação | Sem parâmetros | `200` |
| `POST /approval/policies` | `approval.policies.write` | Cria política | `{ processType, departmentId?, minimumAmount?, maximumAmount?, requiresBudgetCheck?, steps? }`; step `{ approverPositionId, mode?, slaHours? }` | `201 { policyId }` |
| `POST /approval/policies/{policyId}/deactivation` | `approval.policies.write` | Desactiva política | Sem corpo | `204` |
| `GET /approval/requests` | `approval.requests.read` | Lista pedidos de aprovação | `processType?`, `pendingFor?` | `200` |
| `GET /approval/requests/{requestId}` | `approval.requests.read` | Consulta estado do pedido | Path `requestId` | `200` estado |
| `GET /approval/requests/{requestId}/history` | `approval.requests.read` | Linha do tempo completa do pedido | Path `requestId` | `200` histórico |
| `POST /approval/requests/{requestId}/decisions` | `approval.requests.decide` | Regista decisão | `{ decidedByEmployeeId, action, notes? }`; action `Approved`, `Rejected`, `ClarificationRequested` | `200` estado |
| `POST /approval/requests/{requestId}/cancellation` | `approval.requests.read` | Cancela pedido | `{ cancelledByEmployeeId }` | `204` |

Modo de step assume `AnyApprover`; `AllApprovers` também é suportado. Uma
decisão incompatível com segregação devolve `403`; decisão rejeitada devolve
`409`.

**Cancelar exige ser quem submeteu (K18, fechado a 2026-08-29).** A permissão
`approval.requests.read` abre a porta — basta para saber que o pedido é seu —
mas quem decide de facto é o domínio: se `cancelledByEmployeeId` não for
igual a `requestedByEmployeeId`, devolve `403`, pela mesma razão de uma
decisão em violação de segregação.

**`history` difere do `GET` simples.** Esse devolve só `pendingApprovers` —
quem falta decidir agora, para um cliente que espera pela sua vez. O
histórico devolve **todas** as atribuições congeladas na submissão
(`assignments`), incluídas as já decididas e as de passos futuros, mais os
dados da própria submissão (`requestedByEmployeeId`, `amount`, `currency`,
`submittedAt`, `closedAt`) que o outro não expõe. É para quem reconstrói o que
aconteceu, não para quem opera o passo corrente.

**Desactivar uma política não afecta os pedidos em curso:** cada um guarda a
política que lhe foi aplicada e os aprovadores que dela resultaram, congelados
na submissão (BR-6). Desactivar decide o que vem a seguir.

Desactivar uma política já desactivada devolve `204` na mesma. **Não há
reactivação** — a submissão recusa quando duas políticas igualmente específicas
empatam, e reactivar uma antiga podia criar esse empate sem que quem reactiva o
visse. Quem precisa dela outra vez cria-a.

⚠ O cancelamento exige `approval.requests.read`, e não uma permissão de
escrita — está registado como **K18** e a permissão pode mudar. Não construir
o menu a contar que quem lê também cancela.

## Audit

| Método e rota | Permissão | O que faz | Query | Sucesso |
|---|---|---|---|---|
| `GET /audit/entries` | `audit.trail.read` | Consulta trilha de auditoria | `entityType?`, `entityId?`, `limit` (default `50`) | `200` colecção |

Não existe endpoint HTTP para escrever auditoria. Os módulos escrevem na
trilha através de contrato interno.

## Notifications

| Método e rota | Permissão | O que faz | Query | Sucesso |
|---|---|---|---|---|
| `GET /notifications/me` | JWT | Lista notificações do utilizador autenticado | `unreadOnly?` (default `false`), `limit` (default `50`) | `200` colecção |
| `POST /notifications/{notificationId}/read` | JWT | Marca notificação como lida | Sem corpo | `204` |
| `POST /notifications/read-all` | JWT | Marca todas as não lidas do próprio | Sem corpo | `200 { marcadas }` |

O utilizador é obtido do token, nunca do request. Uma notificação inexistente
ou de outro utilizador devolve `404`.

`read-all` devolve **quantas ficaram marcadas** em vez de `204`: o cliente
acabou de mostrar um contador de não lidas, e assim confirma-o sem voltar a
pedir a lista. `{ "marcadas": 0 }` é resposta normal e não erro. Só toca nas do
próprio.

## Health

| Método e rota | Permissão | O que faz | Sucesso |
|---|---|---|---|
| `GET /health` | Pública | Confirma que a API alcança a base de dados | `200 { status: "ok", database: "up" }` |

Se a base de dados estiver inacessível, devolve `503` com Problem Details.

## Notas de implementação para o frontend

1. Criar um interceptor HTTP que envie o Bearer token e trate `401` limpando a
   sessão; tratar `403` como falta de permissão, não como sessão expirada.
2. Usar `/identity/me` para construir menus e esconder acções, mas manter a
   autorização no backend como fonte final.
3. Após qualquer `202`, consultar o recurso indicado no header `Location` ou
   no identificador devolvido e apresentar estado pendente.
4. ⚠ **Os enumerados não vêm todos no mesmo formato.** `commercial.status` e
   `procurement.status` (fornecedor) saem como **número** — `0` = activo,
   `1` = inactivo; `hr.status` e o estado da requisição saem como **texto**.
   Verificado contra a API a 2026-08-27. Não assumir um dos dois: ler o campo
   pelo tipo que vem, ou confirmar no `/openapi/v1.json`.
5. Não usar `DELETE`, `PUT` ou `PATCH` para as operações existentes. Cancelar,
   desactivar, fechar e aprovar são todos `POST` por contrato.
6. Enviar `Content-Type: multipart/form-data` apenas para upload de
   documentos; os restantes requests de escrita são JSON.
7. Enviar um correlation/request id quando a infraestrutura do cliente o
   suportar e guardar o valor devolvido pelos logs para diagnóstico.
