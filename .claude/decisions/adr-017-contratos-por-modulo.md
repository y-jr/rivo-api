# ADR-017: Assembly de Contratos por Módulo

## Status

Aceite (2026-08-11)

## Context

ADR-015 §R1 identificou que `hr ↔ approval` é dependência mútua e que em .NET
uma referência circular de projectos não compila. Ficou registado que a
resolução — assemblies de contratos — devia ser decidida **antes** de
implementar o segundo módulo.

Com `audit` a nascer, chegou esse momento.

`module-boundaries.md` já dizia que a fronteira pública de um módulo é
composta apenas por interfaces de serviço, DTOs e contratos de evento. Mas
era regra documentada, não imposta: nada impedia um módulo de referenciar o
`Application` de outro e usar-lhe tudo.

O protótipo mostrou o que acontece a regras só documentadas.

## Requirements

- **Facto** — `hr ↔ approval` terá dependência mútua (ADR-015).
- **Facto** — A fronteira pública é só contratos (module-boundaries.md).
- **Inferência** — Serão catorze módulos; a disciplina tem de escalar sem
  depender de vigilância em revisão.

## Alternatives

1. **Assembly `Rivo.X.Contracts` por módulo** (escolhida).
2. Consumidores referenciam `Rivo.X.Application` directamente.
3. Só eventos na direcção inversa, para quebrar ciclos.

A opção 2 não compila no caso `hr ↔ approval`, e expõe toda a camada
Application como se fosse pública.

A opção 3 resolve metade: `hr → approval` pode ser evento, mas
`approval → hr` (resolver Cargo) é leitura síncrona e continua a precisar de
referência.

## Decision

Cada módulo publica **`Rivo.X.Contracts`** — assembly **sem dependências**,
contendo apenas:

- interfaces de serviço aplicacional;
- DTOs de pedido e resposta;
- contratos de evento;
- catálogo de permissões do módulo.

Consumidores referenciam **apenas** o assembly de contratos. Nunca
`Application`, `Domain` ou `Infrastructure` de outro módulo.

Como os contratos não dependem de nada, `A → B.Contracts` e
`B → A.Contracts` **não formam ciclo**.

### Quando se cria

Um módulo ganha `Contracts` **quando tem consumidor**. `audit` tem um
imediatamente (`identity`); `identity` não tem nenhum e por isso não o
recebe agora.

Criar contratos para módulos sem consumidores seria construir superfície
pública para ninguém.

### Consequência sobre o catálogo de permissões

O catálogo de permissões de cada módulo vive nos seus contratos, porque
`identity` precisa de o ler para decidir que perfis o recebem — é dono do
Perfil de Acesso (ADR-005).

Cada módulo declara **que permissões existem**; `identity` decide **quem as
tem**.

## Consequences

Facilita:

- A fronteira pública passa a ser imposta pelo compilador, não por revisão.
- Ciclos entre módulos deixam de ser possíveis por construção.
- Ver o que um módulo expõe é abrir um projecto pequeno.

Dificulta / exige:

- Um projecto extra por módulo consumido.
- Alterar um contrato tem custo visível — é a intenção.

## Risks

- **Contratos a engordar** até serem a Application inteira. Mitigação: cada
  adição é revista contra module-boundaries.md; um contrato que exponha
  entidades de domínio é defeito.
- **Tentação de referenciar `Application` directamente** por conveniência.
  Mitigação: acrescentar à checklist de
  [prompts/04-review.md](../prompts/04-review.md).

## Revisit When

- O número de projectos se tornar incomportável para o tooling.
- Surgir necessidade de versionar contratos independentemente.

## Related

- [ADR-015](adr-015-atribuicao-cargo.md) §R1,
  [ADR-014](adr-014-rbac-permissoes.md)
- [architecture/module-boundaries.md](../architecture/module-boundaries.md),
  [architecture/dependency-rules.md](../architecture/dependency-rules.md)
