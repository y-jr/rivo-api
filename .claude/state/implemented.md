# Implementado

_Última actualização: 2026-08-27._

Funcionalidade concluída e a funcionar, por módulo. Actualizar como parte de
terminar uma funcionalidade (passo 8 do fluxo em [CLAUDE.md](../CLAUDE.md)).

**Nove dos catorze módulos têm código:** `identity`, `audit`, `hr`,
`documents`, `notifications`, `approval`, `fiscal`, `commercial`, `finance`.
Os restantes cinco — `procurement`, `payroll`, `projects`, `inventory`,
`fleet` — estão definidos em [modules/](../modules/) e não têm código.

⚠ **Dois estão reduzidos ao mínimo pelo ADR-036**, e não implementados por
inteiro: `fiscal` (só taxa com vigência e determinação) e `commercial` (só
Cliente). `finance` tem os cinco contextos internos desde 2026-08-25 e os
documentos lançam nos livros, mas a contabilidade está de pé e **vazia** — o
plano de contas carrega-se e as regras de postagem definem-se, e sem elas nada
lança. Ver a ressalva em cada secção.

> As datas até 2026-08-16 vêm do carimbo das migrações EF Core — o repositório
> só passou a estar sob git nesse dia. A partir daí vêm do histórico.

## Formato

```
## <módulo>
- <funcionalidade> — <data> — <nota breve, ADR relacionado se aplicável>
```

## identity

- Autenticação por JWT bearer com sessão persistida — 2026-08-10 — ADR-013
- Sessão como entidade de domínio, com IP, user agent, expiração absoluta e
  revogação — 2026-08-10 — ADR-013
- ASP.NET Core Identity como fonte de contas — 2026-08-10 — ADR-012; fecha
  implicitamente o ORM em EF Core
- Catálogo dos sete Perfis de Acesso, semeados; permissões transportadas como
  role claims — 2026-08-10 — ADR-014
- Bootstrap idempotente do Admin e do decisor iniciais, por configuração —
  2026-08-10 — ADR-016
- Entrar com Google, por ID token validado contra o JWKS da Google —
  2026-08-22 — ADR-032. Desagua na mesma sessão persistida do login por
  password; **não cria contas** (ADR-016) e exige `email_verified`. Sem
  `Google:ClientId` configurado, o endpoint responde 501 e nada mais muda.
  Sem migração — `identity.app_user_login` já existia
- Endpoints: `register`, `login`, `login/google`, `logout`, `me`, `users`,
  `roles`, `users/{id}/roles`

**Por satisfazer, deliberadamente registado:**

- Só existe expiração **absoluta**. O requisito de 15 minutos por inactividade
  para perfis decisórios **não está satisfeito**.
- Sem refresh token e sem MFA. **O login com Google não traz MFA** — a 2FA da
  conta Google não é exigível nem verificável pelo Rivo (ADR-032).
- Das sete entradas do catálogo, **cinco têm permissões**: `Admin`, `HR`,
  `Manager`, `Finance` e `Sales`. `AssetManager` e `ProjectManager` continuam
  vazias porque dependem de módulos que não existem — inventá-las seria
  adivinhar.
- ~~O IP registado na sessão é o do proxy, não o do cliente.~~ **K8 fechado em
  2026-08-16** — cabeçalhos reencaminhados, com a confiança a vir de o
  container não publicar porto no host (ADR-031).

## audit

- Trilha append-only com `AuditEvent` imutável — 2026-08-10 — BR-10
- Contrato `IAuditTrail`, consumido pelos restantes módulos — 2026-08-10 —
  primeiro uso de ADR-017
- Consulta filtrada por tipo e identificador de entidade, com limite —
  2026-08-10
- `GET /audit/entries` é **só leitura**. Não existe endpoint de escrita: a
  trilha é escrita pelo contrato interno, nunca por HTTP — um endpoint público
  permitiria forjar registos

**Por satisfazer:** o append-only é imposto em código, não pela base de dados
(K9); a escrita da trilha não é transaccional com a operação auditada (K10).

## hr

- Colaborador, Departamento, Cargo e Atribuição de Cargo — 2026-08-11
- Contrato `EmployeeReference` / `IEmployeeDirectory` como **único** caminho de
  acesso a Colaborador a partir de outros módulos — 2026-08-11 — ADR-010
- Ligação opcional entre Colaborador e conta de `identity` — 2026-08-11 —
  ADR-004
- Separação entre Cargo e Perfil de Acesso imposta na autorização: o catálogo
  de Cargos exige `Admin`, a atribuição exige `HR` — 2026-08-11 — ADR-005,
  ADR-015. É esta separação que fecha a escalada de privilégios
- Anexação e listagem de documentos do colaborador, com FK entre schemas —
  2026-08-11 — ADR-009, ADR-010
- **Contrato de Trabalho** — 2026-08-22 — tipo (sem termo / a termo /
  prestação de serviços), vigência, remuneração base e moeda ISO 4217. O tipo
  manda na vigência: sem termo recusa data de fim, a termo exige-a. Duas
  relações laborais em vigor ao mesmo tempo são recusadas com `409`; um
  contrato cessado não colide, para que a recontratação seja possível.
  Permissões próprias `hr.contracts.read` / `.write`, separadas de
  `hr.employees.read` porque a lista traz o salário
- **Assiduidade** — 2026-08-22 — marcação de ponto numa rota só
  (`POST /hr/attendance/clock`), que abre ou fecha o dia consoante o estado;
  registo e justificação de faltas; consulta por intervalo com filtro de
  anomalias, que é a vista da fila de RH. Um registo por colaborador e por dia,
  imposto por índice único — a verificação no caso de uso não chega para um
  relógio de ponto com rede instável
