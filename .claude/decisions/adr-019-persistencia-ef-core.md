# ADR-019: Persistência — EF Core e Convenções de Mapeamento

## Status

Aceite (2026-08-15).

**Registo retroactivo.** O ORM foi decidido implicitamente pelo ADR-012 e as
convenções de mapeamento foram estabelecidas em 2026-08-10, ao montar
`identity`. Este ADR torna ambos explícitos.

## Context

O ADR-012 escolheu ASP.NET Core Identity e observou, numa linha final, que
"esta decisão fecha implicitamente o ORM: **EF Core**".

Uma decisão fechada implicitamente é uma decisão que ninguém pode citar. Pior:
[technology-decisions.md](../architecture/technology-decisions.md) continuou a
listar "ORM / abordagem de acesso a dados" como em aberto, o que contradizia o
código.

E as **convenções de mapeamento** — que são o que garante o ownership
exclusivo de schema do ADR-002 — nunca foram registadas em lado nenhum, apesar
de estarem aplicadas de forma idêntica nos cinco módulos.

## Requirements

- **Facto** — PostgreSQL, um schema lógico por domínio, ownership exclusivo de
  tabela (ADR-002).
- **Facto** — Chaves primárias UUID, valores monetários em `numeric`,
  concorrência optimista por coluna `version` (ADR-002).
- **Facto** — Um módulo nunca lê tabelas de outro (ADR-010, ADR-017).
- **Facto** — A única FK entre schemas permitida aponta para a chave primária
  do contexto dono (ADR-010).
- **Facto** — O `Domain` não pode conhecer framework.
- **Inferência** — A base de dados pode não estar pronta quando a aplicação
  arranca, tanto em Docker como em produção com failover.

## Constraints

- O modelo de dados de `identity` é imposto pelo ASP.NET Core Identity
  (ADR-012); adapta-se por configuração, não por modelação livre.
- O PostgreSQL dobra identificadores não citados para minúsculas.

## Alternatives

1. **EF Core** (escolhida — na prática, imposta pelo ADR-012).
2. Dapper, ou SQL directo.
3. EF Core em `identity`, Dapper nos restantes módulos.

A opção 2 dá controlo total sobre o SQL e nenhuma surpresa de tradução. Foi
posta de lado porque o ADR-012 já obriga a EF Core em `identity`, e ter um
único ORM vale mais do que o ganho marginal.

A opção 3 é a pior das três: dois modelos de acesso a dados, duas formas de
escrever repositórios, duas curvas de aprendizagem, e a fronteira entre elas a
ser decidida caso a caso para sempre.

## Trade-offs

EF Core traz migrações, tracking e um modelo declarativo. Em troca, esconde o
SQL gerado — e num domínio financeiro, uma consulta que degrada em silêncio é
um problema real. Mitiga-se com índices declarados de propósito para as
consultas conhecidas, como já se faz em `hr`.

## Decision

**EF Core com Npgsql.** Convenções vinculativas:

### Um `DbContext` por módulo, dono de exactamente um schema

```csharp
public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    public const string Schema = "hr";

    protected override void OnModelCreating(ModelBuilder builder)
        => builder.HasDefaultSchema(Schema);
}
```

O schema é declarado como `public const string` no próprio contexto, e é essa
constante que alimenta o `HasDefaultSchema` e a tabela de histórico de
migrações (ADR-020). **Não há um `DbContext` partilhado**, e nenhum contexto
declara `DbSet` de entidade que não seja do seu módulo.

### `snake_case`, imposto por convenção e não à mão

`UseSnakeCaseNamingConvention()` (pacote `EFCore.NamingConventions`), em todos
os módulos. Os nomes PascalCase ficariam permanentemente dependentes de aspas
no PostgreSQL — o mesmo raciocínio já aceite no ADR-012 §Decision.

Tabelas nomeadas explicitamente com `ToTable("...")`, no **singular**:
`employee`, `position_assignment`, `audit_event`.

### Chaves primárias: UUID **versão 7**

`Guid.CreateVersion7()`, não `Guid.NewGuid()`.

