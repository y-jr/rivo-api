# Problemas Conhecidos

_Última actualização: 2026-08-27._

Este ficheiro regista duas coisas distintas, e a distinção importa:

- **Lacunas de arquitectura (K1–K7)** — pontos assinalados em `docs/` e não
  resolvidos. Não são decisões em aberto; são buracos conhecidos no desenho.
- **Defeitos (K8–K18)** — comportamento real do código implementado que não
  satisfaz um requisito. Seis módulos estão em produção de desenvolvimento,
  logo há defeitos de código a registar.

  Fechados: K8, K9, K14, K15. Abertos: K10, K11, K12, K13, K16, K17, K18.

Os anti-padrões do protótipo ficam listados à parte, porque a tentação de os
repetir é real.

## Lacunas assinaladas em `docs/` e não resolvidas

| # | Lacuna | Impacto | Onde |
|---|---|---|---|
| K1 | Sobreposição entre Activos Fixos (`finance`, com depreciação) e Activos (`inventory`) | Nenhum dos módulos pode assumir ownership; bloqueia modelação de ambos | `docs` §1.2; [pending-decisions](pending-decisions.md) |
| K2 | Motor de cálculo fiscal não detalhado (taxas de IVA e incidência, escalões de IRT, taxas de INSS, mapeamento de códigos de isenção). O **modelo de dados** está fixado pelo XSD do SAF-T AO; as **regras de cálculo** não | Bloqueia `fiscal`, e por arrasto `commercial`, `procurement`, `payroll` | [modules/fiscal.md](../modules/fiscal.md) |
| K7 | Cadeia de `Hash`/`HashControl` do SAF-T implica assinatura ordenada e imutável dos documentos — requisito arquitectural ainda sem desenho | Afecta `commercial` e `finance`; tem impacto em concorrência e em ordenação de emissão | [modules/fiscal.md](../modules/fiscal.md) |
| K3 | Fluxo de despesa eventual avulsa do SGAP não coberto | Lacuna funcional — expansão de `procurement`, não módulo novo | `docs` §2 |
| K4 | Validação de conformidade documental antes da decisão (checklist DAF do SGAP) | Lacuna — `docs` aponta para expansão de `fiscal` como serviço de validação | `docs` §2 |
| K5 | Anti-fraccionamento (janela 30 dias) é regra nova, sem precedente no protótipo | Precisa de desenho — regra de `approval` alimentada por dados de `finance` | [domain/business-rules.md](../domain/business-rules.md) BR-7 |
| ~~K6~~ | ~~Disponibilidade de tesouraria ligada à execução~~ | **Fechado a 2026-08-25.** `BankAccount` é a disponibilidade e `ExecutePayment` verifica-a como metade de BR-5. O extracto (`BankMovement`) tornou-a auditável: o saldo deixou de ser um número sem explicação | [modules/finance.md](../modules/finance.md) |

## Anti-padrões do protótipo a não repetir

Registados porque a tentação de os repetir é real:

| # | Anti-padrão | Correcção |
|---|---|---|
| A1 | 5 implementações paralelas de aprovação | Motor único em `approval`; nenhum módulo tem passos de aprovação próprios |
| A2 | 2 tabelas de auditoria quase idênticas (`audit_logs`, `payroll_audit_logs`) | Capacidade única `audit` |
| A3 | Workflow de aprovação embutido em `payment_requests` | Pedido de Pagamento tem estado `elegível`/`executado`; a decisão vive em `approval` |
| A4 | Trigger que inseria até 20 notificações na mesma transacção da mudança de estado | Efeitos secundários fora da transacção de negócio |
| A5 | Storage de ficheiros reinventado por módulo (`file_url`, `pdf_path`, …) | Capacidade única `documents` (ADR-009) |
| A6 | `audit_logs` sem coluna de IP | `ip` obrigatório por desenho |
| A7 | RBAC com 4 papéis em código vs. 7 perfis no documento de produto | Catálogo único em `identity` |
| A8 | Política de escrita em tabelas de aprovação = "qualquer membro autenticado", verificação real só no frontend | Imposição no servidor/domínio (ADR-008) |
| A9 | `employees` sem FK para `auth.users` — "autenticado" e "colaborador" podiam não coincidir | Ligação explícita e opcional (ADR-004) |

## Defeitos activos

### ~~K8 — IP da sessão é o do proxy~~ — **RESOLVIDO 2026-08-16**

