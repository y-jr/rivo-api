# ADR-030: Migração no Arranque, por Interruptor Explícito

## Status

Aceite (2026-08-20).

**Ajusta o gate do [ADR-020](adr-020-migracoes-por-modulo.md).** A decisão de
fundo — um `DbContext`, um histórico e um schema por módulo — não muda. O que
muda é *onde* a migração corre.

## Context

O ADR-020 tirou a migração do arranque da aplicação e fez dela passo de
pipeline, por duas razões concretas:

1. Várias instâncias competiriam pelo mesmo schema.
2. Uma migração destrutiva correria sem ninguém a aprovar.

Ficou restrita a `Development`, e em produção passou a ser um passo do CD, que
gerava um bundle por módulo e o corria contra a connection string lida do Key
Vault.

**Esse passo deixou de existir.** O deployment passou a ser por SSH e
`docker compose` numa VPS (ADR-031): o CD faz `git pull`, `docker compose up
--build` e mais nada. Não há runner com acesso à base de dados, não há Key
Vault de onde ler a ligação, e não há sítio onde encaixar o bundle sem
inventar um.

Sem decisão, o resultado seria o do primeiro deployment descrito no ADR-028: um
ambiente com a aplicação de pé e sem esquema nenhum.

## Requirements

- **Facto** — O CD é `git pull` + `docker compose up` (ADR-031).
- **Facto** — O compose corre **uma** instância da API.
- **Facto** — ADR-020: migrar automaticamente é perigoso com instâncias
  concorrentes ou com migrações destrutivas.
- **Facto** — ADR-028: o seed é idempotente e não carrega esses riscos.
- **Inferência** — Um ambiente novo tem de ficar utilizável com um comando, ou
  o passo em falta é feito à mão e não fica registado em lado nenhum.

## Constraints

- Não há ponto de entrada de linha de comandos na aplicação para migrar sem a
  arrancar.
- O `.env` da VPS é escrito à mão, uma vez. O deployment não lhe toca.

## Alternatives

1. **Migração no arranque, ligada por interruptor de configuração**
   (escolhida).
2. Container de migração à parte no compose, que corre e termina antes da API.
3. Passo manual: quem faz deployment corre as migrações por SSH antes do
   `compose up`.
4. Voltar a migrar sempre no arranque, sem interruptor.

A opção 2 é a mais próxima do espírito do ADR-020 — separa migrar de servir, e
uma migração falhada impede a API de subir. Rejeitada por custo
desproporcionado hoje: exigiria uma segunda imagem ou um ponto de entrada
alternativo, e um `depends_on: service_completed_successfully` que duplica
metade do compose, para tratar um risco (instâncias concorrentes) que uma
instância não tem. **É a opção certa no dia em que forem várias instâncias.**

A opção 3 é o género de passo manual não registado que produz ambientes
divergentes — é exactamente o que o ADR-028 documenta ter corrido mal.

A opção 4 reintroduz o risco do ADR-020 por arrasto, sem ninguém decidir. É a
diferença entre uma decisão e um esquecimento.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| Interruptor (1) | Um comando põe o ambiente de pé; a aprovação fica escrita | Migração e serviço no mesmo processo |
| Container à parte (2) | Separação limpa; falha antes de servir | Segunda imagem, compose ao dobro |
| Manual (3) | Controlo total | Passos por registar; ambientes que divergem |
| Sempre (4) | Simples | Perde-se a decisão do ADR-020 |

## Decision

**A migração corre no arranque quando `Database:MigrateOnStartup` for `true`.
Por omissão é `false`.**

```yaml
# docker-compose.yml
Database__MigrateOnStartup: ${MIGRATE_ON_STARTUP:-true}
```

As duas razões do ADR-020 ficam tratadas, e é por isso que isto não é uma
regressão:

- **Instâncias concorrentes:** é uma só, por desenho do compose. Se um dia
  forem várias, põe-se `MIGRATE_ON_STARTUP=false` e a migração volta a ser
  passo próprio — sem tocar em código.