- **Benefícios** — 2026-08-22 — catálogo e adesão separados: o benefício existe
  independentemente de alguém o ter. Descontinuar um benefício impede adesões
  novas **sem cancelar as existentes**. Não se adere duas vezes ao mesmo
  benefício enquanto a primeira estiver activa
- **Recrutamento** — 2026-08-22 — vaga e candidato, com o funil
  `Applied → Screening → Interview → Offer → Hired`. **Avança um passo de cada
  vez**, sem saltos nem recuos; rejeitar é o único desvio e vale de qualquer
  fase. Contratar tem endpoint próprio, cria o Colaborador e liga-o à
  candidatura — é a fronteira entre recrutamento e quadro de pessoal
- **Onboarding e Offboarding** — 2026-08-22 — um agregado para os dois, com
  checklist. A regra que lhe dá sentido: **um processo não se conclui com
  tarefas pendentes**, e a recusa diz quantas faltam. Um processo sem tarefas
  nenhumas também não fecha. O último dia de trabalho é obrigatório na saída

- **BR-20 fechado** — 2026-08-23 — ADR-034. Atribuir um Cargo com autoridade
  de aprovação deixou de devolver `501`: cria uma atribuição **pendente** e
  submete-a a `approval`. Pendente **não confere Cargo nenhum**
  (`IsEffectiveAt` só reconhece efectivas), e é isso que mantém fechado o
  caminho de escalada. `POST /hr/position-assignments/{id}/approval-outcome`
  aplica a decisão — idempotente, e é `hr` que pergunta, porque `approval` não
  pode modificar dados do módulo de origem. Sem política configurada, a
  submissão é recusada com `409` e nada fica gravado
- **Férias** — 2026-08-23 — ADR-034. Segue o mesmo padrão de submissão da
  atribuição de Cargo (`docs` §1(d)): o pedido é criado, submetido a governança
  e **só é ausência depois de aprovado** — `CoversDate` só reconhece os
  aprovados. Recusa ausências sobrepostas; sem motor de governança responde
  `501`, sem política responde `409`. **Não verifica saldo**: as regras de
  acumulação e carry-over não estão detalhadas em `docs`, e um contador
  construído por suposição daria um número errado com ar de certo
- **Worker de reconciliação** — 2026-08-23 — varre as atribuições pendentes com
  processo associado e aplica as já decididas, sem ninguém carregar em nada. A
  promoção automática fica na trilha com **actor nulo**, distinta de uma feita
  por uma pessoa. Sondagem de 60&nbsp;s por omissão, desligável por
  configuração

## documents

- Upload e download com hash de integridade e tecto de 25 MB por ficheiro —
  2026-08-11
- Porta `IDocumentStorage` com implementação em sistema de ficheiros —
  2026-08-11
- Catálogo de metadados; a ligação ao contexto de origem e a classificação
  ficam na origem, não aqui — 2026-08-11 — ADR-009

**Por satisfazer:** conteúdo sem cifra em repouso (K11); ficheiro órfão se a
gravação de metadados falhar (K12).

## notifications

- Fila em tabela com estado; enfileirar **não** entrega — 2026-08-11 —
  contrato `INotifier`, corrige o anti-padrão A4
- Worker de entrega por sondagem, com recuo exponencial e lotes limitados —
  2026-08-11
- Leitura restrita ao destinatário e marcação como lida; o destinatário vem do
  token, nunca do pedido — 2026-08-11

**Por satisfazer — importante:** o canal registado é
`LoggingNotificationChannel`, que **escreve em log e não envia e-mail**.
O percurso de entrega (fila, worker, estado, recuo) é real e testável; o envio
não existe. Ver K13 em [known-issues.md](known-issues.md).

## Plataforma

- Solução `Rivo.slnx`: módulos em camadas
  API / Application / Domain / Infrastructure, mais o host `Rivo.Api` —
  2026-08-10. **A 2026-08-24 são 45 projectos em `src/` e nove módulos**
- Assembly `Rivo.X.Contracts` em `audit`, `documents`, `hr` e `notifications` —
  2026-08-11 — ADR-017. `identity` não tem, por não ter consumidor; criá-lo
  seria construir superfície pública para ninguém
- Um schema lógico por domínio e um `DbContext` por módulo — 2026-08-10 —
  ADR-002. **Motor trocado de PostgreSQL para SQL Server em 2026-08-20**, com
  as migrações regeneradas de raiz — ADR-029
- Migrações EF Core independentes por módulo, aplicadas no arranque quando
  `Database:MigrateOnStartup` o permite — 2026-08-10, gate revisto em
  2026-08-20 — ADR-030
- Docker Compose: `docker-compose.yml` com a API contra o SQL Server externo,
  `docker-compose.dev.yml` a acrescentar o motor em container para
  desenvolvimento e CI; imagem multi-fase, utilizador não-root — 2026-08-10,
  revisto em 2026-08-20
- `GET /health`, que verifica também o alcance da base de dados — 2026-08-10
- OpenAPI e Swagger UI, expostos por `OpenApi:Expose` — 2026-08-10, gate
  revisto em 2026-08-27 — ADR-038. A omissão é `IsDevelopment()`; no ambiente
  publicado abre-se com `EXPOSE_OPENAPI=true` no `.env`, **sem renomear o
  ambiente** — renomeá-lo desliga os cabeçalhos reencaminhados (K8) e põe a
  página de excepções de desenvolvimento à frente do pipeline
- Workflow de CI em GitHub Actions, dois jobs — 2026-08-16 — ADR-023. A
  ressalva de então ("nunca executado, o repositório não está sob git") caducou
  no mesmo dia: está em `y-jr/rivo-api` e ambos os jobs correm
