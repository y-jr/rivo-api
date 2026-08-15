# Rivo — SAF-T AO: Contrato de Completude de Dados

**Versão do esquema:** SAF-T AO **1.01_01** (assumida em vigor)
**Namespace:** `urn:OECD:StandardAuditFile-Tax:AO_1.01_01`
**Ficheiro fixado:** [`schemas/SAFTAO1.01_01.xsd`](schemas/SAFTAO1.01_01.xsd)
**SHA256:** `E9A938E1F47AC3D84FFBB26D0D95B827FC769A065C9D20533D0262C12F8C2631`
**Origem:** `https://github.com/assoft-portugal/SAF-T-AO`
(`XSD/SAFTAO1.01_01.xsd`)
**Dimensão:** 122,6 KB · 2963 linhas · 61 `complexType`, 78 `simpleType`,
126 `enumeration`

O XSD está fixado no repositório para imunizar o projecto contra alteração
ou indisponibilidade da origem. **Não editar.** Substituir apenas quando a
AGT publicar versão nova — e, nesse caso, registar ADR, porque muda o modelo
de dados.

---

## 0. O que este documento é e não é

**É** um contrato de **completude de dados**: define o mínimo que os módulos
transaccionais têm de capturar **no momento da transacção** para que a
exportação SAF-T seja possível depois. Se não for capturado nesse momento,
não há como reconstruir mais tarde.

**Não é** fonte de regras de cálculo. O elemento `TaxTable` transporta
taxas, mas representa *a tabela da empresa tal como exportada* — o XSD
define o campo, não a regra de incidência.

**Estado das duas metades do domínio fiscal:**

| Metade | Estado |
|---|---|
| Modelo de dados fiscal | **Fechado** por este XSD |
| Motor de cálculo (taxas, incidência, escalões, isenções) | **Em aberto** — ver §5 |

---

## 1. Estrutura do ficheiro

Raiz `AuditFile`, quatro secções:

```
AuditFile
 ├── Header               metadados da empresa e do período
 ├── MasterFiles          dados de referência
 │    ├── GeneralLedgerAccounts
 │    ├── Customer
 │    ├── Supplier
 │    ├── Product
 │    └── TaxTable
 ├── GeneralLedgerEntries lançamentos contabilísticos
 └── SourceDocuments      documentos comerciais
      ├── SalesInvoices
      ├── MovementOfGoods
      ├── WorkingDocuments
      ├── Payments
      └── PurchaseInvoices
```

---

## 2. Mapeamento secção → módulo dono

Determina que módulo é responsável por capturar cada bloco.

| Secção SAF-T | Módulo dono | Notas |
|---|---|---|
| `Header` | `fiscal` | Inclui `SoftwareValidationNumber` — ver §4 |
| `GeneralLedgerAccounts` | `finance` (Contabilidade) | Plano de contas, PGC angolano |
| `Customer` | `commercial` | Dono confirmado do Cliente |
| `Supplier` | `procurement` | Dono confirmado do Fornecedor |
| `Product` | `inventory` | Tipos P, S, O, E, I |
| `TaxTable` | `fiscal` | Catálogo de taxas da empresa |
| `GeneralLedgerEntries` | `finance` (Contabilidade) | `Journal` → `Transaction` |
| `SalesInvoices` | `commercial` + `finance` (AR) | Base em `commercial`; factura em `finance` |
| `MovementOfGoods` | `inventory` | Guias GR, GT, GA, GD |
| `WorkingDocuments` | `commercial` | Documentos de conferência pré-facturação |
| `Payments` | `finance` (Tesouraria) | — |
| `PurchaseInvoices` | `procurement` + `finance` (AP) | — |

`fiscal` **lê** todos estes módulos para gerar a exportação. Esta é a
direcção "relato" da dupla relação descrita em
[`../modules/fiscal.md`](../modules/fiscal.md) — distinta da direcção
"determinação", em que são eles que consultam `fiscal`.

---

## 3. Restrições vinculativas por módulo

### `commercial`

| Restrição | Detalhe |
|---|---|
| **Numeração de factura** | `InvoiceNo` no formato `[Tipo] [Série]/[Sequencial]` — ex.: `FT S001/1`. Sem duplicados dentro do ficheiro |
| **Tipos de documento** | Facturas: `FT`, `FR`, `GF`, `FG` · Notas: `NC`, `ND` · Recibos: `AC`, `AR`, `AF`, `TV` · Ramo segurador: `RP`, `RE`, `CS`, `LD`, `RA` |
| **Estado** | `InvoiceStatus`: `N` normal, `S` autofacturação, `A` anulado, `R` resumo |
| **`TaxPointDate`** | Data de expedição do bem ou de prestação do serviço, por linha — pode diferir da data do documento |
| **Isenção** | `TaxExemptionReason` **e** `TaxExemptionCode` obrigatórios quando há isenção |
| **`SelfBillingIndicator`** | Em Customer e Supplier |
| `WorkingDocuments` | `WorkStatus`, `WorkStatusDate` |

### `finance`

