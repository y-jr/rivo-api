# finance — Financeiro

**Classificação:** core domain. O domínio mais denso do sistema.

## Responsabilidade

O ciclo de vida do dinheiro: o que se deve, o que se é devido, o que existe
em caixa, e como isso se traduz em livros contabilísticos.

Bounded contexts internos:

| Contexto | Responsabilidade |
|---|---|
| **Contas a Pagar (AP)** | Facturas de compra, pedidos de pagamento |
| **Tesouraria** | Contas bancárias, disponibilidade, execução de pagamentos e recebimentos |
| **Contas a Receber (AR)** | Facturas de venda, recebimentos |
| **Contabilidade & Fecho** | Plano de contas (PGC angolano), lançamentos, fecho |
| **Planeamento** | Centros de custo, orçamentos, previsão de custos departamentais |

Tesouraria **não é módulo separado** — é contexto interno de `finance`. O
ponto Approval→Tesouraria é o ponto de consistência forte do sistema, e
mantê-lo dentro da mesma fronteira transaccional é uma das razões
principais do monólito modular (ADR-001).

## Conceitos

| Conceito | Notas |
|---|---|
| Factura de Venda / Recebimento | AR; cliente pertence a `commercial` |
| Factura de Compra | AP; fornecedor pertence a `procurement` |
| Pedido de Pagamento | estado limitado a `elegível` / `executado` — **sem passos de aprovação embutidos** |
| Execução de Pagamento | só criável se `approval` confirmar decisão "Aprovado", revalidada no momento |
| Disponibilidade de Tesouraria | consultada antes de qualquer execução |
| Conta Bancária | multi-moeda (AOA, USD, EUR) |
| Centro de Custo | departamento_id **opcional**; responsável próprio (ADR-005) |
| Orçamento / Linha de Orçamento | tecto de controlo, mensal por centro de custo |
| Previsão de Custos Departamentais | input mensal ao carregamento de caixa — **entidade distinta de Orçamento** (ADR-006) |
| Plano de Contas / Lançamento / Linha | PGC angolano |

## Possui

Tudo o acima, mais activos fixos e depreciação, câmbio, e reconciliação
bancária.

## Depende de

`procurement` (fornecedor, factura de compra), `commercial` (cliente),
`hr` (`ReferenciaColaborador` para responsável de centro de custo),
`approval` (decisões), `fiscal` (determinações e relatórios),
`documents` (comprovativos), `audit`, `notifications`.

## Consumido por

`approval` (disponível orçamental), `payroll`, `projects`, `fiscal`,
`fleet`, `inventory`.

## Contratos publicados

- **Disponível orçamental** — leitura estreita para verificação em
  `approval` (BR-8). Um dos dois pontos onde o God Module pode nascer;
  contrato explícito e versionado.
- Estado de pagamento/recebimento.
- Custo por centro de custo e período.

## Eventos

- `PagamentoExecutado`
- `FacturaVendaEmitida`
- `RecebimentoRegistado`

## Não pode

- **Embutir workflow de aprovação em Pedido de Pagamento.** A decisão vive
  em `approval`. Isto corrige directamente o anti-padrão do protótipo, onde
  `payment_requests` tinha o workflow na própria tabela.
- Possuir Departamento — isso é `hr` (ADR-005).
- Codificar regras fiscais — consulta `fiscal`.
- Ler tabelas de `procurement`, `commercial` ou `hr`.

## Regras de negócio

- Nenhum pagamento executado sem decisão de aprovação registada (BR-1).
- Na execução: **revalidar** o estado da decisão e verificar disponibilidade
  de caixa (BR-5, SGAP RN-020). Dupla barreira estado + saldo.
- Segregação: quem aprova não paga (BR-3).
- Concorrência optimista na execução de pagamento (BR-17).
- Sem eliminação física de pagamentos ou documentos fiscais (BR-14).

## Perguntas em aberto

- Sobreposição entre Activos Fixos (`finance`) e Inventário (`inventory`) —
  `docs` §1.2 assinala mas não resolve.
- Mecanismo de reconciliação bancária (OFX/CSV/MT940/API) — depende do
  banco.
- Fonte da taxa de câmbio (candidato: BNA).
- Postagem em `finance`: em tempo real ou em lote a partir dos módulos de
  origem?

## Estado

**Contas a Receber iniciado em 2026-08-24 — ADR-036.** As cinco camadas
existem, com schema `finance`, migração aplicada e rotas alcançáveis.

