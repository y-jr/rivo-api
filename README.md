# Rivo

Plataforma de gestão empresarial para PMEs em Angola. Monólito modular em
C#/.NET sobre SQL Server.

A documentação arquitectural vive em [`.claude/`](.claude/). As fontes de
verdade estão em [`.claude/docs/`](.claude/docs/); as decisões em
[`.claude/decisions/`](.claude/decisions/).

## Pré-requisitos

- .NET SDK 10.0+
- Docker

## Dois ficheiros de compose, e a diferença importa

| Ficheiro | Base de dados | Para quê |
|---|---|---|
| `docker-compose.yml` | **SQL Server externo**, via `CONNECTION_STRING` | Produção, na VPS (ADR-031) |
| `+ docker-compose.dev.yml` | SQL Server **em container** | Desenvolvimento e CI |

A sobreposição de desenvolvimento existe para que nem o CI nem o
desenvolvimento escrevam na base de dados real (ADR-029). O `docker-compose.yml`
sozinho fala com o servidor externo — é esse que a VPS usa.

## Arrancar

```bash
cp .env.example .env
# preencher, depois:
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build

curl http://localhost:5080/health
# {"status":"ok","database":"up"}
```

A API aplica as migrações no arranque quando `MIGRATE_ON_STARTUP=true`
(ADR-030), por isso não é preciso mais nenhum passo.

### Alternativa: API no host, base de dados em Docker

Ciclo de desenvolvimento mais rápido, sem reconstruir a imagem a cada
alteração:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d sqlserver
dotnet run --project src/Rivo.Api
```

Nos dois casos a API responde em `http://localhost:5080`.

## Swagger

Só em desenvolvimento:

- Interface: <http://localhost:5080/swagger>
- Documento OpenAPI: <http://localhost:5080/openapi/v1.json>

Para experimentar os endpoints protegidos: `POST /identity/login`, copiar o
`accessToken` da resposta, clicar em **Authorize** e colar. O prefixo `Bearer`
é acrescentado automaticamente.

> O container de base de dados publica em **14330**, não 1433, porque a 1433
> costuma estar ocupada por uma instalação local do SQL Server. Dentro do
> container o porto continua a ser 1433.

## Migrações

```bash
dotnet tool install --global dotnet-ef

dotnet ef migrations add <Nome> \
  --project src/Modules/Identity/Rivo.Identity.Infrastructure \
  --startup-project src/Rivo.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Modules/Identity/Rivo.Identity.Infrastructure \
  --startup-project src/Rivo.Api
```

> **Uma migração de cada vez, com build pelo meio.** As ferramentas do EF Core
> lêem o assembly compilado, não o código-fonte: gerar duas migrações seguidas
> com `--no-build` faz a segunda repetir a primeira, porque o snapshot da
> anterior ainda não está no binário.

A ordem entre módulos importa e está no `Program.cs`: `hr` depois de
`documents`, porque a chave estrangeira entre schemas exige que
`documents.document` já exista; `identity` por último, porque o seu seed
depende dos schemas dos outros.

## Autenticação

JWT bearer com sessão persistida (ADR-013). O token transporta `sid`; cada
pedido autenticado confirma que a sessão continua activa, o que torna a
revogação imediata.

| Método | Rota | Exige | Descrição |
|---|---|---|---|
| POST | `/identity/register` | — | Cria conta |
| POST | `/identity/login` | — | Abre sessão e emite token |
| POST | `/identity/login/google` | — | O mesmo, a partir de um ID token da Google (ADR-032) |
| POST | `/identity/logout` | autenticação | Revoga a sessão |
| GET | `/identity/me` | autenticação | Identidade, perfis e permissões |
| GET | `/identity/users` | `identity.users.read` | Lista contas |
| GET | `/identity/roles` | `identity.roles.read` | Lista perfis e permissões |
| POST | `/identity/users/{id}/roles` | `identity.roles.assign` | Atribui perfil |

```bash
curl -X POST http://localhost:5080/identity/register \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@rivo.ao","password":"Rivo!Password2026"}'

TOKEN=$(curl -s -X POST http://localhost:5080/identity/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@rivo.ao","password":"Rivo!Password2026"}' | jq -r .accessToken)

curl http://localhost:5080/identity/me -H "Authorization: Bearer $TOKEN"
```

Password: mínimo 12 caracteres, com maiúscula, minúscula, dígito e símbolo.
Bloqueio após 5 tentativas falhadas.

### Entrar com Google

