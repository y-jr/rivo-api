# ADR-021: Ambiente de Desenvolvimento Local — Docker Compose

## Status

Aceite (2026-08-15).

**Registo retroactivo.** Implementado em 2026-08-10, quando a stack se tornou
executável pela primeira vez.

## Context

[technology-decisions.md](../architecture/technology-decisions.md) listava
"ambiente de desenvolvimento local / containerização" como decisão em aberto.

Tornou-se bloqueante mal existiu código: sem base de dados a correr não há
migrações, e sem stack reprodutível não há verificação. As seis suites de
verificação partem literalmente de `docker compose down -v`, ou seja,
dependem desta decisão para existirem.

**Este ADR é sobre desenvolvimento local. Não é sobre produção**, e a
distinção é importante — ver Riscos.

## Requirements

- **Facto** — PostgreSQL (ADR-002).
- **Facto** — As suites de verificação exigem uma stack reprodutível a partir
  de base de dados vazia.
- **Facto** — Segredos não podem ser versionados
  ([standards/security.md](../standards/security.md)).
- **Facto** — As migrações aplicam-se no arranque em `Development` (ADR-020).
- **Inferência** — O ambiente tem de ficar utilizável num só comando, ou
  divergirá entre máquinas.

## Constraints

- A porta 5432 costuma estar ocupada por uma instalação local de PostgreSQL.
- Um volume nomeado herda dono e permissões do directório que existe na imagem
  no ponto de montagem.

## Alternatives

1. **Docker Compose com API e base de dados em containers** (escolhida).
2. Base de dados em Docker, API sempre no host.
3. .NET Aspire.
4. Instalação local de PostgreSQL, sem containers.

A opção 4 é rejeitada por divergir entre máquinas — é o problema que os
containers existem para resolver.

A opção 2 é mais rápida no ciclo de desenvolvimento e **continua disponível**
(ver Decision), mas não serve como definição do ambiente: as suites de
verificação reiniciam containers para testar persistência, e precisam de que a
API também seja um container.

A opção 3 daria orquestração local, painel e telemetria sem configuração.
Rejeitada por acrescentar um modelo de composição próprio a um projecto que
tem exactamente dois serviços — complexidade sem requisito, que é o que
[CLAUDE.md](../CLAUDE.md) manda evitar. Reavaliável se os serviços crescerem.

## Decision

**Docker Compose, dois serviços: `postgres` e `api`.**

### Base de dados

- `postgres:17-alpine`, volume nomeado para os dados.
- **Porta 5433 no host**, 5432 dentro do container — a 5432 costuma estar
  ocupada por uma instalação local.
- `healthcheck` com `pg_isready`, e `depends_on: condition: service_healthy` na
  API. Não basta o container arrancar: a API migra logo no arranque e precisa
  de aceitar ligações.

### API

- Publicada em **5080** no host, 8080 no container.
- `restart: unless-stopped`. **Não é decorativo:** o `depends_on` só se aplica
  no `up`, não no `restart`. Sem política de reinício, uma corrida no arranque
  exigiria intervenção manual — e as suites de verificação reiniciam
  containers de propósito.

### Imagem

- **Build em duas fases:** o SDK só existe para compilar; a imagem final leva
  apenas o runtime. Menor, e com menos superfície de ataque.
- **Utilizador não-root** (`USER $APP_UID`, fornecido pela imagem base). Um
  processo comprometido fica sem privilégios administrativos no container.
- Porta 8080, que é a omissão das imagens .NET desde a versão 8 precisamente
  por não exigir root.
- A raiz do armazenamento de documentos é criada **na imagem**, com o dono
  certo. Tem de ser aqui e não em runtime: sem isso, o volume nasce
  pertencente a `root` e a aplicação — que corre sem privilégios — não
  consegue escrever.

### Trade-off explícito no `Dockerfile`

Copia-se a árvore inteira antes do `restore`, em vez de listar cada `.csproj`.
Listar preservaria a cache do restore entre alterações de código, mas obrigaria
a editar o `Dockerfile` sempre que nascesse um módulo — e o esquecimento só
apareceria como falha de build. **Num monólito que vai crescer para catorze
módulos, a correcção vale mais do que os segundos de cache.**

### Segredos

Vêm de um ficheiro `.env`, que está em `.gitignore` e nunca é versionado.
Referenciados com a forma que falha ruidosamente:

```yaml
Jwt__SigningKey: ${JWT_SIGNING_KEY:?defina JWT_SIGNING_KEY no .env}
```

Um segredo em falta **impede o `up`** em vez de arrancar o ambiente numa
configuração insegura. `.env.example` documenta o que é preciso, sem valores.

A password do PostgreSQL é a excepção: está literal no `docker-compose.yml`,
marcada como credencial de desenvolvimento. Só é aceitável porque a base de
dados não é exposta para fora do host.

### Alternativa mantida para ciclo rápido

```bash
docker compose up -d postgres
dotnet run --project src/Rivo.Api
```

Suportada de propósito. Nos dois casos a API responde em `localhost:5080`.

## Consequences

Facilita:

- `docker compose up -d --build` dá um ambiente utilizável, com migrações
  aplicadas, sem passo manual.
- As suites de verificação tornam-se possíveis e reprodutíveis.
- Onboarding sem instalar PostgreSQL.

Dificulta / exige:

- Cada alteração de código exige reconstruir a imagem, salvo se se usar a
  alternativa acima.
- O `.env` tem de ser criado à mão em cada máquina nova, e um esquecimento
  falha o `up` — deliberadamente.
- A cache do `restore` perde-se a cada alteração de código.

## Risks

- **Confundir isto com topologia de produção.** É o risco principal. Este
  ambiente tem um único container de API, base de dados no mesmo host,
  segredos em ficheiro e migrações no arranque — **nada disso é aceitável em
  produção**, e o ADR-020 já exclui explicitamente o último ponto. As decisões
  de produção continuam em aberto e não se derivam daqui.
- **K8 é consequência directa desta topologia:** a API atrás do gateway da
  rede Docker regista o IP do gateway, não o do cliente. Corrigi-lo exige
  saber a topologia de produção, que não existe.
- **Documentos guardados em volume, sem cifra** (K11). Em desenvolvimento é
  aceitável; a resolução depende do serviço de armazenamento de produção.
- **Divergência entre a imagem e o que corre no host** na alternativa rápida —
  uma diferença de runtime pode só aparecer no container.

## Revisit When

- A infraestrutura de produção for escolhida — obriga a separar claramente a
  definição local da de produção, e fecha K8 e K11.
- O worker de entrega de `notifications` precisar de escalar
  independentemente da API, o que hoje não pode porque partilham processo.
- Os serviços passarem de dois para muitos, altura em que .NET Aspire volta a
  ser candidato razoável.

## Related

- [ADR-002](adr-002-database.md), [ADR-016](adr-016-bootstrap-autoridade.md),
  [ADR-019](adr-019-persistencia-ef-core.md),
  [ADR-020](adr-020-migracoes-por-modulo.md)
- [standards/security.md](../standards/security.md)
- [state/known-issues.md](../state/known-issues.md) — K8, K11
