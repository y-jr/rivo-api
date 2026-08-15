# ADR-003: Sem Multi-Tenancy na v1

## Status

Aceite (decisão D6, fechada pelo cliente)

## Context

O documento de produto (`docs/rivo-suite-descricao-modulos.md`) descreve o
Rivo como plataforma SaaS multi-tenant com isolamento por `tenant_id` e RLS,
e o protótipo confirmava-o (965 ocorrências de `tenant_id`).

O documento de requisitos do SGAP, em contrapartida, **não menciona
multi-tenancy uma única vez** — é escrito para uma organização única.

O cliente fechou esta questão: **v1 sem multi-tenancy**. Onde o documento de
produto diz o contrário, a decisão do cliente prevalece — colisão
explicitamente assinalada em `docs/rivo-dados-integracoes-seguranca-v1.md`.

## Requirements

- **Facto** — Âmbito de negócio actual: uma empresa.
- **Facto** — SGAP escrito para organização única.
- **Facto (colisão)** — Documento de produto descreve multi-tenant.
  Superado por decisão do cliente.

## Constraints

Decisão de negócio, não técnica. Não é reversível por configuração.

## Alternatives

1. **Sem multi-tenancy.**
2. Multi-tenancy por `tenant_id` + RLS (como o protótipo).
3. Modelar "preparado para multi-tenancy" sem o activar.

A opção 3 foi rejeitada: introduz o custo de multi-tenancy (propagação de
`tenant_id` por todo o modelo, repositórios e queries) sem qualquer
benefício presente, e cria falsa segurança — infraestrutura não exercitada
não funciona quando for precisa.

## Trade-offs

Simplicidade imediata contra custo alto de introdução futura. Dado que o
requisito não existe e não está previsto, o custo presente da opção 2 não é
justificável.

## Decision

Identidade e dados são de **uma empresa, sem partição por tenant**.

Não haverá:

- `TenantId` propagado pelo modelo de domínio.
- Partição de dados por tenant.
- Lógica de isolamento por tenant.
- Middleware de resolução de tenant.
- Repositórios ou filtros de base de dados com consciência de tenant.
- Configuração multi-empresa.

Isto **não** significa que todos os utilizadores tenham as mesmas
permissões. `identity` continua responsável pela autenticação e pelo
controlo de acesso, e cada módulo define as suas permissões específicas.

## Consequences

Facilita:

- Modelo de identidade, autorização e dados substancialmente mais simples.
- Sem risco de bugs de isolamento entre tenants.
- Desenvolvimento, teste e deployment mais simples.

Dificulta:

- O Rivo não pode servir várias empresas no mesmo deployment.
- Introduzir multi-tenancy mais tarde é alteração arquitectural
  significativa, não um interruptor.

**Consequência de segurança, importante:** sem fronteira de tenant, a
primeira linha de defesa passa a ser **inteiramente** a autorização por
perfil/cargo/processo. Já não há isolamento de dados por organização a
compensar uma falha de autorização. Isto eleva a exigência sobre a
segregação de funções (ADR-008) — ver
[standards/security.md](../standards/security.md) §"Nota sobre a ausência
de multi-tenancy".

## Risks

- Pressão comercial para servir uma segunda empresa antes de haver
  arquitectura para isso. Mitigação: esta decisão é explícita e o custo de a
  reverter está documentado.

## Revisit When

Existir requisito real de servir múltiplas empresas ou tenants. Nessa
altura, ADR dedicado que avalie o impacto em: identidade, autenticação,
autorização, persistência, ownership de dados por módulo, registos
existentes, APIs e isolamento de segurança.

**Não assumir que se introduz multi-tenancy acrescentando uma coluna de
tenant.**

## Related

- `docs/rivo-arquitetura-global-v1.md` §6, `…seguranca-v1.md` §3.6
- [ADR-002](adr-002-database.md), [ADR-008](adr-008-segregacao-funcoes.md)
- [modules/identity.md](../modules/identity.md)