Segundo caminho de autenticação, não substituto (ADR-032). O frontend obtém um
ID token junto da Google e envia-o ao servidor, que valida a assinatura contra
as chaves públicas da Google e devolve **o mesmo** `LoginResponse` do login por
password — mesmo token, mesma sessão revogável, mesma trilha.

```bash
curl -X POST http://localhost:5080/identity/login/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"<ID token devolvido pela Google>"}'
```

Configura-se com uma variável, opcional:

```bash
GOOGLE_CLIENT_ID=   # OAuth 2.0 Client ID, tipo "Web application"
```

Não há `ClientSecret`: validar uma assinatura precisa da chave pública, e o
fluxo de redirect que exigiria o segredo foi rejeitado por reintroduzir
cookies que o ADR-013 tinha afastado.

**Três comportamentos que convém saber antes de testar:**

| Situação | Resposta |
|---|---|
| `GOOGLE_CLIENT_ID` vazio | **501** — o Google não está ligado neste ambiente |
| Token válido, sem conta Rivo com esse e-mail | **401** — o Google não cria contas (ADR-016) |
| Token válido, com conta | **200**, com `accessToken` |

O 501 é deliberado: um 401 mandaria procurar o defeito na conta de quem
tentou, que é o sítio errado.

Um Admin cria a conta primeiro (`POST /identity/register` ou o seed de
arranque); a primeira entrada por Google liga-se a ela pelo e-mail, e só se a
Google confirmar ser dono desse endereço. As entradas seguintes usam o `sub`,
que não muda se a pessoa trocar de e-mail.

⚠ **Isto não é MFA.** A 2FA da conta Google é da Google — o Rivo não a exige
nem a consegue verificar. O requisito de MFA para perfis decisórios continua
por satisfazer.

## Autorização

RBAC com permissões (ADR-014):

```
User ──> Perfil de Acesso ──> Permissões ──> Policy no endpoint
```

Sete perfis semeados: **Admin, Manager, Finance, HR, Sales, AssetManager,
ProjectManager**. Só o Admin tem permissões nesta fase — os outros existem
vazios até haver módulos de negócio para autorizar.

As permissões são claims de perfil (`app_role_claim`), resolvidas no login e
transportadas no JWT. **Alterar o perfil de alguém só surte efeito no login
seguinte**; para forçar, revoga-se a sessão.

### Bootstrap de autoridade

Ninguém nasce com autoridade, logo não há quem conceda a primeira. Isso
resolve-se por **seed controlado e idempotente** (ADR-016), executado depois
das migrações:

```
Migrations → Perfis + Permissões → Utilizadores + Associações
```

As credenciais vêm do ficheiro `.env`, **nunca do repositório**:

```bash
cp .env.example .env
# preencher, depois:
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
```

| Variável | Para quê |
|---|---|
| `BOOTSTRAP_ADMIN_EMAIL` / `_PASSWORD` | Utilizador com perfil `Admin` |
| `BOOTSTRAP_DECIDER_EMAIL` / `_PASSWORD` | Utilizador com perfil `Finance` |
| `JWT_SIGNING_KEY` | Assinatura do token, mínimo 32 caracteres |
| `CONNECTION_STRING` | Ligação ao SQL Server |
| `MIGRATE_ON_STARTUP` / `SEED_ON_STARTUP` | Interruptores de migração e seed (ADR-030) |

Admin e decisor usam **o mesmo mecanismo** — são entradas diferentes da mesma
lista em `docker-compose.yml`, com perfis diferentes. Acrescentar um terceiro
é acrescentar `Bootstrap__Users__2__*`.

O seed **nunca altera contas existentes**, incluindo passwords: repô-las a
cada arranque sobrescreveria uma credencial que o utilizador tivesse mudado.

O bootstrap **não participa das regras normais de autorização** — usa o
`UserManager` directamente, porque existe precisamente para o momento em que
ainda não há ninguém com autoridade para conceder autoridade.

⚠ **Limitação:** o seed atribui apenas Perfis de Acesso. A autoridade de
decisão prevista em ADR-015 vem do **Cargo**, e criar um Cargo com autoridade
exige decisão de `approval` — módulo que ainda não existe. É por isso que a
atribuição desse tipo de Cargo devolve `501`.

## Testes

Dois níveis, com propósitos diferentes. Nenhum substitui o outro.

### Domínio — rápido, sem infraestrutura

```bash
dotnet test
```

100 testes em cinco módulos (ADR-022), a correr em menos de 2 segundos. Sem
Docker, sem base de dados, sem rede. Testam invariantes: `hr` 45,
`notifications` 20, `documents` 16, `audit` 10, `identity` 9.