É **só a factura de venda**. Contas a Pagar, Tesouraria, Contabilidade e
Planeamento continuam por fazer — e com eles o *disponível orçamental* que BR-8
exige de `approval`, e a execução de pagamento de BR-1, BR-3 e BR-5. Nada disso
é preciso para emitir.

`Rivo.Finance.Contracts`, `Domain`, `Application`, `Infrastructure` e `Api`,
com 34 testes de domínio.

### O que já está imposto

| Regra | Forma concreta |
|---|---|
| Numeração `FT S001/1` sequencial, sem duplicados | `DocumentSeries` é agregado próprio, com contador de concorrência. Duas emissões simultâneas colidem na série e uma sai com `409` — em vez de saírem duas facturas com o mesmo número |
| Imutabilidade da factura emitida | Não há como acrescentar uma linha depois: `Issue` recebe-as todas e não existe método que as altere. Imposto pela forma do agregado, não por uma verificação que alguém pode esquecer |
| Anular, nunca eliminar (BR-14) | `Cancel` muda o estado para `A` e guarda motivo e data. Não existe método de eliminação. As linhas e os totais ficam |
| Anular duas vezes recusado | Não é idempotente de propósito: o segundo motivo apagaria o primeiro sem rasto |
| Determinação à data do facto gerador | `TaxPointDate` é distinta de `IssuedOn` e é ela que escolhe a taxa (ADR-011 §3) |
| Arredondamento por linha, a duas casas | O total é a soma dos valores já arredondados, não o arredondamento da soma — senão o total gravado não bate com a soma visível no documento |
| `Version` em `SalesInvoice` e `DocumentSeries` | ADR-025 |

### O cliente fica congelado, e isso não contraria BR-18

A factura guarda nome, NIF e morada **tal como estavam na emissão**, além do
`CustomerId`.

BR-18 proíbe cópias *operacionais* que ficam obsoletas em silêncio. Uma factura
é facto histórico: resolver o cliente ao vivo faria uma correcção de nome
reescrever retroactivamente todas as facturas passadas, que é exactamente o que
a imutabilidade proíbe. É o mesmo princípio de BR-6 em `approval` — contexto
congelado na submissão.

### Rotas

| Método | Rota | Permissão |
|---|---|---|
| GET | `/finance/series` | `finance.invoices.read` |
| POST | `/finance/series` | `finance.series.write` |
| GET | `/finance/sales-invoices?customerId=&from=&to=` | `finance.invoices.read` |
| GET | `/finance/sales-invoices/{invoiceId}` | `finance.invoices.read` |
| POST | `/finance/sales-invoices` | `finance.invoices.write` |
| POST | `/finance/sales-invoices/{invoiceId}/cancellation` | `finance.invoices.cancel` |

**Não há `DELETE`.** BR-14 na forma da API.

**Quatro permissões, e a separação é deliberada:** `Sales` emite e consulta;
`Finance` consulta e **anula**, sem emitir — desfazer não é a mesma autorização
que fazer, e é BR-3 aplicada ao documento em vez de ao pagamento. Abrir séries é
só `Admin`: uma série paralela é a forma óbvia de emitir fora da sequência
auditável.

### Um número atribuído não se devolve

Se a emissão falhar **depois** de `Allocate`, o número fica queimado e a
sequência ganha um buraco. É deliberado — reutilizar um número já atribuído poria
dois documentos diferentes com o mesmo número, que é o que a numeração existe
para impedir.

Na prática o buraco é raro: tudo o que pode falhar (cliente inexistente ou
desactivado, taxa em falta, isenção sem catálogo) é verificado **antes** de se
tocar na série. Verificado contra a API — uma emissão recusada deixa
`nextSequence` intacto.

### Por fazer

- **Contas a Pagar**, **Tesouraria**, **Contabilidade & Fecho**, **Planeamento**.
- **BR-1, BR-3 e BR-5** — execução de pagamento com decisão revalidada e
  disponibilidade de caixa. Nada disto existe.
- **Disponível orçamental**, que `approval` precisa para BR-8. Enquanto não
  existir, uma política com `RequiresBudgetCheck` recusa a submissão.
- **Nota de crédito (NC)** — hoje corrige-se anulando e emitindo outra. A NC
  exige referenciar o documento corrigido, e `DocumentType` só declara `FT`.
