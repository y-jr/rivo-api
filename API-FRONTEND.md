# Rivo API — Guia para o Frontend

_Regenerado a 2026-09-04 a partir do `/openapi/v1.json` da aplicação a
correr, cruzado rota a rota com as permissões declaradas no código._

**244 endpoints**: 4 públicos, 18 só com autenticação, 222 com permissão
exigida. A contagem inclui `/health`.

## Como este documento é produzido

Não é escrito à mão, e a distinção importa porque a versão anterior tinha
ficado com metade das rotas por documentar sem que nada o assinalasse.

| Coluna | Fonte |
|---|---|
| Método, rota, request, query | `/openapi/v1.json`, gerado pela própria aplicação |
| Permissão | Extraída do `.RequireAuthorization(...)` de cada endpoint, com o valor real da constante (`IdentityPermissions.UsersRead` → `identity.users.read`) |
| Código de sucesso | Extraído do `Results.*` de cada handler |

**O OpenAPI não sabe as permissões e não sabe os códigos de sucesso.** Os
minimal APIs devolvem `IResult` sem `.Produces<T>()`, por isso o documento
gerado declara `200` para tudo — o que é falso para **121 das 244 rotas**,
que devolvem `201`, `202` ou `204`. É por isso que estas duas colunas vêm do
código e não do OpenAPI, e é por isso que este ficheiro continua a existir
em vez de se remeter para o Swagger.

## Como ligar

Em desenvolvimento a API responde em `http://localhost:5080`. Em produção,
a URL pública do deployment.

Rotas protegidas usam:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json
```

O token obtém-se em `POST /identity/login` ou `POST /identity/login/google`.
Guardar `accessToken` e `expiresAt`. O token transporta uma sessão
revogável: depois de `POST /identity/logout`, o frontend deve tratá-lo como
inválido mesmo que ainda não tenha expirado.

⚠ **O ambiente publicado não tem TLS** (K16). O token e as credenciais
viajam em claro. É aceitável em teste, não em produção.

O Swagger é publicado por interruptor próprio, `EXPOSE_OPENAPI` (ADR-038),
e não pelo nome do ambiente:

- Interface: `/swagger`
- Documento: `/openapi/v1.json`

Se responderem `404`, o interruptor está a `false` e este documento é o
contrato.

## Permissões e perfis

Há **70 permissões** e **8 perfis de acesso**. `Admin` tem as 70; os outros
sete têm subconjuntos desenhados por segregação de funções — por exemplo,
quem aprova um pagamento não o executa, e por isso nenhum perfil tem
`finance.payments.request` e `finance.payments.execute` ao mesmo tempo além
do `Admin`.

Duas notas que poupam tempo a quem constrói o frontend:

- **`JWT` na coluna de permissão significa "qualquer utilizador
  autenticado"**, e é quase sempre uma rota de "o próprio" — o portal do
  colaborador e o portal do cliente resolvem a identidade a partir do token
  e **nunca aceitam um identificador vindo do pedido**. Não há como pedir
  os dados de outra pessoa por estas rotas.
- **`403` não é sempre falta de permissão.** Também aparece quando a
  segregação de funções bloqueia a acção — quem submeteu um pedido de
  aprovação recebe `403` ao tentar decidi-lo, mesmo tendo
  `approval.requests.decide`.

## Respostas e erros

| Código | Significado |
|---|---|
| `200` | Leitura ou operação concluída com corpo |
| `201` | Recurso criado; devolve normalmente o identificador |
| `202` | Operação aceite mas pendente de decisão de aprovação |
| `204` | Concluída sem corpo |
| `400` | Corpo ou parâmetros inválidos — `ValidationProblemDetails`, mensagens em `errors` |
| `401` | Token ausente, inválido, expirado ou sessão revogada |
| `403` | Autenticado, mas sem permissão ou bloqueado por segregação de funções |
| `404` | Inexistente — ou pertence a outro utilizador, que é indistinguível de propósito |
| `409` | Conflito com o estado actual, duplicação, ou concorrência optimista (ADR-035) |
| `501` | Capacidade não configurada neste ambiente (Google, motor de aprovação, código de isenção fiscal) |
| `503` | Dependência indisponível — sobretudo a base de dados, em `/health` |

Onde a coluna "Sucesso" mostra dois códigos (`200/202`), a rota devolve um
ou outro consoante o resultado: tipicamente `202` quando a operação ficou à
espera de uma decisão de aprovação.

Datas vão como `YYYY-MM-DD`. Valores monetários são decimais — **nunca
vírgula flutuante**, e o backend recusa-os arredondados de forma diferente
da que enviou.

Os campos exactos das respostas de leitura não estão aqui: confirmam-se em
`/openapi/v1.json`, porque vários read models são inferidos directamente
dos casos de uso e não têm DTO HTTP nomeado.

## Identidade e acesso

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `POST /identity/register` | Pública | { email, password } | `201` |
| `POST /identity/login` | Pública | { email, password } | `200` |
| `POST /identity/login/google` | Pública | { idToken } | `200` |
| `POST /identity/logout` | JWT | Sem corpo | `204` |
| `GET /identity/me` | JWT | Sem corpo | `200` |
| `GET /identity/users` | `identity.users.read` | Sem corpo | `200` |
| `GET /identity/roles` | `identity.roles.read` | Sem corpo | `200` |
| `POST /identity/users/{userId}/roles` | `identity.roles.assign` | { profile } | `204` |
| `POST /identity/me/password` | JWT | { currentPassword, newPassword } | `204` |
| `GET /identity/me/sessions` | JWT | Sem corpo | `200` |
| `POST /identity/me/sessions/{sessionId}/revocation` | JWT | Sem corpo | `204` |
| `POST /identity/users/{userId}/password-reset` | `identity.users.write` | { newPassword } | `204` |
| `POST /identity/users/{userId}/status` | `identity.users.write` | { active, reason } | `204` |
| `POST /identity/users/{userId}/roles/{profile}/removal` | `identity.roles.assign` | Sem corpo | `204` |

## Auditoria

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /audit/entries` | `audit.trail.read` | query: `entityType, entityId, limit` | `200` |

