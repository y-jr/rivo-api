# ADR-031: Deployment em VPS por SSH e Docker Compose

## Status

Aceite (2026-08-20).

**Substitui o [ADR-027](adr-027-app-service-em-vez-de-container-apps.md)** e,
com ele, todo o caminho de deployment em Azure — App Service, Container
Registry, Key Vault, o template Bicep e o workflow `cd-staging`.

## Context

O ADR-027 escolheu Azure App Service depois de a subscrição institucional
recusar um segundo ambiente de Container Apps. A análise estava certa para a
pergunta que respondia: *qual serviço do Azure serve melhor esta aplicação?*

A pergunta mudou. **O Rivo passa a correr numa VPS da organização**, ao lado
dos outros sistemas e contra o mesmo SQL Server (ADR-029). Deixa de haver
subscrição institucional no caminho, e com ela deixa de fazer sentido o
andaime todo que existia para lidar com ela: identidade gerida por OIDC,
registo de imagens privado, Key Vault, um template Bicep de infraestrutura.

Existe uma referência directa: outra API da mesma organização já é publicada
desta forma, e o método está provado em operação.

## Requirements

- **Facto** — O destino é uma VPS Linux com Docker e um reverse proxy à
  frente, na rede `proxy`.
- **Facto** — A base de dados é externa à VPS e não faz parte do deployment
  (ADR-029).
- **Facto** — Os segredos vivem em `/opt/projects/rivo/.env`, escrito à mão.
- **Facto** — O worker de entrega de `notifications` é um `BackgroundService`
  no mesmo processo da API: o processo tem de estar sempre de pé.
- **Inferência** — Um deployment tem de ser reversível sem reconstruir: um
  `git checkout` de um commit anterior e um `up --build` bastam.

## Constraints

- Não há registo de imagens: a VPS constrói a sua a partir do código.
- Não há runner com acesso à base de dados — foi isto que forçou o ADR-030.
- O container não publica porto no host; o único caminho até ele é o proxy.

## Alternatives

1. **SSH + `git pull` + `docker compose up --build` na VPS** (escolhida).
2. Construir a imagem no CI, publicá-la num registo e a VPS só puxar.
3. Manter o Azure App Service.

A opção 2 é melhor em quase tudo o que é medível: a marca da imagem é o SHA do
commit, o rollback é reimplantar uma marca anterior, e a VPS não gasta CPU a
compilar. Rejeitada **por agora** por exigir um registo — e um registo é uma
conta, credenciais na VPS e uma política de retenção, para uma aplicação com
um deployment. Fica registada como o próximo passo, não como alternativa
descartada.

A opção 3 mantinha uma segunda plataforma viva para uma aplicação que passou a
viver noutro sítio, com a base de dados noutro sítio ainda.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| SSH + compose (1) | Um método, já provado; sem infraestrutura de suporte | Build na VPS; rollback é `git checkout` |
| Registo de imagens (2) | Rollback por marca; VPS sem compilar | Um registo a operar |
| App Service (3) | Escala e logs geridos | Uma plataforma para uma aplicação; ligação atravessa a Internet |

## Decision

**`.github/workflows/main.yml`: em `push` para `main`, ligar por SSH à VPS e
correr `git pull`, `docker compose down`, `docker compose up -d --build`,
`docker image prune -f`.**

Depois disso, sondar `/health` a partir de um container descartável na rede
`proxy` — a API não publica porto, e a imagem de runtime do .NET não traz
`curl`. Sem esta sonda, o deployment ficava verde quando o `docker` devolvia,
e não quando a aplicação estava utilizável; uma migração falhada passava
despercebida.

O que desaparece do repositório:

| Removido | Porquê |
|---|---|
| `.github/workflows/cd-staging.yml` | Substituído por `main.yml` |
| `infra/main.bicep` | Não há infraestrutura Azure a descrever |

O que **fica**: `BlobDocumentStorage` e os pacotes `Azure.*`. A escolha de
armazenamento é por configuração e não por ambiente (ADR-027) — sem
`DocumentStorage:AccountName`, usa-se o sistema de ficheiros, que é o caso da
VPS. O caminho de Blob Storage não estorva e é a resposta pronta para K11 se
um dia houver uma conta.

O CI (`ci.yml`) mantém-se e continua a bloquear PRs. O CD responde "isto está a
correr?" e não bloqueia nada — misturá-los faria uma falha de deployment
parecer uma falha de código.

### O que a VPS tem de ter, uma vez

```
/opt/projects/rivo          repositório clonado, com acesso de leitura ao remoto
/opt/projects/rivo/.env     preenchido à mão (ver .env.example)
docker network create proxy rede partilhada com o reverse proxy
```

Segredos do GitHub: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `PORT`.

## Consequences

**Mais fácil:** um método de deployment para os sistemas todos da organização.
Nada de identidade federada, registo de imagens ou Key Vault a manter para uma
aplicação.

**Mais difícil:**

- **Rollback.** Era reimplantar a marca anterior da imagem; passa a ser
  `git checkout <sha> && docker compose up -d --build` na VPS. Mais lento, e
  reconstrói em vez de repor.
- **Logs e métricas.** Eram do App Service; passam a ser `docker compose logs`
  numa máquina. Observabilidade fica em aberto.
- **Escala.** Uma instância. Não há botão.

**Custo aceite:** a VPS compila a cada deployment. Para uma imagem .NET com
cache de camadas é da ordem do minuto, e não vale um registo hoje.

## Risks

- **`.env` desalinhado do `.env.example`.** Uma variável nova adicionada ao
  compose e esquecida na VPS derruba o arranque — com uma mensagem clara,
  porque as obrigatórias usam `${VAR:?mensagem}`. Detecta-se na sonda de
  `/health` do próprio deployment.
- **Deploy de uma migração destrutiva.** Ver ADR-030: o interruptor aprova
  todas as migrações, e a revisão do PR é onde a decisão se toma.
- **A API confia em `X-Forwarded-For` de qualquer origem.** Só é seguro
  enquanto o proxy for o único caminho até ao container. Publicar o porto 8080
  no host quebra isso em silêncio — o registo de IP passa a ser falsificável.
- **Sem cópia do volume de documentos.** `rivo-documents-data` vive na VPS e
  não entra no backup da base de dados. Fica em aberto, ver
  [state/pending-decisions.md](../state/pending-decisions.md).

## Revisit When

- Houver mais do que uma instância, ou necessidade de deployment sem
  indisponibilidade — aí é registo de imagens (opção 2) e migração como passo
  próprio.
- O tempo de build na VPS passar a incomodar.
- Aparecer requisito de observabilidade que `docker compose logs` não sirva.

## Related

- [ADR-027](adr-027-app-service-em-vez-de-container-apps.md) — o deployment que
  este substitui
- [ADR-029](adr-029-sql-server-em-vez-de-postgresql.md) — a base de dados
  externa que este deployment pressupõe
- [ADR-030](adr-030-migracao-no-arranque-por-interruptor.md) — como as
  migrações passam a chegar à base de dados
- [ADR-023](adr-023-pipeline-ci.md) — o CI, que se mantém
