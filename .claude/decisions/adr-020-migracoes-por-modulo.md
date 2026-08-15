# ADR-020: Migrações Independentes por Módulo

## Status

Aceite (2026-08-15).

**Registo retroactivo.** Implementado em 2026-08-10 com `identity` e replicado
nos quatro módulos seguintes.

## Context

O ADR-002 fixou "um schema lógico por domínio, ownership exclusivo de tabela",
mas não disse como o esquema chega à base de dados.
[technology-decisions.md](../architecture/technology-decisions.md) registava a
pergunta em aberto na forma exacta: *"tooling de migrações, e como manter
migrações independentes por módulo"*.

A segunda metade é a que interessa. Migrações EF Core é a resposta óbvia; o
problema real é que, por omissão, **o EF Core guarda o histórico de migrações
numa única tabela `__EFMigrationsHistory`**. Cinco módulos a partilhar essa
tabela seriam cinco módulos acoplados no arranque: qualquer um veria as
migrações dos outros, e a ordem passaria a ser global.

## Requirements

- **Facto** — Ownership exclusivo de schema por módulo (ADR-002).
- **Facto** — Um `DbContext` por módulo (ADR-019).
- **Facto** — `docker compose up` tem de deixar o ambiente utilizável num só
  comando (ver ADR-021).
- **Facto** — As suites de verificação correm a partir de
  `docker compose down -v`, ou seja, de base de dados vazia.
- **Inferência** — Serão catorze módulos, desenvolvidos em alturas
  diferentes. Um módulo novo não pode obrigar a re-gerar migrações de outro.

## Constraints

- Em produção, várias instâncias da aplicação competiriam pelo mesmo schema se
  migrassem no arranque.
- O seed de `identity` atribui permissões declaradas por outros módulos
  (ADR-014, ADR-017), logo depende dos schemas desses módulos já existirem.

## Alternatives

1. **Migrações EF Core por módulo, com tabela de histórico própria em cada
   schema** (escolhida).
2. Migrações EF Core partilhadas num único `DbContext` e assembly.
3. Ferramenta de migração independente do ORM (DbUp, Flyway, Liquibase), com
   SQL escrito à mão.

A opção 2 é a que o EF Core faz por omissão e é a mais simples até ao segundo
módulo. Rejeitada por acoplar os módulos no arranque e por tornar a ordem das
migrações global — exactamente a erosão de fronteiras que o ADR-017 combate ao
nível do compilador.

A opção 3 dá controlo total sobre o SQL e desacopla das convenções do EF Core.
Rejeitada por acrescentar uma ferramenta e um segundo modelo mental quando o
EF Core, correctamente configurado, já satisfaz o requisito.

## Decision

**Migrações EF Core, um conjunto por módulo, com histórico próprio.**

### Cada módulo tem a sua tabela de histórico, no seu schema

```csharp
npgsql.MigrationsHistoryTable("__ef_migrations_history", HrDbContext.Schema)
```

**É isto que torna a independência real.** O histórico de `hr` vive em
`hr.__ef_migrations_history`; o de `audit` em `audit.__ef_migrations_history`.
Nenhum módulo vê as migrações de outro, e dois módulos podem evoluir o seu
esquema sem coordenação.

### As migrações vivem no módulo

`src/Modules/<Módulo>/Rivo.<Módulo>.Infrastructure/Persistence/Migrations/`.
Nunca num projecto central de migrações.

```bash
dotnet ef migrations add <Nome> \
  --project src/Modules/Hr/Rivo.Hr.Infrastructure \
  --startup-project src/Rivo.Api \
  --context HrDbContext
```

### A FK entre schemas declara-se em SQL na migração

O único caso permitido pelo ADR-010 (`hr.employee_document →
documents.document`) não pode ser expresso por navegação de EF, porque a
entidade do outro lado pertence a outro módulo (ADR-017). Declara-se com SQL
directo dentro da migração de `hr`.

**Consequência aceite:** cria uma ordem implícita de aplicação — o schema
`documents` tem de existir antes. Ver Riscos.

### Aplicação no arranque: só em `Development`

Cada módulo expõe `InitialiseXModuleAsync()`, chamado por `Program.cs` dentro
de `if (app.Environment.IsDevelopment())`.

**A restrição a `Development` é a parte importante da decisão.** Em produção,
migrar automaticamente no arranque é perigoso por duas razões independentes:
várias instâncias competiriam pelo mesmo schema, e uma migração destrutiva
correria sem ninguém a aprovar.

### A ordem de inicialização é explícita e tem razão de ser

```
audit → documents → notifications → hr → identity
```

`identity` vem **por último**: o seu seed de Perfis de Acesso atribui
permissões declaradas por `audit`, `hr` e `documents` (ADR-014, ADR-017), cujos
schemas têm de existir primeiro.

## ⚠ Decisão deliberadamente não tomada aqui

**Produção não tem caminho de migração.** Este ADR fecha o "como" em
desenvolvimento e fixa que produção **não** usa o arranque. Não escolhe o que
usa em vez disso.

A resolução prevista é `dotnet ef migrations bundle` como passo próprio de
pipeline, com aprovação antes de correr — mas isso depende da infraestrutura
de produção, que não está decidida. Continua registado em
[state/pending-decisions.md](../state/pending-decisions.md).

## Consequences

Facilita:

- Um módulo novo traz as suas migrações e não toca em nenhum outro.
- `docker compose up -d --build` deixa o ambiente utilizável sem passo manual.
- As suites de verificação podem partir de base de dados vazia e ser
  reprodutíveis.

Dificulta / exige:

- Cada módulo repete o bloco de configuração do histórico. Duplicação aceite.
- Os comandos `dotnet ef` exigem sempre `--project` e `--context` explícitos;
  não há um contexto por omissão.
- A ordem de inicialização em `Program.cs` é conhecimento implícito.

## Risks

- **A ordem de inicialização não é imposta por nada.** Um módulo novo inserido
  na posição errada — ou `identity` deixado de não ser o último — falha no
  arranque com um erro de base de dados que não diz que o problema é de ordem.
  **É o risco mais provável desta decisão.** Mitigação possível: declarar
  dependências entre inicializadores em vez de confiar na ordem das chamadas.
- **A FK entre schemas cria acoplamento de ordem** entre `hr` e `documents`,
  que hoje está satisfeito por acidente da ordem escolhida. Se `documents`
  passasse para depois de `hr`, a migração falharia.
- **Divergência entre migração e modelo** se alguém alterar o esquema à mão na
  base de dados. Detecta-se ao gerar a migração seguinte, tarde demais.

## Revisit When

- A infraestrutura de produção for escolhida — obriga a fechar o caminho de
  migração que este ADR deixa em aberto.
- Um módulo precisar de uma migração que dependa do schema de outro para além
  do caso já previsto de FK.
- O número de módulos tornar a ordem de inicialização difícil de raciocinar.

## Related

- [ADR-002](adr-002-database.md), [ADR-010](adr-010-referencia-entre-contextos.md),
  [ADR-014](adr-014-rbac-permissoes.md),
  [ADR-016](adr-016-bootstrap-autoridade.md) §R2,
  [ADR-017](adr-017-contratos-por-modulo.md),
  [ADR-019](adr-019-persistencia-ef-core.md),
  [ADR-021](adr-021-ambiente-local-docker.md)
- [standards/persistence.md](../standards/persistence.md)