- Deployment na VPS — 2026-08-23 — ADR-031. `.github/workflows/main.yml`: SSH,
  `git pull`, `compose up --build`, sonda de `/health`. Ambiente publicado em
  `http://187.77.178.242`, atrás de Caddy na rede `proxy`. **Sem TLS** enquanto
  não houver domínio — K16

### `GET /approval/requests/{id}/history` — 2026-08-28

Linha do tempo completa de um pedido: submissão, **todas** as atribuições
congeladas por passo, e cada decisão.

- **Difere do `GET` simples.** Esse devolve só `PendingAssignments` — quem falta
  decidir agora, para um cliente que espera pela sua vez. Este devolve todas as
  atribuições, incluídas as já decididas e as de passos futuros, mais os dados
  da submissão (requisitante, valor, moeda) que o outro não expõe
- Só leitura sobre o que já está gravado — não junta nada que `approval` não
  tivesse guardado

2 casos novos, dentro de `verify-hr` (18), a seguir ao cenário que já aprova uma
atribuição de cargo com autoridade (BR-20): reaproveita o pedido em vez de
montar outro.

### `POST /notifications/read-all` — 2026-08-28

Marca todas as não lidas do próprio como lidas. **É o primeiro pedido que um
cliente faz** depois de mostrar um contador de não lidas — sem a rota, marcar
cinquenta eram cinquenta chamadas.

- **Devolve quantas ficaram marcadas**, e não `204`: o cliente acabou de mostrar
  o contador, e assim confirma-o sem voltar a pedir a lista
- **Zero é resultado normal.** Sem nada por ler não se grava nada — uma gravação
  vazia incrementaria contadores de concorrência sem que nada tivesse mudado
- **Sem tecto**, ao contrário da listagem: «marcar todas» que deixasse algumas
  por marcar seria pior do que não existir
- O identificador vem do token e nunca do pedido, como no resto do módulo

4 casos novos em `verify-notifications` (17), e o caso do reinício passou para o
fim, como nas outras suites.

### `GET /documents` — 2026-08-28

Listagem do arquivo, com `category`, `from`, `to` e `limit`.

**A falta doía:** até aqui só se alcançava um documento sabendo o
identificador, e o identificador vive no módulo que o anexou. Um ficheiro
carregado e não ligado a registo nenhum — porque a ligação falhou, ou porque
ninguém a chegou a fazer — ficava irrecuperável.

- **Não substitui a listagem do contexto de origem.** Quem procura os anexos de
  um colaborador pede-os a `hr`, que sabe quais são (ADR-009). Esta serve quem
  procura no arquivo, e não no registo
- **Só os disponíveis.** Um documento anulado continua na base de dados por
  BR-14 — isso interessa a quem audita, não a quem procura um ficheiro
- **Sempre limitada**, com tecto de 200. Sem tecto, a rota cresce com o arquivo
  inteiro e o primeiro ano de uso torna-a inutilizável. Um `limit` acima do
  tecto é **cortado**, e não recusado

4 casos novos em `verify-documents` (17).

### Desactivar políticas de aprovação — 2026-08-27

`POST /approval/policies/{id}/deactivation`. Um endpoint, e sem agregado novo:
`ApprovalPolicy.Deactivate()` já existia no domínio e o store já devolvia a
política rastreada — faltava só a via.

- **Só desactivar, não reactivar.** A submissão recusa quando duas políticas
  igualmente específicas empatam (ADR-034), e reactivar uma antiga podia criar
  esse empate sem que quem reactiva o visse — a recusa apareceria depois, numa
  submissão sem relação com isso
- **Os pedidos em curso não mudam:** cada um guarda a política aplicada e os
  aprovadores congelados na submissão (BR-6)
- Repetível: desactivar uma já desactivada devolve `204` na mesma, e não enche
  a trilha de desactivações que não mudaram nada

**Tirou o SQL directo de duas suites**, que era a razão prática de o construir.

### Gestão de conta em `identity` — 2026-08-27

Seis endpoints que faltavam a qualquer sistema com contas, e nenhum precisou de
migração: o bloqueio do ASP.NET Core Identity já tinha as colunas.

- **Mudar a própria password** exige a actual — sem isso, um token roubado
  mudava a password e trancava o dono cá fora. **Termina as outras sessões** e
  mantém a de onde se mudou: quem muda fá-lo por suspeitar que alguém a sabe
- **Repor a password de outra conta** termina **todas** as sessões, avisa o
  dono, e fica na trilha com **acção própria** — é o caminho por onde uma conta
  é tomada, e quem audita tem de o encontrar sem o procurar entre as mudanças
  legítimas
- **Activar e desactivar contas.** ⚠ **Era a lacuna mais grave do módulo:** não
  havia como cortar o acesso a quem sai da empresa. Desactivar termina as
  sessões e fecha os **dois** caminhos de entrada — password e Google. Exige
  razão, e desactivar a própria conta é recusado
- **Ver e terminar as próprias sessões.** A lista marca a corrente, para não se
  terminar aquela de onde se está a olhar. Revogar a de outra pessoa devolve o
  mesmo que sessão inexistente
- **Retirar um perfil de acesso.** Sem efeito imediato no token já emitido — as
  permissões resolvem-se na autenticação (ADR-014)
- `GET /identity/users` passou a dar `isActive` e `roles`

Nova permissão `identity.users.write`, separada de `roles.assign`: quem atribui
perfis decide o que uma pessoa pode fazer; quem repõe passwords decide **quem
ela é**.

**20 testes de Application novos** (28 no módulo). Cinco dos seis endpoints
foram exercitados contra a stack.