- **Recebimentos.** A factura sai; o dinheiro a entrar não está modelado.

### Consumidor final e menção fiscal (2026-08-25)

Duas das três perguntas que o ADR-036 deixou em aberto foram respondidas. Ver a
adenda desse ADR.

**Menção de não-validade fiscal.** `SalesInvoice.FiscalNotice`, vinda de
`Finance:FiscalNotice` e **congelada na emissão**. É o ponto: no dia em que
houver `SoftwareValidationNumber`, esvazia-se a configuração, as facturas novas
saem sem menção, e as **emitidas antes mantêm a que lhes foi gravada** — porque
continuam a não ser válidas. Derivá-la em leitura apagaria a marca de todo o
histórico no instante da certificação.

**Consumidor final.** `CustomerId` é anulável e `InvoicedParty.FinalConsumer(…)`
constrói o retrato de quem não se identificou. As duas metades têm de bater
certo: consumidor final com identificador de cliente, ou cliente registado sem
ele, é recusado. A morada fica **vazia, não nula** — vazio é "não existe
morada", nulo seria "não sabemos".

⚠ **O identificador vem de `Finance:FinalConsumerTaxId`, com omissão
`CONSUMIDORFINAL`** — deliberadamente não plausível como NIF. A convenção
angolana não está verificada em fonte primária, e um número com ar de real seria
tomado por verificado. **Substituir pelo oficial antes de certificar.** Vazio
bloqueia a venda a consumidor final.

**Série de numeração.** Uma contínua por tipo de documento, `S001`, sem reinício
anual, criada pelo **seed** no arranque. Sem isso, um ambiente novo devolve
`404` na primeira factura, e o passo esquecido só aparece quando alguém tenta
facturar. Idempotente: se já existir, não lhe toca nem lhe recua o contador.

### O ciclo de venda está fechado (2026-08-25)

Factura → nota de crédito → recibo → saldo. As três peças que faltavam.

| Peça | O que impõe |
|---|---|
| `CreditNote` | Corrige uma factura sem lhe tocar. Série **NC** própria, referência textual congelada, e o **facto gerador da factura corrigida** — o imposto que se devolve é o que foi liquidado, não o de hoje (ADR-011 §3) |
| `Receipt` | Dinheiro recebido. Série **RG**, meio de pagamento do SAF-T, e **uma linha por factura liquidada** — sem isso não há como saber o que ficou por receber |
| Saldo | `GET /finance/sales-invoices/{id}/balance` — facturado, creditado, recebido, em aberto, liquidado |

**Anular e creditar não são a mesma coisa.** Anular tira a factura inteira do
mapa de dívida; creditar reduz o que ela pede e deixa rasto do quanto e do
porquê. Uma factura anulada não se credita — já não há o que corrigir.

### O saldo é calculado, não guardado

`OutstandingAsync` faz **três consultas**, não um `join`. Um `join` entre notas
e recibos multiplicaria as linhas quando houvesse mais do que uma de cada, e o
total sairia inflacionado — o erro clássico de somar sobre um produto
cartesiano, que não dá sinal, só um número errado.

Um saldo em coluna seria um ponto de contenção a cada recebimento, e ficaria
errado em silêncio no dia em que alguém estornasse um recibo sem o recalcular.

**É a invariante que nenhum agregado impõe sozinho** — nem a factura vê as suas
notas, nem o recibo vê os outros recibos. Vive no store pela mesma razão que a
unicidade do NIF vive no de `commercial`.

### Segregação em três funções

| Perfil | Emite | Credita / anula | Recebe |
|---|---|---|---|
| `Sales` | ✅ | — | — |
| `Finance` | — | ✅ | ✅ |
| `Admin` | ✅ | ✅ | ✅ |

**`finance.receipts.write` é permissão própria**, separada de emitir: quem pode
declarar dinheiro recebido sem cobrar nada pode fazer uma dívida desaparecer. É
a razão de cobrança e tesouraria serem funções distintas.

**Estornar não vem com ela** — exige `finance.invoices.cancel`, porque desfazer
um recebimento faz a dívida voltar a existir.

### Rotas acrescentadas

