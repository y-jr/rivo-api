# Prompt: Refactor

Usar para reestruturação sem alteração de comportamento observável.

## Passos

1. Confirmar que o refactor **não** altera comportamento de negócio. Se
   altera, é alteração funcional — usar
   [03-feature.md](03-feature.md), com testes a reflectir o novo
   comportamento.

2. Confirmar que se mantém dentro das fronteiras existentes. Se as altera, é
   decisão arquitectural primeiro
   ([01-architecture.md](01-architecture.md)).

3. Apoiar-se nos testes existentes para apanhar regressões. Se a cobertura
   não permitir refactorizar com segurança, escrever primeiro testes de
   caracterização ([05-test.md](05-test.md)).

4. Manter o refactor **isolado** — sem o misturar com funcionalidade ou
   correcção no mesmo lote.

5. Voltar a passar a checklist de [04-review.md](04-review.md).

6. Actualizar [state/](../state/) só se o refactor mudar algo lá
   documentado.

## Refactors legítimos neste projecto

Os que corrigem anti-padrões herdados do protótipo
([state/known-issues.md](../state/known-issues.md)) — por exemplo, mover
passos de aprovação de um módulo para `approval`, ou consolidar storage de
ficheiros em `documents`.

Estes **não** são refactors puros: mudam ownership e fronteiras. Tratá-los
como alteração arquitectural, com ADR se não forem triviais.

## Refactors a evitar

- Extrair abstracção "para o caso de vir a ser preciso".
- Generalizar um motor de aprovações para além do âmbito fixado em ADR-007.
- Unificar conceitos que estão deliberadamente separados: Cargo vs. Perfil,
  Departamento vs. Centro de Custo (ADR-005), Orçamento vs. Previsão
  (ADR-006), os três tipos de contrato (ADR-009). **Parecerem duplicados não
  é razão para os fundir** — a separação é decisão registada.