⚠ **O sexto não.** `password-reset` rebentou com 500 — faltavam os token
providers do Identity, que `GeneratePasswordResetTokenAsync` exige. A correcção
(`AddDefaultTokenProviders()`) está aplicada e compila, **e não foi
exercitada**: o motor do Docker caiu durante a reconstrução.

⚠ **O bloqueio por tentativas falhadas continua inactivo.** As opções estão
configuradas (5 tentativas, 15 minutos), mas quem as aplica é o `SignInManager`,
que este módulo não usa — `CheckPasswordAsync` só compara o hash. Defeito
pré-existente, encontrado ao construir isto.

- `Rivo.Fiscal`, `Rivo.Commercial` e `Rivo.Finance` acrescentados à solução e
  ao host — 2026-08-24 — ADR-036
- Contrato HTTP escrito para quem consome a API de fora —
  [API-FRONTEND.md](../../API-FRONTEND.md), 2026-08-27. As **119 rotas** (118
  de módulo mais `/health`, 4 públicas) com permissão exigida, corpo de pedido
  e código de sucesso. Verificado contra o código: a contagem bate, e as
  permissões conferidas por amostragem — foi a conferir que apareceu o K18

## Verificação

**Doze suites** PowerShell caixa-preta contra a stack em Docker, **256 casos**,
todas re-executáveis.

> ⚠ **Estado da verificação a 2026-08-25, ao fechar a postagem automática.**
>
> A última corrida completa deu **190 de 191**. A única falha foi o caso 38 de
> `verify-ledger` — escrito nessa corrida — a correr contra o container
> construído **antes** da separação `DocumentNumber` / `ArchivalKey`. Falhou por
> afirmar exactamente a regra nova contra código velho.
>
> **A corrida de confirmação com o container reconstruído não chegou a
> acontecer:** o motor do Docker Desktop passou a responder `500` e não voltou.
> Os 45 casos de `verify-ledger` passaram isolados antes dessa última alteração.
>
> **Fechado a 2026-08-27.** O motor do Docker voltou, a stack foi reconstruída
> e `verify-all` deu **191 de 191**, já com `procurement` dentro da imagem e com
> o ADR-038 aplicado. A ressalva acima é histórico.
>
> **No mesmo dia passou a 246 de 246**, com `verify-procurement` a fechar a
> lista. Corrida duas vezes seguidas antes de cada entrada, para provar que se
> limpa a si própria — cria a política de que precisa e desactiva-a no fim.
>
> Os 629 testes .NET passam todos, os 4 de Testcontainers incluídos.

Eram seis suites e 66 casos em 2026-08-16. `verify-hr` cresceu 13 → 16 com as
funcionalidades novas de `hr`; `verify-authorization` 8 → 9 ao distinguir os
dois 404 de atribuição de perfil; e `verify-fiscal` (13), `verify-commercial`
(12) e `verify-finance` (18) nasceram em 2026-08-24, fechando a lacuna que os
três módulos do ADR-036 tinham deixado.

> **O runner reportava 71 até 2026-08-16.** `Select-String` é case-insensitive
> por omissão, e cinco das seis suites terminam com "Todos os testes
> passaram." — a palavra "passaram" casava com o padrão `PASSA` e era contada
> como um caso. Corrigido com `-CaseSensitive` e ancoragem ao formato que
> `Test-Case` emite. Os 66 são reais; os 71 eram 66 mais cinco linhas de
> resumo.

| Suite | Casos |
|---|---|
| `verify-bootstrap` | 9 |
| `verify-authorization` | 9 |
| `verify-audit` | 10 |
| `verify-hr` | 16 |
| `verify-documents` | 13 |
| `verify-notifications` | 13 |
| `verify-fiscal` | 13 |
| `verify-commercial` | 12 |
| `verify-finance` | 29 |
| `verify-payables` | 22 |
| `verify-ledger` | 45 |

`verify-finance` corre por último e **monta os seus pré-requisitos pelas rotas
de `fiscal` e `commercial`** — taxa, vigências e cliente. Não há atalho por SQL
de propósito: se a montagem falhar, é porque o caminho real de emissão está
partido, e é isso que interessa saber.

**`verify-all.ps1` demora cerca de 25 minutos**, e quase tudo é espera: seis das
nove suites reiniciam a stack para verificar persistência, e o SQL Server leva
perto de um minuto a voltar a ficar saudável de cada vez. Não é lentidão a
corrigir — é o preço de verificar que os dados sobrevivem ao processo.

⚠ **Filtrar respostas JSON do lado do PowerShell não é de confiar aqui.**
`Invoke-RestMethod` devolve arrays de forma inconsistente: desembrulha-os quando
têm um só elemento, e noutros casos entrega-os ao pipeline como **um só item**.
Nesse caso `$_.campo -eq $valor` compara uma lista com um escalar e devolve o
subconjunto correspondente, que sendo não-vazio é *verdadeiro* — o
`Where-Object` deixa passar tudo e o `[0]` apanha o registo errado.

Custou um falso `409` em `verify-hr` que parecia defeito de `approval`. Onde é
preciso escolher um registo específico, **usa-se `Invoke-Sql`**.

A partir de `docker compose down -v`:

```
docker compose up -d --build
pwsh -File scripts/verify-all.ps1
```

O runner espera que a stack assente entre suites: várias reiniciam containers
para verificar persistência, e em cadeia sem pausa a seguinte começaria contra
uma API ainda a subir.

## Testes de domínio

**100 testes, cinco módulos** — 2026-08-15 — ADR-022. xUnit v2.9.3, sem
biblioteca de asserções. Um projecto por domínio de módulo, em
`tests/Modules/<Módulo>/Rivo.<Módulo>.Domain.Tests/`.

