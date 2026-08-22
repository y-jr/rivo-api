# ADR-029: SQL Server em vez de PostgreSQL

## Status

Aceite (2026-08-20).

**Substitui a escolha de motor do [ADR-002](adr-002-database.md).** Tudo o
resto do ADR-002 — um schema lógico por domínio, ownership exclusivo de
tabela, sem FK entre schemas excepto para a chave primária do contexto dono —
mantém-se intacto e é o que torna esta troca possível sem redesenhar nada.

## Context

O Rivo passa a correr numa VPS, contra uma instância de **SQL Server já
existente e já operada** — a mesma que serve outros sistemas da organização.
Não é uma preferência técnica: é a infraestrutura que existe, com backups,
monitorização e um administrador.

O ADR-002 escolheu PostgreSQL por razões que continuam válidas em abstracto
(JSON nativo, custo, ecossistema). Nenhuma delas responde à pergunta que se
pôs agora: *quem é que opera a base de dados em produção?* Levantar um
PostgreSQL só para o Rivo significaria uma segunda tecnologia de dados para a
mesma equipa manter, com backups e retenção próprios, ao lado de um servidor
que já faz isso.

## Requirements

- **Facto** — A ligação de destino é SQL Server, autenticação SQL, com
  `Encrypt=True`.
- **Facto** — ADR-002: um schema lógico por domínio, com ownership exclusivo.
  O SQL Server tem schemas com a mesma semântica de espaço de nomes.
- **Facto** — ADR-020: histórico de migrações por módulo, no schema do módulo.
  `MigrationsHistoryTable(nome, schema)` existe nos dois providers.
- **Facto** — ADR-025: a concorrência optimista é uma coluna `version` gerida
  pela aplicação, e não `xmin` nem `rowversion`. É portável por construção.
- **Facto** — BR-10 exige que a trilha de auditoria seja append-only ao nível
  da base de dados (K9).
- **Inferência** — O domínio não conhece o motor (ADR-019, verificado por
  teste de arquitectura). A troca fica contida em `Infrastructure`.

## Constraints

- `jsonb` não tem equivalente em SQL Server.
- Gatilhos em SQL Server **não disparam em `TRUNCATE TABLE`**, ao contrário do
  PostgreSQL, que tem gatilhos `BEFORE TRUNCATE`.
- `identity` é palavra reservada em T-SQL: o schema tem de vir entre parênteses
  rectos em SQL escrito à mão.
- O histórico de migrações existente foi gerado para PostgreSQL e não é
  convertível.

## Alternatives

1. **Trocar o provider e regenerar as migrações** (escolhida).
2. Manter PostgreSQL e levantar uma instância própria na VPS.
3. Abstrair o acesso a dados para suportar os dois motores.

A opção 2 mantém o ADR-002 intacto e é tecnicamente a mais confortável.
Rejeitada por acrescentar uma tecnologia de dados a operar — backups,
retenção, actualizações, um segundo modelo de recuperação — quando já existe
um servidor operado a que a aplicação tem acesso.

A opção 3 é a que parece prudente e é a que sai mais cara. Suportar dois
motores significa testar contra dois motores para sempre, e renunciar a tudo o
que for específico de cada um — a começar pelo gatilho que impõe o
append-only. Paga-se portabilidade que ninguém pediu com garantias que o
domínio exige.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| Trocar de provider (1) | Uma só base de dados a operar, já com backups | `jsonb`; gatilho de `truncate`; o histórico de migrações |
| PostgreSQL próprio (2) | Nada muda no código | Uma segunda tecnologia de dados a manter |
| Abstrair (3) | Independência de motor | Testes a dobrar; sem garantias específicas do motor |

## Decision

**Trocar `Npgsql.EntityFrameworkCore.PostgreSQL` por
`Microsoft.EntityFrameworkCore.SqlServer` e regenerar as migrações de raiz.**

O que muda, e só isto:

| Antes (PostgreSQL) | Agora (SQL Server) |
|---|---|
| `UseNpgsql(...)` | `UseSqlServer(...)` |
| `jsonb` em `audit_event` | `nvarchar(max)`, com JSON lá dentro |
| Gatilho `plpgsql` append-only | Gatilho `INSTEAD OF UPDATE, DELETE` em T-SQL |
| Gatilho `BEFORE TRUNCATE` | Tabela sentinela com FK — ver abaixo |
| `ON DELETE RESTRICT` | `ON DELETE NO ACTION`, que é o mesmo |
| `Testcontainers.PostgreSql` | `Testcontainers.MsSql` |