Fechado na Fase 1. `ForwardedHeadersMiddleware` activo fora de `Development`,
com `KnownNetworks` e `KnownProxies` vazios e `ForwardLimit = 1`.

Com as duas listas vazias o cabeçalho é aceite de qualquer origem, e isso só é
seguro porque não há outra origem: o container não publica porto nenhum no
host, e o único caminho até ele é o reverse proxy, que reescreve
`X-Forwarded-For`.

**⚠ Publicar o porto 8080 no host reabre este defeito em silêncio**, e nada no
código o detecta. A garantia é topológica, e a topologia é a do ADR-031.

**⚠ Pôr `ASPNETCORE_ENVIRONMENT=Development` no ambiente publicado reabre-o
também** — a condição é `if (!app.Environment.IsDevelopment())`. Aconteceu
entre 2026-08-26 e 2026-08-27: o commit `0301ef5` fixou `Development` no
`docker-compose.yml` para abrir o Swagger, e levou os cabeçalhos
reencaminhados atrás. Refechado pelo **ADR-038**, que deu ao Swagger
interruptor próprio e devolveu o nome do ambiente ao que era.

<details><summary>Registo original</summary>

### K8 — IP da sessão é o do proxy, não o do cliente

- **Módulo:** `identity`
- **Impacto:** `HttpContext.Connection.RemoteIpAddress` devolve o endereço de
  quem estabelece a ligação TCP. Com a API em container, isso é o gateway da
  rede Docker (`::ffff:172.20.0.1`); atrás de um balanceador, será o
  balanceador. **O IP guardado em `user_session` não identifica o cliente**,
  o que esvazia o requisito de auditoria BR-9.
- **Contorno:** nenhum. Correr a API directamente no host regista o IP
  correcto, mas não é a topologia de produção.
- **Seguimento:** configurar `ForwardedHeadersMiddleware` para ler
  `X-Forwarded-For`. **Não é trivial:** aceitar esse cabeçalho sem restringir
  os proxies de confiança permite a qualquer cliente forjar o próprio IP —
  troca um registo inútil por um registo falsificável, que é pior. Exige
  saber a topologia de produção (há proxy? qual? que redes?), que ainda não
  está decidida.

### ~~K9 — Garantia append-only não é imposta pela base de dados~~ — **RESOLVIDO 2026-08-16, refeito em SQL Server 2026-08-20**

Fechado pela migração `EnforceAppendOnly`. Em SQL Server (ADR-029), os três
caminhos de destruição estão cobertos por duas peças:

| Caminho | O que o impede |
|---|---|
| `UPDATE` | Gatilho `INSTEAD OF`, que lança e aborta a transacção |
| `DELETE` | O mesmo gatilho |
| `TRUNCATE` | A tabela `audit.audit_event_truncate_guard`: o motor recusa truncar uma tabela referenciada por FK |

`TRUNCATE` precisou de peça própria porque, ao contrário do PostgreSQL, o SQL
Server não dispara gatilhos nessa instrução.

**As duas peças foram reutilizadas a 2026-08-25** em `finance.bank_movement`, o
extracto de conta. A razão é a mesma que aqui: um registo que se pode editar não
serve para o que existe — uma trilha reescrita não audita nada, e um extracto
reescrito não reconcilia nada. **A ressalva abaixo vale igualmente para essa
tabela.**

**⚠ Fica por fazer a metade que depende de privilégios:** quem for dono da
tabela pode remover o gatilho e a sentinela. Protege contra o erro, não contra
o adversário com privilégios totais — e a base de dados é hoje acedida com
`sa`. O seguimento é um utilizador aplicacional restrito aos schemas do Rivo,
com papel separado para retenção; está em
[pending-decisions.md](pending-decisions.md).

<details><summary>Registo original</summary>

- **Módulo:** `audit`
- **Impacto:** `AuditEvent` é imutável em código (sem setters públicos, sem
  métodos de alteração), mas nada impede um `UPDATE` ou `DELETE` directo em
  `audit.audit_event`. BR-10 exige append-only.
- **Contorno:** nenhum. A imutabilidade actual depende de a aplicação ser o
  único caminho de escrita.
- **Seguimento:** revogar `UPDATE`/`DELETE` na tabela para o utilizador
  aplicacional, com um papel separado para retenção. Depende da decisão sobre
  utilizadores de base de dados por módulo, que está em aberto.

