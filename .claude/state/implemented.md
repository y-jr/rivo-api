# Implementado

_Última actualização: 2026-08-25._

Funcionalidade concluída e a funcionar, por módulo. Actualizar como parte de
terminar uma funcionalidade (passo 8 do fluxo em [CLAUDE.md](../CLAUDE.md)).

**Nove dos catorze módulos têm código:** `identity`, `audit`, `hr`,
`documents`, `notifications`, `approval`, `fiscal`, `commercial`, `finance`.
Os restantes cinco — `procurement`, `payroll`, `projects`, `inventory`,
`fleet` — estão definidos em [modules/](../modules/) e não têm código.

⚠ **Três estão reduzidos ao mínimo pelo ADR-036**, e não implementados por
inteiro: `fiscal` (só taxa com vigência e determinação), `commercial` (só
Cliente), `finance` (só a factura de venda). Ver a ressalva em cada secção.

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
- OpenAPI e Swagger UI expostos **só em `Development`** — 2026-08-10
- Workflow de CI em GitHub Actions, dois jobs — 2026-08-16 — ADR-023. A
  ressalva de então ("nunca executado, o repositório não está sob git") caducou
  no mesmo dia: está em `y-jr/rivo-api` e ambos os jobs correm
- Deployment na VPS — 2026-08-23 — ADR-031. `.github/workflows/main.yml`: SSH,
  `git pull`, `compose up --build`, sonda de `/health`. Ambiente publicado em
  `http://187.77.178.242`, atrás de Caddy na rede `proxy`. **Sem TLS** enquanto
  não houver domínio — K16
- `Rivo.Fiscal`, `Rivo.Commercial` e `Rivo.Finance` acrescentados à solução e
  ao host — 2026-08-24 — ADR-036

## Verificação

**Dez suites** PowerShell caixa-preta contra a stack em Docker, **141 casos**
(2026-08-24), todas re-executáveis.

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
| `verify-payables` | 17 |

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

Continua por cobrir, e é o desequilíbrio que cresce a cada módulo: **Application
tem 8 testes** (só `identity`) contra 273 no domínio, e a **Infrastructure tem
4** (só `notifications`). A camada API do host ganhou 9 em 2026-08-24
(`tests/Rivo.Api.Tests`, ADR-035); as APIs de módulo continuam sem testes
próprios.

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

_2026-08-24 — **só Contas a Receber**, ADR-036._

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

**Fora:** Contas a Pagar, Tesouraria, Contabilidade & Fecho, Planeamento,
recebimentos, nota de crédito. Com eles ficam por fazer BR-1, BR-3, BR-5 e o
disponível orçamental que BR-8 exige de `approval`.