| Método | Rota | Permissão |
|---|---|---|
| GET | `/finance/sales-invoices/{id}/balance` | `finance.invoices.read` |
| GET | `/finance/credit-notes?salesInvoiceId=` | `finance.invoices.read` |
| GET | `/finance/credit-notes/{id}` | `finance.invoices.read` |
| POST | `/finance/credit-notes` | `finance.invoices.cancel` |
| POST | `/finance/credit-notes/{id}/cancellation` | `finance.invoices.cancel` |
| GET | `/finance/receipts?customerId=&from=&to=` | `finance.receipts.read` |
| GET | `/finance/receipts/{id}` | `finance.receipts.read` |
| POST | `/finance/receipts` | `finance.receipts.write` |
| POST | `/finance/receipts/{id}/cancellation` | `finance.invoices.cancel` |

**Continua sem `DELETE`** em qualquer delas (BR-14).

### Por fazer

- **Nota de débito (ND)** — corrige para cima. O `DocumentType` declara FT, NC e
  RG; a ND fica de fora porque não apareceu caso de uso.
- **Nota de crédito sobre várias facturas.** O SAF-T permite; aqui uma nota
  referencia **uma** factura.
- **Adiantamentos.** Receber mais do que se deve é recusado com `409` — um
  adiantamento é outro documento, e não existe.
- **Contas a Pagar, Tesouraria, Contabilidade & Fecho, Planeamento**, e com eles
  BR-1, BR-3, BR-5 e o disponível orçamental de BR-8.

### Contas a Pagar e Tesouraria (2026-08-25)

O que restava do módulo, e é onde **BR-1, BR-3, BR-5 e BR-17** se encontram.

| Peça | O que impõe |
|---|---|
| `BankAccount` | Disponibilidade de tesouraria. O saldo é **o ponto de contenção do sistema** — duas execuções simultâneas competem por ele, e o contador de concorrência faz uma perder com `409`. É o caso concreto que BR-17 nomeia |
| `PurchaseInvoice` | O que se deve. **O número é do fornecedor, não nosso** — ao contrário da factura de venda, esta chega já numerada |
| `PaymentRequest` | Pedido de pagamento. **Sem passos de aprovação**, e é o ponto todo |
| `ExecutePayment` | Onde a dupla barreira de BR-5 se monta |

### O anti-padrão do protótipo está fechado

`modules/finance.md` proíbe embutir workflow no Pedido de Pagamento — corrige
`payment_requests` do protótipo, que tinha o workflow na própria tabela.

Por isso os estados são **dois**: `Eligible` e `Executed` (mais `Cancelled`, que
é BR-14). **Não há "pendente de aprovação"** — isso é estado do processo, não do
pedido, e copiá-lo para cá seria guardar em `finance` uma verdade que é de
`approval` e que fica obsoleta em silêncio. O que fica é um ponteiro:
`ApprovalRequestId`.

A suite verifica isto por SQL: nenhuma coluna de workflow em
`finance.payment_request`.

### A dupla barreira de BR-5

Monta-se na camada Application porque **nenhuma das metades cabe num agregado**:
o estado da decisão vive em `approval`, o saldo vive na conta, e o pedido não vê
nem um nem outro.

1. **A decisão, revalidada no momento.** Não lida de um campo — entre a
   aprovação e a execução podem passar dias, e nesse intervalo o processo pode
   ter sido cancelado. `Unknown` conta como recusa: a ausência de decisão não é
   aprovação.
2. **O saldo.** Sai antes de marcar o pedido, para que um saldo insuficiente não
   deixe o pedido executado sem dinheiro ter saído.

E **BR-3 é imposta pelo agregado**, com a lista de decisores vinda de
`approval`: quem decidiu não pode executar. Devolve **403 e não 409** — não é o
estado que impede, é *esta pessoa* — e a tentativa vai para a trilha com acção
própria, porque é evento de segurança.

**Uma só gravação:** saldo e pedido na mesma transacção. É por este ponto que o
ADR-001 escolheu monólito modular.

### A ligação a `approval` é por inversão, e aqui é obrigatória

`finance` declara `IPaymentApproval` nas suas palavras; o adaptador vive no
composition root.

Em `hr` a inversão era higiene. **Aqui é necessária:** `modules/approval.md` diz
que `approval` lê `finance` para o disponível orçamental de BR-8. Uma referência
directa traria de volta o ciclo que o ADR-034 fechou, no dia em que BR-8 for
implementada.

### Três funções, três pessoas

| Perfil | Regista e pede | Aprova | Executa |
|---|---|---|---|
| `Manager` | ✅ | ✅ | — |
| `Finance` | — | ✅ | ✅ |