O ADR-002 fixou "UUID" sem escolher versão. A escolha importa: um UUIDv4 é
aleatório, e como chave primária espalha as inserções por todo o índice
B-tree, provocando divisões de página e perda de localidade. O UUIDv7 é
ordenado no tempo — mantém as inserções recentes juntas, que é o padrão de
escrita de praticamente todas as tabelas transaccionais do Rivo.

**Esta decisão estende o ADR-002; não o contradiz.**

### Enums persistidos como texto

`HasConversion<string>()` com `HasMaxLength`. Nunca como inteiro: um número na
base de dados é ilegível em diagnóstico, e reordenar o `enum` em código
corromperia silenciosamente todas as linhas existentes.

### Nenhuma navegação atravessa a fronteira de módulo

FKs internas declaram-se por `HasOne<T>().WithMany().HasForeignKey(...)`, sem
propriedade de navegação, com `DeleteBehavior.Restrict`.

A **FK entre schemas** permitida pelo ADR-010 declara-se em **SQL directo na
migração**, nunca por navegação de EF: a entidade do outro lado pertence a
outro módulo e não pode ser referenciada a partir daqui (ADR-017). É o caso de
`hr.employee_document → documents.document`.

### Resiliência de ligação

`EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: 5s)` em todos os
módulos. O `depends_on` do Compose só vale no `up`, não no `restart`, e em
produção há failover.

### Os repositórios são portas

A Application define a porta (`IHrStore`, `IDocumentStorage`); a
Infrastructure implementa-a com EF Core. O `Domain` não conhece `DbContext`.

## ⚠ Desvio conhecido ao ADR-002

**A coluna `version` de concorrência optimista não está implementada em
nenhuma entidade.** Nenhum agregado declara token de concorrência.

Não é omissão silenciosa — é registada aqui e em
[state/known-issues.md](../state/known-issues.md) (K14). A razão é que nenhum
dos cinco agregados implementados tem contenção real de escrita concorrente.

**Deixa de ser aceitável em `approval`:** BR-17 exige explicitamente
concorrência optimista nas decisões, e é o cenário clássico — duas pessoas a
decidir o mesmo pedido em simultâneo. Implementar aí, e retroactivamente onde
a contenção aparecer.

## Consequences

Facilita:

- Ownership de schema garantido por construção: um módulo não consegue mapear
  a tabela de outro sem que isso seja visível.
- Migrações e modelo declarativo sem tooling adicional.
- O `Domain` mantém-se livre de framework, testável sem base de dados.

Dificulta / exige:

- **Não há transacção entre módulos.** Contextos distintos são transacções
  distintas — é a causa directa do K10 (a trilha de auditoria e a operação
  auditada não são atómicas). Aceite; o padrão outbox resolve-o se o volume o
  exigir.
- O SQL gerado tem de ser vigiado nas consultas quentes.
- Cada módulo novo repete o bloco de registo do `DbContext`. É duplicação
  aceite, em troca de independência.

## Risks

- **Consulta a degradar em silêncio** à medida que os dados crescem.
  Detecta-se com observabilidade de base de dados, que ainda não existe.
- **Tentação de um `DbContext` partilhado** quando surgir a primeira consulta
  que atravessa módulos. É a porta de entrada da erosão de fronteiras;
  a resposta correcta é um read model ou um contrato, nunca um contexto comum.
- **Divergência de convenções entre módulos** à medida que a equipa cresce.
  Mitigação: teste de arquitectura que verifique `HasDefaultSchema` e
  `UseSnakeCaseNamingConvention` em todos os contextos.

## Revisit When

- Uma consulta crítica não for exprimível em EF Core com desempenho aceitável
  — a resposta é SQL directo nesse repositório, não trocar de ORM.
- Aparecer contenção de escrita concorrente num agregado (fecha o desvio
  acima).
- O volume exigir particionamento ou estratégias que o EF Core não modele.

## Related

- [ADR-002](adr-002-database.md) — estende-o quanto à versão do UUID; regista
  desvio quanto à coluna `version`
- [ADR-010](adr-010-referencia-entre-contextos.md),
  [ADR-012](adr-012-aspnet-core-identity.md),
  [ADR-017](adr-017-contratos-por-modulo.md),
  [ADR-020](adr-020-migracoes-por-modulo.md)
- [standards/persistence.md](../standards/persistence.md)