```
dotnet test
```

| Módulo | Testes | O que fixam |
|---|---|---|
| `hr` | 45 | Vigência de Atribuição de Cargo (BR-6), **atribuição pendente não confere o Cargo** (BR-20/ADR-015), marca de autoridade (BR-21), desactivar não elimina (BR-14) |
| `notifications` | 20 | Leitura e entrega como estados independentes, recuo exponencial, abandono ao 5.º insucesso, propriedade do agregado |
| `documents` | 16 | Hash obrigatório, recusa de ficheiro vazio, anulação lógica que não apaga (BR-14) |
| `audit` | 10 | **Imutabilidade verificada por reflexão** (BR-10), actores não interactivos |
| `identity` | 9 | Sessão revogada deixa de valer de imediato, revogação idempotente, marcador explícito de IP desconhecido (BR-9) |

Correm em menos de 2 segundos, sem Docker e sem base de dados.

**Verificado por mutação** em 2026-08-15: removida a verificação de estado de
`PositionAssignment.IsEffectiveAt`, falhou exactamente um teste — o que fixa a
invariante que fecha a escalada de privilégios do ADR-015 — e mais nenhum. A
alteração foi revertida.

### Fora do implementado

_Actualizado a 2026-08-24 — o parágrafo anterior dizia que não existiam testes
de arquitectura, o que deixou de ser verdade em 2026-08-16._

**Existem 21 testes de arquitectura** (ADR-024, ADR-025): fronteiras de módulo,
camadas, ciclos, autorização declarada em todo o endpoint e contadores de
concorrência. As fronteiras deixaram de depender de revisão humana.

**Application: 51 testes** (2026-08-25) — `finance` 43, `identity` 8. Os
restantes sete módulos continuam sem. A **Infrastructure tem 4** (só
`notifications`). A camada API do host ganhou 9 em 2026-08-24
(`tests/Rivo.Api.Tests`, ADR-035); as APIs de módulo continuam sem testes
próprios.

`Rivo.Finance.Application.Tests` nasceu porque o ADR-022 fixou um projecto por
*domínio* de módulo, e em `finance` isso deixou de chegar: BR-5 tem uma metade
em `approval` e outra na conta, o saldo em aberto de uma factura é invariante
sobre o conjunto, e a taxa à data do facto gerador é orquestração entre dois
módulos. Nada disso cabe num agregado, e nada disso tinha teste unitário.

**Verificado por mutação** em 2026-08-25, duas vezes:

| Mutação | Falharam | Esperado |
|---|---|---|
| Desligar a verificação de estado em `ExecutePayment` (a ordem que era o defeito) | 2 | Segunda execução e pedido cancelado |
| Determinar a taxa da nota de crédito à data de hoje em vez da do facto gerador | 1 | O teste do ADR-011 §3 |

As alterações foram revertidas.

---

## approval

_2026-08-23 — ADR-034._

- Cinco camadas, schema `approval`, 17 testes de domínio
- `ApprovalPolicy` com passos por Cargo e modo `AnyApprover`/`AllApprovers`;
  `ApprovalRequest` com atribuições e decisões
- **BR-2** (quem submete não decide) e **BR-4** (uma pessoa decide uma vez por
  pedido) lançam `SegregationOfDutiesException` e devolvem **403** — não 409:
  não é o estado que impede, é *esta pessoa*
- **BR-6** — aprovadores e contexto congelados na submissão; a política fica
  como rasto, não como chave estrangeira viva
- Decisões imutáveis: corrigir é decidir outra vez
- **Sem endpoint de submissão, e é deliberado.** Submeter é acto do módulo de
  origem, por `IApprovalGateway`
- Dois consumidores: atribuição de Cargo (BR-20) e pedidos de férias, ambos de
  `hr`, ligados por inversão de dependência no composition root
- `PositionApprovalReconciliationWorker` aplica decisões por sondagem (60 s por
  omissão); a promoção automática fica na trilha com **actor nulo**
- `Manager` e `Finance` deixaram de ser perfis vazios

**Por satisfazer:** SLA e escalonamento (o passo guarda o prazo, nada o faz
cumprir), Delegação (modelada em `docs`, sem código), BR-7 e BR-8 e metade de
BR-3 — todos dependem de `finance` ter Planeamento e Tesouraria.

## fiscal

_2026-08-24 — **fatia mínima**, ADR-036._

- Cinco camadas, schema `fiscal`, 18 testes de domínio
- `TaxRateSchedule` — série de versões da mesma taxa, com **não sobreposição de
  vigências** imposta. É o que torna a determinação determinística
- Determinação **à data do facto gerador** (ADR-011 §3), que é parâmetro
  obrigatório e não `UtcNow`
- Instrumento legal obrigatório em cada versão (ADR-011 §4)
- `ITaxDetermination` publicado; `commercial` e `finance` consomem-no

⚠ **Não é o motor fiscal.** Fora: certificação AGT, exportação SAF-T,
declarações periódicas, IRT e INSS, e o catálogo de códigos de isenção — sem
ele, emitir com `ISE`/`NS` devolve **501**. `TaxCodes` só fixa `ISE` e `NS`,
que são os únicos verificados em fonte documentada.

## commercial

_2026-08-24 — **reduzido ao Cliente**, ADR-036._

- Cinco camadas, schema `commercial`, 20 testes de domínio
- `Customer` com nome, NIF e morada de facturação obrigatórios — o conjunto que
  o SAF-T exige do elemento `Customer`
- Desactivar, nunca eliminar (BR-14). **Não há `DELETE`**
- `ICustomerDirectory` publicado
- `Sales` deixou de ser perfil vazio

