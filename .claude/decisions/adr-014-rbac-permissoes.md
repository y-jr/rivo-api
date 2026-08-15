# ADR-014: Autorização — RBAC com Permissões como Role Claims

## Status

Aceite (2026-08-10)

## Context

ADR-005 fixou Perfil de Acesso (`identity`) como distinto de Cargo (`hr`).
ADR-012 escolheu ASP.NET Core Identity. Faltava o modelo de autorização: como
se liga um utilizador a um perfil, um perfil a permissões, e como se verifica
tudo isso num pedido.

O protótipo tinha quatro papéis em código contra sete no documento de
produto, e a verificação real só no frontend.

## Requirements

- **Facto** — Sete Perfis de Acesso no documento de produto.
- **Facto** — Toda a verificação de autorização no servidor, nunca só na
  interface.
- **Facto** — Perfil ≠ Cargo (ADR-005).
- **Facto** — Sem multi-tenancy (ADR-003).
- **Inferência** — Os módulos de negócio vão declarar permissões próprias
  quando existirem.

## Constraints

- Sem ABAC, sem OAuth/OIDC externo, sem serviços adicionais.
- Sem abstracções especulativas.

## Alternatives

### Onde guardar as permissões

1. **Claims de perfil em `app_role_claim`** (escolhida).
2. Tabelas `permission` + `role_permission` próprias.
3. Só perfis, sem permissões — `[Authorize(Roles = "Admin")]`.

A opção 3 foi rejeitada: espalha nomes de perfil pelos endpoints e obriga a
tocar em código sempre que a política de acesso muda.

A opção 2 foi rejeitada por duplicar a fonte de verdade. O catálogo tem de
existir em código de qualquer forma — as policies referenciam-no —, e ter
também uma tabela cria duas listas que podem divergir. `app_role_claim` já
existe e guarda o que varia: **que perfil tem que permissão**.

### Onde resolver as permissões

1. **No login, para dentro do token** (escolhida).
2. A cada pedido, por consulta à base de dados.

## Decision

```
User ──(app_user_role)──> Role ──(app_role_claim)──> Permission ──> Policy
```

- **Permissão = claim de tipo `permission`** num perfil. **Zero tabelas
  novas.**
- **Catálogo em código:** `Permissions` e `AccessProfiles` em `Application`.
  Acrescentar uma permissão é mexer num sítio só.
- **Resolvidas no login** e transportadas no JWT. A verificação por pedido é
  comparação de claims em memória, sem tocar na base de dados.
- **Uma policy por permissão**, registada no arranque a partir do catálogo.
  O endpoint declara `.RequireAuthorization("identity.roles.read")`.
- **Nenhuma verificação dentro dos handlers.** Se o pedido chega ao handler,
  já está autorizado.

### Permissões nesta fase

Apenas o que existe para proteger: `identity.users.read`,
`identity.roles.read`, `identity.roles.assign`.

**Só o Admin as recebe.** Os outros seis perfis são criados vazios: não há
módulos de negócio para autorizar, e inventar permissões seria adivinhar.
Cada módulo declarará o seu catálogo quando for implementado.

### Seed

Idempotente por construção: cria o que falta, não toca no que existe, **nunca
remove**. Uma remoção acidental do catálogo em código deixaria utilizadores
sem acesso sem ninguém dar por isso.

**Não cria utilizadores** — é a separação face a dados de negócio, e evita
password administrativa em código.

## Consequences

Facilita:

- Autorização declarativa, legível no endpoint.
- Nenhuma consulta extra por pedido.
- Acrescentar permissões não exige migração de schema.

Dificulta / exige:

- **Alterar permissões de um perfil só se reflecte no login seguinte.**
  Mitigável revogando a sessão (ADR-013).
- O token cresce com o número de permissões. Irrelevante à escala actual;
  revisitar se um perfil vier a acumular centenas.

## Risks

- **Nenhum utilizador nasce Admin.** O seed não cria contas, logo o primeiro
  Admin precisa de atribuição fora de banda. Documentado no README como passo
  manual. **Mecanismo de bootstrap por decidir** — ver
  [pending-decisions](../state/pending-decisions.md).
- **Permissões obsoletas no token** durante a vida da sessão. Aceite: a
  janela é a duração da sessão, e existe revogação.
- **Catálogo em código pode divergir das atribuições em base de dados** se
  uma permissão for renomeada. Renomear exige migração de dados deliberada.

## Revisit When

- Um módulo precisar de autorização dependente de dados (ex.: "só o
  requisitante pode alterar a sua requisição") — isso é ABAC e este modelo
  não o cobre.
- O número de permissões por perfil tornar o token grande.
- Surgir necessidade de gerir permissões em runtime, o que reabriria a
  alternativa da tabela própria.

## Related

- [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-004](adr-004-identity-auth-vs-authz.md),
  [ADR-005](adr-005-modelo-organizacional.md),
  [ADR-012](adr-012-aspnet-core-identity.md),
  [ADR-013](adr-013-jwt-bearer-e-sessao.md)
- [modules/identity.md](../modules/identity.md)