**As migrações são recriadas, não convertidas.** O histórico anterior descrevia
tipos que não existem no destino; mantê-lo seria guardar uma narrativa que
nenhuma base de dados vai voltar a reproduzir. Não há dados de produção a
preservar — o primeiro ambiente real nasce desta migração.

### O append-only sobrevive, por outro caminho

`UPDATE` e `DELETE` são recusados por um gatilho `INSTEAD OF`, que aborta a
transacção do chamador. `TRUNCATE TABLE` não dispara gatilho nenhum em SQL
Server, e não há forma de o interceptar — mas o motor **recusa truncar uma
tabela referenciada por uma chave estrangeira**, mesmo que a tabela que a
referencia esteja vazia. É para isso, e só para isso, que existe
`audit.audit_event_truncate_guard`.

Os três caminhos de destruição ficam fechados; a peça que os fecha é que muda.

### `snake_case` mantém-se

A convenção de nomes nasceu de uma limitação do PostgreSQL — identificadores
não citados são dobrados para minúsculas. Em SQL Server deixou de ser
obrigatória. Mantém-se na mesma: é o padrão escrito em
[standards/naming.md](../standards/naming.md), e trocá-la renomearia o esquema
inteiro sem que nenhum requisito o peça.

## Consequences

**Mais fácil:** uma só base de dados a operar, com os backups e a retenção que
a organização já faz. Um administrador que já conhece o motor.

**Mais difícil:**

- Consultar dentro dos valores de auditoria. Era `jsonb` com operadores e
  índices próprios; passa a `nvarchar(max)` com `JSON_VALUE` e `OPENJSON`. A
  garantia de que o conteúdo é JSON válido passa a ser da aplicação.
- SQL escrito à mão contra o schema `identity` precisa de `[identity]`.
- DML sobre `notifications.notification` exige `SET QUOTED_IDENTIFIER ON`, por
  causa do índice filtrado. O `sqlcmd` deixa-o desligado por omissão, e o erro
  1934 que daí resulta não diz nada sobre a causa — os scripts de verificação
  passam `-I` por isso.

**Custo aceite:** o histórico de migrações recomeça. Quem quiser saber como o
esquema chegou aqui tem os ADRs, não as migrações.

## Risks

- **Divergência entre o container de desenvolvimento e o servidor real.** A
  imagem local é `mssql/server:2022-latest` e o servidor de destino pode ser
  outra versão. Detecta-se com `SELECT @@VERSION` nos dois; mitiga-se fixando
  a imagem na versão do servidor assim que ela for conhecida.
- **O gatilho pode ser removido por quem for dono da tabela.** É a mesma
  contrapartida que existia em PostgreSQL, e continua aceite: protege contra o
  erro, não contra o adversário com privilégios totais.
- **A base de dados é partilhada com outros sistemas.** O isolamento é por
  schema, não por instância. Um `DROP SCHEMA` acidental vindo de outro sistema
  não encontra resistência nenhuma. Mitiga-se com um utilizador aplicacional
  restrito aos cinco schemas do Rivo — decisão em aberto, ver
  [state/pending-decisions.md](../state/pending-decisions.md).

## Revisit When

- O volume de auditoria tornar a consulta sobre `nvarchar(max)` demasiado
  lenta — a resposta seria colunas computadas com índice, não voltar atrás.
- A organização deixar de operar SQL Server.
- Aparecer necessidade real de correr o Rivo contra mais do que um motor.

## Related

- [ADR-002](adr-002-database.md) — a escolha que este substitui, e o desenho de
  schemas que se mantém
- [ADR-019](adr-019-persistencia-ef-core.md) — EF Core, e o domínio sem motor
- [ADR-020](adr-020-migracoes-por-modulo.md) — migrações e histórico por módulo
- [ADR-021](adr-021-ambiente-local-docker.md) — ambiente local em Docker
- [ADR-025](adr-025-concorrencia-optimista.md) — `version` gerida pela
  aplicação, e portanto portável
- [ADR-026](adr-026-testes-de-integracao.md) — testes contra o motor real
- [ADR-031](adr-031-deployment-em-vps.md) — o deployment que motivou a troca
