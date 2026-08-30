# ADR-039: `inventory` e Activos Fixos de `finance` Coexistem, com Relação Explícita

## Status

Aceite (2026-08-30). Decisão do utilizador, em resposta directa ao K1.

Fecha o **K1**. Desbloqueia `inventory` → Movimento.

## Context

K1 (`known-issues.md`) registava a sobreposição entre Activos Fixos
(`finance`, com depreciação) e Activos (`inventory`) como um buraco de
desenho: nenhum dos dois módulos podia assumir o ownership sem decisão, e
isso bloqueava a modelação dos dois — em particular, `inventory.Movimento`
(sem ele `QuantityOnHand` nunca sai de zero).

`docs/rivo-arquitetura-global-v1.md` §1.2 assinala a sobreposição e não a
resolve. `pending-decisions.md` marcava-a como pergunta de domínio e negócio
em aberto.

## Decision

**Os dois módulos coexistem, com donos distintos sobre facetas distintas do
mesmo bem físico:**

- **`inventory` é dono do activo físico/operacional** — localização,
  responsável, estado, movimentos.
- **`finance` é dono do activo contabilístico** — valor, capitalização,
  depreciação, abate.

**Deve existir uma relação explícita entre o registo de `inventory` e o
Activo Fixo de `finance`, idealmente 1:1 quando representam o mesmo bem.**
Nem todo o item de `inventory` é Activo Fixo — mercadorias e consumíveis
podem existir só em `inventory`, sem contrapartida em `finance`.

**Não é decisão de engenharia sobre qual mecanismo concreto liga os dois**
— este ADR fixa a fronteira e a existência da relação; o desenho do campo de
referência (nome, obrigatoriedade, sentido) fica para quando `finance` tiver
Activos Fixos para ligar. Até lá, `inventory.Movimento` avança sem essa
referência, ou com um campo opcional reservado (`FixedAssetId?`), consistente
com a regra geral do ADR-010 — referência por identificador, nunca FK entre
schemas, nunca cópia de atributos.

## Consequences

### O que fica melhor

- **K1 fecha.** `inventory.Movimento` pode avançar sem ambiguidade de
  ownership.
- **Consumíveis e mercadorias têm lugar claro** — só `inventory`, sem
  obrigação de existirem em `finance`.
- **A fronteira não força um módulo a saber o que é do outro.** `inventory`
  não calcula depreciação; `finance` não sabe onde está uma viatura.

### O que fica em aberto, e é assumido

- **O mecanismo de ligação 1:1 não está desenhado.** Fica para quando
  `finance` tiver Activos Fixos — que continua sem código, e sem data.
  `inventory.Movimento` não espera por essa peça.
- **Um bem sem par no outro módulo é estado válido**, dos dois lados: um
  consumível só em `inventory`, ou (menos comum) um Activo Fixo sem
  contrapartida operacional em `inventory`.

## Related

K1 (`known-issues.md`), `pending-decisions.md` §Domínio e negócio,
`modules/inventory.md`, `modules/finance.md`, ADR-010 (referência por
identificador entre módulos).
