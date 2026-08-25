# ADR-037: O Plano de Contas é Dado Carregado, e a Verificação Orçamental é uma Pergunta

## Status

Aceite (2026-08-25).

Fecha **BR-8** e o **K6**. Não substitui nenhum ADR; aplica o ADR-011 a um
segundo caso e completa a inversão que o ADR-034 começou.

## Context

Contabilidade & Fecho e Planeamento eram os dois contextos internos que
faltavam a `finance`. Construí-los levantou duas perguntas que não são de
implementação.

### O que o Rivo pode saber sobre um plano de contas

`docs/rivo-fiscal-saft-ao-v1.md` mapeia `GeneralLedgerAccounts` para
`finance` (Contabilidade) e diz "Plano de contas, PGC angolano". O XSD em
`docs/schemas/` fixa a **forma**: `SAFAOGLAccountID` restringe o código a
`[0-9a-zA-Z\-/._+*]{1,30}`, `GroupingCategory` enumera seis valores, e a
documentação do elemento diz que "excepto para as contas do 1.º grau, deve ser
indicada a conta agregadora respectiva, do grau imediatamente superior".

**O que o XSD não fixa é o conteúdo.** Que contas existem no PGC angolano, com
que códigos e que descrições, não está em fonte primária neste repositório — o
único documento que fala de regras fiscais angolanas é o levantamento
provisório, e o `CLAUDE.md` proíbe implementar a partir dele.

### O que atravessa a fronteira em BR-8

BR-8 exige verificação orçamental antes da decisão. `approval` decide;
`finance` possui o orçamento. `docs` avisa que este é **um dos dois pontos onde
o God Module pode nascer**.

A dificuldade concreta: o orçamento é por **centro de custo**, e
`ApprovalSubmission` carrega **departamento**. D4 fixa que o mapeamento entre
os dois é opcional e **não é 1:1** — um departamento pode alimentar vários
centros de custo. Traduzir de um para o outro pode ser ambíguo.

## Decision

### 1. O Rivo fixa a estrutura do plano de contas e não o conteúdo

`LedgerAccount` impõe o formato do código, as seis categorias, a agregadora
obrigatória fora do 1.º grau, a separação entre contabilidade geral e
analítica, e que **só contas de movimento (`GM`/`AM`) recebem lançamentos**.

**Nenhuma conta vem semeada.** O plano carrega-se por
`POST /finance/ledger/accounts`, de cima para baixo.

### 2. A partida dobrada é imposta no agregado

`JournalEntry.Post` recebe as linhas todas, soma os dois lados e recusa se não
baterem. **Um lançamento nasce inteiro ou não nasce** — não há rascunho.

Corrigir faz-se com outro lançamento, de regularização (`R`) ou de ajustamento
(`J`), que o SAF-T distingue precisamente para isso.

### 3. O período contabilístico vai de 1 a 16

⚠ Há divergência dentro de `docs/`: a tabela de restrições em
`rivo-fiscal-saft-ao-v1.md` diz "Período | 1–12"; o XSD restringe
`SAFAOAccountingPeriod` a **1..16**.

**Segue-se o XSD.**

### 4. `IBudgetAvailability` é uma pergunta e uma resposta

`approval` pergunta se um valor cabe. Nada mais atravessa: nem orçamentos, nem
centros de custo, nem lançamentos.

**A rubrica atravessa `approval` sem ser interpretada**, exactamente como o
`SourceReference` já fazia. Sem rubrica, a verificação recua para o
departamento e **recusa se a tradução for ambígua**.

Dos cinco resultados, **um só deixa passar**.

### 5. `approval` referencia `Rivo.Finance.Contracts` directamente

Não há ciclo: `finance` declara `IPaymentApproval` nas suas próprias palavras e
o composition root é que os liga. A direcção é uma só.

### 6. Quem elabora o orçamento não o aprova

`finance.planning.write` e `finance.budgets.approve` são permissões distintas e
as listas de perfil **não se sobrepõem**. Um orçamento aprovado não se altera.

## Consequences

### O que fica melhor

- **A contabilidade não mente.** Não há um plano de contas plausível a induzir
  em erro; há uma estrutura correcta e vazia, e quem a preenche é quem sabe.
- **BR-8 deixa de ser uma recusa** e passa a ser uma regra. O K6 fecha.
- **A verificação orçamental não pode ser contornada por quem a sofre**, porque
  quem pede não aprova o tecto contra que é medido.
- **`approval` continua sem saber o que aprova.** A rubrica opaca preserva isso.

### O que fica pior, e é assumido

- **O sistema não serve para nada até alguém carregar o plano.** É a
  contrapartida directa de não o inventar, e é deliberada — a alternativa é um
  plano errado que ninguém revê porque parece certo.
- **O consumo orçamental é um limite inferior.** Conta compromissos — pedidos
  de pagamento não cancelados imputados ao centro de custo. Despesa que chegue
  aos livros sem passar por um pedido não consome orçamento.
- **A verificação é à data de hoje**, não à data do pedido: `ApprovalSubmission`
  não carrega data. Um pedido retroactivo é medido contra o mês corrente.
- **Os documentos não geram lançamentos.** A factura de venda, o recibo e a
  execução de pagamento não postam nos livros — a contabilidade regista-se à
  mão. Ligá-los depende do plano carregado, e por isso não podia vir antes.
- **`approval` ganhou uma dependência de `finance`.** Sem ciclo, mas real: são
  agora dois módulos que se conhecem por contrato em vez de um.

## Alternatives considered

### A. Semear um plano de contas PGC a partir do levantamento provisório

**Rejeitada.** É exactamente o que o `CLAUDE.md` proíbe, e o modo de falha é o
pior possível: um plano plausível não é revisto, e o erro aparece no primeiro
ficheiro entregue à AGT.

### B. `approval` receber o centro de custo em vez do departamento

**Rejeitada.** Faria `approval` conhecer um conceito de `finance`. A rubrica
opaca dá o mesmo resultado sem o custo.

### C. `finance` adivinhar o centro de custo a partir do departamento

**Rejeitada como caminho único**, mantida como recuo. Com dois centros de custo
no mesmo departamento — que D4 permite — adivinhar significaria verificar
contra um tecto que ninguém indicou. Onde a rubrica falta, recusa-se.

### D. Deixar passar quando não se consegue verificar

**Rejeitada.** É a inversão exacta do que BR-8 existe para impedir. Uma política
que exige verificação está a dizer que não se decide sem saber.

### E. Contar o realizado (lançamentos) em vez do comprometido

**Rejeitada para a v1.** No momento em que BR-8 é perguntada, a despesa ainda
não foi lançada — contar só o realizado deixaria passar tudo até ao fecho do
mês. Quando os documentos postarem automaticamente, isto volta a ser uma
pergunta em aberto: somar as duas fontes contaria a dobrar.

### F. Inverter também a direcção `approval → finance`

**Rejeitada.** `approval` já referencia `Rivo.Hr.Contracts` directamente, e não
há ciclo a resolver. Uma porta declarada em `approval` e adaptada no
composition root seria simetria sem ganho.
