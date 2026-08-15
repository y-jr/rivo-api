# ADR-013: Credencial — JWT Bearer com Sessão Persistida

## Status

Aceite (2026-08-10)

## Context

ADR-012 escolheu ASP.NET Core Identity mas deixou o **esquema de credencial em
aberto** — cookie ou JWT bearer. Sem essa decisão não há login funcional.

Em paralelo, `docs/rivo-dados-integracoes-seguranca-v1.md` §1.2 modela
**Sessão** com IP e expiração, entidade que o ASP.NET Core Identity não
fornece.

As duas questões resolvem-se em conjunto, porque um JWT puro é
**irrevogável**: uma vez emitido, vale até expirar. Isso colide com o
requisito de "bloqueio técnico" herdado do SGAP.

## Requirements

- **Facto** — Frontend React separado (documento de produto).
- **Facto** — Auditoria regista IP (BR-9).
- **Facto** — Tentativas não autorizadas registadas, não só bloqueadas (BR-12).
- **Facto** — Sessões com expiração por inactividade; referência de 15 min
  para perfis decisórios.
- **Facto** — `docs` §1.2 modela Sessão com IP e expiração.

## Alternatives

1. **JWT bearer + sessão persistida** (escolhida).
2. Cookie de autenticação.
3. JWT puro, sem estado no servidor.

A opção 3 foi rejeitada: sem estado no servidor não há revogação. Um
utilizador despedido continuaria a poder aprovar pagamentos até o token
expirar.

A opção 2 é mais resistente a XSS (cookie `HttpOnly`), mas obriga a lidar com
CSRF e complica um frontend desacoplado. Continua viável se o contexto mudar.

## Trade-offs

JWT dá independência ao cliente e evita CSRF; em troca, o token fica exposto a
XSS no armazenamento do browser, e a revogação exige estado no servidor — que
é precisamente o que a Sessão traz.

Validar a sessão a cada pedido custa uma consulta indexada por chave primária.
Aceite: é o preço da revogação imediata.

## Decision

**JWT bearer**, com cada token ligado a uma **Sessão persistida**.

- O token transporta `sid` (identificador da sessão), `sub`, `email`, `jti` e
  os Perfis de Acesso como claims de role.
- `Session` é entidade de **Domain** — tem regras próprias (activa, expirada,
  revogada) e é livre de framework.
- Regista `IpAddress`, `UserAgent`, `CreatedAt`, `ExpiresAt`, `RevokedAt`.
- A cada pedido autenticado, `JwtBearerEvents.OnTokenValidated` confirma que a
  sessão continua activa. Assinatura válida **não basta**.
- `ClockSkew = TimeSpan.Zero` — sem os 5 minutos de tolerância por omissão.
- Sem chave estrangeira de `user_session` para `app_user`: a sessão é facto
  histórico e deve sobreviver à remoção lógica da conta, como a auditoria.

## Consequences

Facilita:

- Revogação imediata: terminar sessão invalida o token no pedido seguinte.
- Rasto de IP por sessão, para auditoria.
- Cliente desacoplado, sem CSRF.

Dificulta / exige:

- Uma consulta à base de dados por pedido autenticado.
- O token fica exposto a XSS no cliente — mitigação é responsabilidade do
  frontend.

## Risks

- **Expiração por inactividade não implementada.** Só existe expiração
  absoluta. Implementá-la exige actualizar a sessão a cada pedido (escrita por
  pedido) ou uma estratégia de janela. **Por decidir** — o requisito de 15 min
  para perfis decisórios ainda não está satisfeito.
- **Sem refresh token.** Expirada a sessão, o utilizador volta a autenticar-se.
  Aceitável agora; revisitar se a duração se revelar incómoda.
- **Chave de assinatura em `appsettings.Development.json`.** Aceitável em
  desenvolvimento; produção exige gestão de segredos.

## Revisit When

- A expiração por inactividade passar a ser exigida em auditoria.
- O custo da consulta por pedido se tornar mensurável.
- Surgir requisito de SSO, que provavelmente reabre também ADR-012.

## Related

- [ADR-004](adr-004-identity-auth-vs-authz.md),
  [ADR-012](adr-012-aspnet-core-identity.md)
- [modules/identity.md](../modules/identity.md),
  [standards/security.md](../standards/security.md)
