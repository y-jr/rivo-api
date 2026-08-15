# ADR-015: Atribuição de Cargo — Autoridade e Aprovação

## Status

Aceite (2026-08-10)

## Context

ADR-005 fixou Cargo como conceito de `hr`, distinto de Perfil de Acesso, e
determinou que é atribuído por **Atribuição de Cargo com histórico**
(desde/até). ADR-007 e ADR-010 estabeleceram que `approval` resolve
aprovadores **por Cargo**.

Ficou por decidir **quem tem autoridade para criar uma Atribuição de Cargo**.
Não constava de nenhum ADR nem dos `docs`.

A lacuna esconde um problema de segurança concreto:

> Quem puder atribuir Cargos pode decidir **quem aprova pagamentos** — sem
> tocar em perfis nem permissões.

Alguém com permissões de RH atribui-se "Director Financeiro" e passa a ser
aprovador acima da alçada. O RBAC de ADR-014 **não vê nada**, porque nenhuma
permissão foi alterada. É um caminho de escalada de privilégios paralelo ao
sistema de autorização.

BR-4 ("não pode intervir em papéis conflituantes no mesmo processo") não
protege: verifica no momento da aprovação, não impede a aquisição do Cargo.

Num sistema cujo requisito central herdado do SGAP é *"quem valida não
aprova, quem aprova não paga"*, isto é material.

## Requirements

- **Facto** — `approval` resolve aprovadores por Cargo (ADR-005, ADR-010).
- **Facto** — Segregação de funções imposta tecnicamente, não só na interface
  (SGAP, ADR-008).
- **Facto** — Alterações de configuração são auditadas com a mesma disciplina
  que transacções de negócio (BR-13).
- **Facto** — Aprovadores são congelados na submissão; alterações
  organizacionais posteriores não recalculam processos em curso (BR-6).

## Constraints

- Cargo pertence a `hr`; `identity` não o modela (ADR-004, ADR-005).
- Sem ABAC (ADR-014).

## Alternatives

| # | Quem atribui | Avaliação |
|---|---|---|
| A | Perfil `HR` | Dono natural do registo. **Deixa a escalada aberta.** |
| B | Só `Admin` | Mais restrito, mas põe uma operação de RH no perfil técnico e mistura responsabilidades. Não elimina a escalada — transfere-a. |
| C | `HR` atribui; atribuições que confiram autoridade de aprovação passam por `approval` | Escolhida. |

A e B foram rejeitadas por deixarem intacto o caminho de escalada: em ambas,
um único actor decide quem aprova pagamentos.

## Trade-offs

C acrescenta um campo ao Cargo e um fluxo de aprovação. Em troca, fecha a
escalada usando o mecanismo de governança que o sistema já terá — não
inventa um segundo.

Custo real: uma atribuição sensível deixa de ter efeito imediato.

## Decision

Separam-se **duas operações com autoridades distintas**:

### 1. Catálogo de Cargos — quem existe

Criar, alterar ou desactivar um Cargo é **administração de dados de
referência**. Mudança rara.

- **Autoridade:** perfil `Admin`.
- Cada Cargo tem a marca **`confere_autoridade_aprovacao`** (booleano).

### 2. Atribuição de Cargo — quem ocupa

Operação corrente de RH.

- **Autoridade:** perfil `HR`.
- **Se o Cargo tiver `confere_autoridade_aprovacao = false`:** produz efeito
  imediatamente.
- **Se tiver `confere_autoridade_aprovacao = true`:** a atribuição fica
  **pendente** e é submetida a `approval`. Só produz efeito após decisão
  "Aprovado". Até lá, `ReferenciaColaborador.cargo_actual` **não** reflecte o
  cargo pendente.

### Regras vinculativas

1. Uma atribuição pendente **não confere autoridade nenhuma**. O efeito é da
   decisão, não da submissão.
2. **Quem submete a atribuição não pode decidi-la** (BR-2 aplica-se).
3. Toda a atribuição — pendente, aprovada ou rejeitada — é auditada, com o
   Cargo, o colaborador, o período e o autor (BR-13).
4. A marca `confere_autoridade_aprovacao` só é alterável por `Admin`, e a
   alteração é auditada. Baixá-la é, na prática, desactivar o controlo.

## Consequences

Facilita:

- Fecha o caminho de escalada: nenhum actor isolado decide quem aprova
  pagamentos.
- Reutiliza o motor de aprovações em vez de criar um segundo mecanismo.
- A separação catálogo/atribuição alinha autoridade com frequência de
  mudança.

Dificulta / exige:

- Atribuições sensíveis deixam de ter efeito imediato. Numa substituição
  urgente (o CFO sai a meio de um processo), a demora é real — mitigável pela
  delegação que `approval` já prevê, não por contornar este controlo.
- `hr` passa a ter estado intermédio: atribuição pendente vs. efectiva.
- Mais um tipo de processo no catálogo de `approval`.

## Risks

### R1 — Ciclo de dependências entre `hr` e `approval`

Esta decisão **torna concreto um ciclo que já estava latente** na tabela de
[dependency-rules.md](../architecture/dependency-rules.md):

```
hr       → approval   (submete férias e, agora, atribuições de cargo)
approval → hr         (resolve aprovadores por Cargo)
```

As regras proíbem dependências circulares, e em .NET uma referência mútua de
projectos nem sequer compila.

**Resolução recomendada, a aplicar quando `hr` e `approval` forem
implementados:** cada módulo publica um assembly de contratos sem
dependências (`Rivo.Hr.Contracts`, `Rivo.Approval.Contracts`), e os
consumidores referenciam contratos, nunca implementações. `hr` →
`Approval.Contracts` e `approval` → `Hr.Contracts` não formam ciclo, porque
os contratos não dependem de nada.

**Não se aplica agora** — só existe o módulo `identity`, e introduzir o
padrão sem módulos que o exijam seria abstracção especulativa. Fica
registado para não ser redescoberto.

### R2 — Arranque

Quem aprova a primeira atribuição de um Cargo com autoridade? Não há ainda
ninguém com esse Cargo.

É o mesmo problema do primeiro Admin (ADR-014). Devem ser resolvidos em
conjunto, com um mecanismo de bootstrap único e auditado. **Por decidir.**

### R3 — Marca mal atribuída

Um Cargo que devia estar marcado e não está passa a atribuível sem controlo.
Mitigação: a marca é do `Admin`, é auditada, e a revisão periódica dos
Cargos marcados deve fazer parte do processo de auditoria.

## Revisit When

- Um Cargo precisar de autoridade condicional (ex.: aprova até certo valor
  apenas em determinado departamento) — isso é ABAC e este modelo não o
  cobre.
- A demora nas atribuições sensíveis se revelar operacionalmente insuportável.

## Related

- [ADR-005](adr-005-modelo-organizacional.md),
  [ADR-007](adr-007-approval-supporting-domain.md),
  [ADR-010](adr-010-referencia-entre-contextos.md),
  [ADR-014](adr-014-rbac-permissoes.md)
- [modules/hr.md](../modules/hr.md),
  [modules/approval.md](../modules/approval.md)
