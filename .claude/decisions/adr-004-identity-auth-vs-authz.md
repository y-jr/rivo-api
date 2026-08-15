# ADR-004: Identidade — Autenticação é Infraestrutura, Autorização é Domínio

## Status

Aceite (decisão D1, fechada pelo cliente)

## Context

No protótipo, identidade estava fragmentada em três conceitos:
`profiles`+`user_roles` (utilizador autenticado), `employees` (registo
organizacional, **sem** FK para `auth.users`) e `client_users` (identidade
de portal).

Consequência: "quem está autenticado" e "quem é o colaborador/aprovador"
podiam não coincidir — um colaborador não tinha necessariamente utilizador.
Qualquer regra de segregação de funções depende de resolver esta ambiguidade
primeiro.

## Requirements

- **Facto** — Segregação de funções é regra de negócio explícita do SGAP.
- **Facto** — MFA obrigatório para aprovadores e Finanças (SGAP RNF-001).
- **Facto** — RBAC com 7 perfis no documento de produto, mas apenas 4 no
  código do protótipo (inconsistência a não herdar).

## Constraints

Sem multi-tenancy (ADR-003) — a autorização é a primeira linha de defesa.

## Alternatives

1. **Split: autenticação = infraestrutura; autorização = domínio
   partilhado.**
2. Identidade inteira como infraestrutura, delegada a um provider.
3. Identidade inteira como domínio, incluindo autenticação.

A opção 2 falha porque "que papéis pode esta pessoa acumular no mesmo
processo" é regra de negócio, não configuração de provider. A opção 3
constrói infraestrutura sem valor — autenticação é problema resolvido.

## Trade-offs

O split obriga a manter uma fronteira clara entre o que é delegável e o que
não é. Em troca, permite trocar de provider de autenticação sem tocar em
regras de negócio.

## Decision

**Autenticação = infraestrutura.** "É mesmo esta pessoa?" não tem regras de
negócio. Delegável a um provider.

**Autorização/RBAC = domínio partilhado.** `identity` é bounded context
próprio, dono de Utilizador, Perfil de Acesso, Atribuição de Perfil e
Sessão.

Um Colaborador (`hr`) pode existir **sem** utilizador associado. A ligação
é opcional e explícita.

## Consequences

Facilita:

- Provider de autenticação substituível sem impacto no domínio.
- Ambiguidade "utilizador vs. colaborador" resolvida por desenho.

Dificulta / exige:

- Manter a fronteira: `identity` **não** modela Cargo nem estrutura
  organizacional (ADR-005).
- Fluxos que precisam de "a pessoa por trás do login" têm de resolver
  utilizador → colaborador explicitamente.

## Risks

- **Deriva:** a autorização atrair dados organizacionais e tornar-se um
  segundo `hr`. Mitigação: ADR-005 fixa Cargo em `hr` e proíbe-o em
  `identity`.

## Revisit When

- Surgir requisito de federação de identidade ou SSO empresarial que altere
  o que é delegável.
- Os 7 perfis se revelarem insuficientes e for preciso autorização por
  operação, não por módulo.

## Related

- `docs/rivo-arquitetura-global-v1.md` §4,
  `…seguranca-v1.md` §1.2, §3.1, §3.2
- [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-005](adr-005-modelo-organizacional.md),
  [ADR-008](adr-008-segregacao-funcoes.md)
- [modules/identity.md](../modules/identity.md)