## Documentos

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `POST /documents` | `documents.write` | multipart: ficheiro `file` | `201` |
| `GET /documents` | `documents.read` | query: `category, from, to, limit` | `200` |
| `GET /documents/{documentId}` | `documents.read` | Sem corpo | `200` |
| `GET /documents/{documentId}/metadata` | `documents.read` | Sem corpo | `200` |

## Notificações

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /notifications/me` | JWT | query: `unreadOnly, limit` | `200` |
| `POST /notifications/{notificationId}/read` | JWT | Sem corpo | `204` |
| `POST /notifications/read-all` | JWT | Sem corpo | `200` |

## Recursos Humanos

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /hr/employees` | `hr.employees.read` | Sem corpo | `200` |
| `POST /hr/employees` | `hr.employees.write` | { fullName, departmentId, hiredOn } — **`userId` dá 400** (ADR-054) | `201` |
| `POST /hr/employees/{employeeId}/account` | `hr.employees.link_account` | { userId } — **fora do perfil HR** (ADR-051) | `204` |
| `DELETE /hr/employees/{employeeId}/account` | `hr.employees.link_account` | Sem corpo — decisões já tomadas continuam válidas (ADR-052) | `204` |
| `GET /hr/employees/{employeeId}/account-history` | `hr.employees.link_account` | Sem corpo — que conta pôde agir por esta pessoa, e quando (ADR-053) | `200` |
| `GET /hr/employees/{employeeId}` | `hr.employees.read` | Sem corpo | `200` |
| `GET /hr/departments` | `hr.departments.read` | Sem corpo | `200` |
| `POST /hr/departments` | `hr.departments.write` | { name, managerId } | `201` |
| `GET /hr/positions` | `hr.positions.read` | Sem corpo | `200` |
| `POST /hr/positions` | `hr.positions.write` | { name, hierarchyLevel, grantsApprovalAuthority } | `201` |
| `POST /hr/employees/{employeeId}/positions` | `hr.positions.assign` | { positionId, effectiveFrom, effectiveTo } | `201/202` |
| `POST /hr/position-assignments/{assignmentId}/approval-outcome` | `hr.positions.assign` | Sem corpo | `200/202` |
| `POST /hr/employees/{employeeId}/documents` | `hr.employees.write` | { documentId, category } | `201` |
| `GET /hr/employees/{employeeId}/documents` | `hr.employees.read` | Sem corpo | `200` |
| `GET /hr/contracts` | `hr.contracts.read` | query: `employeeId` | `200` |
| `POST /hr/contracts` | `hr.contracts.write` | { employeeId, type, startsOn, endsOn, monthlySalary, currency, notes } | `201` |
| `POST /hr/contracts/{contractId}/termination` | `hr.contracts.write` | { on } | `204` |
| `GET /hr/attendance` | `hr.attendance.read` | query: `from, to, employeeId, anomaliesOnly` | `200` |
| `POST /hr/attendance/clock` | `hr.attendance.write` | { employeeId, day, late } | `200` |
| `POST /hr/attendance/absences` | `hr.attendance.write` | { employeeId, day, justification } | `201/200` |
| `GET /hr/leave` | `hr.leave.read` | query: `employeeId` | `200` |
| `POST /hr/leave` | `hr.leave.write` | { employeeId, type, startsOn, endsOn, reason } | `202` |
| `POST /hr/leave/{leaveId}/cancellation` | `hr.leave.write` | Sem corpo | `204` |
| `POST /hr/leave/{leaveId}/approval-outcome` | `hr.leave.write` | Sem corpo | `200/202` |
| `GET /hr/benefits` | `hr.benefits.read` | Sem corpo | `200` |
| `POST /hr/benefits` | `hr.benefits.write` | { name, kind, monthlyValue, currency, description } | `201` |
| `GET /hr/benefits/enrolments` | `hr.benefits.read` | query: `employeeId` | `200` |
| `POST /hr/benefits/enrolments` | `hr.benefits.write` | { employeeId, benefitId, startsOn } | `200` |
| `POST /hr/benefits/enrolments/{enrolmentId}/cancellation` | `hr.benefits.write` | { on } | `204` |
| `GET /hr/recruitment/openings` | `hr.recruitment.read` | Sem corpo | `200` |
| `POST /hr/recruitment/openings` | `hr.recruitment.write` | { title, departmentId, vacancies, description, requirements } | `200` |
| `POST /hr/recruitment/openings/{openingId}/closure` | `hr.recruitment.write` | Sem corpo | `204` |
| `GET /hr/recruitment/candidates` | `hr.recruitment.read` | query: `openingId` | `200` |
| `POST /hr/recruitment/openings/{openingId}/candidates` | `hr.recruitment.write` | { fullName, email, phone, appliedOn } | `200` |
| `POST /hr/recruitment/candidates/{candidateId}/stage` | `hr.recruitment.write` | { stage } | `204` |
| `POST /hr/recruitment/candidates/{candidateId}/hire` | `hr.employees.write` | { departmentId } | `201` |
| `GET /hr/lifecycle` | `hr.lifecycle.read` | query: `kind, employeeId` | `200` |
| `POST /hr/lifecycle` | `hr.lifecycle.write` | { employeeId, kind, lastWorkingDay, reason, tasks } | `201` |
| `POST /hr/lifecycle/{processId}/tasks/{taskId}/completion` | `hr.lifecycle.write` | Sem corpo | `200` |
| `POST /hr/lifecycle/{processId}/completion` | `hr.lifecycle.write` | Sem corpo | `204` |