| Restrição | Detalhe |
|---|---|
| **Plano de contas** | `GroupingCategory` enumerado: `GR`, `GA`, `GM`, `AR`, `AA`, `AM`; `GroupingCode` referencia a conta-pai |
| **Saldos** | Débito e crédito de abertura e de fecho por conta |
| **`TransactionID`** | Construído de data da transacção + `JournalID` + `DocArchivalNumber` |
| **Período** | 1–12 |
| **Meios de pagamento** | `NU` numerário, `TB` transferência, `CH` cheque, `CC` cartão de crédito, `CD` cartão de débito, **`MB` Multicaixa**, `PR` permuta, `CS` compensação, `DE` moeda electrónica, `OU` outro |
| **`WithholdingTax`** | Retenção na fonte |
| **Moeda estrangeira** | Elemento `Currency` opcional nos totais — suporta multi-moeda AOA/USD/EUR |
| **Totais** | `NetTotal`, `TaxPayable`, `GrossTotal`; `Settlement` para descontos acordados |

### `inventory`

| Restrição | Detalhe |
|---|---|
| **Tipos de produto** | `P`, `S`, `O`, `E`, `I` |
| **Tipos de movimento** | `GR` guia de remessa, `GT` guia de transporte, `GA` movimento de activo fixo, `GD` guia de devolução |
| **Numeração** | `[TipoMovimento] [CódigoInterno]/[Número]` — ex.: `GR DOC001/42` |
| **Estado** | `MovementStatus`: `N`, `T` conta de terceiros, `A` anulado, `F` facturado, `R` resumo |
| **Logística** | `MovementStartTime`, `MovementEndTime`, `ShipTo`, `ShipFrom` |
| **`CustomsDetails`** | Números UN, opcional |

### `procurement`

`PurchaseType` espelha os tipos de facturação, acrescentando `NL` e `RC`
para regime de caixa. Totais com desagregação de imposto dedutível.

### Transversal

| Restrição | Consequência |
|---|---|
| **Estado `A` = anulado** em facturas, movimentos e pagamentos | Confirma BR-14: anulação lógica, **nunca** eliminação física |
| **Unicidade** | `AccountIDConstraint`, `CustomerIDConstraint`, `SupplierIDConstraint`, `ProductCodeConstraint`; sem números de documento duplicados |
| **Integridade referencial** | Documentos e transacções referenciam master data |

---

## 4. Requisitos arquitectónicos que decorrem do XSD

Não são campos a preencher — são requisitos de desenho.

### 4.1 Cadeia de assinatura (`Hash` / `HashControl`)

`Hash` (máx. 172 caracteres) e `HashControl` (máx. 70) existem em
`SalesInvoices`, `MovementOfGoods` e `WorkingDocuments`.

Implicações, **por desenhar**:

- Os documentos têm de ser assinados **em cadeia e por ordem** — cada um
  encadeia no anterior da mesma série.
- Isso implica **imutabilidade** após emissão e **ordenação estrita** da
  emissão dentro de uma série.
- Tem impacto directo em concorrência: duas emissões simultâneas na mesma
  série não podem produzir a mesma posição na cadeia.
- `HashControl` admite `"0"` para software não validado — o que confirma que
  a validação é um estado do produto, não do documento.

Registado como **K7** em [`../state/known-issues.md`](../state/known-issues.md).
Candidato a ADR quando `commercial` for desenhado.

### 4.2 Certificação do software

`SoftwareValidationNumber` no `Header` é campo do esquema. Implica que o
**Rivo terá de ser certificado pela AGT** antes de poder emitir documentos
fiscais em produção.

É requisito de projecto, não de um módulo. O processo concreto está em
aberto — ver §5.

### 4.3 Captura no momento certo

Vários campos não são reconstituíveis depois: `TaxPointDate`,
`MovementStartTime`/`EndTime`, `ShipFrom`/`ShipTo`, códigos de isenção,
posição na cadeia de hash.

**Consequência:** o desenho de `commercial`, `inventory` e `finance` tem de
capturar estes dados desde a primeira versão, mesmo que a exportação SAF-T
só seja implementada depois. Adiar a captura torna a exportação impossível
para o histórico.

---

## 5. O que continua em aberto

O XSD **não** resolve nada disto:

| Lacuna | Bloqueia |
|---|---|
| Taxas de IVA e regras de incidência (normal, reduzida, isenta) | `commercial`, `procurement`, `finance` |
| Escalões e cálculo de IRT | `payroll` |
| Taxas de INSS e repartição empregador/trabalhador | `payroll` |
| Mapeamento situação legal → `TaxExemptionCode` (o campo existe; o mapeamento não) | `commercial`, `fiscal` |
| Prazos e formatos das declarações periódicas à AGT | `fiscal` |
| Processo de certificação de software junto da AGT | projecto |
| Existe API oficial da AGT? **Hipótese:** não — tratar como geração de ficheiro para submissão via portal | `fiscal` |

Rastreado em [`../state/pending-decisions.md`](../state/pending-decisions.md).

---

## 6. Nota de implementação

Quando existir árvore de código, o XSD deve ser **vendorizado no
codebase** (por exemplo em `Modules/Fiscal/Infrastructure/Schemas/`) para
validação em runtime da exportação gerada. A cópia em
[`schemas/`](schemas/) serve de referência documental e de fixação de
versão — as duas devem manter-se idênticas, verificáveis pelo SHA256 acima.

A exportação gerada deve ser **validada contra o XSD nos testes**, não
apenas em produção.