⚠ **Sem validação de formato do NIF**, porque as regras não estão verificadas
em fonte primária. **Sem consumidor final**: o NIF é obrigatório, logo não se
factura a quem não o forneça. **Sem funil comercial** — Lead, Oportunidade,
Proposta, Contrato Comercial e Acção de Cobrança não existem.

## finance

_2026-08-24 a 2026-08-25 — **AR, AP e Tesouraria**, ADR-036._

_A fatia de 2026-08-24 era só Contas a Receber; o resto veio no dia seguinte._

- Cinco camadas, schema `finance`, 34 testes de domínio
- `DocumentSeries` como agregado próprio, para impor numeração sequencial sem
  duplicados. Duas emissões simultâneas colidem no contador de concorrência da
  série e uma sai com **409**, em vez de saírem duas facturas com o mesmo número
- `SalesInvoice` **emitida inteira num acto só** — não há rascunho nem forma de
  acrescentar linha depois. A imutabilidade é imposta pela forma do agregado
- Cliente **congelado** na factura (nome, NIF, morada). Não contraria BR-18: uma
  factura é facto histórico, e resolver o cliente ao vivo reescreveria
  retroactivamente o passado
- Anular, nunca eliminar (BR-14), com motivo obrigatório. Anular duas vezes é
  recusado com 409
- Arredondamento por linha a duas casas; o total é a soma dos arredondados
- Quatro permissões separadas: `Sales` emite, `Finance` anula sem emitir,
  `Admin` abre séries

⚠ **As facturas não são documentos fiscais válidos em Angola** — têm a forma,
falta a certificação da AGT e a cadeia `Hash`/`HashControl` (ADR-036).

### Ciclo de venda fechado — 2026-08-25

- `CreditNote` (série NC) e `Receipt` (série RG), cada um com série própria
- **A nota de crédito herda o facto gerador da factura corrigida**, não a data
  de hoje: o imposto que se devolve é o que foi liquidado (ADR-011 §3)
- Saldo em aberto **calculado, não guardado** — total menos creditado menos
  recebido, contando só documentos não anulados. Uma coluna de saldo seria
  ponto de contenção a cada recebimento e ficaria errada em silêncio no dia em
  que alguém estornasse um recibo sem a recalcular
- Crédito e recebimento consomem o mesmo saldo. Receber ou creditar a mais é
  **409**, não 400: é conflito de estado
- Consumidor final, com o identificador vindo de configuração e menção fiscal
  congelada na emissão

### Contas a Pagar e Tesouraria — 2026-08-25

- `BankAccount`, `PurchaseInvoice`, `PaymentRequest`, `ExecutePayment`
- **BR-1, BR-3 e BR-5 impostas e verificadas.** A dupla barreira monta-se na
  Application porque nenhuma das metades cabe num agregado
- O **anti-padrão A3 do protótipo está fechado**: dois estados (`Eligible`,
  `Executed`) mais `Cancelled`. Sem "pendente de aprovação" — isso é estado do
  processo, e o que fica é um ponteiro
- Ligação a `approval` **por inversão**, e aqui é obrigatória: BR-8 fará
  `approval` ler `finance`, e uma referência directa traria de volta o ciclo
  que o ADR-034 fechou
- `Finance` paga sem pedir, `Manager` pede sem pagar — **BR-3 começa no
  catálogo de permissões**, antes de o domínio a impor

### Extracto de conta — 2026-08-25

- `BankMovement`, a linha do extracto. O saldo sozinho não é reconciliável:
  reconciliação bancária compara **movimentos**, não saldos
- O movimento **nasce dentro do agregado**, em `Deposit`/`Withdraw` — saldo e
  extracto alteram-se no mesmo acto ou não se alteram
- `BalanceAfter` **congelado**, não recalculado ao ler: se um dia o saldo
  divergir da soma, é essa coluna que mostra onde e quando
- `SourceType`/`SourceId` fazem o percurso de volta ao documento que causou o
  movimento — é o que a reconciliação precisa
- **Append-only imposto pelo motor**, com a mesma peça do K9: gatilho
  `INSTEAD OF UPDATE, DELETE` mais sentinela contra `TRUNCATE`
- As contas que já existiam receberam **um movimento de abertura** na migração

### Contabilidade & Fecho — 2026-08-25

- `LedgerAccount`, `Journal`, `JournalEntry` (+ linhas), `AccountingPeriod`
- **O plano de contas carrega-se, não vem semeado.** O XSD do SAF-T fixa a
  forma; o PGC angolano **não está em fonte primária** neste projecto, e
  inventá-lo seria pior do que não o ter — pareceria certo, e a divergência só
  apareceria no primeiro ficheiro entregue à AGT. Mesma posição de ADR-011 para
  as taxas
- **A partida dobrada é imposta no agregado.** Um lançamento nasce inteiro ou
  não nasce: sem rascunho, porque um lançamento a meio não equilibra
- Só contas de movimento (`GM`/`AM`) recebem lançamentos — lançar numa
  agregadora faria o total dela deixar de ser a soma das filhas
- `TransactionID` do SAF-T único, imposto por índice: é composto por três coisas
  que quem lança escolhe, e o ficheiro só seria recusado meses depois
- **Fechar um período pára a escrita**, e é isso que faz de um balancete já
  entregue um facto. Reabrir exige motivo, é de `Admin`, e fica na trilha com
  **acção própria**
- Balancete com abertura, movimento e fecho por conta — o que o
  `GeneralLedgerAccounts` do SAF-T precisa. A abertura de um período é o fecho
  do anterior, calculada e não guardada