## Aprovações

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /approval/policies` | `approval.policies.read` | Sem corpo | `200` |
| `POST /approval/policies` | `approval.policies.write` | { processType, departmentId, minimumAmount, maximumAmount, requiresBudgetCheck, steps } | `201` |
| `POST /approval/policies/{policyId}/deactivation` | `approval.policies.write` | Sem corpo | `204` |
| `GET /approval/requests` | `approval.requests.read` | query: `processType, pendingFor` | `200` |
| `GET /approval/requests/{requestId}` | `approval.requests.read` | Sem corpo | `200` |
| `GET /approval/requests/{requestId}/history` | `approval.requests.read` | Sem corpo | `200` |
| `POST /approval/requests/{requestId}/decisions` | `approval.requests.decide` | { action, notes } — **quem decide vem do token** (ADR-050) | `200` |
| `POST /approval/requests/{requestId}/cancellation` | `approval.requests.read` | Sem corpo — **quem cancela vem do token** (ADR-050) | `204` |

## Fiscal

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /fiscal/tax-rates` | `fiscal.rates.read` | Sem corpo | `200` |
| `POST /fiscal/tax-rates` | `fiscal.rates.write` | { kind, code, description } | `201` |
| `POST /fiscal/tax-rates/{scheduleId}/versions` | `fiscal.rates.write` | { percentage, effectiveFrom, effectiveTo, legalInstrument } | `201` |
| `GET /fiscal/tax-rates/determination` | `fiscal.rates.read` | query: `taxCode, taxPointDate, kind` | `200` |
| `GET /fiscal/income-tax-schedule` | `fiscal.rates.read` | Sem corpo | `200` |
| `POST /fiscal/income-tax-schedule/versions` | `fiscal.rates.write` | { brackets, effectiveFrom, effectiveTo, legalInstrument } | `201` |
| `GET /fiscal/income-tax-schedule/determination` | `fiscal.rates.read` | query: `taxableIncome, taxPointDate` | `200` |
| `GET /fiscal/subsidy-exemptions` | `fiscal.rates.read` | query: `kind` | `200` |
| `POST /fiscal/subsidy-exemptions/versions` | `fiscal.rates.write` | { kind, amount, effectiveFrom, effectiveTo, legalInstrument } | `201` |
| `GET /fiscal/subsidy-exemptions/determination` | `fiscal.rates.read` | query: `kind, taxPointDate` | `200` |