</details>

### K10 — Escrita da trilha não é transaccional com a operação auditada

- **Módulo:** `audit` + consumidores
- **Impacto:** `audit` tem `DbContext` próprio, logo a escrita da trilha e a
  operação de negócio são transacções distintas. Se a segunda falhar depois
  de a primeira ter sido gravada, fica registada uma acção que não aconteceu;
  se falhar a escrita da trilha, a operação de negócio é abortada (as
  excepções propagam-se deliberadamente).
- **Contorno:** o comportamento actual erra do lado seguro — falha ruidosa em
  vez de perda silenciosa de auditoria.
- **Seguimento:** padrão outbox, se o volume ou a fiabilidade o exigirem.
  Não é necessário à escala actual.

### K11 — Documentos sem cifra em repouso — **REABERTO EM 2026-08-20**

Esteve fechado entre 2026-08-16 e 2026-08-20, por `BlobDocumentStorage` sobre
Azure Blob Storage: cifra do serviço, sem a aplicação ver nem gerir chave
nenhuma.

**Com o deployment em VPS (ADR-031) não há Blob Storage.** O armazenamento
volta a ser o sistema de ficheiros, no volume `rivo-documents-data` — sem cifra
em repouso, e desta vez com dados reais, que era exactamente a distinção que
tornava o caminho local aceitável.

O código de Blob continua lá e a escolha é por configuração
(`DocumentStorage:AccountName`), não por ambiente: basta uma conta para o
fechar outra vez. Alternativas na VPS: cifra ao nível do sistema de ficheiros
ou do disco, ou um serviço compatível com S3.

<details><summary>Registo original</summary>

### K11 — Documentos sem cifra em repouso

- **Módulo:** `documents`
- **Impacto:** `standards/security.md` exige **AES-256 em repouso** para
  anexos. O armazenamento em sistema de ficheiros guarda-os em claro. Um
  acesso ao volume lê contratos de trabalho e documentos fiscais.
- **Contorno:** nenhum ao nível da aplicação.
- **Seguimento:** normalmente resolve-se abaixo da aplicação — volume cifrado,
  ou cifra do lado do servidor no armazenamento de objectos. Cifrar na
  aplicação exigiria gestão de chaves, que é decisão pendente; criptografia
  com chave mal gerida seria pior do que esta ausência assinalada.
  **Depende da decisão sobre o serviço de armazenamento de produção.**

### K12 — Ficheiro órfão se a gravação de metadados falhar

- **Módulo:** `documents`
- **Impacto:** o conteúdo é escrito antes do registo. Se a gravação em base de
  dados falhar, fica um ficheiro sem metadados a apontar-lhe.
- **Contorno:** o modo de falha inverso — metadados a apontar para ficheiro
  inexistente — seria pior, e é por isso que a ordem é esta.
- **Seguimento:** limpeza periódica de ficheiros sem registo correspondente.
  Não urgente: o órfão ocupa espaço mas não corrompe nada.

### K13 — Notificações não são entregues fora da aplicação

- **Módulo:** `notifications`
- **Impacto:** o canal registado é `LoggingNotificationChannel`, que escreve
  uma linha de log e devolve. A fila, o worker, os estados e o recuo
  exponencial são reais; **o envio de e-mail não existe**. Uma notificação com
  `SendEmail = true` é marcada como entregue sem que ninguém a receba.
- **Contorno:** nenhum. É deliberado e está documentado no próprio código — o
  canal existe para que o percurso de entrega seja testável sem fornecedor.
- **Seguimento:** implementar `INotificationChannel` sobre o provider de
  e-mail transaccional e substituir o registo. **Depende da decisão de
  provider**, que está em aberto. Até lá, não confiar em notificação por
  e-mail para nada que tenha consequência — designadamente para pedidos de
  aprovação quando `approval` existir.

### ~~K14 — Concorrência optimista não implementada~~ — **RESOLVIDO 2026-08-16**

Fechado por [ADR-025](../decisions/adr-025-concorrencia-optimista.md). Coluna
`version` como token de concorrência em seis agregados, com três isenções
justificadas e imposição por teste de arquitectura. Verificado contra o motor
real: de duas escritas com a mesma versão de partida, a primeira afecta uma
linha e a segunda nenhuma — e o EF Core lança em vez de sobrepor.