⚠ **Período 1–16 e não 1–12.** A tabela em `docs/rivo-fiscal-saft-ao-v1.md` diz
1–12; o XSD em `docs/schemas/` restringe a 1..16. Segue-se o XSD — é contra ele
que o ficheiro valida.

### Planeamento — 2026-08-25

- `CostCentre`, `Budget` (+ linhas mensais), `DepartmentCostForecast`
- **Centro de Custo não é Departamento** (D4): o mapeamento é opcional e não é
  1:1, e o responsável pode não ser o gestor do departamento
- **Previsão de Custos não é Orçamento** (D3): uma é do departamento e alimenta
  o carregamento de caixa, a outra é do centro de custo e é tecto de controlo.
  Coexistem sobre o mesmo período e nunca se fundem — verificado por SQL, que
  confirma que nenhuma das tabelas tem a coluna da outra
- Um orçamento por centro de custo e ano: dois tectos tornariam BR-8 ambígua

### BR-8 fechada — 2026-08-25

- `IBudgetAvailability`: **uma pergunta e uma resposta**. É um dos dois pontos
  onde o `docs` avisa que o God Module pode nascer, e a estreiteza é a mitigação
- A verificação corre **antes da decisão e antes de resolver aprovadores** — o
  ponto de RN-017 é que ninguém decide sobre uma despesa que já se sabe não
  caber
- **Dos cinco resultados, um só deixa passar.** "Não consegui verificar" recusa
  como "não cabe"
- A rubrica **atravessa `approval` sem ser interpretada**, como o
  `SourceReference` — sem ela, `finance` teria de adivinhar a partir do
  departamento, e o mapeamento não é 1:1
- **`finance.planning.write` e `finance.budgets.approve` não se sobrepõem.** Se
  fossem a mesma pessoa, bastava subir o tecto para o pedido passar a caber
- Direcção `approval → Rivo.Finance.Contracts`, **sem ciclo**: `finance` usa a
  sua própria porta `IPaymentApproval`, e o composition root é que os liga

⚠ **Consumo = compromissos, não realizações.** Despesa que chegue aos livros sem
passar por um pedido de pagamento não consome orçamento — este número é um
limite inferior do consumo real. E a verificação é **à data de hoje**, não à do
pedido.

### Postagem automática — 2026-08-25

- `PostingRule`: um acontecimento, um diário, e linhas que dizem de que
  **parcela** do documento se servem — `Net`, `Tax` ou `Gross`
- **A tradução é configuração**, pela mesma razão que o plano de contas é: os
  códigos vêm do plano carregado, e embuti-los em código obrigaria a inventá-lo
- **A regra equilibra enquanto expressão**, verificada na configuração:
  `Gross = Net + Tax`, e os dois lados têm de dar a mesma soma de coeficientes.
  Apanha o erro que mais custaria — debitar o total e creditar só o líquido
  equilibra numa factura isenta e falha em todas as outras
- **Documento e lançamento na mesma transacção.** `PostDocument` não grava;
  acrescenta à unidade de trabalho de quem chama. Se a postagem falhar, o
  documento não é emitido — verificado: emitir para período fechado dá `409` e
  nem factura nem lançamento ficam gravados
- **Sem regra, não posta**, e isso é estado legítimo: ligar Contabilidade não
  pode partir a facturação de quem ainda não carregou um plano
- **Idempotente por construção**: o número de arquivo deriva do número do
  documento (`FT S001/42` → `FT-S001-42`), e o índice único do `TransactionID`
  é que impede o duplicado — não uma verificação que alguém pode esquecer
- Cinco acontecimentos ligados: factura de venda, nota de crédito, recibo,
  factura de compra e execução de pagamento
- `finance.ledger.close` para definir regras, e não `.write`: uma regra decide
  como **todos** os documentos futuros lançam

⚠ **Um período que ninguém abriu passou a aceitar lançamentos** — mudança de
semântica que a postagem obrigou. A linha regista um *fecho*, não dá licença;
exigi-la faria a facturação parar no dia 1 de cada mês.

⚠ **A anulação não estorna.** Anular um documento não gera lançamento inverso —
o original fica, e corrige-se por regularização, à mão. É a lacuna mais visível.

**Fora:** estorno automático, activos fixos e depreciação (bloqueados por
**K1**), adiantamentos, nota de débito, câmbio, e a reconciliação bancária
propriamente dita.

## procurement

_2026-08-27 — **os quatro agregados**. A cadeia pára no 3-way match._

- Cinco camadas, schema `procurement`, **100 testes de domínio**
- `Supplier`: nome, NIF único, IBAN, contactos, activação/desactivação.
  Desactivar, nunca eliminar (BR-14) — **não há `DELETE`**
- **IBAN verificado pela norma ISO 13616** (mod-97). É a única validação de
  formato do módulo, e a assimetria justifica-a: um NIF errado dá uma factura
  por corrigir, um IBAN errado paga a outra pessoa
- `PurchaseRequisition`: rascunho com linhas, submissão a `approval`, aplicação
  da decisão, cancelamento. **Depois de submetida não se altera** — o valor que
  seleccionou a faixa da alçada já foi congelado do outro lado (BR-6)
- `ISupplierDirectory` publicado, com procura por identificador e por NIF —
  este último para quem tem a factura na mão e não o identificador
- `IProcurementApprovalSubmission` invertido no composition root, como em `hr` e
  em `finance`. **`procurement` não sabe que `approval` existe**
- `ApprovalProcessTypes.PurchaseRequisition` — segundo processo com valor
  monetário, e o segundo sobre que BR-8 verifica orçamento. Sem rubrica: a
  verificação recua para o departamento
- `Manager` ganhou `ProcurementPermissions.ForRequesters`; `Finance` ganhou
  **só** `SuppliersRead` — quem fixa o IBAN não pode ser quem executa o
  pagamento