## Comercial

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /commercial/customers` | `commercial.customers.read` | query: `includeInactive` | `200` |
| `POST /commercial/customers` | `commercial.customers.write` | { name, taxId, addressDetail, city, country, email, phone } | `201` |
| `GET /commercial/customers/{customerId}` | `commercial.customers.read` | Sem corpo | `200` |
| `POST /commercial/customers/{customerId}/details` | `commercial.customers.write` | { name, addressDetail, city, country, email, phone } | `204` |
| `POST /commercial/customers/{customerId}/status` | `commercial.customers.write` | { active } | `204` |
| `POST /commercial/customers/{customerId}/account` | `commercial.customers.write` | { userId } | `204` |
| `POST /commercial/customers/{customerId}/owner` | `commercial.customers.write` | { employeeId } | `204` |

## Financeiro

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /finance/series` | `finance.invoices.read` | Sem corpo | `200` |
| `POST /finance/series` | `finance.series.write` | { code } | `201` |
| `GET /finance/sales-invoices` | `finance.invoices.read` | query: `customerId, from, to` | `200` |
| `POST /finance/sales-invoices` | `finance.invoices.write` | { customerId, series, issuedOn, taxPointDate, currency, lines } | `201` |
| `GET /finance/sales-invoices/{invoiceId}` | `finance.invoices.read` | Sem corpo | `200` |
| `POST /finance/sales-invoices/{invoiceId}/cancellation` | `finance.invoices.cancel` | { reason } | `204` |
| `GET /finance/sales-invoices/{invoiceId}/balance` | `finance.invoices.read` | Sem corpo | `200` |
| `GET /finance/credit-notes` | `finance.invoices.read` | query: `salesInvoiceId` | `200` |
| `POST /finance/credit-notes` | `finance.invoices.cancel` | { salesInvoiceId, series, issuedOn, reason, lines } | `201` |
| `GET /finance/credit-notes/{creditNoteId}` | `finance.invoices.read` | Sem corpo | `200` |
| `POST /finance/credit-notes/{creditNoteId}/cancellation` | `finance.invoices.cancel` | { reason } | `204` |
| `GET /finance/receipts` | `finance.receipts.read` | query: `customerId, from, to` | `200` |
| `POST /finance/receipts` | `finance.receipts.write` | { series, receivedOn, method, notes, settlements } | `201` |
| `GET /finance/receipts/{receiptId}` | `finance.receipts.read` | Sem corpo | `200` |
| `POST /finance/receipts/{receiptId}/cancellation` | `finance.invoices.cancel` | { reason } | `204` |
| `GET /finance/payment-claims` | `finance.receipts.read` | query: `customerId, status` | `200` |
| `POST /finance/payment-claims/{claimId}/confirmation` | `finance.receipts.write` | Sem corpo | `200` |
| `POST /finance/payment-claims/{claimId}/rejection` | `finance.receipts.write` | { reason } | `204` |
| `GET /finance/accounts` | `finance.payables.read` | query: `includeClosed` | `200` |
| `POST /finance/accounts` | `finance.payables.write` | { name, bank, iban, currency } | `201` |
| `POST /finance/accounts/{accountId}/deposits` | `finance.payables.write` | { amount, reference } | `204` |
| `POST /finance/accounts/{accountId}/withdrawals` | `finance.payables.write` | { amount, description } | `204` |
| `POST /finance/accounts/{accountId}/closure` | `finance.payables.write` | { active, reason } | `204` |
| `POST /finance/accounts/{accountId}/reopening` | `finance.payables.write` | Sem corpo | `204` |
| `GET /finance/accounts/{accountId}/statement` | `finance.payables.read` | query: `from, to` | `200` |
| `GET /finance/purchase-invoices` | `finance.payables.read` | query: `dueBefore` | `200` |
| `POST /finance/purchase-invoices` | `finance.payables.write` | { supplierInvoiceNumber, supplierId, purchaseOrderId, supplierName, supplierTaxId, issuedOn, dueOn, currency, netTotal, taxTotal, description } | `201` |
| `GET /finance/purchase-invoices/{purchaseInvoiceId}` | `finance.payables.read` | Sem corpo | `200` |
| `GET /finance/purchase-invoices/{purchaseInvoiceId}/match` | `finance.payables.read` | Sem corpo | `200` |
| `GET /finance/payment-requests` | `finance.payables.read` | query: `purchaseInvoiceId` | `200` |
| `POST /finance/payment-requests` | `finance.payments.request` | { purchaseInvoiceId, amount, requestedByEmployeeId, requestedOn, costCentreId, notes } | `202` |
| `GET /finance/payment-requests/{paymentRequestId}` | `finance.payables.read` | Sem corpo | `200` |
| `POST /finance/payment-requests/{paymentRequestId}/cancellation` | `finance.payments.request` | { reason } | `204` |
| `POST /finance/payment-requests/{paymentRequestId}/execution` | `finance.payments.execute` | { bankAccountId, executedByEmployeeId, method, reference } | `200` |
| `GET /finance/ledger/accounts` | `finance.ledger.read` | query: `includeInactive` | `200` |
| `POST /finance/ledger/accounts` | `finance.ledger.write` | { code, name, category, parentCode } | `201` |
| `POST /finance/ledger/accounts/{accountId}/deactivation` | `finance.ledger.write` | Sem corpo | `204` |
| `GET /finance/ledger/journals` | `finance.ledger.read` | query: `includeInactive` | `200` |
| `POST /finance/ledger/journals` | `finance.ledger.write` | { code, name } | `201` |
| `GET /finance/ledger/entries` | `finance.ledger.read` | query: `journalId, fiscalYear, period` | `200` |
| `POST /finance/ledger/entries` | `finance.ledger.write` | { journalCode, archivalNumber, transactionDate, fiscalYear, period, description, type, lines } | `201` |
| `GET /finance/ledger/entries/{entryId}` | `finance.ledger.read` | Sem corpo | `200` |
| `POST /finance/ledger/entries/{entryId}/void` | `finance.ledger.write` | { reason } | `204` |
| `GET /finance/ledger/periods` | `finance.ledger.read` | query: `fiscalYear` | `200` |
| `POST /finance/ledger/periods` | `finance.ledger.write` | { fiscalYear, number } | `201` |
| `POST /finance/ledger/periods/{fiscalYear}/{number}/closure` | `finance.ledger.close` | { closedByEmployeeId } | `204` |
| `POST /finance/ledger/periods/{fiscalYear}/{number}/reopening` | `finance.ledger.close` | { reason } | `204` |
| `GET /finance/ledger/trial-balance` | `finance.ledger.read` | query: `fiscalYear, period` | `200` |
| `GET /finance/ledger/posting-rules` | `finance.ledger.read` | query: `includeInactive` | `200` |
| `POST /finance/ledger/posting-rules` | `finance.ledger.close` | { event, journalCode, description, lines } | `201` |
| `POST /finance/ledger/posting-rules/{ruleId}/deactivation` | `finance.ledger.close` | Sem corpo | `204` |
| `GET /finance/ledger/chart-versions` | `finance.ledger.read` | query: `includeInactive` | `200` |
| `POST /finance/ledger/chart-versions` | `finance.ledger.close` | { jurisdiction, name, version, source, effectiveFrom, effectiveTo } | `201` |
| `GET /finance/ledger/accounting-rules` | `finance.ledger.read` | query: `includeInactive` | `200` |
| `POST /finance/ledger/accounting-rules` | `finance.ledger.close` | { code, name, sourceType, source, effectiveFrom, effectiveTo, lines } | `201` |
| `POST /finance/ledger/accounting-rules/{ruleId}/deactivation` | `finance.ledger.close` | Sem corpo | `204` |
| `GET /finance/planning/cost-centres` | `finance.planning.read` | query: `includeInactive` | `200` |
| `POST /finance/planning/cost-centres` | `finance.planning.write` | { code, name, departmentId, responsibleEmployeeId } | `201` |
| `GET /finance/planning/budgets` | `finance.planning.read` | query: `costCentreId, fiscalYear` | `200` |
| `POST /finance/planning/budgets` | `finance.planning.write` | { costCentreId, fiscalYear, currency, monthlyCeilings } | `201` |
| `POST /finance/planning/budgets/{budgetId}/revision` | `finance.planning.write` | { monthlyCeilings } | `204` |
| `POST /finance/planning/budgets/{budgetId}/approval` | `finance.budgets.approve` | { approvedByEmployeeId } | `204` |
| `GET /finance/planning/cost-forecasts` | `finance.planning.read` | query: `departmentId, fiscalYear` | `200` |
| `POST /finance/planning/cost-forecasts` | `finance.planning.write` | { departmentId, fiscalYear, month, currency, operationalCosts, fixedCosts, submit } | `201` |

