# Prompt: Decisão Arquitectural

Usar quando uma tarefa exige decisão que afecta a arquitectura: nova escolha
tecnológica, nova dependência entre módulos, novo padrão, ou qualquer coisa
não coberta por [architecture/](../architecture/) ou por um ADR existente.

## Método

Não responder "usa X porque escala melhor". A análise deve percorrer:

1. **Problema** — qual é o problema real?
2. **Requisitos** — que requisitos funcionais e não funcionais influenciam?
   Rotular Facto / Inferência / Hipótese.
3. **Constraints** — limitações técnicas, de negócio, equipa, prazo.
4. **Decisões já tomadas** — ver [decisions/](../decisions/) e
   [state/pending-decisions.md](../state/pending-decisions.md).
5. **Alternativas** — opções razoáveis, incluindo as que serão rejeitadas.
6. **Trade-offs** — o que se ganha e o que se perde em cada uma.
7. **Impacto** no domínio, nos dados e na operação.
8. **Riscos**.
9. **Recomendação** com fundamento no contexto do Rivo, não em best
   practice genérica.
10. **Consequências**.
11. **Como validar** a decisão.
12. **ADR**, quando aplicável.

## Regras

- Verificar primeiro se já está decidido. Não re-litigar um ADR aceite sem
  informação nova — se o contexto mudou genuinamente, propor **substituir**
  explicitamente.
- Preferir a opção que mantém as fronteiras limpas
  ([module-boundaries.md](../architecture/module-boundaries.md)) e não
  introduz infraestrutura que o requisito actual não justifica.
- Quando duas soluções satisfazem os requisitos, preferir a mais simples, a
  mais fácil de testar e operar, com menos dependências e menos pontos de
  falha. **Complexidade tem de ser justificada por um requisito.**
- Decisão não trivial ou difícil de reverter: não decidir sozinho. Apresentar
  ao utilizador com recomendação e o trade-off principal.

## Depois de decidir

1. Escrever ADR com
   [decisions/adr-template.md](../decisions/adr-template.md), numerado
   sequencialmente.
2. Actualizar os documentos afectados em
   [architecture/](../architecture/) ou [modules/](../modules/).
3. Remover o item de
   [state/pending-decisions.md](../state/pending-decisions.md) e acrescentá-lo
   à tabela de decisões fechadas.
