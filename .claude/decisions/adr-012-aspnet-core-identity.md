# ADR-012: Autenticação — ASP.NET Core Identity

## Status

Aceite (2026-08-10)

## Context

ADR-004 fixou o split autenticação/autorização mas deixou o **mecanismo
concreto de autenticação em aberto**, listado em
[state/pending-decisions.md](../state/pending-decisions.md).

Com o início da implementação, a decisão passou a ser bloqueante: não é
possível montar o módulo `identity` sem escolher.

## Requirements

- **Facto** — Autenticação individual, sem contas partilhadas.
- **Facto** — MFA obrigatório para perfis com poder de aprovação ou execução
  financeira.
- **Facto** — Password com hash forte, nunca reversível.
- **Facto** — Sessões com expiração por inactividade.
- **Facto** — RBAC por Perfil de Acesso (ADR-005), distinto de Cargo.
- **Facto** — Chaves substitutas UUID (ADR-002).
- **Facto** — Um schema por domínio (ADR-002).
- **Facto** — Backend em C#/.NET.

## Constraints

- Sem multi-tenancy (ADR-003).
- Autenticação é infraestrutura, delegável; autorização é domínio (ADR-004).

## Alternatives

1. **ASP.NET Core Identity.**
2. Provider externo (Auth0, Entra ID, Keycloak).
3. Implementação própria.

A opção 3 está fora de questão: construir gestão de credenciais de raiz é
risco de segurança sem contrapartida.

A opção 2 é viável e continua a sê-lo no futuro — ADR-004 desenhou a
autenticação precisamente para ser substituível. Rejeitada agora por
acrescentar dependência externa, custo e latência de integração numa fase em
que o requisito é uma empresa única.

## Trade-offs

ASP.NET Core Identity traz hashing, lockout, tokens, MFA e confirmação de
e-mail sem código próprio. Em troca, impõe o seu modelo de dados
(`IdentityUser`, `IdentityRole`) e ainda não modela sessão como entidade —
ver Riscos.

## Decision

**ASP.NET Core Identity**, com EF Core sobre PostgreSQL.

Configuração vinculativa:

- **Chaves `Guid`**, não `string` — ADR-002. Implica
  `IdentityUser<Guid>`/`IdentityRole<Guid>`.
- **Schema `identity`**, com `HasDefaultSchema` — ADR-002.
- **Tabelas em snake_case** (`app_user`, `app_role`, `app_user_role`, …).
  Os nomes PascalCase do Identity ficariam permanentemente dependentes de
  aspas no PostgreSQL, que dobra identificadores não citados para
  minúsculas.
- **`IdentityRole` = Perfil de Acesso** (ADR-005). **Cargo não entra aqui** —
  é de `hr`.
- `AddIdentityCore`, não `AddIdentity` — este último regista autenticação por
  cookie, e o esquema de credencial ainda não está decidido.

Esta decisão fecha implicitamente o ORM: **EF Core**, por ser o store nativo
do Identity.

## Consequences

Facilita:

- Hashing, lockout, tokens de recuperação e MFA disponíveis sem código
  próprio.
- Integração natural com a autorização do ASP.NET Core.

Dificulta / exige:

- O modelo de dados é imposto pelo framework; adaptações fazem-se por
  configuração, não por modelação livre.
- `ApplicationUser` herda de um tipo do framework e por isso vive em
  `Infrastructure`, não em `Domain` — o Domain tem de permanecer livre de
  framework
  ([dependency-rules.md](../architecture/dependency-rules.md)).

## Risks

- **Sessão não modelada.** `docs/rivo-dados-integracoes-seguranca-v1.md`
  §1.2 modela Sessão com IP e expiração. O ASP.NET Core Identity não a
  modela como entidade. **Por reconciliar** — ver decisões pendentes.
- **Esquema de credencial por decidir** — cookie vs. JWT bearer. Enquanto não
  for decidido, não há login funcional. Registado como pendente; `AddIdentityCore`
  foi escolhido para não pré-decidir.
- **Substituição futura por provider externo** continua possível, mas o custo
  cresce com o volume de utilizadores criados entretanto.

## Revisit When

- Surgir requisito de SSO empresarial ou federação de identidade.
- O Rivo evoluir para SaaS multi-empresa (reabre também ADR-003).

## Related

- [ADR-002](adr-002-database.md), [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-004](adr-004-identity-auth-vs-authz.md),
  [ADR-005](adr-005-modelo-organizacional.md)
- [modules/identity.md](../modules/identity.md)