## Procurement

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /procurement/suppliers` | `procurement.suppliers.read` | query: `includeInactive` | `200` |
| `POST /procurement/suppliers` | `procurement.suppliers.write` | { name, taxId, iban, email, phone } | `201` |
| `GET /procurement/suppliers/{supplierId}` | `procurement.suppliers.read` | Sem corpo | `200` |
| `POST /procurement/suppliers/{supplierId}/details` | `procurement.suppliers.write` | { name, iban, email, phone } | `204` |
| `POST /procurement/suppliers/{supplierId}/status` | `procurement.suppliers.write` | { active } | `204` |
| `GET /procurement/requisitions` | `procurement.requisitions.read` | query: `requestedByEmployeeId, status` | `200` |
| `POST /procurement/requisitions` | `procurement.requisitions.write` | { requestedByEmployeeId, departmentId, justification, currency, requestedOn, lines } | `201` |
| `GET /procurement/requisitions/{requisitionId}` | `procurement.requisitions.read` | Sem corpo | `200` |
| `POST /procurement/requisitions/{requisitionId}/submission` | `procurement.requisitions.write` | Sem corpo | `202` |
| `POST /procurement/requisitions/{requisitionId}/approval-outcome` | `procurement.requisitions.read` | Sem corpo | `200/202` |
| `POST /procurement/requisitions/{requisitionId}/cancellation` | `procurement.requisitions.write` | { reason } | `204` |
| `GET /procurement/orders` | `procurement.orders.read` | query: `requisitionId, supplierId` | `200` |
| `GET /procurement/orders/{purchaseOrderId}` | `procurement.orders.read` | Sem corpo | `200` |
| `POST /procurement/requisitions/{requisitionId}/orders` | `procurement.orders.write` | { supplierId, issuedOn, expectedOn, lines } | `201` |
| `POST /procurement/orders/{purchaseOrderId}/cancellation` | `procurement.orders.write` | { reason } | `204` |
| `GET /procurement/receipts` | `procurement.receipts.read` | query: `purchaseOrderId` | `200` |
| `GET /procurement/receipts/{goodsReceiptId}` | `procurement.receipts.read` | Sem corpo | `200` |
| `POST /procurement/orders/{purchaseOrderId}/receipts` | `procurement.receipts.write` | { receivedByEmployeeId, receivedOn, deliveryNote, lines } | `201` |
| `POST /procurement/receipts/{goodsReceiptId}/cancellation` | `procurement.receipts.write` | { reason } | `204` |

## Salários

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /payroll/runs` | `payroll.runs.read` | Sem corpo | `200` |
| `POST /payroll/runs` | `payroll.runs.write` | { year, month, openedByEmployeeId } | `201` |
| `GET /payroll/runs/{runId}` | `payroll.runs.read` | Sem corpo | `200` |
| `POST /payroll/runs/{runId}/items` | `payroll.runs.write` | { employeeId, grossSalary, foodAllowance, transportAllowance, vacationAllowance, christmasAllowance } | `201` |
| `POST /payroll/runs/{runId}/submission` | `payroll.runs.write` | Sem corpo | `200` |
| `POST /payroll/runs/{runId}/decision` | `payroll.runs.read` | Sem corpo | `200` |
| `POST /payroll/runs/{runId}/items/{itemId}/documents` | `payroll.runs.write` | { documentId, category } | `201` |
| `GET /payroll/runs/{runId}/items/{itemId}/documents` | `payroll.runs.read` | Sem corpo | `200` |

