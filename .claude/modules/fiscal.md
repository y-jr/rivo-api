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

- **Tabela de escalões de IRT (Tabela B, vigente desde 01/01/2026)** —
  **confirmada pelo utilizador a 2026-08-30**, incluindo os dois pontos que
  bloqueavam `payroll`: parcela fixa do escalão 150.001–200.000 = 12.500 Kz
  (o salto na fronteira da isenção é real), e parcela fixa do escalão
  1.500.001–2.000.000 = 292.250 Kz. **A fonte é o utilizador, não o Anexo I
  da Lei n.º 14/25 nem parecer de fiscalista** — continua por obter; ver
  `state/pending-decisions.md`. **Carregada como dado versionado desde
  2026-08-30** (`IncomeTaxSchedule`, ADR-011) — a reserva é sobre a fonte do
  valor, não sobre o mecanismo.
- **INSS é dedutível à matéria colectável do IRT?** — resolvido antes: sim,
  só a parcela do trabalhador (3%), artigo 7.º do CIRT. **Implementado desde
  2026-08-30**: `AddPayrollItem` deduz o INSS do trabalhador antes de pedir o
  IRT.
- **Taxas de INSS** — 8%/3% confirmadas; sem tecto contributivo
  (**confirmado pelo utilizador a 2026-08-30**, mesma reserva de fonte).
  Carregadas como `TaxRateSchedule` (`TaxKind.EmployeeSocialSecurity` /
  `EmployerSocialSecurity`, código `INSS`) — o mesmo mecanismo do IVA, sem
  desenho novo.
- **Lista de isenções de IVA e respectivos códigos** — indispensável, porque
  o SAF-T exige `TaxExemptionReason` **e** `TaxExemptionCode` em cada linha
  isenta.
- Outras taxas reduzidas de IVA além dos 5% para equipamento industrial.
- Tratamento de subsídios (alimentação, transporte, férias, Natal) em IRT —
  continua sem resposta; `PayrollItem` não distingue componentes do salário
  bruto ainda.
- Prazo de entrega do INSS; regras de expatriados — continuam sem resposta.
- Prazos e formatos das **declarações periódicas** à AGT.
- **Processo de certificação de software** junto da AGT
  (`SoftwareValidationNumber`).
- Existe API oficial da AGT? `docs` regista como **hipótese** que não; até
  confirmação, tratar como geração de ficheiro para submissão via portal.

## Estado

**Fatia mínima iniciada em 2026-08-24 — ADR-036. Motor de IRT/INSS
acrescentado em 2026-08-30**, depois de o utilizador confirmar directamente
os valores até então por confirmar.

**As cinco camadas existem desde 2026-08-24**, com schema `fiscal`, migração
aplicada e rotas alcançáveis.

O objectivo do produto passou a ser **emitir**, não emitir com validade legal.
`fiscal` deixa de ser um bloco de fase e passa a ser o que a emissão precisa:
uma taxa com vigência e um contrato de determinação.

### O que existe

`Rivo.Fiscal.Contracts` e `Rivo.Fiscal.Domain`, com 39 testes de domínio (18
de `TaxRateSchedule`, 21 de `IncomeTaxSchedule`).

| Peça | O que impõe |
|---|---|
| `TaxRateSchedule` | Série de versões da mesma taxa plana (IVA, INSS). A raiz é a série e não a versão, porque a invariante é sobre o conjunto |
| Não sobreposição de vigências | Sem ela, "que taxa vigorava em Março" pode ter duas respostas e a determinação deixa de ser determinística |
| `InForceOn(data)` | Puramente temporal. Devolver nulo é a resposta certa — recair na versão mais próxima inventaria o valor |
| Instrumento legal obrigatório | ADR-011 §4. Sem ele, "porquê este valor" fica sem resposta na auditoria |
| Isenção com taxa ≠ 0 recusada | Ou o código isenta, ou há imposto a liquidar |
| `ITaxDetermination` | Determinação **à data do facto gerador**, que é parâmetro obrigatório em vez de `UtcNow` lá dentro. Devolve a **taxa** — quem pede multiplica |
| `IncomeTaxSchedule` | Série de versões da tabela de escalões de IRT — mesmo padrão de `TaxRateSchedule`, mas cada versão é um conjunto de escalões (Parcela Fixa + Taxa × Excesso de), não um único número |
| `SelectBracket`/`Compute` | O escalão de maior "excesso de" que a matéria colectável ainda ultrapassa, nunca iguala — é o que faz 150.000 cair na isenção e 150.001 já não |
| `IIncomeTaxDetermination` | Devolve o **montante já calculado**, ao contrário de `ITaxDetermination`. A fórmula do escalão é regra fiscal (`modules/fiscal.md` — "nenhum outro módulo implementa regra de imposto"); percentagem × montante não é |