Um projecto por domínio de módulo, em
`tests/Modules/<Módulo>/Rivo.<Módulo>.Domain.Tests/`.

Junta-se-lhes `Rivo.Identity.Application.Tests` (8 casos): as regras do login
federado — não criar contas, recusar e-mail por verificar, desaguar sempre
numa sessão persistida — são de orquestração e não vivem em entidade nenhuma
(ADR-032). Testá-las pela suite caixa-preta exigiria um ID token verdadeiro da
Google, que o CI nunca vai ter.

Um teste de domínio que precise de `DbContext`, `HttpContext` ou de ficheiros
está a assinalar que a regra vazou da camada — é defeito de arquitectura, não
de teste.

### End-to-end — o sistema montado

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml down -v
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build
pwsh -File scripts/verify-all.ps1
```

> **PowerShell 7+ recomendado.** `verify-all` usa `pwsh` quando existe e
> recorre ao `powershell` 5.1 quando não. O 5.1 é fallback, não é suportado em
> pé de igualdade: `verify-authorization` usa `Join-String`, que só existe a
> partir do PowerShell 6.2, e sob o 5.1 essa linha falha — mas só no caminho
> de erro, ou seja, quando uma verificação já está a falhar.
>
> `winget install Microsoft.PowerShell`

`verify-all` corre as seis suites por ordem e **espera que a stack assente
entre elas** — várias reiniciam containers para verificar persistência, e sem
pausa a suite seguinte começaria contra uma API ainda a subir.

Cada suite também corre isolada: `pwsh -File scripts/verify-hr.ps1`.

Seis suites, 66 casos:

| Suite | Cobre |
|---|---|
| `verify-bootstrap` | Migrações, seed idempotente, Admin e decisor criados, passwords fora do código e dos logs |
| `verify-authorization` | 401 sem autenticação, 403 sem permissão, catálogo de perfis fechado |
| `verify-audit` | Registo, login falhado (BR-12), atribuição de perfil com actor correcto (BR-13) |
| `verify-hr` | Separação catálogo/atribuição de Cargo (ADR-015), `501` para cargo com autoridade |
| `verify-documents` | Hash de integridade, FK entre schemas a bloquear eliminação (ADR-009) |
| `verify-notifications` | Isolamento por destinatário, worker de entrega, enfileirar sem afectar o negócio |

## Integração contínua

`.github/workflows/ci.yml`, em cada push para `main` e em cada pull request
(ADR-023). Dois jobs, deliberadamente separados:

| Job | O que faz | Papel |
|---|---|---|
| `build-and-test` | `restore → build (Release) → test` | Rápido e determinístico. **É este que bloqueia um PR** |
| `verify-stack` | Sobe a stack em Docker e corre as seis suites | Mais lento e mais frágil; corre depois do primeiro |

Separados para que uma falha de infraestrutura não se disfarce de falha de
código. As credenciais das suites são geradas a cada execução com
`openssl rand` — o ambiente é efémero, e não há segredos de repositório
envolvidos. O `verify-stack` usa a sobreposição de desenvolvimento, por isso
corre contra um SQL Server em container e nunca toca na base de dados real.

## Deployment

`.github/workflows/main.yml`, em cada push para `main` (ADR-031). Liga por SSH
à VPS e corre, em `/opt/projects/rivo`:

```bash
git pull origin main
docker compose down
docker compose up -d --build
docker image prune -f
```

Repare-se no `docker compose` **sem `-f`**: na VPS vale só o
`docker-compose.yml`, que fala com o SQL Server externo. A sobreposição de
desenvolvimento fica de fora.

No fim, sonda `/health` a partir de um container descartável na rede `proxy` —
a API não publica porto no host, e a imagem de runtime do .NET não traz `curl`.
Sem essa sonda, uma migração falhada passava por deployment bem sucedido.

### O que a VPS tem de ter, uma vez

```bash
git clone <remoto> /opt/projects/rivo
cp /opt/projects/rivo/.env.example /opt/projects/rivo/.env   # e preencher
docker network create proxy
```

O `.env` é escrito à mão e o deployment nunca lhe toca. Uma variável nova no
compose e esquecida ali derruba o arranque — com mensagem explícita, porque as
obrigatórias usam `${VAR:?...}`.

Segredos do repositório no GitHub: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`,
`PORT`.

> ⚠ **A API não deve publicar porto no host.** O único caminho até ela é o
> reverse proxy na rede `proxy`, e é disso — não de configuração — que depende
> a confiança em `X-Forwarded-For` (K8). Publicar 8080 reabre o defeito sem
> nenhum sinal.

## Convenções de código