## Projectos

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /projects` | `projects.projects.read` | query: `includeClosed` | `200` |
| `POST /projects` | `projects.projects.write` | { name, startDate } | `201` |
| `GET /projects/{projectId}` | `projects.projects.read` | Sem corpo | `200` |
| `POST /projects/{projectId}/closure` | `projects.projects.write` | { endDate } | `204` |
| `POST /projects/{projectId}/milestones` | `projects.projects.write` | { name, targetDate } | `201` |
| `POST /projects/{projectId}/milestones/{milestoneId}/reached` | `projects.projects.write` | { reachedOn } | `204` |
| `POST /projects/{projectId}/tasks` | `projects.projects.write` | { title, dueDate, assignedEmployeeId } | `201` |
| `POST /projects/{projectId}/tasks/{taskId}/assignment` | `projects.projects.write` | { employeeId } | `204` |
| `POST /projects/{projectId}/tasks/{taskId}/completion` | `projects.projects.write` | Sem corpo | `200` |
| `POST /projects/{projectId}/tasks/{taskId}/cancellation` | `projects.projects.write` | Sem corpo | `204` |
| `POST /projects/{projectId}/budget` | `projects.projects.write` | { amount, currency } | `204` |
| `POST /projects/{projectId}/allocations` | `projects.projects.write` | { kind, resourceId, startsOn } | `201` |
| `POST /projects/{projectId}/allocations/{allocationId}/end` | `projects.projects.write` | { endsOn } | `204` |

## Inventário

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /inventory/items` | `inventory.items.read` | query: `includeInactive` | `200` |
| `POST /inventory/items` | `inventory.items.write` | { sku, name, unit } | `201` |
| `GET /inventory/items/{itemId}` | `inventory.items.read` | Sem corpo | `200` |
| `POST /inventory/items/{itemId}/status` | `inventory.items.write` | { active } | `204` |
| `POST /inventory/items/{itemId}/movements/receipts` | `inventory.items.write` | { series, receivedOn, method, notes, settlements } | `200` |
| `POST /inventory/items/{itemId}/movements/issues` | `inventory.items.write` | { warehouseId, quantity, reason, occurredOn } | `200` |
| `POST /inventory/items/{itemId}/movements/adjustments` | `inventory.items.write` | { warehouseId, quantityDelta, reason, occurredOn } | `200` |
| `POST /inventory/items/{itemId}/movements/transfers` | `inventory.items.write` | { fromWarehouseId, toWarehouseId, quantity, reason, occurredOn } | `201` |
| `GET /inventory/warehouses` | `inventory.items.read` | query: `includeInactive` | `200` |
| `POST /inventory/warehouses` | `inventory.items.write` | { code, name } | `201` |
| `GET /inventory/warehouses/{warehouseId}` | `inventory.items.read` | Sem corpo | `200` |
| `POST /inventory/warehouses/{warehouseId}/status` | `inventory.items.write` | { active } | `204` |
| `GET /inventory/counts` | `inventory.items.read` | query: `warehouseId` | `200` |
| `POST /inventory/counts` | `inventory.items.write` | { warehouseId, occurredOn } | `201` |
| `GET /inventory/counts/{countId}` | `inventory.items.read` | Sem corpo | `200` |
| `POST /inventory/counts/{countId}/lines` | `inventory.items.write` | { itemId, countedQuantity } | `201` |
| `POST /inventory/counts/{countId}/close` | `inventory.items.write` | Sem corpo | `200` |
| `POST /inventory/counts/{countId}/cancellation` | `inventory.items.write` | { reason } | `204` |
| `GET /inventory/valuation` | `inventory.items.read` | query: `from, to` | `200` |

