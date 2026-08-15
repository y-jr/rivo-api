# ADR-005: Modelo Organizacional — Cargo ≠ Perfil, Departamento ≠ Centro de Custo

## Status

Aceite (decisões D2 e D4, fechadas pelo cliente)

## Context

Duas ambiguidades do protótipo que, se herdadas, tornariam impossível
resolver aprovadores de forma correcta.

**D2 — Cargo vs. Perfil de Acesso.** O SGAP raciocina em cargos ("Chefe de
Departamento", "DAF", "Director Geral", "CEO", "CFO"). O Rivo tinha apenas
perfis de acesso (Admin/Manager/Finance/…). São coisas diferentes: um
Director Financeiro e um contabilista podem ambos ter o perfil `Finance`,
mas só um deles aprova acima de determinada alçada.

**D4 — Departamento vs. Centro de Custo.** No protótipo, `departments` tem
`manager_id` (RH) e `cost_centers` tem `responsible_id` próprio
(Financeiro). Existem **duas noções distintas de "quem é responsável"**, que
podem divergir.

## Requirements

- **Facto** — O SGAP resolve aprovadores por cargo, não por perfil de
  acesso.
- **Facto** — `cost_centers` tem responsável próprio, distinto do gestor de
  departamento.
- **Inferência, confirmada pelo cliente** — a divergência é intencional: nem
  todo o centro de custo corresponde 1:1 a um departamento.

## Constraints

`identity` não pode tornar-se um segundo ponto de dados organizacionais
(ADR-004).

## Alternatives

1. **Manter as quatro entidades distintas, com donos separados.**
2. Fundir Cargo em Perfil de Acesso.
3. Fundir Centro de Custo em Departamento (1:1 obrigatório).

As opções 2 e 3 foram rejeitadas: perdem informação que o negócio distingue
e impedem regras de aprovação correctas.

## Trade-offs

Quatro entidades em vez de duas é mais modelo a manter. Em troca, cada uma
tem dono claro e a resolução de aprovadores torna-se possível.

## Decision

Quatro conceitos distintos, com donos explícitos:

| Conceito | Dono | Responde a |
|---|---|---|
| **Perfil de Acesso** | `identity` | "O que este utilizador pode ver/fazer no sistema?" |
| **Cargo** | `hr` | "Que posição organizacional ocupa esta pessoa?" |
| **Departamento** | `hr` | Unidade organizacional, com gestor |
| **Centro de Custo** | `finance` | Dimensão financeira, com responsável próprio |

Regras vinculativas:

- `identity` **não** contém Cargo.
- Cargo é atribuído por **Atribuição de Cargo com histórico** (desde/até) —
  não é coluna fixa em Colaborador. Um cargo é ocupado por alguém num
  período.
- Centro de Custo tem `departamento_id` **opcional**. O mapeamento a
  departamento não é obrigatório nem 1:1.
- `approval` resolve aprovadores por **Cargo**, nunca por Perfil de Acesso.

## Consequences

Facilita:

- Resolução correcta de aprovadores, incluindo histórico (quem ocupava o
  cargo à data da submissão).
- Contabilidade analítica independente do organograma.
- Autorização e organização evoluem sem se contaminarem.

Dificulta / exige:

- Quatro entidades a manter em vez de duas.
- Um utilizador pode ter perfil sem cargo, e vice-versa — os fluxos têm de
  lidar com isso explicitamente.
- Relatórios por "departamento" e por "centro de custo" podem divergir. É
  intencional, não defeito.

## Risks

- **Confusão terminológica** em código e conversas. Mitigação:
  [standards/naming.md](../standards/naming.md) fixa os termos e proíbe
  usá-los como sinónimos.
- Configuração incorrecta de mapeamento centro de custo ↔ departamento
  produzindo relatórios enganadores. Mitigação: tornar a ausência de
  mapeamento visível, não silenciosa.

## Revisit When

- O negócio decidir que centro de custo passa a corresponder sempre 1:1 a
  departamento.
- Surgir necessidade de hierarquia de cargos com semântica de delegação
  automática.

## Related

- `docs/rivo-arquitetura-global-v1.md` §3, §9 (D2, D4)
- [ADR-004](adr-004-identity-auth-vs-authz.md),
  [ADR-006](adr-006-budget-vs-previsao-custos.md),
  [ADR-007](adr-007-approval-supporting-domain.md)
- [modules/hr.md](../modules/hr.md),
  [modules/identity.md](../modules/identity.md),
  [modules/finance.md](../modules/finance.md)