`finance.payments.execute` é separada de `finance.payments.request`: quem pede
não paga. E `Finance` não pede — se pedisse e pagasse, faltava só aprovar, e
`approval` recusa quem submeteu (BR-2). **BR-3 começa no catálogo de
permissões, antes de o domínio a impor.**

### Rotas

| Método | Rota | Permissão |
|---|---|---|
| GET/POST | `/finance/accounts` | `finance.payables.read` / `.write` |
| POST | `/finance/accounts/{id}/deposits` | `finance.payables.write` |
| GET | `/finance/accounts/{id}/statement?from=&to=` | `finance.payables.read` |
| GET/POST | `/finance/purchase-invoices` | `finance.payables.read` / `.write` |
| GET | `/finance/purchase-invoices/{id}` | `finance.payables.read` |
| GET | `/finance/payment-requests?purchaseInvoiceId=` | `finance.payables.read` |
| GET | `/finance/payment-requests/{id}` | `finance.payables.read` |
| POST | `/finance/payment-requests` | `finance.payments.request` |
| POST | `/finance/payment-requests/{id}/cancellation` | `finance.payments.request` |
| POST | `/finance/payment-requests/{id}/execution` | `finance.payments.execute` |

Criar um pedido devolve **`202`**, não `201`: existe e ainda não é pagável.

### Por fazer

- **Contabilidade & Fecho** e **Planeamento** — os dois contextos que restam. Com
  Planeamento vem o **disponível orçamental** que BR-8 exige de `approval`;
  enquanto não existir, uma política com `RequiresBudgetCheck` recusa a
  submissão.
- **Adiantamentos.** Um pedido não excede a factura, e não há documento para
  pagar antes de dever.
- **Reconciliação bancária propriamente dita** — confrontar o extracto do Rivo
  com o do banco. O extracto existe (ver abaixo); falta importar o do banco e
  emparelhar movimentos.
- **Câmbio.** Pagar em moeda diferente da conta é recusado — não há conversão
  automática, porque o câmbio é uma decisão e ninguém a tomou.

### Extracto de conta (2026-08-25)

O saldo sozinho não é reconciliável. Uma conta com `86.000,00` não diz como lá
chegou, e **reconciliação bancária é uma comparação entre movimentos, não entre
saldos.** `BankMovement` é a linha do extracto.

| Campo | Porquê |
|---|---|
| `Direction` + `Amount` | Sentido e quantia. O valor é sempre positivo; o sinal está na direcção |
| `BalanceAfter` | O saldo **depois** do movimento, congelado. Guardado e não recalculado ao ler — se um dia o saldo divergir da soma, é esta coluna que mostra onde e quando |
| `SourceType` + `SourceId` | O percurso de volta ao documento que o causou. Par texto/identificador em vez de FK: a origem pode vir de outro contexto interno, e uma FK obrigaria a tabela a conhecer todas as origens de antemão |

**O movimento nasce dentro do agregado**, em `Deposit`/`Withdraw`. Saldo e
extracto alteram-se no mesmo acto ou não se alteram — um chamador que se
esquecesse de registar o movimento deixaria o extracto a mentir em silêncio, e
ninguém daria por isso até à primeira reconciliação.

A colecção **nunca é carregada no caminho de escrita**: acrescentar um movimento
não obriga a ler os anteriores, por isso pagar numa conta com dez anos de
histórico custa o mesmo que numa conta nova.

**Append-only imposto pelo motor**, com a mesma peça que a trilha de auditoria
usa desde o K9 — gatilho `INSTEAD OF UPDATE, DELETE` mais tabela sentinela
contra `TRUNCATE`. Um extracto que se pode editar não serve para reconciliar
nada. Corrigir faz-se como na contabilidade: com outro movimento em sentido
contrário, que também fica no extracto.

`GET /finance/accounts/{id}/statement` devolve abertura, movimentos, totais e
fecho. **`reconciles` é nulo quando a janela tem fim** — num extracto de Março o
fecho não deve bater com o saldo de hoje, e dizer que não reconcilia seria
mentir ao contrário.

As contas que já existiam receberam **um movimento de abertura** na migração: um
extracto que fechasse a zero contra um saldo que não é zero pareceria defeito,
quando é apenas o facto de os movimentos anteriores nunca terem sido
registados. Uma linha explícita a dizê-lo é mais honesta do que uma divergência
por explicar.