O mecanismo é gerido pela aplicação e não pelo motor, e foi por isso que
atravessou a troca para SQL Server sem uma linha de alteração (ADR-029).

Deixou atrás de si o K15, que é a metade que faltava.

### ~~K15 — Colisão de concorrência devolve `500`, não `409`~~ ✅ Fechado em 2026-08-24

- **Módulo:** todos os que têm agregados com `version`
- **Impacto:** o ADR-025 fez com que uma escrita concorrente passe a lançar
  `DbUpdateConcurrencyException` em vez de sobrepor em silêncio. Mas **nenhum
  handler a tratava**: a excepção subia e o cliente recebia `500 Internal
  Server Error`. Semanticamente errado — não é falha do servidor, é conflito de
  estado, e o cliente devia poder reler e repetir.
- **Fechado por:** ADR-035. `ConcurrencyConflictHandler`, um `IExceptionHandler`
  registado no composition root, traduz a excepção em `409 Conflict` com
  `ProblemDetails`.

  Ficou no host e não em cada módulo porque **nenhuma camada Application
  referencia o EF Core** — não há onde apanhar a excepção dentro do módulo sem
  lhe arrastar a infraestrutura. Registado uma vez, vale para os seis módulos, e
  um módulo novo herda-o sem fazer nada.

  Sem repetição automática, de propósito: repetir sozinho uma decisão de
  aprovação aplicá-la-ia sobre um estado que o autor não viu, que é o que BR-17
  existe para impedir.

  Verificado por 5 testes em `tests/Rivo.Api.Tests` — o primeiro projecto de
  teste da camada API do host.

## Formato para defeitos futuros

```
## <título curto>
- Módulo: <módulo>
- Impacto: <o que falha ou está em risco>
- Contorno: <se houver>
- Seguimento: <o que tem de acontecer>
```

### K16 — Sem TLS no acesso à API publicada

**Detectado em 2026-08-23**, na configuração do reverse proxy da VPS.

A VPS ainda não tem domínio, e o Let's Encrypt exige um nome — não emite
certificados para endereços IP. O reverse proxy (Caddy, em
`/opt/projects/proxy/`) serve por IP em HTTP simples.

**Consequência:** o token JWT viaja em claro. Quem observe a rede entre o
cliente e a VPS lê-o, e com ele passa a poder agir como o utilizador até a
sessão expirar — sessenta minutos (ADR-013). O mesmo vale para as credenciais
enviadas a `POST /identity/login`.

**Isto não pode ir para produção.** É aceitável apenas enquanto o ambiente for
de teste e sem dados reais.

**Correcção:** obter um domínio, apontar o DNS ao IP da VPS, e trocar o bloco
`:80` do `Caddyfile` pelo nome. O Caddy obtém e renova o certificado sozinho —
o volume `caddy-data` já está no compose precisamente para os guardar.

**Nota:** o proxy vive fora do repositório do Rivo de propósito. É partilhado
com os outros sistemas da organização (ADR-031), e uma aplicação não deve ser
dona de infraestrutura partilhada.

### K17 — A documentação da API está aberta num ambiente sem TLS

**Detectado em 2026-08-27**, a auditar o commit `0301ef5`.

O ambiente publicado serve `/swagger` e `/openapi/v1.json` — decisão
deliberada de quem o opera, agora com interruptor próprio (ADR-038). O
documento descreve **as 119 rotas, os corpos de pedido e as permissões
exigidas**, e viaja em HTTP simples enquanto o K16 durar.

**Consequência:** quem observe a rede não precisa de adivinhar a superfície da
API — ela é-lhe entregue. Não é escalada de privilégio, é poupança de
reconhecimento: continua a ser preciso um token válido para chamar seja o que
for. Mas com o K16 aberto, o token também viaja em claro.

**Contorno:** `EXPOSE_OPENAPI=false` no `.env` da VPS fecha-o sem tocar em
código nem no nome do ambiente.

**Seguimento:** fecha com o K16. Com TLS, o risco desce ao que o Swagger é em
qualquer API interna — e a decisão de o deixar aberto deixa de ter custo.

### ~~K18 — Cancelar um pedido de aprovação exige permissão de leitura~~ — **RESOLVIDO 2026-08-29**

**Detectado em 2026-08-27**, a cruzar o `API-FRONTEND.md` com o código.

