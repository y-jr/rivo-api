# Implementado

_Última actualização: 2026-08-30._

Funcionalidade concluída e a funcionar, por módulo. Actualizar como parte de
terminar uma funcionalidade (passo 8 do fluxo em [CLAUDE.md](../CLAUDE.md)).

**Os catorze módulos têm código.** Dez estão completos ou em fatia
deliberada: `identity`, `audit`, `hr`, `documents`, `notifications`,
`approval`, `fiscal`, `commercial`, `finance`, `procurement`. Os últimos
quatro — `payroll`, `projects`, `inventory`, `fleet` — nasceram a 2026-08-29
como **esqueletos** sob prazo de apresentação: CRUD sem regra de negócio, sem
testes, sem verificação end-to-end. **Todos os quatro ganharam regra de
negócio real a 2026-08-30** — ver a secção própria de cada um mais abaixo.

⚠ **Dois estão reduzidos ao mínimo pelo ADR-036**, e não implementados por
inteiro: `fiscal` (taxa plana com vigência e determinação, mais a tabela de
escalões de IRT desde 2026-08-30 — continua sem SAF-T nem declarações) e
`commercial` (só Cliente). `finance` tem os cinco contextos internos desde
2026-08-25 e os documentos lançam nos livros, mas a contabilidade está de pé
e **vazia** — o plano de contas carrega-se e as regras de postagem
definem-se, e sem elas nada lança. Ver a ressalva em cada secção.

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

_2026-08-30 — segundo consumidor do desenho ADR-009._ `payroll` liga o
Recibo a um Item de Folha pelo mesmo padrão que `hr` usa para os documentos
de um colaborador — ver a secção `payroll` para o detalhe.

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

### Levantamento e fecho de conta bancária — 2026-08-28

`POST /finance/accounts/{id}/withdrawals`, `.../closure`, `.../reopening`. Os
dois primeiros já existiam no domínio — `BankAccount.Withdraw` e `.Close` —,
faltava a via.

- **`withdrawals` não é o pagamento a fornecedor.** Esse continua a passar por
  `ExecutePayment`, com a dupla barreira de BR-5. É para o resto do que sai de
  uma conta sem decisão de aprovação — comissões, transferências entre contas
- **Fechar passou a exigir saldo zero, e é regra nova no domínio.** Sem ela,
  fechar uma conta com dinheiro dentro escondia esse dinheiro atrás de uma
  conta que diz não estar em uso. É invariante de uma conta só, por isso vive
  em `BankAccount.Close()` e não na camada Application
- Reabrir não repõe saldo nenhum — devolve só o uso
- Levantar acima do saldo e fechar com saldo diferente de zero devolvem os
  dois `409`: é o estado da conta a impedir, não o corpo do pedido

A regra nova partiu um teste existente (`ContaFechada_NaoMovimenta`, que
fechava uma conta com 500.000 dentro) — corrigido para fechar com saldo zero, e
acrescentados os três cenários que a regra cria: não fecha com saldo, fecha e
reabre com saldo zero, esvazia e só depois fecha.

5 casos novos em `verify-payables` (27, com o caso do reinício movido para o
fim).

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

**20 testes de Application novos** (28 no módulo). **Os seis endpoints,
exercitados contra a stack** — o sexto só a 2026-08-28.

`password-reset` tinha rebentado com 500 — faltavam os token providers do
Identity, que `GeneratePasswordResetTokenAsync` exige. A correcção
(`AddDefaultTokenProviders()`) foi confirmada: o endpoint responde como
esperado numa corrida completa e limpa.

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