## Frota

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /fleet/vehicles` | `fleet.vehicles.read` | query: `includeInactive` | `200` |
| `POST /fleet/vehicles` | `fleet.vehicles.write` | { plateNumber, model } | `201` |
| `GET /fleet/vehicles/{vehicleId}` | `fleet.vehicles.read` | Sem corpo | `200` |
| `POST /fleet/vehicles/{vehicleId}/deactivation` | `fleet.vehicles.write` | Sem corpo | `204` |
| `POST /fleet/vehicles/{vehicleId}/maintenance` | `fleet.vehicles.write` | { type, description, startedOn } | `201` |
| `POST /fleet/vehicles/{vehicleId}/maintenance/{maintenanceId}/closure` | `fleet.vehicles.write` | { endedOn, cost } | `204` |
| `POST /fleet/vehicles/{vehicleId}/assignments` | `fleet.vehicles.write` | { employeeId, startedOn } | `201` |
| `POST /fleet/vehicles/{vehicleId}/assignments/{assignmentId}/closure` | `fleet.vehicles.write` | { endedOn } | `204` |
| `POST /fleet/vehicles/{vehicleId}/maintenance-plans` | `fleet.vehicles.write` | { description, intervalDays, firstDueOn } | `201` |
| `POST /fleet/vehicles/{vehicleId}/maintenance-plans/{planId}/cycles` | `fleet.vehicles.write` | { completedOn } | `200` |
| `POST /fleet/vehicles/{vehicleId}/maintenance-plans/{planId}/cancellation` | `fleet.vehicles.write` | Sem corpo | `204` |
| `GET /fleet/maintenance-plans/due` | `fleet.vehicles.read` | query: `withinDays` | `200` |
| `POST /fleet/vehicles/{vehicleId}/trips` | `fleet.vehicles.write` | { driverId, startedOn, endedOn, startOdometer, endOdometer, purpose } | `201` |
| `POST /fleet/vehicles/{vehicleId}/expenses` | `fleet.vehicles.write` | { category, amount, occurredOn, description } | `201` |
| `GET /fleet/vehicles/{vehicleId}/documents` | `fleet.vehicles.read` | Sem corpo | `200` |
| `POST /fleet/vehicles/{vehicleId}/documents` | `fleet.vehicles.write` | { documentId, category } | `201` |

## Mensagens e tickets

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /messaging/conversations` | `messaging.conversations.read` | query: `status, kind` | `200` |
| `GET /messaging/conversations/{conversationId}` | `messaging.conversations.read` | Sem corpo | `200` |
| `POST /messaging/conversations/{conversationId}/messages` | `messaging.conversations.write` | { body } | `201` |
| `POST /messaging/conversations/{conversationId}/closure` | `messaging.conversations.write` | Sem corpo | `204` |

