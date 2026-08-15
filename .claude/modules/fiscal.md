# fiscal — Fiscal & Compliance

**Classificação:** supporting domain.

## Responsabilidade

Motor fiscal angolano. Determina e impõe os requisitos fiscais, estatutários
e regulamentares que os módulos de negócio têm de respeitar.

É contexto à parte — não feature de `finance` — porque as regras do regime
fiscal angolano são específicas o suficiente para ter domínio próprio.

**Não possui as transacções de negócio subjacentes.**

## Âmbito — Angola

| Área | Detalhe |
|---|---|
| Impostos | IVA, IRT, contribuições INSS |
| Autoridade | AGT — Administração Geral Tributária |
| Ficheiro de auditoria | SAF-T AO (exportação XML estruturada) |
| Contabilidade | PGC angolano (plano de contas — possuído por `finance`) |
| Protecção de dados | Lei n.º 22/11 |
| Moedas | AOA, USD, EUR |

## Conceitos

| Conceito | Notas |
|---|---|
| Regra Fiscal | — |
| Determinação Fiscal | resposta a um pedido de um módulo transaccional |
| Declaração Fiscal | IVA / IRT / INSS / AGT, por período |
| Exportação SAF-T | por período; ficheiro via `documents` |
| Requisitos de Documento Fiscal | numeração, campos obrigatórios, classificações |

## Possui

Regra Fiscal, Determinação, Declaração, Exportação SAF-T, Requisitos de
Documento Fiscal.

## Duas direcções, duas capacidades

`fiscal` tem duas relações distintas com os módulos transaccionais. Não as
confundir:

| Capacidade | Direcção | Natureza |
|---|---|---|
| **Determinação fiscal** | transaccionais → `fiscal` | Eles perguntam, `fiscal` responde. `fiscal` **não** depende deles |
| **Relato e exportação** (SAF-T AO, declarações) | `fiscal` → transaccionais | `fiscal` **lê** os dados que tem de reportar |

`docs/rivo-arquitetura-global-v1.md` §1.5 confirma: `Fiscal & Compliance
──lê──> Financeiro, Payroll`.

A leitura para relato faz-se por **contratos publicados / read models**,
nunca por acesso directo a tabelas.

## Depende de

**Para determinação:** `documents`, `audit`.

**Para relato/exportação:** `finance` (razão, facturas de venda e de compra,
pagamentos), `commercial` (clientes), `procurement` (fornecedores),
`inventory` (produtos, movimentos de mercadoria), `payroll` (base de IRT e
INSS).

## Consumido por

`commercial` (conformidade de factura, imposto na venda), `procurement`
(imposto na compra), `finance` (relatórios estatutários), `payroll`
(IRT, INSS).

## Contratos publicados

- Determinação fiscal para uma operação (venda, compra, salário).
- Requisitos de conformidade de documento fiscal.
- Geração de declaração e de exportação SAF-T.

## Não pode

- Possuir transacções comerciais ou de compra.
- Possuir o razão geral — isso é `finance`.
- Executar pagamentos.
- Escrever directamente na persistência de outro módulo.

## Regras de negócio

- É a **fonte autoritativa** das regras fiscais. Nenhum outro módulo pode
  implementar regras de imposto por sua conta.
- Retenção de documentos fiscais pelos prazos legais angolanos (BR-15).
- Sem eliminação física de documentos fiscais (BR-14).

## Fonte normativa: XSD do SAF-T AO

**SAF-T AO v1.01_01**, namespace `urn:OECD:StandardAuditFile-Tax:AO_1.01_01`.

- Análise completa: `docs/rivo-fiscal-saft-ao-v1.md`
- XSD fixado no repositório: `docs/schemas/SAFTAO1.01_01.xsd`

É **contrato de completude de dados**, não fonte de regras de cálculo.
Define o mínimo que os módulos transaccionais têm de capturar no momento da
transacção para que a exportação seja possível depois. Se não for capturado
aí, não há como reconstruir.

### Restrições que impõe a outros módulos

| Restrição | Módulo |
|---|---|
| Numeração `[Tipo] [Série]/[Sequencial]` (ex. `FT S001/1`), sem duplicados | `commercial` |
| Tipos de documento: FT, FR, GF, FG, NC, ND, AC, AR, AF, TV (+ ramo segurador RP, RE, CS, LD, RA) | `commercial` |
| `Hash` + `HashControl` — cadeia de assinatura por documento; implica **imutabilidade e ordem de assinatura** | `commercial`, `finance` |
| Estado `A` (anulado) — confirma anulação lógica, nunca eliminação física (BR-14) | todos |
| Meios de pagamento: NU, TB, CH, CC, CD, **MB (Multicaixa)**, PR, CS, DE, OU | `finance` |
| `WithholdingTax` — retenção na fonte | `finance`, `payroll` |
| `TaxExemptionReason` + `TaxExemptionCode` obrigatórios em isenção | `commercial`, `fiscal` |
| `GroupingCategory` GR/GA/GM/AR/AA/AM; saldos de abertura e fecho | `finance` |
| Tipos de produto P, S, O, E, I | `inventory` |
| Guias `GR`, `GT`, `GA`, `GD` com `MovementStartTime`/`EndTime`, `ShipTo`/`ShipFrom` | `inventory` |
| `SoftwareValidationNumber` — o próprio Rivo terá de ser certificado pela AGT | projecto |

### O que o XSD **não** resolve

`TaxTable` transporta taxas, mas são *a tabela da empresa como exportada* —
o XSD define o campo, não a regra de incidência.

## Taxas e escalões são dados, não código (ADR-011)

Todas as regras abaixo mudam **anualmente, por lei orçamental**. São dados de
referência versionados com **vigência temporal**, propriedade de `fiscal`:

- Nenhuma taxa, escalão ou limiar em código.
- `vigente_desde` / `vigente_ate` obrigatórios.
- Determinação feita **à data do facto gerador**, não à data do cálculo.
- Instrumento legal registado com cada versão (ex.: "Lei n.º 14/25").

Levantamento provisório em `docs/rivo-fiscal-regras-angola-v1.md` —
**não implementável sem verificação profissional.**

## Perguntas em aberto

Detalhe e estado de confiança em `docs/rivo-fiscal-regras-angola-v1.md` §5.

- **Tabela de escalões de IRT** — fontes secundárias **contradizem-se**
  (11 escalões/mín. 16% vs. 12 escalões/mín. 13%). Exige o Anexo I da
  Lei n.º 14/25. Bloqueia `payroll`.
- **INSS é dedutível à matéria colectável do IRT?** Desconhecido, e muda o
  valor retido a toda a gente.
- **Taxas de INSS** — 8%/3% (Decreto Presidencial n.º 227/18) com alteração
  pendente noticiada para 10%/5%, por confirmar.
- **Lista de isenções de IVA e respectivos códigos** — indispensável, porque
  o SAF-T exige `TaxExemptionReason` **e** `TaxExemptionCode` em cada linha
  isenta.
- Outras taxas reduzidas de IVA além dos 5% para equipamento industrial.
- Tratamento de subsídios (alimentação, transporte, férias, Natal) em IRT.
- Existe tecto contributivo no INSS?
- Prazos e formatos das **declarações periódicas** à AGT.
- **Processo de certificação de software** junto da AGT
  (`SoftwareValidationNumber`).
- Existe API oficial da AGT? `docs` regista como **hipótese** que não; até
  confirmação, tratar como geração de ficheiro para submissão via portal.

## Estado

Não iniciado. Modelo de dados fixado pelo XSD; motor de cálculo por levantar.