**Dezassete suites** PowerShell caixa-preta contra a stack em Docker, **336
casos**, todas re-executáveis.

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
>
> **Fechado a 2026-08-28.** As seis funcionalidades novas desse dia —
> correcção do `password-reset`, desactivação de políticas de `approval`,
> listagem de documentos, `read-all` de notificações, histórico de pedidos de
> aprovação, e levantamento/fecho de conta bancária — foram confirmadas contra
> a stack numa corrida completa e limpa, **262 de 262 casos, as doze suites**.
> `verify-hr` trazia também uma correcção de suite (caso 13: assumia que quem
> decide está sempre em `assignments[0]`, o que falha quando o Cargo tem mais
> do que um ocupante — corrigido para verificar pertença, não posição).
>
> **Falha intermitente encontrada e não resolvida, sem relação com nenhuma das
> seis.** Em corridas separadas da mesma cadeia completa, o passo que lista as
> políticas activas de um tipo e as desactiva uma a uma — presente em três
> sítios: `verify-ledger` caso 44, e duas vezes em `verify-procurement` (a
> limpeza inicial e o caso 55) — devolve por vezes um `404` inesperado numa
> política que a listagem, momentos antes, mostrava activa.
>
> Investigado a fundo, com instrumentação directa em cada um dos três sítios:
> a teoria de duplicação de linhas pelo `Include` sem `AsSplitQuery` do EF Core
> em `ApprovalStore.ListPoliciesAsync`/`ListPoliciesForProcessAsync` foi
> **descartada duas vezes com prova directa** — leitura instrumentada sem
> duplicados, incluindo logo a seguir a um reinício da API; a proximidade a um
> reinício também foi descartada, porque o caso 44 nunca corre perto de um e
> falhou na mesma. A lógica do endpoint e do caso de uso (`DeactivatePolicy`)
> está correcta — só devolve `404` quando a política genuinamente não existe.
> Não há processo concorrente a interferir na mesma base de dados, e
> `Restart-RivoStack`, por omissão, nunca reinicia a base de dados (só a API).
>
> **Sem causa de código confirmada.** A leitura mais provável é a base de
> dados partilhada de desenvolvimento ter acumulado estado de um dia com
> vários crashes do motor Docker e tarefas mortas a meio da corrida — mas duas
> corridas limpas consecutivas depois de o Docker estabilizar não bastam para
> o provar, e nesta mesma sessão o defeito voltou a aparecer numa corrida
> posterior, já com o Docker estável. Fica registado como investigação em
> aberto, não como defeito fechado nem como causa atribuída: **se reaparecer,
> reabrir a suspeita de defeito de aplicação em vez de assumir ambiente.**
>
> **Continuado a 2026-08-28, sessão seguinte, a correr as verificações
> pendentes do trabalho por commitar do 3-way match.**
>
> Antes de a stack sequer arrancar: um **impasse de arranque num volume
> novo**, distinto de tudo o que está registado acima — a API nunca chegava a
> `/health`, num ciclo de tentativas e reinícios sem fim. **Corrigido**, com
> três arranques limpos consecutivos a prová-lo. Detalhe em
> [known-issues.md, K19](known-issues.md) — resolvido no mesmo dia em que foi
> encontrado.
>
> Com a stack de pé, `verify-procurement` cresceu de 55 para **58 casos** — a
> limpeza que a nota acima chama "caso 55" passou a caso 58, empurrada pelos
> três novos: o 3-way match (`GET /finance/purchase-invoices/{id}/match`) —
> encomendado, recebido e facturado lado a lado, recusa ligar a uma ordem
> doutro fornecedor, e regista divergência de valor sem bloquear. **Três casos
> novos falharam na primeira corrida** (54, 55, 56) — não por defeito da
> aplicação:
> a suite usava `$financeHeaders` para registar a factura, e é `Manager` quem
> tem `PayablesWrite` — `Finance` é tesouraria, e "quem desfaz não faz"
> também quer dizer que não emite (`AccessProfiles.cs`). Corrigido nos três
> casos; confirmados numa corrida limpa a seguir, **265 de 267**.
>
> **A falha intermitente da limpeza de política — os casos 44 acima e o novo
> 58 — reapareceu: terceira vez, mesma conclusão.** Investigada de novo, desta
> vez com uma teoria concreta e
> testável: `implemented.md` já registava, noutro contexto, que
> `Invoke-RestMethod` por vezes entrega uma lista ao pipeline como um só item,
> e o `Where-Object` seguinte deixa passar tudo. O padrão dos dois casos
> batia certo com essa descrição. Aplicada a correcção (`@(...)` a forçar
> array) aos dois pontos — **e o 404 continuou a acontecer, com a correcção
> no lugar.** A protecção fica, por ser válida noutro sentido; a causa
> continua sem se confirmar. Promovido a **K20** em
> [known-issues.md](known-issues.md), com as três investigações resumidas
> num sítio só.
>
> Corrida final, limpa, a partir de `docker compose down -v`: **265 de 267,
> as doze suites** — os dois casos de K20 são a única falha conhecida por
> resolver.
>
> **Continuado a 2026-08-29.** Quatro suites novas para os módulos esqueleto
> — `verify-payroll` (16), `verify-projects` (14), `verify-inventory` (13),
> `verify-fleet` (15) — e uma quinta, `verify-approval` (10), a fechar a
> lacuna do cancelamento de pedidos (K18) que nenhuma suite exercitava pela
> rota. Escrevê-las apanhou três defeitos reais (`Vehicle.Deactivate()` sem
> rota, as quatro entidades a sair directas na resposta HTTP, `payroll` a
> devolver 500 em vez de 400) — ver `payroll, projects, inventory, fleet`
> acima para o detalhe. **332 de 335**, cada suite nova corrida isolada
> contra o ambiente publicado — os 3 que faltam são o K20, agora em três
> sítios (`verify-ledger`, `verify-procurement`, `verify-payroll`), não dois.
> `verify-all.ps1` cresceu de doze para dezassete suites.
>
> **Estorno automático, mesmo dia.** `verify-ledger.ps1` ganhou o caso 44
> (`ReverseDocumentPosting`), empurrando os dois casos finais para 45 e 46 —
> o de limpeza (K20) e o de persistência. `known-issues.md` actualizado com o
> renumerar. Corrida isolada, a seguir a `verify-payables.ps1` (de quem
> depende a política genérica de `finance.payment_request`): **46 de 46**,
> zero falhas — desta vez nem o caso 45 apanhou o K20. As dezassete suites
> somam agora **336 casos** (era 335); numa corrida completa continuam a
> esperar-se até 3 K20 conhecidos (ledger #45, procurement #58, payroll #15),
> não uma garantia de zero.

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
| `verify-hr` | 18 |
| `verify-documents` | 17 |
| `verify-notifications` | 17 |
| `verify-fiscal` | 13 |
| `verify-commercial` | 12 |
| `verify-finance` | 29 |
| `verify-payables` | 30 |
| `verify-ledger` | 46 |
| `verify-payroll` | 16 |
| `verify-approval` | 10 |
| `verify-procurement` | 58 |
| `verify-projects` | 14 |
| `verify-inventory` | 13 |
| `verify-fleet` | 15 |

**336 casos ao todo** — contagem directa de `Test-Case` em cada script, e não
soma acumulada de entradas anteriores desta tabela, que tinha ficado para trás
em `verify-documents`, `verify-hr` e `verify-notifications`, e nunca chegara a
incluir `verify-procurement`. As cinco últimas linhas nasceram a 2026-08-29.

> **Continuado a 2026-08-30.** `verify-fiscal` cresceu de 13 para 20 (motor
> de IRT/INSS — semeia INSS e a Tabela B, idempotente por vigência real) e
> `verify-payroll` de 16 para 17 (cálculo real substitui a verificação de
> campos nulos, mais o caso de recusa por falta de dados fiscais); ver as
> secções `fiscal` e `payroll` acima para o detalhe. `verify-projects`,
> `verify-fleet` e `verify-inventory` cresceram no mesmo dia por razões não
> fiscais (Orçamento, Plano de Manutenção, Movimento). **398 casos ao todo**
> (era 336); `verify-all.ps1` completo confirmado em **395/398** — as 3
> falhas são o K20 (limpeza de política), em `verify-ledger` (caso 45),
> `verify-payroll` (caso 16, renumerado — era 15) e `verify-procurement`
> (caso 58), nenhuma nova.

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
- **Cancelar exige ser quem submeteu** — 2026-08-29, fecha o K18. Mesma família
  de excepção de BR-2/BR-4 (`SegregationOfDutiesException`), mesmo `403` e não
  `409`. A permissão do endpoint mantém-se `approval.requests.read` — a regra
  é do domínio, não da permissão

**Por satisfazer:** SLA e escalonamento (o passo guarda o prazo, nada o faz
cumprir), Delegação (modelada em `docs`, sem código), BR-7 e BR-8 e metade de
BR-3 — todos dependem de `finance` ter Planeamento e Tesouraria. Verificação
end-to-end do cancelamento — não existe `verify-approval.ps1`, e é o único
caminho de escrita do módulo sem cobertura caixa-preta nenhuma.

## fiscal

_2026-08-24 — **fatia mínima**, ADR-036._

- Cinco camadas, schema `fiscal`, 18 testes de domínio
- `TaxRateSchedule` — série de versões da mesma taxa, com **não sobreposição de
  vigências** imposta. É o que torna a determinação determinística
- Determinação **à data do facto gerador** (ADR-011 §3), que é parâmetro
  obrigatório e não `UtcNow`
- Instrumento legal obrigatório em cada versão (ADR-011 §4)
- `ITaxDetermination` publicado; `commercial` e `finance` consomem-no

⚠ **Não é o motor fiscal completo.** Fora: certificação AGT, exportação
SAF-T, declarações periódicas, e o catálogo de códigos de isenção — sem ele,
emitir com `ISE`/`NS` devolve **501**. `TaxCodes` só fixa `ISE` e `NS`, que
são os únicos verificados em fonte documentada.

_2026-08-30 — **motor de cálculo de IRT/INSS.**_ IRT e INSS deixam de estar
fora de âmbito. `TaxKind` ganhou `EmployeeSocialSecurity` e
`EmployerSocialSecurity` — o INSS (3%/8%, sem tecto, confirmado pelo
utilizador) carrega-se pelo mecanismo já existente de `TaxRateSchedule`,
sem desenho novo, porque é uma taxa plana como o IVA.

O IRT precisou de agregado novo: `IncomeTaxSchedule` — série de versões de
uma **tabela** de escalões progressivos, mesmo padrão de vigência e
`InForceOn` de `TaxRateSchedule`, mas cada versão guarda vários
`IncomeTaxBracket` (Parcela Fixa + Taxa × Excesso de) em vez de um único
número. `SelectBracket` escolhe o escalão de maior "excesso de" que a
matéria colectável ainda ultrapassa — nunca iguala — o que reproduz 150.000
como isenção e 150.001 já no escalão seguinte (salto de 12.500 Kz,
confirmado pelo utilizador, não corrigido como se fosse defeito).

`IIncomeTaxDetermination` é contrato novo, distinto de `ITaxDetermination`:
devolve o **montante já calculado**, não só a taxa — decisão deliberada,
porque a fórmula do escalão é regra fiscal (autoridade exclusiva de
`fiscal`), e "percentagem × montante" não é. Rotas novas:
`GET /fiscal/income-tax-schedule`, `POST /fiscal/income-tax-schedule/versions`,
`GET /fiscal/income-tax-schedule/determination`.

**Defeito real, só visível contra a API a correr**: o `switch` exaustivo
`ListTaxRates.ToDomain`/`ToContract` (ADR-010, tradução `Domain.TaxKind` ↔
`Contracts.TaxKind`) não tinha entrada para os dois valores novos — 500 ao
determinar INSS, não apanhado por `dotnet test` porque nenhum teste de
domínio ou de aplicação passava por esse caminho de tradução. Corrigido no
mesmo dia.

Testes: `Rivo.Fiscal.Domain.Tests` cresceu de 18 para 39 (`IncomeTaxSchedule`,
incluindo o exemplo documentado bruto 250.000 → IRT 38.900 e as fronteiras
exactas da Tabela B). `verify-fiscal.ps1` cresceu de 12 para 20 — semeia o
INSS e a Tabela B **de forma idempotente por código e vigência reais**
(2020-01-01 em diante), ao contrário dos casos 1-12 da mesma suite, que usam
um código por corrida. Consumido por `payroll` — ver secção própria.

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

**Fora:** activos fixos e depreciação (K1 fechado por ADR-039 a 2026-08-30 —
deixaram de estar bloqueados por ownership, continuam sem código),
adiantamentos, nota de débito, câmbio, e a reconciliação bancária
propriamente dita.

### Estorno automático — 2026-08-29

Anular uma factura, uma nota de crédito ou um recibo passou a gerar o
lançamento inverso, na mesma unidade de trabalho da anulação —
`ReverseDocumentPosting` (`Rivo.Finance.Application/UseCases/PostDocument.cs`),
registado em `modules/finance.md`. Detalhe completo lá; aqui só o essencial:

- Inverte as linhas do lançamento **original** (mesma conta, mesmo valor,
  lado trocado) — não as da regra de postagem actual, que pode ter mudado
  entretanto.
- Lança com a data de **hoje**, num período próprio, nunca no período do
  documento original — funciona mesmo que esse período já tenha fechado.
- O original fica intacto (BR-14); é a soma dos dois lançamentos que cancela
  o efeito, não a alteração de um deles.
- Sem lançamento original (documento emitido antes de haver plano de contas),
  não há nada a estornar, e isso não bloqueia a anulação.
- Se o estorno não se consegue lançar (período de hoje fechado, diário
  desactivado), a anulação também não se grava — mesma disciplina que
  `PostDocument` já impunha à emissão.

8 testes de Application novos (`ReverseDocumentPostingTests.cs`), mais um caso
de verificação end-to-end (`verify-ledger.ps1` caso 44 — os dois que fecham a
suite passaram a 45 e 46, ver known-issues.md).

### Factura de compra ligada ao Fornecedor — 2026-08-28

`RegisterPurchaseInvoice` passou a consumir `ISupplierDirectory`, publicado
por `procurement` desde a Ordem de Compra existir e até agora sem consumidor.
O domínio já aceitava `SupplierId` opcional — faltava só a via.

- **Indicado directamente, tem de existir.** Quem regista já sabe o
  fornecedor (escolhido numa lista) e passa o identificador; se não existir em
  `procurement`, é recusado com `400` — quem chama afirmou uma ligação que não
  é verdade, e ignorá-la em silêncio esconderia um erro do próprio cliente
- **Sem identificador, tenta-se ligar pelo NIF.** É o caso comum: quem tem a
  factura em papel não tem o identificador. Não encontrar não é erro — nem
  toda a despesa passa por um Fornecedor qualificado (uma factura de
  electricidade, por exemplo)
- **`supplierName`/`supplierTaxId` continuam obrigatórios em ambos os casos, e
  não são substituídos pelo que `procurement` tiver guardado.** O documento é
  facto histórico (BR-18) — o retrato é o que veio no papel, a ligação é só
  para saber a quem se deve, não para reescrever o que a factura diz
- **Não é retroactivo.** As facturas já registadas continuam com
  `SupplierId` nulo; a ligação só se aplica ao que se regista daqui para a
  frente

Direcção `finance → procurement` activada em
`ProjectReferenceTests.DependenciasDeclaradas` — já estava pré-aprovada em
`architecture/dependency-rules.md`, só não estava ligada. Sem ADR novo: a
decisão arquitectural já tinha sido tomada, isto foi aplicá-la.

4 testes de Application novos (132 no módulo), 3 casos novos em
`verify-payables` (30, casos 5 a 7, a seguir ao que já cobria o número do
fornecedor e o índice único).

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

⚠ **O 3-way match não existe.** A ligação ao Fornecedor está feita
(2026-08-28, ver `finance`) — falta comparar **quantidades e valores** entre
Ordem, Recepção e a factura de compra, que é a comparação que dá nome à
prática.

⚠ **A ordem não tem número próprio.** Escolher o formato — prefixo, reinício
anual, se admite saltos — é decisão de negócio sem fonte neste repositório.

⚠ **Não há tolerância de desvio sobre o aprovado.** Um limiar (5%? 10%?) é
decisão de negócio, e inventá-lo seria abrir a alçada por um número escolhido
aqui. É o ponto de configuração a preencher.

~~⚠ `finance` ainda não consome o Fornecedor.~~ **Fechado a 2026-08-28** — ver
`finance`, "Factura de compra ligada ao Fornecedor".

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

## payroll

_2026-08-29 — **esqueleto**, sob prazo de apresentação. Decisão explícita,
registada aqui e em cada `modules/*.md`, não descoberta depois. `projects`,
`fleet` e `inventory` nasceram no mesmo lote — ver as secções próprias
abaixo — mas saíram desta categoria a 2026-08-30._

Os catorze módulos passam a ter código nesse dia. Cada um: cinco camadas, um
schema próprio, migração inicial, CRUD por permissão de `identity`. **Zero
testes de domínio, zero regra de negócio além da que o próprio CRUD impõe**,
neste.

**Verificação end-to-end escrita e corrida a 2026-08-29** —
`scripts/verify-payroll.ps1` (16 casos), `verify-projects.ps1` (14, ver
secção `projects` para o que cresceu depois), `verify-inventory.ps1` (13,
ver secção `inventory`), `verify-fleet.ps1` (15, ver secção `fleet`), mesmo
padrão das outras dez suites (schema isolado, permissão por perfil, CRUD,
401/403, trilha de auditoria, persistência ao reiniciar). Confirmam o
contrato HTTP, não regra de negócio — não há regra a confirmar.

Escrever as suites apanhou três defeitos reais, nenhum cosmético:

- **`Vehicle.Deactivate()` sem rota.** Existia no domínio, não tinha
  endpoint — só se descobriu ao tentar verificar o caso "desactivar esconde
  da listagem". Acrescentado `POST /fleet/vehicles/{id}/deactivation`.
- **As quatro entidades de domínio saíam directas na resposta HTTP**,
  violando a regra de `architecture/dependency-rules.md` — `Status` (enum)
  serializava como o inteiro subjacente (`0`/`1`), não como `"Active"`.
  Corrigido com `View` (DTO) e `ToView(...)` em cada endpoint de listagem e
  consulta, nos quatro módulos.
- **`payroll`: mês fora de 1–12 devolvia 500, não 400.**
  `PayrollRun.Open()` lança `ArgumentOutOfRangeException`; `OpenPayrollRun`
  não a apanhava. `OpenPayrollRun.ExecuteAsync` passou a devolver
  `OpenRunResult` (sucesso ou rejeição com mensagem), e o endpoint mapeia a
  rejeição para `Results.ValidationProblem` (400).

**K20 reapareceu uma quarta vez**, no bloco de limpeza inicial de
`verify-payroll.ps1` — mesmo padrão exacto das outras duas suites (ver
`known-issues.md`). Não se reabriu a investigação; esse bloco (só ele, por
correr fora de `Test-Case`, antes do caso 1) foi envolvido em `try/catch`
para não abortar a suite inteira por um 404 já conhecido e sem causa de
código confirmada.

- **`payroll`** — `PayrollRun` (ano/mês, estado) e `PayrollItem`
  (colaborador, salário bruto). Submete-se a `approval` pelo total bruto,
  mesmo desenho de `IProcurementApprovalSubmission`: sem ciclo a quebrar,
  composition root a traduzir. Aprovar/recusar aplica-se deste lado
  (`MarkApproved`/`MarkRefused`) quando `payroll` pergunta — `approval` nunca
  altera dados de negócio de origem.

  **Os campos de cálculo existem e ficam sempre nulos** —
  `NetSalary`, `WithholdingTax`, `SocialSecurityContribution`. A ordem do
  cálculo do IRT está confirmada em lei (artigo 7.º do Código do IRT, Lei
  n.º 18/14), mas os escalões concretos vêm de `fiscal`, que não tem tabela
  angolana carregada, e `CLAUDE.md` proíbe implementar a partir do
  levantamento não verificado. Um número calculado sem regra real por trás
  mentiria pior do que a ausência do campo — decisão tomada em sessão, não
  suposta.

  Confirmado: abrir folha, acrescentar item, submeter devolve `409` sem
  política configurada para `payroll.payroll_run` — mesmo comportamento que
  `procurement` tem sem política, não falha nova.

_2026-08-30 — **motor de cálculo de IRT/INSS, com regra de negócio real.**_
Deixa de ser esqueleto: `AddPayrollItem` pergunta a `fiscal`, nunca calcula
por si (`modules/fiscal.md` — "nenhum outro módulo implementa regra de
imposto"). Ordem exacta do artigo 7.º do CIRT: determina o INSS do
trabalhador (`TaxKind.EmployeeSocialSecurity`, código `INSS`) à data do fim
do período (`PayrollRun.PeriodEndDate`, não `UtcNow` — ADR-011 §3), deduz-o
do bruto para obter a matéria colectável, pede o IRT sobre essa matéria a
`IIncomeTaxDetermination` (ver secção `fiscal` abaixo), e só então
`PayrollItem.ApplyCalculation(withholdingTax, socialSecurityContribution)`
grava os três campos — `NetSalary` é sempre `GrossSalary − WithholdingTax −
SocialSecurityContribution`, calculado dentro do método, nunca recebido como
terceiro parâmetro que pudesse discordar da soma.

**Recusa, não omissão**: sem taxa de INSS ou tabela de IRT em vigor à data
do facto gerador, o item não nasce — `AddItemOutcome.FiscalDataMissing`,
mapeado a 400 (`ValidationProblem`), mesmo padrão de `IssueSalesInvoice`
perante `TaxDeterminationOutcome.NoRateInForce`. Testado abrindo uma folha
de 2019, fora da vigência semeada por `verify-fiscal.ps1` (2020-01-01 em
diante).

**Um segundo defeito 400-vs-409 apanhado e corrigido no mesmo lote**: o
`catch (Exception error) when (error is InvalidOperationException or
ArgumentOutOfRangeException)` original de `AddPayrollItem` mapeava tanto
"folha já não está em rascunho" (conflito de estado) como "salário não
positivo" (campo mal preenchido) para o mesmo outcome — `Rejected`, sempre
409. Separado em dois `catch`: `ArgumentOutOfRangeException` → `Rejected`
(400), `InvalidOperationException` → `Conflict` (409). Mesma disciplina
aplicada em `fleet` e `projects` a 2026-08-30 (ver secções próprias).

Testes: `Rivo.Payroll.Domain.Tests` (novo projecto, 16 casos) —
`PayrollItem.ApplyCalculation` e o ciclo `PayrollRun`, incluindo o exemplo
documentado ponta-a-ponta (bruto 250.000 → líquido 203.600). `verify-payroll`
cresceu de 5 (campos ficam nulos) para 17 casos — o cálculo real substitui
essa verificação, mais um caso novo para a recusa por falta de dados
fiscais. Ver `state/known-issues.md` K20: o caso de limpeza de política
mudou de número (era 15, passou a 16) mas continua o mesmo defeito
pré-existente, sem causa de código.

_2026-08-30, mesmo dia — **Recibo, ligado a `documents`.**_ `PayrollItemDocument`
— entidade independente, não filha do agregado `PayrollRun`: anexar um
documento não é decisão da folha, é registo à parte, feito depois de a folha
já ter dito o que tinha a dizer. Mesmo desenho exacto de
`Rivo.Hr.Domain.EmployeeDocument` (ADR-009): FK real para
`payroll.payroll_item(id)`, e para `documents.document(id)` por SQL numa
migração própria — `AddCrossSchemaDocumentForeignKey`, mesmo nome e mesma
razão da versão de `hr` ("quando `payroll` migra, `documents` já tem de
estar migrado" — `Program.cs` já garantia essa ordem). Índice único em
`document_id`: um ficheiro só se liga uma vez, mesma defesa de `hr`.

**Upload e anexar são passos separados**, mesma disciplina de `hr`: o
upload (`POST /documents`) exige `documents.write`; anexar
(`POST /payroll/runs/{runId}/items/{itemId}/documents`) exige
`payroll.runs.write`, porque está a alterar-se o registo do item, não o
arquivo. `ListPayrollItemDocuments` junta em memória o que `payroll` sabe
(a ligação) com o que `documents` sabe (nome, tipo, tamanho) — mesma
junção de `ListEmployeeDocuments`.

**Uma regra de negócio nova, e assinalada como inferência**: só se anexa
um documento a um item de uma folha **Aprovada** — 409 antes disso. Não é
requisito confirmado em `docs/`; é dedução desta sessão (um recibo é prova
do que foi autorizado, e os valores de um item ainda podem mudar em Draft
ou PendingApproval), registada como tal em `modules/payroll.md` para ser
revista se aparecer um caso de uso real que precise do contrário. `hr` não
impõe nada semelhante a `EmployeeDocument` — a diferença é deliberada, não
copiada por inércia.

Testes: `Rivo.Payroll.Domain.Tests` cresceu de 16 para 22
(`PayrollItemDocumentTests`, mesmo desenho de `EmployeeDocumentTests` em
`hr`). `verify-payroll` cresceu de 17 para 22 casos: recusa antes de
Aprovada (409), anexar com sucesso, listar com os metadados de `documents`,
documento inexistente (404), e o anexo auditado com actor. Todos passaram à
primeira corrida contra a stack local — zero defeito apanhado, ao contrário
das duas rondas anteriores do mesmo dia (`fleet`, e o motor de IRT/INSS
acima).

## projects

`Project` (nome, datas, estado Active/Closed) nasceu esqueleto a 2026-08-29 —
ver a secção acima para esse lote. _2026-08-30 — **Marco e Tarefa, com regra
de negócio real.**_

`Project` passou a agregado raiz de dois filhos, ambos com a mesma invariante
comum: nada se acrescenta nem se altera depois de o projecto fechar
(`EnsureActive`, mesma leitura que impede reabrir o próprio `Project`).

- **Marco** — nome, data alvo (não anterior ao início do projecto), estado
  Pending/Reached. `Reach(reachedOn)` vale uma vez só — um marco alcançado
  não volta a "por alcançar".
- **Tarefa** — título, prazo opcional (não anterior ao início do projecto),
  atribuição opcional a Colaborador, estado Pending/Done/Cancelled. Concluir
  e cancelar são estados finais: nenhum dos dois se repete nem se reverte um
  no outro. Cancelar nunca elimina (BR-14) — fica como facto histórico, mesma
  leitura de `PurchaseRequisition.Cancel`.

**A atribuição de Tarefa referencia o Colaborador só por identificador**
(ADR-010): a camada Application verifica que existe em `hr`
(`IEmployeeDirectory.FindAsync`) antes de gravar — devolve 404 se não existir
— e nunca copia nome, departamento ou cargo (BR-18). Mesma forma da
verificação do requisitante em `procurement.OpenRequisition`. `hr` entrou nas
dependências declaradas de `projects` em `ProjectReferenceTests` — já estava
prevista em `architecture/dependency-rules.md`, só por ligar.

A entidade de domínio deixou de sair directa da API: `ProjectView`,
`MilestoneView` e `ProjectTaskView` (em `Rivo.Projects.Application`, mesmo
padrão de `RequisitionView` em `procurement`) substituem o `ProjectView` que
antes vivia na camada Api — `GetProject`/`ListProjects` passaram a devolvê-los
directamente, com Marcos e Tarefas aninhados.

Seis endpoints novos, todos sob `projects.projects.write`:
`POST /projects/{id}/milestones`,
`POST /projects/{id}/milestones/{milestoneId}/reached`,
`POST /projects/{id}/tasks`,
`POST /projects/{id}/tasks/{taskId}/assignment`,
`POST /projects/{id}/tasks/{taskId}/completion`,
`POST /projects/{id}/tasks/{taskId}/cancellation`.

**29 testes de domínio** (`Rivo.Projects.Domain.Tests`, novo projecto —
primeiro teste de qualquer um dos quatro esqueletos de 2026-08-29).
`scripts/verify-projects.ps1` cresceu de 14 para 28 casos e **confirmou
28/28 contra a stack local a 2026-08-30**, sem nenhuma falha — nem sequer o
K20 habitual, porque esta suite não toca políticas de `approval`.

**Orçamento, na mesma sessão (2026-08-30), desbloqueado por ADR-040.**
`Project` ganhou um terceiro filho, `ProjectBudget` — **zero ou um por
projecto**, ao contrário de Marco e Tarefa: `SetBudget(amount, currency, at)`
cria-o da primeira vez e revê-o depois, sem histórico de revisões, só o
valor actual.

- **A moeda fixa-se na primeira vez.** Uma revisão para outra moeda é
  recusada (`InvalidOperationException` → 409) — não porque a conversão seja
  impossível, mas porque decidir a taxa de câmbio não é decisão deste
  método.
- **Distinto do orçamento por centro de custo de `finance`** (ADR-040,
  ADR-037) — as duas entidades nunca se fundem. A validação cruzada (uma
  despesa de projecto contra o disponível de `finance`, sem duplicar a
  entidade) continua por desenhar; este fecho só implementa a entidade e a
  regra dentro de `projects`.
- Sujeito à mesma invariante de Marco e Tarefa: nem definir nem rever é
  possível depois de o projecto fechar.

`ProjectBudgetView` (em `ProjectView.Budget`, nulo até `SetBudget` ser
chamado) segue o mesmo padrão de `MilestoneView`/`ProjectTaskView`. Um
endpoint novo, `POST /projects/{id}/budget`, serve tanto a definição inicial
como a revisão.

**10 testes de domínio novos** (`Rivo.Projects.Domain.Tests` cresceu de 29
para 39). `scripts/verify-projects.ps1` cresceu de 28 para 33 casos e
**confirmou 33/33 contra a stack local a 2026-08-30, sem nenhuma falha na
primeira corrida**.

**Continua por fazer:** Alocação de Recursos (pessoas além da atribuição de
Tarefa, viaturas, custos) — sem decisão própria ainda, ver "Perguntas em
aberto" em `modules/projects.md`.

## fleet

`Vehicle` (matrícula única, modelo, estado Active/InMaintenance/Inactive)
nasceu esqueleto a 2026-08-29 — ver a secção `payroll` acima para esse lote.
_2026-08-30 — **Manutenção e Atribuição, com regra de negócio real.**_

`Vehicle` passou a agregado raiz de dois filhos:

- **Manutenção** (`MaintenanceRecord`) — tipo (Preventive/Corrective),
  descrição, data de início, data de fim opcional. Só um registo aberto de
  cada vez por viatura; `Vehicle.Status` continua a resumir o estado.
  `SendToMaintenance()`/`ReturnFromMaintenance()` (o par booleano do
  esqueleto) foram substituídos por `OpenMaintenance(...)`/
  `CloseMaintenance(maintenanceId, ...)`, que criam e fecham um registo real
  em vez de só mudar um enum.
- **Atribuição** (`VehicleAssignment`) — motorista (Colaborador por
  identificador), data de início, data de fim opcional. Só uma atribuição
  aberta de cada vez; reatribuir exige terminar a actual primeiro.

**Manutenção e Atribuição não se excluem** — uma viatura atribuída pode ir
para revisão sem perder o motorista, e vice-versa. Só `Status == Inactive`
bloqueia as duas.

**A atribuição referencia o Colaborador só por identificador** (ADR-010): a
camada Application verifica que existe em `hr`
(`IEmployeeDirectory.FindAsync`) antes de gravar — devolve 404 se não existir
— e nunca copia nome, departamento ou cargo (BR-18). Mesma forma da
verificação de `hr` em `projects.AddTask`. `hr` entrou nas dependências
declaradas de `fleet` em `ProjectReferenceTests` — já estava prevista em
`architecture/dependency-rules.md`, só por ligar.

A entidade de domínio deixou de sair directa da API: `VehicleView`,
`MaintenanceRecordView` e `VehicleAssignmentView` (mesmo padrão de
`ProjectView` em `projects`) substituem o mapeamento `ToView` que antes vivia
na camada Api.

Quatro endpoints novos, todos sob `fleet.vehicles.write`:
`POST /fleet/vehicles/{id}/maintenance`,
`POST /fleet/vehicles/{id}/maintenance/{maintenanceId}/closure`,
`POST /fleet/vehicles/{id}/assignments`,
`POST /fleet/vehicles/{id}/assignments/{assignmentId}/closure`. O antigo
`POST /fleet/vehicles/{id}/maintenance` (corpo `{ inMaintenance: bool }`)
desapareceu — substituído pelo par abrir/fechar.

**25 testes de domínio** (`Rivo.Fleet.Domain.Tests`, novo projecto).
`scripts/verify-fleet.ps1` cresceu de 15 para 26 casos e **confirmou 26/26
contra a stack local a 2026-08-30**.

**A corrida apanhou dois defeitos reais**, mesma causa em dois sítios:
`OpenMaintenance` e `Assign` deviam devolver `409 Conflict` quando a viatura
já estava em manutenção/atribuída ou inactiva — devolviam `400
ValidationProblem`, porque um único `catch` apanhava `ArgumentException`
(pedido malformado, 400 correcto) e `InvalidOperationException` (conflito de
estado, devia ser 409) e mapeava os dois para o mesmo desfecho. Corrigido
separando os `catch` e acrescentando o desfecho `Conflict` a
`OpenMaintenanceOutcome`/`AssignVehicleOutcome`. **O mesmo defeito latente
existia em `projects.AddMilestone`/`AddTask`** (fechar o projecto também
lança `InvalidOperationException`) — corrigido no mesmo padrão antes de se
manifestar lá por falta de teste; `verify-projects.ps1` caso 23 passou de
esperar 400 a esperar 409.

**Plano de Manutenção, na mesma sessão (2026-08-30).** `Vehicle` ganhou um
terceiro filho, `MaintenancePlan` — calendário preventivo, distinto do
registo histórico de Manutenção: os dois não se ligam automaticamente,
concluir um ciclo do plano não exige um registo.

- **Vários planos activos ao mesmo tempo são normais** — "óleo a cada 90
  dias" e "pneus a cada 180 dias" são dois planos da mesma viatura, sem
  exclusão mútua (ao contrário de Manutenção e Atribuição).
- **`CompleteCycle` reagenda a partir de quando foi concluído**, não da data
  que estava marcada — não empilha ciclos em atraso se a conclusão vier
  tarde.
- **`Cancel` não tem guarda de `Status`** — cancelar os planos de uma
  viatura que acabou de ficar inactiva é o que se espera, ao contrário de
  agendar ou concluir um ciclo, que continuam bloqueados em `Inactive`.

**O "alerta" é uma consulta, não uma notificação empurrada** —
`GET /fleet/maintenance-plans/due?withinDays=N` lista viaturas com plano
activo devido até N dias a partir de hoje, incluindo o já atrasado, via novo
`IVehicleStore.ListWithDuePlansAsync`. **Decisão tomada nesta sessão:**
`notifications.INotifier.QueueAsync` entrega a um `RecipientUserId` de
`identity`, e não existe forma de resolver "todos os `AssetManager`" para um
destinatário concreto — essa capacidade não está em `identity`. Inventar
essa resolução aqui seria adivinhar uma peça de outro módulo que não foi
decidida; a consulta é o alerta possível sem isso.

Três endpoints novos, todos sob `fleet.vehicles.write`, mais uma leitura:
`POST /fleet/vehicles/{id}/maintenance-plans`,
`POST /fleet/vehicles/{id}/maintenance-plans/{planId}/cycles`,
`POST /fleet/vehicles/{id}/maintenance-plans/{planId}/cancellation`,
`GET /fleet/maintenance-plans/due`.

**17 testes de domínio novos** (`Rivo.Fleet.Domain.Tests` cresceu de 25 para
42). `scripts/verify-fleet.ps1` cresceu de 26 para 38 casos e **confirmou
38/38 contra a stack local a 2026-08-30, sem nenhuma falha na primeira
corrida** — a distinção 400 (pedido malformado) vs. 409 (conflito de
estado), corrigida mais cedo no mesmo dia (ver acima), já nasceu aplicada
correctamente aqui.

**Continuam por fazer:** Registo de Viagem, Despesa de Frota, Seguros.

## inventory

`InventoryItem` (SKU único, nome, unidade) nasceu esqueleto a 2026-08-29 —
ver a secção `payroll` acima para esse lote. _2026-08-30 — **Movimento, com
regra de negócio real, desbloqueado por ADR-039.**_

`InventoryItem` passou a agregado raiz de `StockMovement` — Recepção, Saída
e Ajuste, os três tipos que fazem sentido sem Armazém (Transferência fica de
fora, `modules/inventory.md` §Perguntas em aberto continua a assinalá-la).

- **Recepção** — quantidade positiva, aumenta `QuantityOnHand`.
- **Saída** — quantidade positiva, reduz `QuantityOnHand`. **Nunca abaixo de
  zero**: sair mais do que há em mão é recusado (409), não truncado.
- **Ajuste** — correcção de contagem, para cima ou para baixo. **Exige
  motivo** — uma correcção sem explicação é exactamente o que a validação
  existe para impedir. Também nunca pode puxar `QuantityOnHand` para
  negativo.

**`QuantityOnHand` deixou de ser um campo escrito directamente — é a soma
assinada de `Movements`**, mantida a cada movimento aceite. Um item inactivo
não aceita nenhum dos três.

A entidade de domínio deixou de sair directa da API: `InventoryItemView` e
`StockMovementView` (mesmo padrão de `ProjectView`/`VehicleView`) substituem
o mapeamento `ToView` que antes vivia na camada Api.

Três endpoints novos, todos sob `inventory.items.write`:
`POST /inventory/items/{id}/movements/receipts`,
`POST /inventory/items/{id}/movements/issues`,
`POST /inventory/items/{id}/movements/adjustments`.

**21 testes de domínio** (`Rivo.Inventory.Domain.Tests`, novo projecto), com
um teste dedicado à invariante de fundo:
`QuantityOnHand == Movements.Sum(m => m.Quantity)` depois de uma sequência
de recepções, saídas e ajustes. `scripts/verify-inventory.ps1` cresceu de 13
para 25 casos e **confirmou 25/25 contra a stack local a 2026-08-30, sem
nenhuma falha na primeira corrida** — a distinção 400 (pedido malformado) vs.
409 (conflito de estado), corrigida em `fleet` e `projects` mais cedo no
mesmo dia (ver secção `fleet`), já nasceu aplicada correctamente aqui: não
apanhou nada.

**Continuam por fazer:** Armazém, Transferência, Contagem, valorização de
stock.

Permissões atribuídas aos perfis que já esperavam por módulos de negócio:
`ProjectManager` (estava vazio) fica com `projects`; `AssetManager` ("gere
activos e existências") fica também com `inventory` e `fleet`;
`HumanResources` fica com `payroll`, por ser onde a folha nasce hoje. Nenhum
dos sete Perfis de Acesso continua vazio — `verify-bootstrap` caso 2
verifica isto e foi corrigido no mesmo dia: assumia `ProjectManager` sempre
vazio, deixou de ser verdade.

Testes de arquitectura ajustados: `ProjectReferenceTests.DependenciasDeclaradas`
precisava dos quatro módulos novos e de `Identity` a apontar para eles. 21/21.

