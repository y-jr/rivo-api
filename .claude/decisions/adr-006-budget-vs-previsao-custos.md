# ADR-006: Orçamento ≠ Previsão de Custos Departamentais

## Status

Aceite (decisão D3, fechada pelo cliente)

## Context

O Rivo tem `budgets`/`budget_lines`: mensais, por centro de custo, com
função de tecto de controlo orçamental.

O SGAP pede **planeamento de custos departamentais mensais**, com um ciclo
de submissão → consolidação → aprovação ligado ao **carregamento de caixa**.

Semanticamente sobrepõem-se — ambos são "valor previsto por período por
unidade organizacional" — mas servem propósitos diferentes: um controla
gastos contra um tecto; o outro alimenta previsão de tesouraria.

## Requirements

- **Facto** — `budgets`/`budget_lines` são mensais e por `cost_center_id`.
- **Facto** — O SGAP liga o planeamento de custos ao carregamento de caixa.
- **Facto** — Verificação orçamental antes da decisão de aprovação
  (RN-017).

## Constraints

Centro de Custo (`finance`) e Departamento (`hr`) são distintos e o
mapeamento é opcional (ADR-005). Os dois conceitos não estão sequer
ancorados na mesma dimensão.

## Alternatives

1. **Duas entidades distintas que coexistem.**
2. Uma entidade com um campo de "propósito".
3. Estender Orçamento com os atributos de previsão de caixa.

As opções 2 e 3 foram rejeitadas: fundir um instrumento de **controlo**
(tecto que não deve ser excedido) com um instrumento de **previsão**
(estimativa que se espera errada) produz um conceito que não serve bem
nenhum dos dois. Além disso ancoram-se em dimensões diferentes — centro de
custo vs. departamento.

## Trade-offs

Duas entidades exigem reconciliação quando o negócio quiser comparar
previsto vs. orçamentado. Em troca, cada uma mantém semântica limpa e um
ciclo de vida próprio.

## Decision

**Duas entidades distintas em `finance`, nunca fundidas:**

| Entidade | Âncora | Propósito | Ciclo |
|---|---|---|---|
| **Orçamento** (+ Linha de Orçamento) | centro de custo | Tecto de controlo | Anual, com linhas mensais |
| **Previsão de Custos Departamentais** | departamento | Input ao carregamento de caixa | Mensal: submissão → consolidação → aprovação |

Relacionam-se **por referência** ao mesmo período e unidade, quando o
mapeamento centro de custo ↔ departamento existir. Nunca por fusão de
tabela.

A verificação orçamental de `approval` (BR-8) consulta **Orçamento**, não
Previsão.

## Consequences

Facilita:

- Controlo orçamental e previsão de tesouraria evoluem independentemente.
- O ciclo mensal de submissão/aprovação da previsão não perturba o
  orçamento anual.

Dificulta:

- Comparar "previsto vs. orçamentado" exige reconciliação explícita, porque
  as âncoras podem divergir (ADR-005).
- Duas superfícies de introdução de dados para os gestores.

## Risks

- **Divergência silenciosa** entre os dois valores, sem ninguém reparar.
  Mitigação: expor a comparação como relatório explícito, não deixá-la
  implícita.
- Utilizadores confundirem os dois. Mitigação: nomes fixados em
  [standards/naming.md](../standards/naming.md).

## Revisit When

O negócio decidir que a previsão de caixa passa a derivar automaticamente do
orçamento, tornando a entrada dupla redundante.

## Related

- `docs/rivo-arquitetura-global-v1.md` §2, §9 (D3)
- [ADR-005](adr-005-modelo-organizacional.md)
- [modules/finance.md](../modules/finance.md)