`POST /approval/requests/{requestId}/cancellation` estava protegido só por
`approval.requests.read`. Todos os outros endpoints de escrita de `approval`
exigem uma permissão de escrita: `approval.policies.write` para criar
políticas, `approval.requests.decide` para decidir.

**Impacto:** quem conseguia ver um pedido de aprovação conseguia cancelá-lo.
`CancelRequest` não verificava quem submeteu — não havia dono a comparar.

**Decisão tomada:** cancelar é acto de quem submeteu — a mesma pergunta que
BR-2 e BR-3 já tinham respondido para decidir e para pagar, respondida do
mesmo modo. `ApprovalRequest.Cancel` já tinha o comentário desta intenção
("só o requisitante... o faz"), nunca implementado; ficou implementado
tal como o comentário sempre disse, com uma excepção — sem o carve-out para
"quem administra" que o comentário também mencionava, nunca confirmado como
requisito e por isso não incluído.

**Corrigido por:** `ApprovalRequest.Cancel(cancelledByEmployeeId, at)` recusa
com `SegregationOfDutiesException` quando o chamador não é
`RequestedByEmployeeId` — mesma família de excepção de BR-2/BR-4.
`CancelRequest` (Application) apanha-a e audita a tentativa
(`SegregationViolationAttempted`), como já acontecia para decisões. O
endpoint devolve `403`, não `409` — não é o estado do pedido que impede, é
esta pessoa. A permissão do endpoint mantém-se `approval.requests.read`: abre
a porta a quem tem visibilidade sobre o pedido; a regra real é do domínio.

O corpo do pedido passa a exigir `cancelledByEmployeeId` — quebra de
contrato para quem já integrasse contra este endpoint. Actualizado em
`API-FRONTEND.md`.

**Por verificar:** nenhuma suite caixa-preta exercita
`POST /approval/requests/{id}/cancellation` directamente — não há
`verify-approval.ps1`, e nenhuma das suites de `hr`/`finance`/`procurement`
que passam por `approval` chega a testar o cancelamento. A regra está coberta
por 2 testes de domínio novos (`Cancel_ByTheRequester_...`,
`Cancel_ByAnyoneOtherThanTheRequester_...`), no mesmo padrão de BR-2 — mas a
lacuna de verificação end-to-end é anterior a esta correcção e continua.

### ~~K19 — Arranque preso num volume novo, à espera de uma base que só a migração criaria~~ — **RESOLVIDO 2026-08-28**

- **Módulo:** plataforma (`Rivo.Api/Program.cs`)
- **Impacto:** o ADR-030 corre a migração no arranque, atrás de uma sonda que
  espera uma ligação real antes de a disparar — acrescentada em `c3dd29d`
  (2026-08-25) para fechar uma corrida de `docker compose restart` (o SQL
  Server ainda a recuperar `rivo`, o EF a concluir que a base não existe, e
  `CREATE DATABASE` a rebentar com o erro 1801). Essa sonda abria ligação
  **já apontada ao nome da base**, e isso resolvia o caso do reinício e
  **impedia sempre o primeiro arranque**: contra um volume genuinamente novo, a
  base não existe mesmo, o SQL Server recusa sempre esse login (mesmo sintoma
  do caso de recuperação), e nada além da própria migração — que ficava presa
  atrás da sonda — a criaria. Impasse, não demora: **139 tentativas sem
  avançar, reinício do container, e o mesmo ciclo outra vez, para sempre.**
- **Como não foi apanhado antes:** o CI recria o ambiente do zero a cada
  corrida (deveria ter apanhado isto desde 2026-08-25), mas este ramo nunca
  chegou a correr lá — está por publicar. Em desenvolvimento local, um `docker
  compose down -v` completo é raro; o normal é reiniciar sobre um volume que já
  tem `rivo`, onde a sonda sempre funcionou.
- **Detectado em 2026-08-28**, ao tentar correr as verificações end-to-end
  pendentes a partir de `docker compose down -v` — precisamente o caminho que
  ninguém tinha percorrido desde a correcção de 25/08.
- **Corrigido por:** a sonda passou a perguntar a `master` — que existe
  sempre — pelo `state_desc` da base alvo em `sys.databases`, e distingue as
  três respostas: sem linha (nunca existiu — segue para a migração, que a
  cria), `ONLINE` (segue), qualquer outro estado (`RECOVERING`, ... — espera,
  que é o caso original). Ver `EsperarBaseAlvoOuInexistenteAsync` em
  `Program.cs`.
