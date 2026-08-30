# ADR-040: Orçamento de Projecto e Orçamento de `finance` São Entidades Distintas, Relacionadas

## Status

Aceite (2026-08-30). Decisão do utilizador.

Desbloqueia `projects` → Orçamento de Projecto.

## Context

`modules/projects.md` §Perguntas em aberto perguntava se o Orçamento de
Projecto se relaciona com o Orçamento por centro de custo de `finance`
(`Planning`, ADR-037), ou é independente. Sem resposta, `projects` não podia
modelar Orçamento sem arriscar duplicar uma entidade que já existe em
`finance`.

## Decision

**Não são a mesma entidade.**

- **O Orçamento de Projecto pertence a `projects`** — controla a
  dotação/alocação do próprio projecto.
- **O Orçamento de `finance` continua a controlar** orçamento financeiro,
  centro de custo e disponibilidade (ADR-037, BR-8).

**Devem estar relacionados**, para que uma despesa de projecto possa ser
validada contra o orçamento financeiro **sem duplicar a entidade**. O
mecanismo concreto dessa validação — pergunta directa a `finance`, publicação
de um contrato de leitura, ou outro — fica por desenhar quando o Orçamento de
Projecto tiver código; este ADR fixa que a relação existe e que nenhum dos
lados copia o orçamento do outro.

## Consequences

### O que fica melhor

- **`projects` pode modelar Orçamento sem re-litigar `finance.Planning`.** As
  duas entidades continuam separadas, cada uma com o seu dono.
- **Uma despesa de projecto tem para onde ser validada** — a relação existe
  de propósito para isso, mesmo que o mecanismo ainda não esteja desenhado.

### O que fica em aberto, e é assumido

- **O mecanismo de validação cruzada não está desenhado.** Fica para quando
  `projects.Orçamento` tiver código a sério — ADR-010 (referência por
  identificador, nunca cópia) continua a valer como restrição de fundo.
- **A relação com `procurement`** (requisições geradas por projecto —
  dependência directa ou por eventos) continua em aberto, sem resposta deste
  ADR.

## Related

`modules/projects.md` §Perguntas em aberto, `modules/finance.md`, ADR-037
(Planeamento e disponibilidade orçamental), ADR-010.
