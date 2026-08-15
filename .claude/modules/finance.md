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

Não iniciado.
