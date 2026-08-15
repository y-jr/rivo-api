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

Verificado em `scripts/verify-hr.ps1` (13 casos).

### Fora do implementado

Contrato de Trabalho, Férias, Assiduidade, Benefícios, Recrutamento,
Onboarding e Offboarding. Todos dependem de Colaborador existir — entram sem
retrabalho do núcleo.

### ⚠ Bloqueio activo

**Atribuir um Cargo com `confere_autoridade_aprovacao = true` devolve
`501 Not Implemented`** e não grava nada.

BR-20 exige decisão de `approval`, e esse módulo não existe. Recusar é
deliberado: criar a atribuição em estado permanentemente pendente seria
confuso, e torná-la efectiva abriria a escalada de privilégios que ADR-015
fecha.

O modelo de dados já contempla o estado `Pending` — só falta o caminho de
decisão.