- **Verificado:** três arranques consecutivos a partir de `docker compose down
  -v`, todos prontos em segundos.

### K20 — Limpar uma política por rota, no fim de uma suite, falha de forma intermitente

- **Módulo:** verificação end-to-end (`scripts/verify-ledger.ps1` caso 44,
  `scripts/verify-procurement.ps1` caso 58) — não se confirmou defeito na
  aplicação, em duas investigações separadas.
- **Impacto:** as duas suites terminam a desactivar, pela rota
  `POST /approval/policies/{id}/deactivation`, a política de
  `finance.payment_request` que a própria corrida criou — para que a próxima
  corrida não a encontre ainda activa. A chamada devolve por vezes **404**,
  apesar de o identificador vir directamente da listagem imediatamente
  anterior. A suite falha; a política não é sempre correctamente desactivada —
  o que, repetido, acumula políticas activas e pode um dia interferir com o
  caso que verifica "sem política nenhuma, a submissão é recusada".
- **Encontrado e investigado duas vezes, com o mesmo veredicto.** Primeira vez
  a 2026-08-28 (ver histórico em
  [implemented.md §Verificação](implemented.md#verificação)): descartada a
  teoria de duplicação de linhas pelo `Include` sem `AsSplitQuery` em
  `ApprovalStore.ListPoliciesAsync`/`ListPoliciesForProcessAsync` — leitura
  instrumentada sem duplicados, mesmo logo a seguir a um reinício; descartada a
  proximidade a um reinício — o caso 44 nunca corre perto de um e falhou na
  mesma. **Promovida a este ficheiro depois de reaparecer numa segunda
  investigação, no mesmo dia**, que chegou às mesmas conclusões por outro
  caminho:
  - O filtro (`departmentId -eq $script:departamentoId`) foi testado
    isoladamente contra dados reais da base, incluindo o caso `$null` da
    política genérica — discrimina sempre correctamente.
  - `DeactivateApprovalPolicy` só devolve `NotFound` quando `FindPolicyAsync`
    não encontra a linha; `ApprovalStore` não tem cache nem estado partilhado
    entre pedidos.
  - Uma repetição manual da mesma chamada, com o mesmo identificador, momentos
    depois, teve sempre sucesso.
  - Uma corrida fortemente instrumentada, com uma chamada extra antes da real,
    não reproduziu a falha — sugestivo de janela de tempo, não conclusivo.
- **Terceira investigação, mesmo veredicto — e uma teoria concreta afastada
  com prova.** `implemented.md` já documentava, noutro contexto (um falso
  `409` em `verify-hr`), que `Invoke-RestMethod` **por vezes entrega a lista
  inteira ao pipeline como um só item** — nesse caso, `$_.campo -eq $valor`
  compara uma colecção com um escalar, devolve o subconjunto que bate, e
  sendo não-vazio é *verdadeiro*: o `Where-Object` deixa passar tudo. Era a
  explicação com a forma certa — `verify-ledger.ps1` caso 44 e
  `verify-procurement.ps1` caso 58 usam exactamente este padrão — e foi
  aplicada a correcção idiomática (`@(...)` a forçar array) aos dois
  pontos. **Testada contra o 404 real, numa corrida completa a partir de
  volume novo, e não o resolveu:** os mesmos dois casos falharam da mesma
  forma, com a correcção no lugar. A protecção fica — é defesa válida por si
  só, documentada, e não faz mal nenhum — mas **esta não é a causa do K20**.
- **Três investigações independentes, sem causa de código encontrada em
  nenhuma, é o que torna isto um defeito registado e não uma suspeita.** Não é
  "um dia mau do Docker": reapareceu depois do motor estabilizar, voltou a
  reaparecer numa sessão posterior inteiramente nova, e resistiu à correcção
  do modo de falha mais plausível já documentado no projecto.
- **Contorno:** nenhum a nível de configuração. Corrida a corrida, a política
  por limpar acumula-se; não compromete o resto da suite, só o caso da
  limpeza.
- **Seguimento:** reproduzir com instrumentação do lado do servidor (não só do
  script) antes de tentar corrigir de novo — um `Thread.Sleep`/nova tentativa
  no script esconderia o sintoma sem provar a causa. Vale a pena capturar os
  logs do `rivo-api` no instante exacto da falha, já que três tentativas de
  isolar a causa do lado do PowerShell/HTTP não encontraram nada.
