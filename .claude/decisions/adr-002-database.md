# ADR-002: Base de Dados — PostgreSQL com Schema por Domínio

## Status

Aceite.

> **Nota (2026-08-10):** SQL Server e MySQL foram considerados durante esta
> fase e **PostgreSQL foi mantido**. Achados relevantes dessa avaliação
> ficam registados em §Alternatives, para não terem de ser redescobertos.

## Context

O Rivo é um monólito modular (ADR-001) em que cada módulo tem de possuir os
seus dados em exclusivo, sem que a partilha de infraestrutura de base de
dados dissolva as fronteiras.

O protótipo demonstrou o risco: 211 tabelas sem separação lógica clara,
conceitos duplicados por módulo, e acoplamento acidental via tabelas
partilhadas.

## Requirements

- **Facto** — Transaccionalidade forte no ponto Approval → Tesouraria.
- **Facto** — Auditoria append-only, retenção mínima de 10 anos.
- **Facto** — Concorrência controlada em decisões e execuções de pagamento.
- **Facto** — Sem eliminação física de dados sujeitos a auditoria ou
  retenção legal.
- **Facto** — Multi-moeda (AOA, USD, EUR).

## Constraints

- Empresa única, sem multi-tenancy (ADR-003) — não há `tenant_id`.
- PostgreSQL já previsto no documento de produto.

## Alternatives

1. **Uma base de dados, um schema lógico por domínio.**
2. Uma base de dados, um único schema com convenção de nomes por módulo.
3. Uma base de dados por módulo.

A opção 2 foi rejeitada: convenção de nomes não impede tecnicamente um
`JOIN` cruzado — é exactamente o que falhou no protótipo. A opção 3 foi
rejeitada por contradizer ADR-001, eliminar a transacção local em
Approval→Tesouraria, e ter custo operacional sem requisito.

### Outros motores avaliados (2026-08-10)

| Motor | Achado |
|---|---|
| **SQL Server** | Viável. Schema por domínio equivalente; `rowversion` melhor do que coluna `version` gerida à mão. Custos: licenciamento em todos os ambientes (incluindo CI e teste); GUID aleatório em índice agrupado exige mitigação; sem equivalente a `jsonb` |
| **MySQL 8.0+ / InnoDB** | Viável com reservas sérias. **Não tem RLS** — elimina a segunda linha de defesa de ADR-008 e, combinado com ADR-003 (sem multi-tenancy), deixaria a autorização no domínio como **única** barreira. Além disso: sem DDL transaccional (migração falhada não faz rollback), InnoDB agrupa sempre pela PK (UUID exige `BINARY(16)` ordenado por tempo), e o provider EF Core é comunitário (Pomelo) |

**PostgreSQL mantido.** Preserva RLS como defesa em profundidade — que é o
que ADR-008 pressupõe —, `jsonb`, DDL transaccional e ausência de custo de
licenciamento.

## Trade-offs

Schema por domínio dá separação verificável (permissões por schema) mantendo
transacções locais. Custo: exige disciplina explícita sobre FKs entre
schemas — resolvido em ADR-010.

## Decision

**Um PostgreSQL. Um schema lógico por domínio, com ownership exclusivo de
tabela.**

Princípios de modelação vinculativos:

- Sem `tenant_id`, sem partição multi-tenant.
- **Chaves substitutas UUID** em todas as entidades — evita expor sequências
  previsíveis e facilita extracção futura.
- **Concorrência optimista** (coluna `version` ou verificação de estado
  antes de escrever) em qualquer entidade decidida por mais do que uma
  pessoa.
- **Sem eliminação física** em entidades sujeitas a auditoria ou retenção
  legal — apenas anulação lógica, auditada.
- **Trilha de auditoria por referência**, nunca duplicada por domínio.
  Todos escrevem para `audit`.
- **Correlation ID** propagado por pedido para logs e auditoria.

Schemas: `identity`, `hr`, `payroll`, `finance`, `procurement`,
`commercial`, `approval`, `fiscal`, `projects`, `fleet`, `inventory`,
`documents`, `notifications`, `audit`.

## Consequences

Facilita:

- Ownership verificável tecnicamente, não só por convenção.
- Transacção local onde a consistência forte é exigida.
- Uma base de dados para provisionar, salvaguardar e migrar.

Dificulta / exige:

- Regras explícitas para FK entre schemas (ADR-010).
- Migrações organizadas para que um módulo não obrigue a tocar noutro.
- Nenhum acesso cruzado a tabelas — todo o acesso a dados de outro contexto
  passa por contrato publicado.

## Risks

- **Erosão por `JOIN` cruzado.** Mitigação: permissões ao nível do schema e
  revisão; testes de arquitectura quando a stack estiver fechada.
- **Extracção futura** de um módulo para base de dados própria exige
  converter FKs entre schemas em identificadores simples — alteração
  localizada por desenho (ADR-010), não remodelação.

## Revisit When

- Um módulo precisar de perfil de carga ou de disponibilidade
  incompatível com a instância partilhada.
- Surgir requisito de multi-tenancy (reabre também ADR-003).

## Related

- `docs/rivo-dados-integracoes-seguranca-v1.md` §1.1
- [ADR-001](adr-001-architecture-style.md),
  [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-010](adr-010-referencia-entre-contextos.md)
- [standards/persistence.md](../standards/persistence.md)