## Configurações e Administração

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /settings/overview` | `identity.roles.read` + `approval.policies.read` | Sem corpo | `200` |
| `POST /settings/import/customers` | `commercial.customers.write` | multipart: ficheiro `file` | `200` |
| `POST /settings/import/employees` | `hr.employees.write` | multipart: ficheiro `file` | `200` |
| `POST /settings/import/suppliers` | `procurement.suppliers.write` | multipart: ficheiro `file` | `200` |

## Portal do Colaborador

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /portal/me` | JWT | Sem corpo | `200` |

## Portal do Cliente

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /customer-portal/me` | JWT | query: `from, to, currency` | `200` |
| `GET /customer-portal/me/statement` | JWT | query: `from, to, currency` | `200` |
| `POST /customer-portal/me/payment-claims` | JWT | { salesInvoiceId, amount, paidOn, documentId, notes } | `201` |
| `GET /customer-portal/me/payment-claims` | JWT | Sem corpo | `200` |
| `POST /customer-portal/me/messages` | JWT | { body } | `201` |
| `GET /customer-portal/me/messages` | JWT | Sem corpo | `200` |
| `POST /customer-portal/me/tickets` | JWT | { subject, body } | `201` |
| `GET /customer-portal/me/tickets` | JWT | Sem corpo | `200` |
| `POST /customer-portal/me/tickets/{conversationId}/messages` | JWT | { body } | `201` |

## Dashboard Executivo

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /dashboard/overview` | `dashboard.overview.read` | query: `from, to, currency, topCustomers` | `200` |

## Analytics

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /analytics/overview` | `analytics.overview.read` | query: `from, to, currency` | `200` |

## Saúde

| Método e rota | Permissão | Request | Sucesso |
|---|---|---|---|
| `GET /health` | Pública | Sem corpo | `200/503` |


