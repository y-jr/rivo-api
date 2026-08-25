# ADR-036: Emitir sem Certificação — Forma Fiscal sem Conformidade Legal

## Status

Aceite (2026-08-24).

**Reordena o `roadmap-execucao.md`** — a Fase 3 (`fiscal`) deixa de ser um
bloco e passa a ser uma fatia mínima. Não substitui nenhum ADR; restringe o
âmbito de execução do ADR-011 sem alterar as suas regras.

## Context

O objectivo declarado passou a ser **conseguir emitir uma factura**, não
emitir uma factura legalmente válida em Angola.

O planeamento anterior tinha a ordem inversa: a Fase 3 construía o motor
fiscal completo, e a faixa paralela de conformidade tratava a certificação
junto da AGT como o item de prazo mais longo do projecto. Emitir era
consequência de estar conforme.

Três factos do próprio repositório reformulam o problema:

1. **`commercial` não pode emitir a factura de venda.** `modules/commercial.md`
   é explícito — `commercial` fornece a base, `finance`/AR possui a factura e o
   recebimento. Emitir não é encurtar a Fase 3; é abrir uma parte da Fase 4.

2. **O que bloqueia `fiscal` bloqueia sobretudo `payroll`.** Os escalões de IRT
   com fontes contraditórias, o INSS dedutível ou não, o tecto contributivo —
   nada disso é preciso para pôr uma linha de IVA numa factura de venda.

3. **Parte de "legal" é forma de documento, não certificação.** A numeração
   `FT S001/1` sem duplicados, a imutabilidade, a anulação lógica em vez de
   eliminação (BR-14) e o conjunto de campos que o XSD exige são baratos hoje e
   caros depois: o `roadmap-execucao.md` já avisava que o K7 descoberto tarde
   *"obriga a reescrever a emissão de facturas"*.

## Requirements

- **Facto** — `modules/commercial.md`: `commercial` não emite nem possui a
  factura de venda.
- **Facto** — `modules/fiscal.md`: `fiscal` é a fonte autoritativa das regras
  fiscais; **nenhum outro módulo implementa regras de imposto por sua conta**.
- **Facto** — ADR-011: nenhuma taxa em código; `vigente_desde`/`vigente_ate`
  obrigatórios; determinação à data do facto gerador.
- **Facto** — `docs/rivo-fiscal-regras-angola-v1.md` é levantamento provisório
  e **não é fonte de verdade**. Não implementar as regras que lá estão.
- **Decisão do produto (2026-08-24)** — não é preciso emissão legalmente
  válida. É preciso emitir.

## Constraints

- `docs/` continua a ser fonte de verdade e não se reescreve. Esta decisão não
  contradiz `docs/` — **adia** capacidades que lá estão descritas.
- O ADR-011 aplica-se na íntegra ao pouco de `fiscal` que se construir. Uma
  taxa de IVA em constante seria violação, mesmo sendo uma só.

## Alternatives

### A. Documento comercial simples, sem semântica fiscal

Identificador interno, cliente, linhas, total, e o IVA como um valor que quem
emite escreve. É o caminho mais curto até haver algo a emitir.

Rejeitada. Pôr numeração fiscal e imutabilidade depois obriga a remodelar a
tabela e a decidir o que fazer ao histórico já emitido — que é exactamente o
modo de falha que o K7 descreve, aplicado à numeração em vez de à assinatura.
Poupa dias agora e custa uma reescrita.

### B. Tudo menos a papelada da AGT

Inclui a cadeia `Hash`/`HashControl` e o motor de determinação completo.

Rejeitada por dois motivos independentes. O motor completo precisa de regras
que continuam **por verificar profissionalmente** — construí-lo agora é
construir sobre suposições, e a hipótese errada só aparece quando alguém
receber o valor errado. E a cadeia de assinatura sem certificação não produz
valor nenhum: é custo de imutabilidade e de ordenação a servir um requisito que
foi dispensado.

### C. Forma fiscal, sem certificação

A factura nasce com a forma que o SAF-T exige e sem nada do que só serve para
provar conformidade.

## Decision

**Emite-se com a forma do documento fiscal e sem conformidade legal.**

### Dentro do âmbito

| Item | Onde | Porquê |
|---|---|---|
| Numeração `[Tipo] [Série]/[Sequencial]`, sem duplicados, sequencial por série | `finance` | Retrofit obriga a renumerar histórico |
| Estado `N`/`A` — anulação lógica, nunca eliminação (BR-14) | `finance` | Invariante já vinculativa, independente de certificação |
| Imutabilidade da factura emitida | `finance` | Corrigir é emitir nota de crédito, não reescrever |
| Conjunto de campos do XSD na factura e na linha | `finance` | Contrato de completude: o que não for capturado na transacção não se reconstrói depois |
| Taxa de IVA como dado de referência com vigência (ADR-011) | `fiscal` | A regra do ADR-011 aplica-se a uma taxa tanto como a mil |
| `Cliente` | `commercial` | Sem cliente não há factura |

### Fora do âmbito, adiado