- **Código em inglês.** Comentários e comunicações externas em português.
- Tabelas e colunas em `snake_case`, por convenção automática do EF Core.

## Estrutura

```
src/
├── Rivo.Api/                        host + composition root
└── Modules/
    ├── Identity/
    │   ├── Rivo.Identity.Api/            endpoints REST
    │   ├── Rivo.Identity.Application/    casos de uso
    │   ├── Rivo.Identity.Domain/         entidades e invariantes
    │   └── Rivo.Identity.Infrastructure/ persistência e DI
    └── Audit/
        ├── Rivo.Audit.Contracts/         superfície publicada, sem dependências
        ├── Rivo.Audit.Api/
        ├── Rivo.Audit.Application/
        ├── Rivo.Audit.Domain/
        └── Rivo.Audit.Infrastructure/
```

Dependências:

```
Rivo.Api ──> Identity.Api ──> Identity.Application ──> Identity.Domain
         │                            └──> Audit.Contracts
         ├─> Identity.Infrastructure ──┘
         ├─> Audit.Api ──> Audit.Application ──> Audit.Domain
         └─> Audit.Infrastructure ──┘
```

**`identity` consome apenas `Audit.Contracts`**, nunca a Application do
`audit` (ADR-017). Os contratos não dependem de nada, o que impede ciclos
entre módulos por construção.

Cada módulo tem o seu schema na base de dados (`identity`, `audit`) e as suas
próprias migrações, com tabela de histórico própria (ADR-020).

## Auditoria

`audit` regista quem fez o quê, quando e sobre que registo. `identity`
escreve na trilha em: registo de conta, login, **tentativa de login falhada**
(BR-12), logout e atribuição de perfil (BR-13).

| Método | Rota | Exige |
|---|---|---|
| GET | `/audit/entries?entityType=&entityId=&limit=` | `audit.trail.read` |

**Não há endpoint de escrita.** A trilha é escrita pelos módulos através do
contrato interno — um endpoint público permitiria forjar registos.

A listagem omite os valores antes/depois: podem conter dados sensíveis
(BR-16). Consultam-se directamente quando a investigação o exigir.

## Documentos

`documents` guarda ficheiros, metadados e hash SHA-256 para integridade.
Armazenamento em sistema de ficheiros sobre um volume, atrás de um port —
trocar por S3 é implementar a interface.

| Método | Rota | Exige |
|---|---|---|
| POST | `/documents` (multipart: `file`, `category`) | `documents.write` |
| GET | `/documents/{id}` | `documents.read` |
| GET | `/documents/{id}/metadata` | `documents.read` |
| POST | `/hr/employees/{id}/documents` | `hr.employees.write` |
| GET | `/hr/employees/{id}/documents` | `hr.employees.read` |

**A ligação a registos de negócio vive no contexto de origem** (ADR-009):
`hr.employee_document` tem chaves estrangeiras reais para
`hr.employee(id)` **e** `documents.document(id)`. `documents` não conhece
`hr` nem nenhum outro módulo.

Separação de permissões: fazer *upload* exige `documents.write`; *anexar* a
um colaborador exige `hr.employees.write`, porque está a alterar-se o
registo do colaborador.

⚠ **Sem cifra em repouso** — ver K11 em
[`.claude/state/known-issues.md`](.claude/state/known-issues.md).

## Notificações

| Método | Rota | Exige |
|---|---|---|
| GET | `/notifications/me?unreadOnly=&limit=` | autenticação |
| POST | `/notifications/{id}/read` | autenticação |

**Sem permissões.** O que limita o acesso é ser o destinatário — invariante de
propriedade do agregado, verificada no domínio, não política configurável.
Marcar notificação alheia devolve **404, não 403**: distinguir revelaria que
existe.

A entrega corre **fora da transacção de negócio**: o módulo de origem
enfileira e segue; um `BackgroundService` entrega depois, com recuo
exponencial (2, 4, 8, 16 min) e desistência ao 5.º insucesso. Uma falha de
envio nunca derruba a operação que originou a notificação.

⚠ O canal de entrega actual **regista em log**. O fornecedor de e-mail é
decisão pendente.

Existem os módulos `identity`, `audit`, `hr`, `documents` e `notifications`.
Os restantes 9 estão definidos em [`.claude/modules/`](.claude/modules/) mas
não implementados.

## Próximo passo

Semear os Perfis de Acesso e implementar autorização por perfil.

Pendências assinaladas em
[`.claude/state/pending-decisions.md`](.claude/state/pending-decisions.md) —
a mais relevante é a **expiração por inactividade**, que os requisitos preveem
e que ainda não está implementada.
