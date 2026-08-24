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
