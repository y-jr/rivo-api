# Prompt: Implementar uma Funcionalidade

O prompt por omissão para implementar uma alteração concreta.

## Passos

1. **Identificar o módulo dono** —
   [domain/domain-map.md](../domain/domain-map.md), tabela de ownership de
   conceitos.
2. **Confirmar as regras de domínio** — ficheiro do módulo em
   [modules/](../modules/) e
   [domain/business-rules.md](../domain/business-rules.md) para regras
   transversais aplicáveis.
3. **Confirmar fronteiras** — a funcionalidade exige dependência não
   declarada em
   [architecture/dependency-rules.md](../architecture/dependency-rules.md)?
   Se sim, isso é decisão arquitectural primeiro
   ([01-architecture.md](01-architecture.md)).
4. **Confirmar ADRs** — [decisions/](../decisions/).
5. **Confirmar contra `docs/`** se houver qualquer dúvida.
6. **Implementar a menor alteração coerente** — sem abstracção
   especulativa, sem limpeza não relacionada no mesmo lote.
7. **Testes** — [standards/testing.md](../standards/testing.md). Invariantes
   de domínio testadas no domínio, sem base de dados.
8. **Actualizar estado** — [state/implemented.md](../state/implemented.md),
   [state/in-progress.md](../state/in-progress.md), e
   [state/known-issues.md](../state/known-issues.md) se ficou algum atalho
   ou defeito.

## Perguntar antes de escrever

Quando uma funcionalidade é pedida a partir da interface, perguntar
primeiro: **qual é a regra de negócio por trás disto?** O frontend não
determina sozinho a arquitectura do domínio.

Distinguir: concerns de UI, de aplicação, de domínio e de infraestrutura.

## Armadilhas frequentes neste projecto

- Precisar de "quem aprova isto" → é `approval`, resolvendo por **Cargo**
  (`hr`), nunca por Perfil de Acesso (`identity`).
- Precisar de nome ou departamento de uma pessoa → contrato
  `ReferenciaColaborador`, nunca `JOIN` a `hr`.
- Precisar de guardar um ficheiro → `documents` + tabela de ligação própria.
- Precisar de registar quem fez o quê → `audit`, nunca tabela própria.
- Precisar de calcular imposto → `fiscal`, nunca regra local.
- Precisar de executar um pagamento → `finance`/Tesouraria, e só com decisão
  aprovada revalidada no momento.