### Ordem de Compra — 2026-08-27

- **Só nasce de requisição aprovada.** Rascunho, pendente, recusada e cancelada
  recusam todas, e a mensagem diz em que estado está — as quatro corrigem-se de
  maneiras diferentes. Não há ordem avulsa: a rota é
  `POST /procurement/requisitions/{id}/orders`, e a forma diz a regra
- **Ao preço acordado, e não ao estimado.** A requisição diz o que se quer e
  por quanto se estima; a ordem diz o que se encomenda e por quanto se acordou.
  Entre as duas houve cotação — copiar o estimado faria dela campo decorativo
- **O total encomendado não passa o aprovado.** Uma requisição pode dar mais do
  que uma ordem (dividir por dois fornecedores é legítimo), mas três ordens de
  metade cada passariam uma a uma e, juntas, encomendavam acima da alçada.
  Invariante sobre o conjunto, na camada Application — mesma forma do
  `CommittedAsync` de `finance`
- **Cancelar devolve a alçada:** uma ordem cancelada deixou de ser compromisso
- Fornecedor desactivado não recebe encomendas; a moeda é herdada da requisição
- FK reais para a requisição e para o fornecedor — mesmo schema, mesmo módulo —
  e **sem cascata**: apagar uma requisição levaria atrás encomendas que saíram

### Recepção de Mercadoria — 2026-08-27

- **Só contra uma ordem em vigor**, e sempre ligada à **linha** da ordem que
  satisfaz. Receber duas unidades de uma coisa e nenhuma de outra somaria certo
  no total e estaria errado em tudo o resto — e é exactamente a divergência que
  o 3-way match existe para apanhar
- **Parcial é o caso normal.** Recepções sucessivas acumulam por linha, e a
  ordem só fica completa quando todas as linhas chegam por inteiro
- **Nunca acima do encomendado.** O acumulado conta, não só a contagem desta
  vez. Invariante sobre o conjunto, na camada Application
- **Quem recebeu é obrigatório** — uma divergência é uma conversa com alguém, e
  sem nome não há com quem a ter
- **Anular é corrigir um engano de registo**, não devolver ao fornecedor. A
  quantidade volta a contar como por receber, e o registo do erro fica (BR-14)
- **Uma ordem com mercadoria recebida não se cancela** — o material está cá, e
  cancelar a encomenda não o faz desaparecer
- A vista da ordem passou a dar `quantityReceived` por linha e `fullyReceived`
  na raiz: **dois dos três lados do match**

⚠ **Não gere stock, e é fronteira explícita.** `modules/procurement.md`
proíbe-o: níveis e valorização são de `inventory`. O facto fica registado e,
enquanto `inventory` não existir, ninguém o consome.

⚠ **A devolução ao fornecedor não existe.** É outro facto — sai material que
entrou, e do lado do dinheiro dá nota de crédito.

⚠ **O 3-way match não existe.** Faltam-lhe as duas pontas juntas: a factura de
compra é de `finance`, e ligá-la traz a direcção `finance → procurement`, que é
decisão arquitectural.

⚠ **A ordem não tem número próprio.** Escolher o formato — prefixo, reinício
anual, se admite saltos — é decisão de negócio sem fonte neste repositório.

⚠ **Não há tolerância de desvio sobre o aprovado.** Um limiar (5%? 10%?) é
decisão de negócio, e inventá-lo seria abrir a alçada por um número escolhido
aqui. É o ponto de configuração a preencher.

⚠ **`finance` ainda não consome o Fornecedor.** A factura de compra continua a
guardar nome e NIF em texto — e as já emitidas devem continuar assim, porque
guardam o que vigorava à data.

- **`verify-procurement`, 55 casos** — 2026-08-27. Décima segunda suite, e
  `verify-all` em **246/246**

**As duas limpezas deixaram de ser SQL** a 2026-08-27. `verify-ledger` e
`verify-procurement` desactivavam as suas políticas escrevendo directamente na
base de dados, porque não havia rota. Com
`POST /approval/policies/{id}/deactivation`, a limpeza passou a exercitar o
endpoint — deixou de ser só arrumação e passou a verificar também o caminho que
a torna possível. Uma suite que se limpa por um caminho que a aplicação não tem
verifica menos do que parece

O caso que justifica a suite é o **12**: a requisição é relida da base com as
duas linhas intactas. O mapeamento de uma colecção por campo de apoio é onde o
EF Core falha em silêncio — grava e relê sem as linhas, sem erro nenhum — e
nenhum teste de domínio o vê.

Os restantes cobrem o IBAN (normalização, mod-97, e que uma recusa não apaga o
que lá estava), a unicidade do NIF com o índice como segunda linha, BR-14 nos
quatro agregados, o círculo inteiro com `approval` — submeter, decidir do outro
lado, aplicar deste, e a idempotência da segunda chamada —, a ausência de FK a
sair do schema, e a segregação em três direcções: quem paga não qualifica o
fornecedor nem emite ordens, e quem encomenda não regista a chegada.

Os doze da Ordem de Compra fecham a alçada pelos dois lados: `1.000.000` e
`725.000` contra um aprovado de `1.725.000` entram, o kwanza seguinte não, e
cancelar a primeira devolve os `1.000.000` ao disponível.

Os treze da Recepção fazem o mesmo à quantidade: parciais de 4 e 6 somam 10 e
só aí a ordem fecha, receber acima do encomendado recusa contando o acumulado,
e anular devolve a quantidade a "por receber" deixando o registo do erro.

⚠ **A cobertura de Application continua a ser nenhuma**, como nos outros sete
módulos.