| Item | Consequência de adiar |
|---|---|
| Certificação AGT (`SoftwareValidationNumber`) | **As facturas emitidas não são documentos fiscais válidos em Angola** |
| Exportação SAF-T | Sem ficheiro de auditoria para a AGT |
| Cadeia `Hash`/`HashControl` (K7) | Acrescentável depois **sem reescrever a emissão**, porque a numeração, a ordem e a imutabilidade já lá estão |
| Declarações periódicas (IVA, IRT, INSS) | — |
| Motor de IRT e INSS | `payroll` continua bloqueado, como já estava |
| Códigos de isenção (`TaxExemptionCode`) | O campo existe e fica nulo. **Emitir com isenção fica bloqueado** — não se inventa código (`modules/commercial.md`) |

### O que isto assume

Que as facturas emitidas não são entregues a clientes como documento fiscal.
**Esta é a assunção de que tudo o resto depende**, e é decisão de produto
registada aqui, não conclusão técnica.

## Consequences

**Mais fácil:** existe um caminho até emitir que não passa pela AGT nem pelas
perguntas de direito fiscal por responder. `fiscal` deixa de ser um bloco de
fase e passa a ser uma fatia — uma tabela de taxas com vigência e um contrato de
determinação.

**Mais difícil:** o sistema passa a ter uma capacidade que *parece* completa e
não é. Uma factura do Rivo tem número, série e ar de factura, e não é documento
fiscal. Quem olhar para o ecrã não vê a diferença — e é por isso que fica
escrito aqui e em `modules/fiscal.md`.

**Ordem de execução alterada:**

```
fiscal (mínimo)  →  Taxa de IVA com vigência + contrato de determinação
commercial (mínimo)  →  Cliente
finance (mínimo, AR)  →  Factura de venda
```

`fiscal` e `commercial` não dependem um do outro e podem ser feitos em
paralelo. `finance` depende dos dois.

**Fases 5, 6 e 7 não mudam.** `payroll` continua a depender do motor fiscal
completo e das regras por verificar.

## Risks

- **Uma factura não-fiscal ser usada como fiscal.** É o risco central, e é de
  negócio, não de código. Mitigação possível e ainda por decidir: marcar o
  documento visivelmente enquanto não houver certificação. **Decisão em aberto**
  — registada em `pending-decisions.md`.
- **A "fatia mínima" de `fiscal` crescer sozinha.** Uma taxa hoje, um escalão
  amanhã, e reaparece o motor construído sobre o levantamento provisório que
  `CLAUDE.md` proíbe implementar. Detecta-se em revisão: qualquer regra nova em
  `fiscal` que não tenha instrumento legal verificado é para recusar.
- **Adiar o K7 revelar-se mais caro do que se estima.** A estimativa é que a
  assinatura se acrescenta sobre numeração e imutabilidade já existentes. Se
  aparecer um requisito de ordenação que a numeração não satisfaça, o custo
  sobe. Sinal de alerta: emissão concorrente sobre a mesma série.

## Adenda (2026-08-25) — as três decisões que ficaram em aberto

O ADR-036 deixou três perguntas por responder e registadas em
`pending-decisions.md`. O utilizador respondeu-as a 2026-08-25.

### 1. A factura é marcada visivelmente. **Sim.**

`SalesInvoice.FiscalNotice` transporta a menção, vinda de
`Finance:FiscalNotice`. Por omissão:

> Documento sem validade fiscal — software não certificado pela AGT.

**Congelada na emissão, e é o ponto todo.** No dia em que houver
`SoftwareValidationNumber`, esvazia-se a configuração e as facturas novas saem
sem menção — mas as **emitidas antes continuam a não ser válidas**, e a menção
tem de continuar a aparecer nelas. Derivá-la em tempo de leitura apagaria a
marca de todo o histórico no momento exacto da certificação, que é o contrário
do que se quer.

Nula significa sistema certificado. Hoje nenhum ambiente o é.

### 2. Existe consumidor final. **Sim.**

`SalesInvoice.CustomerId` passa a anulável, e `InvoicedParty.FinalConsumer(...)`
constrói o retrato de quem não se identificou. As duas metades têm de bater
certo: consumidor final com identificador de cliente, ou cliente registado sem
ele, é recusado.

A morada fica **vazia, não nula** — vazio é "não existe morada", nulo seria "não
sabemos", e não é o caso.

⚠ **O identificador vem de `Finance:FinalConsumerTaxId` e não do código.** A
convenção angolana para o identificador de consumidor final **não está
verificada em fonte primária** neste repositório, e `CLAUDE.md` proíbe
implementar regras fiscais a partir de levantamento provisório. O valor por
omissão — `CONSUMIDORFINAL` — é deliberadamente **não plausível como NIF**: um
número com ar de real seria tomado por verificado e sobreviveria até à
certificação. **Substituir pelo oficial antes de certificar.** Vazio bloqueia a
venda a consumidor final, com mensagem que diz porquê.

### 3. Séries de numeração: **uma contínua por tipo de documento.**

`S001`, sem reinício anual, criada pelo **seed** no arranque (`Finance:DefaultSeries`,
`Finance:SeedDefaultSeries`).

Reiniciar por ano obrigaria a criar uma série cada Janeiro e a decidir para qual
emitir perto da viragem — mais peças móveis, e cada uma delas uma hipótese de
emitir na série errada. Numeração contínua é igualmente auditável e não tem essa
data.

Semeada e não criada à mão porque, sem ela, um ambiente novo devolve `404` na
primeira factura — e o passo esquecido só aparece quando alguém tenta facturar.
O seed é idempotente (ADR-016, ADR-028): se a série já existe, não lhe toca, e
em particular **não lhe recua o contador**.
