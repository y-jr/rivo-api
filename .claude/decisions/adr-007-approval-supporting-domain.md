# ADR-007: Governança de Decisões é Supporting Domain

## Status

Aceite (resolução R1)

## Context

`docs/rivo-arquitetura-global-v1.md` v1 classificava Governança de Decisões
(Approval) como **core domain**. A justificação apresentada era: "é
declaradamente a proposta de valor do Rivo perante o SGAP".

Em revisão de arquitectura, esse argumento foi contestado: é um argumento de
**posicionamento comercial**, não de análise de domínio. A classificação
core/supporting/generic determina quanto investimento e sofisticação vão
para um domínio — não deve assentar em posicionamento.

## Requirements

- **Facto** — O motor de aprovações é a lacuna crítica: 5 implementações
  paralelas no protótipo, nenhuma cobrindo integralmente o pedido do SGAP.
- **Facto** — As invariantes exigidas (segregação de funções,
  anti-fraccionamento, verificação orçamental) dependem de dados de outros
  contextos do Rivo.
- **Inferência** — Motores de aprovação são um problema bem compreendido,
  com soluções disponíveis no mercado.

## Constraints

O motor não pode tornar-se um God Module que conhece os detalhes internos de
todos os módulos.

## Alternatives

1. **Supporting domain, construído internamente, com âmbito disciplinado.**
2. Core domain, construído internamente, com investimento máximo.
3. Generic domain, adquirindo um workflow engine externo.

A opção 3 foi rejeitada: um motor externo não consegue impor invariantes que
dependem de dados do Rivo — o anti-fraccionamento agrega por fornecedor e
rubrica; a verificação orçamental lê `finance`. Adquirir fragmentaria o
modelo de domínio, que é exactamente o erro do protótipo.

A opção 2 foi rejeitada porque conduz a sobre-investimento em generalidade
de motor (BPMN, designers visuais) em vez de em integração.

## Trade-offs

Reclassificar não muda o que se constrói — muda **onde vai o esforço**. Em
supporting, o esforço vai para as invariantes e para a integração; em core,
tenderia a ir para a generalidade do motor.

## Decision

**Governança de Decisões é supporting domain.**

O diferenciador do Rivo não é o motor de aprovações — é a **integração da
governança de decisões em todos os contextos de negócio**: aprovação ligada
nativamente a Procurement, Payroll, Tesouraria e Comercial, com acesso aos
dados que tornam possíveis regras como anti-fraccionamento e verificação
orçamental. Essa integração é difícil de replicar; o motor não é.

Consequências operacionais:

- **Construir internamente, não adquirir.**
- **Disciplina de âmbito:** sem BPMN, sem designer visual de workflows, sem
  grafos de workflow arbitrários. O workflow é definido pelo Rivo; só as
  regras (alçadas, cargos, departamentos, faixas de valor) são
  configuráveis.
- O investimento vai para as invariantes e para a integração, não para a
  generalidade.

## Consequences

Facilita:

- Âmbito contido e defensável — evita construir um workflow engine genérico
  que ninguém pediu.
- Foco no que realmente diferencia.

Dificulta:

- Se um cliente futuro exigir workflows próprios definidos em runtime, isso
  fica explicitamente fora do âmbito e obriga a reabrir esta decisão.

## Risks

- **Interpretação errada de "supporting" como "menos importante".** Não é:
  continua a ser a lacuna crítica e a concentrar as invariantes mais
  sensíveis do sistema. A classificação diz respeito a diferenciação
  competitiva, não a prioridade de implementação.
- Pressão para adicionar funcionalidade genérica de workflow "porque é
  fácil". Mitigação: a disciplina de âmbito está escrita aqui e em
  [modules/approval.md](../modules/approval.md).

## Revisit When

- Surgir requisito de workflows definidos pelo cliente em runtime.
- Surgir necessidade de processos de aprovação fora do âmbito do Rivo.

## Related

- `docs/rivo-arquitetura-global-v1.md` §10 (R1)
- [modules/approval.md](../modules/approval.md)
- [ADR-008](adr-008-segregacao-funcoes.md)
- [domain/domain-map.md](../domain/domain-map.md)