- **Destrutivo sem aprovação:** **o interruptor é a aprovação.** Deixa de ser
  efeito colateral de `ASPNETCORE_ENVIRONMENT` e passa a ser uma linha que
  alguém escreveu no `.env` da máquina que opera.

O seed segue a mesma forma, por coerência: `Bootstrap:SeedOnStartup`, com
omissão igual à regra do ADR-028 — corre em tudo excepto `Production`. Dizer
`true` explicitamente é o que faz um ambiente de produção novo nascer com
administrador, em vez de nascer sem ninguém com autoridade.

A ordem de migração é obrigatória e está no `Program.cs`: `hr` depois de
`documents`, porque a FK entre schemas exige que `documents.document` já
exista; `identity` por último, porque o seu seed depende dos schemas dos
outros.

### A migração repete-se enquanto o ambiente não estiver pronto

Migrar no arranque põe a aplicação a correr contra uma base de dados que pode
ainda não estar de pé. Com PostgreSQL isso quase nunca se notava — o container
aceitava ligações em cerca de um segundo. **O SQL Server demora dezenas de
segundos**, e passou a ser o caso normal, não a excepção.

`EnableRetryOnFailure` não chega: repete o que a estratégia do EF Core
classifica como transitório, e um servidor que ainda nem abriu o porto não
entra nessa categoria — dá `SocketException`, que sobe e mata o arranque.

Por isso a sequência de migrações corre dentro de uma repetição própria,
limitada por `Database:StartupTimeoutSeconds` (180 s por omissão), que só
repete em dois casos:

| Falha | Porquê passa sozinha |
|---|---|
| `SocketException` | O servidor ainda não atende; atenderá |
| Erro 1801, "base já existe" | A repetição do EF Core correu um `CREATE DATABASE` que já tinha passado no servidor. Na tentativa seguinte a base existe e a migração segue |

Qualquer outra falha — credenciais recusadas, SQL inválido, restrição violada
— sobe à primeira, porque esperar não a resolve. E a espera é limitada: ao fim
do prazo o arranque morre. Um container que reinicia com erro visível é melhor
do que um processo vivo e eternamente à espera, que nenhuma sonda distingue de
saudável.

**Repetir migrações é seguro**, e é o ADR-020 que o garante: cada módulo
consulta a sua tabela de histórico e aplica só o que falta. Uma sequência
interrompida a meio retoma onde ficou.

## Consequences

**Mais fácil:** um ambiente novo — local, VPS, o que for — fica utilizável com
`docker compose up`. O gate deixa de estar preso ao nome do ambiente.

**Mais difícil:** uma migração que falhe derruba o arranque da API. É o
comportamento desejado — uma aplicação a servir contra um esquema desactualizado
é pior do que uma aplicação em baixo — mas significa que o diagnóstico está nos
logs do container, e não num passo de pipeline com nome próprio.

**Custo aceite:** migrar e servir voltam a partilhar processo. Passa a haver
uma configuração a mais para acertar por ambiente.

## Risks

- **`MIGRATE_ON_STARTUP=true` esquecido no dia em que houver duas instâncias.**
  Duas migrações concorrentes ao mesmo schema. Detecta-se por erro de
  arranque numa das instâncias; previne-se ao passar para a opção 2 antes de
  escalar.
- **Migração destrutiva num deployment de rotina.** O interruptor aprova todas
  as migrações, não cada uma. Mitiga-se pela revisão do PR que introduz a
  migração — que é onde a decisão é tomada de facto.

## Revisit When

- A API passar a correr em mais do que uma instância — aí é a opção 2.
- Aparecer uma migração destrutiva que exija aprovação caso a caso.

## Related

- [ADR-020](adr-020-migracoes-por-modulo.md) — migrações por módulo, e o gate
  que este ajusta
- [ADR-028](adr-028-seed-separado-da-migracao.md) — seed separado da migração
- [ADR-029](adr-029-sql-server-em-vez-de-postgresql.md) — o motor onde as
  migrações agora correm
- [ADR-031](adr-031-deployment-em-vps.md) — o deployment que retirou o passo
  de pipeline