**Desde 2026-08-30, `TaxKind` tem `EmployeeSocialSecurity` e
`EmployerSocialSecurity`** além de `ValueAdded` — o INSS usa o mecanismo de
`TaxRateSchedule` sem alteração, porque é uma taxa plana como o IVA. O IRT
precisou de um agregado novo (`IncomeTaxSchedule`), porque é uma tabela de
escalões, não um número: `TaxRateSchedule` não conseguia modelar isso sem
distorcer o que "vigência" significa.

### Códigos: só ISE e NS

`TaxCodes` fixa dois, citados da DS.120 v1.4 em `modules/commercial.md`, porque
são os que obrigam a `TaxExemptionCode`. Os restantes são texto que quem
introduz os dados fornece — o domínio não finge conhecer uma tabela que não
está verificada em fonte primária.

Consequência prática: **emitir com isenção fica bloqueado** enquanto não houver
a lista oficial de códigos. Não se inventa código.

### Adiado por ADR-036

Certificação AGT (`SoftwareValidationNumber`), exportação SAF-T, declarações
periódicas, e a cadeia `Hash`/`HashControl` (K7). **O motor de IRT e INSS já
não está nesta lista — implementado a 2026-08-30.**

⚠ **As facturas emitidas não são documentos fiscais válidos em Angola.** É
assunção de produto registada no ADR-036, não conclusão técnica. **Os valores
de IRT/INSS calculados por `payroll` têm a mesma reserva** — o mecanismo
existe e está testado, mas a fonte dos valores (escalões, taxas) é o
utilizador, não fiscalista nem Anexo I da lei; ver "Perguntas em aberto".

### Rotas

| Método | Rota | Permissão |
|---|---|---|
| GET | `/fiscal/tax-rates` | `fiscal.rates.read` |
| POST | `/fiscal/tax-rates` | `fiscal.rates.write` |
| POST | `/fiscal/tax-rates/{scheduleId}/versions` | `fiscal.rates.write` |
| GET | `/fiscal/tax-rates/determination?taxCode=&taxPointDate=&kind=` | `fiscal.rates.read` |
| GET | `/fiscal/income-tax-schedule` | `fiscal.rates.read` |
| POST | `/fiscal/income-tax-schedule/versions` | `fiscal.rates.write` |
| GET | `/fiscal/income-tax-schedule/determination?taxableIncome=&taxPointDate=` | `fiscal.rates.read` |

Três códigos com significado, verificados contra a API a correr — o mesmo
padrão nas duas famílias de rotas (taxa plana e escalões):

- **`409`** ao introduzir uma versão que se sobrepõe. Não é campo mal
  preenchido — é conflito com o que já lá está, e corrige-se fechando a versão
  anterior.
- **`404`** na determinação sem taxa/tabela em vigor à data. Recusar é a
  resposta certa; recair na versão mais próxima inventaria o valor.
- **`501`** na determinação de IVA com `ISE` ou `NS`. A capacidade não existe
  neste sistema — falta o catálogo de códigos de isenção — e não é defeito do
  pedido.

**Só o `Admin` escreve taxas e escalões.** `Sales` recebe `commercial`, não
`fiscal`: quem vende não fixa a taxa que a sua própria venda vai liquidar. O
corpo JSON de `kind` é o valor ordinal do enum (`1` para
`EmployeeSocialSecurity`, `2` para `EmployerSocialSecurity`) — não há
`JsonStringEnumConverter` registado; a query string aceita o nome (binding de
parâmetro, não de corpo JSON).
