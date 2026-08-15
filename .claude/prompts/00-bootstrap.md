# Prompt: Bootstrap

Usar ao iniciar uma sessão no Rivo, antes de qualquer outra coisa.

## Passos

1. Ler [CLAUDE.md](../CLAUDE.md) — regras não negociáveis e fonte de
   verdade.
2. Ler [state/project-state.md](../state/project-state.md) — estado actual.
3. Ler [state/in-progress.md](../state/in-progress.md) — há trabalho a
   retomar em vez de duplicar?
4. Ler [state/pending-decisions.md](../state/pending-decisions.md) — há
   decisão em aberto que bloqueia a tarefa?
5. Se a tarefa toca um módulo, ler o ficheiro desse módulo em
   [modules/](../modules/) e o
   [domain/domain-map.md](../domain/domain-map.md) **antes** de escrever
   código.
6. Confirmar contra `docs/` sempre que houver dúvida — é a fonte de verdade.
7. Prosseguir com o prompt adequado
   ([01](01-architecture.md)–[06](06-refactor.md)).

## Lembrete permanente

- `docs/` prevalece sobre `.claude/`. Se houver contradição, o ficheiro em
  `.claude/` está errado.
- Nunca editar `docs/`.
- Rotular sempre: **Facto**, **Inferência**, **Hipótese**, **Decisão em
  aberto**.
