# Rivo API - Guia para o Frontend

Documento gerado a partir das rotas implementadas no backend. A API tem 119
endpoints: 115 protegidos por JWT e 4 públicos. A contagem inclui `/health`.

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
| `POST /documents/` | `documents.write` | Faz upload de documento | `multipart/form-data`: ficheiro `file` e campo `category` | `201 DocumentDescriptor` |
| `GET /documents/{documentId}` | `documents.read` | Descarrega ficheiro | Path `documentId` | `200` binário com content type e nome |
| `GET /documents/{documentId}/metadata` | `documents.read` | Consulta metadados | Path `documentId` | `200 { documentId, fileName, contentType, sizeInBytes, category, contentHash, uploadedBy, uploadedAt }` |

O upload aceita ficheiros até 25 MB. Ficheiro vazio ou acima do limite devolve
`400`.

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
| `GET /finance/accounts/{accountId}/statement` | `finance.payables.read` | Consulta extracto | `from?`, `to?` | `200` extracto |
| `GET /finance/purchase-invoices` | `finance.payables.read` | Lista facturas de compra | `dueBefore?` | `200` |
| `GET /finance/purchase-invoices/{purchaseInvoiceId}` | `finance.payables.read` | Consulta factura de compra | Path `purchaseInvoiceId` | `200` factura |
| `POST /finance/purchase-invoices` | `finance.payables.write` | Regista factura de fornecedor | `{ supplierInvoiceNumber, supplierName, supplierTaxId, issuedOn?, dueOn?, currency?, netTotal, taxTotal, description? }` | `201 { purchaseInvoiceId }` |
| `GET /finance/payment-requests` | `finance.payables.read` | Lista pedidos de pagamento | `purchaseInvoiceId?` | `200` |
| `GET /finance/payment-requests/{paymentRequestId}` | `finance.payables.read` | Consulta pedido | Path `paymentRequestId` | `200` pedido |
| `POST /finance/payment-requests` | `finance.payments.request` | Cria pedido sujeito a aprovação | `{ purchaseInvoiceId, amount, requestedByEmployeeId, requestedOn?, costCentreId?, notes? }` | `202 { paymentRequestId, approvalRequestId, estado }` |
| `POST /finance/payment-requests/{paymentRequestId}/cancellation` | `finance.payments.request` | Cancela pedido | `{ reason }` | `204` |
| `POST /finance/payment-requests/{paymentRequestId}/execution` | `finance.payments.execute` | Executa pagamento aprovado | `{ bankAccountId, executedByEmployeeId, method, reference? }` | `200 { estado, saldoRestante }` |

`202` significa `estado: "PendenteAprovacao"`. Executar sem aprovação,
sem fundos ou violando segregação devolve `409` ou `403`, respectivamente.

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
| `GET /approval/requests` | `approval.requests.read` | Lista pedidos de aprovação | `processType?`, `pendingFor?` | `200` |
| `GET /approval/requests/{requestId}` | `approval.requests.read` | Consulta estado do pedido | Path `requestId` | `200` estado |
| `POST /approval/requests/{requestId}/decisions` | `approval.requests.decide` | Regista decisão | `{ decidedByEmployeeId, action, notes? }`; action `Approved`, `Rejected`, `ClarificationRequested` | `200` estado |
| `POST /approval/requests/{requestId}/cancellation` | `approval.requests.read` | Cancela pedido | Sem campos de body explicitamente declarados | `204` |

Modo de step assume `AnyApprover`; `AllApprovers` também é suportado. Uma
decisão incompatível com segregação devolve `403`; decisão rejeitada devolve
`409`.

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

O utilizador é obtido do token, nunca do request. Uma notificação inexistente
ou de outro utilizador devolve `404`.

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
4. Não usar `DELETE`, `PUT` ou `PATCH` para as operações existentes. Cancelar,
   desactivar, fechar e aprovar são todos `POST` por contrato.
5. Enviar `Content-Type: multipart/form-data` apenas para upload de
   documentos; os restantes requests de escrita são JSON.
6. Enviar um correlation/request id quando a infraestrutura do cliente o
   suportar e guardar o valor devolvido pelos logs para diagnóstico.