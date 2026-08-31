# hr — Recursos Humanos

**Classificação:** core domain (bounded context "Ciclo de Vida do
Colaborador").

## Responsabilidade

A relação de trabalho e o ciclo de vida do colaborador, do onboarding ao
offboarding. Possui também a **estrutura organizacional** (Departamento) e
o **catálogo de Cargos**.

`hr` é o módulo com o **maior fan-out de todo o sistema**. Por isso o acesso
externo é estritamente contratual (ADR-010).

## Conceitos

| Conceito | Notas |
|---|---|
| Colaborador | utilizador_id **opcional** — nem todo colaborador tem login |
| Departamento | com gestor; **distinto** de Centro de Custo (ADR-005) |
| Cargo | catálogo organizacional com nível hierárquico e marca `confere_autoridade_aprovacao`; **distinto** de Perfil de Acesso (ADR-005) |
| Atribuição de Cargo | histórica (desde/até), com estado pendente/efectiva — um cargo é ocupado por alguém num período, não é coluna fixa em Colaborador |
| Contrato de Trabalho | tipo, datas, salário base (ADR-009) |
| Férias / Assiduidade / Benefícios | referenciam Colaborador |
| Recrutamento / Onboarding / Offboarding | referenciam Colaborador |

## Possui

Colaborador, Departamento, Cargo, Atribuição de Cargo, Contrato de
Trabalho, Férias, Assiduidade, Benefícios, Recrutamento, Onboarding,
Offboarding, e a tabela de ligação a documentos de colaborador.

## Depende de

`identity` (referência opcional a utilizador), `approval` (férias, pedidos
internos), `documents`, `audit`, `notifications`.

## Consumido por

Praticamente todos os contextos: `approval` (requisitante, aprovador,
Cargo), `payroll`, `finance` (responsável de centro de custo),
`procurement` (requisitante), `projects` (recursos), `fleet` (motorista),
`commercial` (dono comercial).

## Contratos publicados

### `ReferenciaColaborador` (ADR-010)

| Campo | Nota |
|---|---|
| `colaborador_id` | identificador estável |
| `nome_exibicao` | — |
| `estado` | activo / inactivo |
| `departamento_id` | — |
| `cargo_actual` | id + nome, resolvido à data pedida |
| `utilizador_id` | opcional |

Regras vinculativas:

- Os consumidores guardam **apenas `colaborador_id`**. Nunca copiam nome,
  departamento ou cargo para as suas tabelas.
- Excepção única: o snapshot de submissão em `approval` (BR-6, BR-19).
- Precisar de mais campos significa que ou o caso de uso pertence a `hr`,
  ou o contrato precisa de extensão explícita e versionada. Nunca leitura
  directa.

### Resolução por conta de `identity`

`FindByUserIdAsync` (desde 2026-08-31, ADR-042) — o colaborador ligado a
uma conta, se existir. Primeiro consumidor: `Rivo.EmployeePortal` (Portal
do Colaborador), para resolver "o próprio" sem permissão nova — é regra de
contexto, não autorização. Nunca mais do que um por conta: `utilizador_id`
passou a ser único quando preenchido.

### Resolução de Cargo

"Quem ocupa o cargo X à data D" — usado por `approval` para resolver
aprovadores. Usa Atribuição de Cargo, que é histórica.

**Só devolve atribuições efectivas.** Uma atribuição pendente de aprovação
não confere autoridade nenhuma (ADR-015).

## Autoridade sobre Cargos (ADR-015)

Duas operações, duas autoridades:

| Operação | Autoridade | Efeito |
|---|---|---|
| **Catálogo** — criar/alterar/desactivar um Cargo, e marcar `confere_autoridade_aprovacao` | `Admin` | Imediato, auditado |
| **Atribuição** — quem ocupa que Cargo, e quando | `HR` | Imediato **se** o Cargo não conferir autoridade de aprovação |
| **Atribuição de Cargo com autoridade de aprovação** | `HR` submete | Fica **pendente**; só produz efeito após decisão "Aprovado" de `approval` |

**Porquê:** `approval` resolve aprovadores por Cargo. Sem este controlo,
quem atribui Cargos decidiria quem aprova pagamentos sem tocar em perfis nem
permissões — escalada de privilégios invisível ao RBAC.

Quem submete a atribuição não pode decidi-la (BR-2).

## Não pode

- Gerir autenticação, credenciais ou permissões — isso é `identity`.
- Possuir Centro de Custo — isso é `finance` (ADR-005).
- Calcular a folha salarial — isso é `payroll`.
- Ter tabela própria de passos de aprovação para férias ou pedidos
  internos. Submete a `approval` (corrige o padrão do protótipo, onde
  `approval_steps` estava ligado a `employee_requests`).

## Regras de negócio

- Um colaborador pode existir sem utilizador associado.
- **2026-08-31 — quando associado, o utilizador é único** (ADR-042): uma
  conta liga-se, no máximo, a um colaborador. Contratar com uma conta já
  ligada a outro é recusado (409) — verificado no caso de uso, e o índice
  único em `HrDbContext` é a segunda linha de defesa.
- Atribuição de Cargo é histórica; alterações não recalculam processos de
  aprovação em curso (BR-6).
- Minimização e retenção de dados pessoais nos termos da Lei n.º 22/11
  (BR-16).
- Sem eliminação física de dados sujeitos a retenção (BR-14).

## Perguntas em aberto

- Atributos e ciclo de vida exactos de Colaborador — a definir a partir dos
  requisitos funcionais.
- Regras de férias (acumulação, saldo, carry-over) — não detalhadas em
  `docs`.

## Estado

**Núcleo implementado.** Colaborador, Departamento, Cargo e Atribuição de
Cargo, com o contrato `EmployeeReference` publicado em `Rivo.Hr.Contracts`.

**Contrato de Trabalho e Assiduidade** desde 2026-08-22. O primeiro regista o
que foi **acordado** — tipo, vigência, remuneração base e moeda; o segundo
regista o que **aconteceu** — entradas, saídas e faltas.

**Nenhum dos dois calcula nada.** Converter assiduidade em horas pagas, ou
salário base em líquido com IRT e INSS, é de `payroll`, que lê ambos como
entrada de cálculo.

Duas decisões de desenho que valem registo:

- **`hr.contracts.read` é permissão própria, separada de
  `hr.employees.read`.** Ver quem trabalha na empresa e ver quanto cada um
  ganha não são a mesma autorização — juntá-las daria o salário de toda a gente
  a quem só precisa do organograma.
- **Um registo de assiduidade por colaborador e por dia, com índice único.** A
  verificação no caso de uso apanha o uso normal; só a base de dados apanha
  duas marcações simultâneas — que é o que um relógio de ponto com rede
  instável produz.

**Benefícios, Recrutamento, Onboarding e Offboarding** desde 2026-08-22,
fechando a lista de conceitos que este módulo declara possuir — com uma
excepção, abaixo.

- **Benefícios:** catálogo e adesão separados. Descontinuar um benefício impede
  adesões novas sem cancelar as existentes.
- **Recrutamento:** funil que avança um passo de cada vez. Contratar cria o
  Colaborador e liga-o à candidatura — é a fronteira entre candidato e quadro
  de pessoal.
- **Entrada e saída:** um agregado para os dois, com checklist. **Não se conclui
  um processo com tarefas pendentes** — é a regra que separa uma lista de
  verificação de uma decoração, e é o que estes processos costumam falhar.

**Férias** desde 2026-08-23: pedido, retirada e aplicação da decisão. Passa por
`approval` como manda `modules/hr.md` — **sem passos de aprovação próprios**.
Um pedido pendente não é ausência.

⚠ **Sem saldo de férias.** Acumulação e carry-over continuam nas perguntas em
aberto, e implementá-los seria inventar política de direito a férias.

Verificado em `scripts/verify-hr.ps1` (20 casos, cresceu de 18 a
2026-08-31 com a unicidade de `utilizador_id`, ADR-042) e em 129 testes de
domínio.

### ✅ O `501` está fechado desde 2026-08-23

**Atribuir um Cargo com `confere_autoridade_aprovacao = true` deixou de ser
recusado.** Cria-se **pendente** e submete-se a `approval` (ADR-034).

Pendente não confere Cargo nenhum — `currentPosition` continua nulo até haver
decisão — e é isso que mantém fechado o caminho de escalada que o ADR-015
descreve. A resposta é `202`, não `201`: passou por governança e ainda não
produziu efeito.

A ligação é feita por **inversão de dependência**: `hr` declara
`IHrApprovalSubmission` nas suas palavras e não sabe que `approval` existe; o
adaptador vive no composition root. A alternativa que o ADR-015 §R1 previa —
assemblies de contratos dos dois lados — resolve a compilação mas deixa o ciclo
no grafo de módulos, e `Modules_HaveNoDependencyCycles` continuaria a vê-lo.

A decisão aplica-se sozinha em menos de 60 s por
`PositionApprovalReconciliationWorker`; para não esperar, há
`POST /hr/position-assignments/{id}/approval-outcome`, que é idempotente.

**Nenhum endpoint de `hr` devolve `501`.**

### Por fazer

- **Saldo de férias** — acumulação e carry-over continuam nas perguntas em
  aberto. Implementá-los seria inventar política de direito a férias.
- **Cálculo** — converter assiduidade em horas pagas, ou salário base em
  líquido, é de `payroll`, que lê ambos como entrada.
