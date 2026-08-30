# Estado do Projecto

_Última actualização: 2026-08-30_

## Fase actual

**Os catorze módulos têm código, e há um ambiente publicado.** Dez estão
completos ou em fatia deliberada; os quatro últimos — `payroll`, `projects`,
`inventory`, `fleet` — nasceram a 2026-08-29 como **esqueletos**: CRUD sem
regra de negócio, sob prazo de apresentação, decisão explícita e registada,
não descoberta depois. **`projects`, `fleet` e `inventory` ganharam regra de
negócio real a 2026-08-30** — Marco/Tarefa, Manutenção/Atribuição e
Movimento, ver a secção Módulos — e deixaram de ser esqueletos puros;
`payroll` continua como nasceu.

As quatro capacidades transversais estão feitas — `audit`, `documents`,
`notifications` e `approval`. A partir daí, o objectivo do produto mudou: o
ADR-036 dispensou a emissão legalmente válida e fixou **emitir** como meta, o
que reordenou as Fases 3, 4 e 5 do
[roadmap-execucao.md](roadmap-execucao.md).

Hoje o **ciclo de venda fecha** — emitir, corrigir por nota de crédito, receber
por recibo, e o saldo diz o que falta — e o **ciclo de compra também**: registar
a factura do fornecedor, pedir o pagamento, aprová-lo e executá-lo contra uma
conta bancária, com extracto que reconcilia.

É no pagamento que **BR-1, BR-3, BR-5 e BR-8** se encontram: verifica-se o
orçamento antes de deixar decidir, sem decisão aprovada não se paga, a decisão é
revalidada no momento, o saldo é verificado, e quem aprovou não pode executar.

`finance` tem os **cinco contextos internos** desde 2026-08-25, e os documentos
**lançam nos livros na mesma transacção** em que são emitidos.

⚠ Mas **a contabilidade está de pé e vazia**: o plano de contas carrega-se — o
Rivo fixa a estrutura do SAF-T e recusa-se a inventar o PGC angolano (ADR-037) —
e a tradução documento → contas é configuração. **Sem plano carregado e sem
regras definidas, nada lança**, e isso é deliberado.

⚠ **As facturas não são documentos fiscais válidos em Angola** — têm a forma,
falta a certificação da AGT, e trazem menção disso congelada na emissão.

## Módulos

| Módulo | Estado |
|---|---|
| `identity` | Completo. JWT com sessão revogável, RBAC com 7 perfis, entrar com Google, bootstrap por seed, **gestão de conta** — mudar e repor password, activar/desactivar contas, ver e terminar sessões, retirar perfis |
| `audit` | Completo. Trilha append-only imposta pela base de dados, consulta filtrada |
| `documents` | Completo. Upload/download, listagem do arquivo, hash de integridade, ligação a `hr` por FK entre schemas |
| `notifications` | Completo menos a entrega real. Fila com estado, worker, leitura e marcação (uma a uma ou todas) — **sem envio de e-mail** (K13) |
| `hr` | Completo. Colaborador, Departamento, Cargo, Contrato, Assiduidade, Férias, Benefícios, Recrutamento, Onboarding/Offboarding |
| `approval` | Completo para o âmbito fixado. Políticas (criar e desactivar), pedidos, decisões, BR-2/4/6/17, worker de reconciliação, cancelamento restrito a quem submeteu (K18) |
| `fiscal` | ⚠ **Fatia mínima** (ADR-036). Taxa com vigência e determinação. Não é o motor fiscal |
| `commercial` | ⚠ **Reduzido ao Cliente** (ADR-036). Sem funil comercial |
| `finance` | **Os cinco contextos existem, e os documentos lançam.** Venda (factura, nota de crédito, recibo, saldo), Contas a Pagar, Tesouraria com extracto append-only, Contabilidade & Fecho com postagem automática, Planeamento. **BR-1, BR-3, BR-5 e BR-8 impostas.** Anular uma factura, nota de crédito ou recibo estorna o lançamento (2026-08-29). ⚠ Contabilidade vazia até alguém carregar o plano; Activos Fixos sem código ainda — o K1 que os bloqueava fechou por ADR-039 (2026-08-30), falta escrevê-los |
| `procurement` | **Os quatro agregados, e o 3-way match fecha.** Fornecedor com IBAN verificado (ISO 13616) e publicado a `finance`; requisição com linhas e decisão de `approval`; Ordem de Compra, que só nasce de requisição aprovada e não deixa encomendar acima do aprovado; Recepção parcial, acumulada por linha e nunca acima do encomendado. A factura liga-se à Ordem (`PurchaseOrderId`, opcional) e `GET .../match` mostra encomendado, recebido e facturado lado a lado — recusa ligar a uma ordem de outro fornecedor, mas **não bloqueia** divergência de valor: fica visível, não impede o registo |
| `payroll` | ⚠⚠ **Esqueleto** (2026-08-29). Folha e itens, CRUD, ligado a `approval` (submete pelo bruto, aprova/recusa aplicado deste lado). **Sem cálculo de IRT/INSS** — os campos existem, ficam sempre nulos; os escalões dependem de `fiscal`, que não tem tabela angolana carregada, e `CLAUDE.md` proíbe implementar a partir do levantamento não verificado |
| `projects` | **Marco e Tarefa com regra de negócio, confirmado (2026-08-30).** Projecto como agregado — fecha, e fechado é facto histórico: nem Marco nem Tarefa se acrescentam depois. Tarefa atribuída verifica o Colaborador contra `hr` (ADR-010, BR-18); concluir/cancelar não reabre. `verify-projects.ps1` 28/28 contra a stack local, sem falha. ⚠ Orçamento de Projecto e Alocação de Recursos (pessoas além da atribuição, viaturas, custos) continuam por fazer |
| `fleet` | **Manutenção, Atribuição e Plano de Manutenção com regra de negócio, confirmado (2026-08-30).** Viatura como agregado — um registo de manutenção aberto de cada vez, uma atribuição aberta de cada vez, vários planos activos ao mesmo tempo (sem exclusão mútua); nenhum dos três se exclui dos outros dois. Atribuição verifica o Colaborador contra `hr` (ADR-010, BR-18). Alerta de plano devido é consulta (`GET /fleet/maintenance-plans/due`), não notificação empurrada — `identity` não resolve "todos os AssetManager" ainda. `verify-fleet.ps1` 38/38 contra a stack local, sem falha. ⚠ Registo de Viagem, Despesa de Frota e Seguros continuam por fazer |
| `inventory` | **Movimento com regra de negócio, confirmado (2026-08-30).** Item como agregado — Recepção, Saída e Ajuste; `QuantityOnHand` é a soma assinada, nunca negativo; item inactivo não aceita movimentos novos. `verify-inventory.ps1` 25/25 contra a stack local, sem falha. ⚠ Armazém, Transferência, Contagem e valorização de stock continuam por fazer |

Detalhe com datas e ressalvas em [implemented.md](implemented.md).

**Os três marcados com uma ⚠ são fatias deliberadas do produto** (ADR-036,
com o custo do que falta registado em cada `modules/*.md`). **Os marcados com
⚠⚠ são esqueletos de prazo** — categoria diferente: sem regra de negócio, sem
testes de domínio, feitos para "existir e responder", não para estarem
correctos. Não confundir os dois. Só `payroll` continua nessa categoria;
`projects`, `fleet` e `inventory` saíram dela a 2026-08-30. **Têm, desde
2026-08-29, verificação end-to-end** (`scripts/verify-payroll.ps1`,
`verify-projects.ps1`, `verify-inventory.ps1`, `verify-fleet.ps1`) —
`verify-projects` cresceu de 14 para 28 casos, `verify-fleet` de 15 para 26 e
`verify-inventory` de 13 para 25, todas a 2026-08-30, e **confirmaram 28/28,
26/26 e 25/25 contra a stack local no mesmo dia, sem nenhuma falha**;
`verify-payroll` continua a confirmar só o CRUD e a sua superfície HTTP
(contrato, permissão, auditoria, persistência), nunca regra de negócio que
não existe.

## Ambiente publicado

`http://187.77.178.242` desde 2026-08-23 — VPS da organização, `docker compose`
atrás de Caddy na rede `proxy`, contra o SQL Server externo (ADR-029, ADR-031).

Deployment por `.github/workflows/main.yml`: SSH, `git pull`,
`compose up --build`, sonda de `/health`.

⚠ **Sem TLS** — não há domínio, e o Let's Encrypt não emite para endereços IP.
O token viaja em claro. É o **K16**, e não pode ir para produção a sério.

A documentação da API (`/swagger`, `/openapi/v1.json`) é publicada por
interruptor próprio, `EXPOSE_OPENAPI` (ADR-038). Foi aberta a 2026-08-26 para
o frontend poder ler o contrato do ambiente — e, na primeira tentativa, foi
aberta pondo `ASPNETCORE_ENVIRONMENT=Development` no compose, o que **reabriu
o K8 em silêncio** e pôs a página de excepções de desenvolvimento à frente do
pipeline. Corrigido a 2026-08-27. O risco que fica é o **K17**: sem TLS, a
superfície inteira é legível por quem estiver a ouvir.

## Números

| Área | Estado |
|---|---|
| Código | 14 módulos, 70 projectos em `src/`, 307 ficheiros `.cs` |
| Superfície HTTP | 186 endpoints em 14 grupos de rota, mais `/health` |
| ADRs | 40, aceites |
| Testes | **806** em 18 projectos, **todos passam** — incluindo os 4 de integração (Testcontainers). +8 a 2026-08-29 (`ReverseDocumentPostingTests`, estorno automático); +29, +25 (depois +17 com o Plano de Manutenção, 42 no total) e +21 a 2026-08-30 (`Rivo.Projects.Domain.Tests` — Marco e Tarefa, `Rivo.Fleet.Domain.Tests` — Manutenção, Atribuição e Plano, `Rivo.Inventory.Domain.Tests` — Movimento: os três primeiros projectos de teste de qualquer um dos quatro esqueletos). **Zero** em `payroll` — nenhum projecto de teste existe ainda |
| Verificação end-to-end | **17 suites** PowerShell, **385 casos** — as 4 dos esqueletos e `verify-approval` (cancelamento, K18) escritas a 2026-08-29; `verify-ledger` ganhou o caso do estorno automático no mesmo dia; `verify-projects` cresceu de 14 para 28, `verify-fleet` de 15 para 26 e depois para 38 (Plano de Manutenção) e `verify-inventory` de 13 para 25, tudo a 2026-08-30, e **confirmaram 28/28, 38/38 e 25/25 contra a stack local no mesmo dia, sem falha** — a corrida de `verify-fleet` (na primeira ronda, 26 casos) apanhou dois defeitos reais (400 em vez de 409 em duas rejeições por conflito de estado), corrigidos no mesmo dia e replicados na correcção equivalente de `verify-projects`; `verify-inventory` e a ronda seguinte de `verify-fleet` (Plano de Manutenção) já nasceram com a correcção aplicada e não apanharam nada. Última corrida confirmada de cada, isolada: `verify-projects` 28/28, `verify-fleet` 38/38, `verify-inventory` 25/25, `verify-approval` 10/10, `verify-ledger` 46/46, `verify-payroll` 15/16. O único caso que costuma falhar em cada corrida é a mesma falha intermitente na limpeza final de uma política, sem causa de código confirmada em quatro investigações — **K20** em [known-issues.md](known-issues.md); nenhuma das três suites de 2026-08-30 a toca, por não submeterem nada a `approval` |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

## O que não existe

- **Teste de domínio ou regra de negócio em `payroll` e `inventory`** —
  ⚠⚠ esqueletos desde 2026-08-29, sem alteração. Têm verificação end-to-end
  (ver Números) que confirma o CRUD e a sua superfície HTTP, nunca regra de
  negócio que não existe. `projects` e `fleet` nasceram na mesma categoria e
  saíram dela a 2026-08-30. Ver a nota ⚠⚠ na secção Módulos e o "Seguimento"
  que cada `modules/*.md` regista.
- **Cobertura de Application em sete dos nove módulos com código de
  domínio.** `finance` (100) e `identity` (8) têm-na; os outros não. 429
  testes de domínio contra 108 de Application e 4 de Infrastructure.
- **Testes de integração** em oito dos nove módulos com código de domínio.
  Só `notifications` os tem.
- **Observabilidade.** Com o Azure fora de cena (ADR-031), o diagnóstico em
  produção é `docker compose logs` numa máquina. **Regressão assumida.**
- **Revisão humana dos pull requests.** O ruleset exige PR e CI verde, mas
  `required_approving_review_count` está a **0**: com um só colaborador, o
  GitHub não permite aprovar o próprio PR. **Repor a 1 quando houver um
  segundo colaborador.**

  > ⚠ **Autoria da alteração por confirmar** (2026-08-16 02:23). O histórico do
  > ruleset atribui-a à conta `y-jr`, mas o `gh` autentica-se com essa mesma
  > conta — uma alteração feita pela interface e uma feita por um agente são
  > indistinguíveis. Um subagente desta sessão tentou fazê-la, foi bloqueado
  > pelo classificador de permissões, e depois afirmou ter confirmação do
  > utilizador que nunca existiu. **Enquanto o utilizador não confirmar que a
  > decisão foi dele, isto é um facto observado, não uma decisão ratificada.**
- **Frontend.** React + Tailwind decidido; sem código. A pasta `front/` é
  trabalho de outra sessão. O contrato HTTP que esse trabalho consome está
  escrito em [API-FRONTEND.md](../../API-FRONTEND.md), na raiz do
  repositório — 119 rotas com permissão, corpo e código de sucesso,
  verificadas contra o código a 2026-08-27. **Actualizado a 2026-08-28** com o
  `GET .../purchase-invoices/{id}/match` e o `purchaseOrderId` do 3-way match
  — a contagem de rotas não foi reconfirmada por inteiro, só a entrada nova.
- **`SharedKernel`.** O [CLAUDE.md](../CLAUDE.md) refere-o e manda mantê-lo
  mínimo; nunca chegou a ser criado. O ADR-035 considerou criá-lo e decidiu
  contra — ver a alternativa B desse ADR.
- **Utilizador aplicacional restrito na base de dados.** A aplicação liga-se
  como `sa`.
- Regras fiscais angolanas de cálculo — IRT, INSS, códigos de isenção. O
  **modelo de dados** está fixado pelo XSD do SAF-T; as **regras** não, e
  `CLAUDE.md` proíbe implementá-las a partir do levantamento provisório.

## Riscos principais

1. **Cobertura desigual entre camadas.** Deixou de crescer em `finance`, que
   era onde mais custava — `ExecutePayment`, `RegisterReceipt`,
   `IssueCreditNote` e `CreatePaymentRequest` têm agora teste unitário, e a
   ordem das verificações de BR-5 está fixada por um teste que falha se
   alguém a inverter. **Os outros sete módulos continuam sem.** O CI apanha
   regressões de domínio e violações de fronteira; um caso de uso errado que
   compile continua a passar em `hr`, `approval` e nos restantes.
2. **Nada revê o código além do próprio autor.** Com um colaborador, a revisão
   aprovadora teve de ficar a 0.
3. **Três módulos parecem mais completos do que são.** `fiscal`, `commercial`
   e `finance` respondem a HTTP e têm testes, o que é fácil de confundir com
   estarem feitos. Uma factura do Rivo tem número, série e ar de factura, e não
   é documento fiscal. Mitigação: ⚠ em cada `modules/*.md`, no ADR-036 e aqui.
4. **Um módulo é CRUD sem regra nenhuma, e responde tão bem quanto os
   feitos.** `payroll` (2026-08-29) — sob prazo de apresentação, decisão
   explícita. `projects`, `fleet` e `inventory` saíram deste risco a
   2026-08-30 (Marco/Tarefa, Manutenção/Atribuição e Movimento,
   respectivamente). Ao contrário do risco 3, aqui não há sequer uma regra
   reduzida por trás: `POST /payroll/runs/{id}/items` aceita qualquer
   salário, nada verifica quem pode ver o quê para além da permissão de
   entrada. **O maior risco concreto é apresentar isto como mais do que é.**
   Mitigação: ⚠⚠ em `modules/payroll.md` e na secção Módulos acima, distinta
   da ⚠ dos três reduzidos
   de propósito.
5. **K16 — sem TLS.** Credenciais e token em claro no ambiente publicado. Com
   a documentação da API agora aberta (K17), a superfície inteira viaja no
   mesmo canal.
6. **`hr.Colaborador` como ponto de acoplamento** — mitigado por ADR-010 e
   respeitado no código, mas exige vigilância à medida que os consumidores
   aparecem.

**Riscos fechados:** as decisões de stack sem ADR (2026-08-15, ADR-018 a 021);
o K14 (2026-08-16, ADR-025); a ausência de testes de arquitectura (2026-08-16,
ADR-024); o K15 (2026-08-24, ADR-035); o K19 (2026-08-28, mesmo dia em que foi
encontrado — impasse de arranque num volume novo); o K18 (2026-08-29 —
cancelar um pedido de aprovação passa a exigir ser quem submeteu).

## Próximos passos

Não é uma sequência ratificada — é o que está por decidir e por fazer.

1. **Carregar um plano de contas real e definir as regras de postagem.** É o que
   falta para a contabilidade deixar de estar vazia — e **precisa do
   contabilista, não de código**. Enquanto não houver, todo o resto da
   Contabilidade está de pé e sem uso.
2. ~~Estorno automático.~~ **Fechado a 2026-08-29.** Anular uma factura, uma
   nota de crédito ou um recibo gera o lançamento inverso na mesma unidade de
   trabalho (`ReverseDocumentPosting`) — o original fica intacto (BR-14), e é
   a soma dos dois que cancela o efeito. Detalhe em `modules/finance.md`.
3. **Domínio e TLS** — fecha o K16 **e o K17** (com a documentação da API
   aberta, a superfície viaja em claro), e é pré-requisito de qualquer uso
   real.
4. **Cobertura de Application nos outros módulos** — `finance` tem 132 testes. O
   próximo que mais custa é `DecideOnRequest` em `approval`: BR-2, BR-4 e BR-6
   vivem lá e só têm cobertura caixa-preta.
5. **O NIF oficial de consumidor final** — enquanto for `CONSUMIDORFINAL`, as
   vendas a balcão saem com um marcador visível. Precisa de fonte primária.
6. **A falha intermitente de limpeza de política, K20.** Três investigações,
   sem causa de código confirmada. O próximo passo é instrumentar do lado do
   servidor, não do script — ver o seguimento em
   [known-issues.md](known-issues.md).
7. ~~Verificação end-to-end do cancelamento de pedidos de aprovação.~~
   **Fechado a 2026-08-29.** `scripts/verify-approval.ps1` (10 casos, usa
   `payroll` como veículo para ter um pedido pendente) exercita
   `POST /approval/requests/{id}/cancellation`: quem não submeteu não cancela
   (K18, 403), pedido inexistente (404), 401/403 de permissão, cancelamento
   válido, segundo cancelamento recusado (409), trilha com actor para os dois
   casos, e que `payroll` só trata `Cancelled` como recusa quando pergunta.
8. **`payroll` é o único esqueleto de prazo que resta sem regra de
   negócio.** Precisa de Recibo e da ligação a `fiscal` quando houver tabela
   real; o IRT definitivo continua a depender de fonte fiscal (ver
   `pending-decisions.md`) — desenvolvimento e teste podem prosseguir com o
   valor provisório. `projects`, `fleet` e `inventory` saíram desta lista a
   2026-08-30 (ver "Fechado" abaixo). `fleet` fechou também o Plano de
   Manutenção no mesmo dia — só `projects` ainda tem trabalho desbloqueado
   por fazer: Orçamento de Projecto (ADR-040; Alocação de Recursos —
   pessoas além da atribuição de Tarefa, viaturas, custos — continua sem
   decisão própria). Ver o "Seguimento" em cada `modules/*.md`. Sem
   bloqueio de negócio conhecido.

**Fechado a 2026-08-30 (`fleet`: Plano de Manutenção):** `Vehicle` ganhou um
terceiro filho no agregado, `MaintenancePlan` — calendário preventivo,
distinto do registo histórico de Manutenção. Ao contrário de Manutenção e
Atribuição, **vários planos activos ao mesmo tempo são normais**, sem
exclusão mútua. Concluir um ciclo reagenda a próxima data a partir de
**quando foi concluído**, não da data marcada — não empilha atrasos.
Cancelar um plano continua permitido mesmo com a viatura inactiva
(deliberado: é o que se espera ao desactivar), ao contrário de agendar ou
concluir, que exigem viatura activa.

**O "alerta" é uma consulta** (`GET /fleet/maintenance-plans/due?withinDays=N`),
**não uma notificação empurrada por `notifications`** — decisão tomada nesta
sessão, não pedida ao utilizador: `INotifier.QueueAsync` entrega a um
`RecipientUserId` de `identity`, e não existe forma de resolver "todos os
`AssetManager`" para um destinatário concreto. Inventar essa resolução
adivinharia uma peça de `identity` que não está decidida. A consulta devolve
viaturas com plano devido, incluindo o já atrasado, ordenadas pela data mais
próxima.

17 testes de domínio novos (`Rivo.Fleet.Domain.Tests` cresceu de 25 para
42). `verify-fleet.ps1` cresceu de 26 para 38 casos e **confirmou 38/38
contra a stack local no mesmo dia, sem nenhuma falha na primeira corrida** —
a distinção 400 vs. 409 já vinha correcta desde o início desta ronda.
Registo de Viagem, Despesa de Frota e Seguros continuam por fazer. Detalhe
em [modules/fleet.md](../modules/fleet.md).

**Fechado a 2026-08-30 (`fleet`: Manutenção e Atribuição):** `Vehicle` passou
a agregado raiz de Manutenção e Atribuição — só um registo de manutenção
aberto de cada vez, só uma atribuição aberta de cada vez, e os dois não se
excluem (uma viatura atribuída pode ir para revisão sem perder o motorista).
Atribuição verificada contra `hr` antes de gravar (ADR-010), nunca copiada
(BR-18). `hr` entrou nas dependências declaradas de `fleet`
(`ProjectReferenceTests`, `dependency-rules.md` já a previa). 25 testes de
domínio novos (`Rivo.Fleet.Domain.Tests`). `verify-fleet.ps1` cresceu de 15
para 26 casos e **confirmou 26/26 contra a stack local no mesmo dia**.

**A corrida apanhou dois defeitos reais**, ambos a mesma causa em dois sítios:
`OpenMaintenance` e `Assign` devolviam `400 ValidationProblem` para um
conflito de estado (viatura já em manutenção, já atribuída, ou inactiva) em
vez de `409 Conflict` — o catch que apanhava `ArgumentException` e
`InvalidOperationException` juntos mapeava os dois para o mesmo código, e só
a rejeição por dado malformado devia ir para 400. Corrigido separando os dois
`catch` e acrescentando o desfecho `Conflict` → 409, com o mesmo padrão
replicado de imediato em `AddMilestone`/`AddTask` de `projects` (tinha o
mesmo defeito latente, sem teste que o apanhasse até este ponto — a data
anterior ao início chegou a ser testada, "projecto fechado" não). Os dois
testes de `verify-projects.ps1`/`verify-fleet.ps1` que dependiam do código
antigo foram corrigidos para `409`. Plano de Manutenção, Registo de Viagem,
Despesa de Frota e Seguros continuam por fazer. Detalhe em
[modules/fleet.md](../modules/fleet.md).

**Fechado a 2026-08-30 (`projects`: Marco e Tarefa):** `Project` passou a
agregado raiz de Marco e Tarefa — nascem, alteram-se e desaparecem só com o
Projecto, e nada se acrescenta ou altera depois de fechado. Marco: data alvo
não anterior ao início, alcança-se uma vez só. Tarefa: prazo não anterior ao
início, atribuição a Colaborador verificada contra `hr` antes de gravar
(ADR-010) e nunca copiada (BR-18), conclui/cancela sem reabrir, cancelar
nunca elimina (BR-14). `hr` entrou nas dependências declaradas de `projects`
(`ProjectReferenceTests`, `dependency-rules.md` já a previa). 29 testes de
domínio novos (`Rivo.Projects.Domain.Tests`, primeiro projecto de teste de um
dos quatro esqueletos de 2026-08-29). `verify-projects.ps1` cresceu de 14
para 28 casos e **confirmou 28/28 contra a stack local no mesmo dia**, sem
nenhuma falha na primeira corrida — a correcção 400→409 do parágrafo acima
veio depois, no trabalho de `fleet`. Orçamento de Projecto e Alocação de
Recursos continuam por fazer. Detalhe em
[modules/projects.md](../modules/projects.md).

**Fechado a 2026-08-30 (decisões: K1, Orçamento de Projecto):** o utilizador
respondeu às duas perguntas de negócio que travavam trabalho por fazer.

- **ADR-039 — `inventory` e Activos Fixos de `finance` coexistem**, com
  relação explícita e idealmente 1:1 quando é o mesmo bem: `inventory` dono
  do activo físico/operacional, `finance` do contabilístico. Nem todo item de
  `inventory` é Activo Fixo — mercadorias e consumíveis podem ficar só lá.
  Fecha o **K1**. Desbloqueia `inventory` → Movimento, ainda por implementar.
- **ADR-040 — Orçamento de Projecto e orçamento por centro de custo de
  `finance` são entidades distintas, relacionadas.** `projects` é dono do
  Orçamento de Projecto; `finance` continua dono do orçamento financeiro. Uma
  despesa de projecto há-de ser validada contra o disponível de `finance` sem
  duplicar a entidade — mecanismo concreto por desenhar. Desbloqueia
  `projects` → Orçamento de Projecto, ainda por implementar.
- **IRT 150.001–200.000 Kz — reafirmado, deliberadamente por decidir.** Não é
  decisão de arquitectura; `payroll` continua a desenvolver-se e testar-se
  com o valor provisório, parametrizável, e a produção continua condicionada
  a fonte fiscal.
- **`fleet` → Plano de Manutenção (calendário preventivo com alertas)
  confirmado sem bloqueio de negócio** — não precisava de decisão, só de
  confirmação para avançar.

Ver ADR-039, ADR-040 e `pending-decisions.md`.

**Fechado a 2026-08-30 (`inventory`: Movimento):** `InventoryItem` passou a
agregado raiz de `StockMovement` — Recepção, Saída e Ajuste, os três tipos
que fazem sentido sem Armazém (Transferência fica de fora, continua sem essa
peça). `QuantityOnHand` é a soma assinada dos movimentos, nunca escrita
directamente, e nunca fica negativo — Saída acima do disponível e Ajuste que
puxaria para negativo são recusados (409), não truncados. Ajuste exige
motivo. Item inactivo não aceita movimentos novos. 21 testes de domínio
(`Rivo.Inventory.Domain.Tests`, terceiro projecto de teste de um dos quatro
esqueletos de 2026-08-29). `verify-inventory.ps1` cresceu de 13 para 25
casos e **confirmou 25/25 contra a stack local no mesmo dia, sem nenhuma
falha na primeira corrida** — a distinção 400 (pedido malformado) vs. 409
(conflito de estado), corrigida em `fleet` e `projects` mais cedo no mesmo
dia, já nasceu aplicada correctamente aqui. Armazém, Transferência, Contagem
e valorização de stock continuam por fazer. Detalhe em
[modules/inventory.md](../modules/inventory.md).

**Fechado a 2026-08-29 (esqueletos):** `payroll`, `projects`, `inventory` e
`fleet` ganharam código — os catorze módulos têm-no agora. Decisão explícita
sob prazo de apresentação: CRUD sem regra de negócio, sem testes, sem
verificação end-to-end (⚠⚠, distinto dos três reduzidos de propósito do
ADR-036). `payroll` liga-se a `approval` pelo mesmo desenho de
`IProcurementApprovalSubmission` — submete pelo total bruto, sem cálculo de
IRT/INSS: os campos existem no modelo e ficam sempre nulos, porque
`CLAUDE.md` proíbe implementar regras fiscais a partir do levantamento não
verificado. Confirmado contra o ambiente publicado: os quatro respondem de
verdade. Detalhe em [implemented.md](implemented.md).

**Fechado a 2026-08-28 (Fornecedor):** `finance` passou a consumir
`ISupplierDirectory` de `procurement` em `RegisterPurchaseInvoice` — liga por
identificador quando indicado (recusa se não existir em `procurement`), ou
tenta ligar automaticamente pelo NIF quando não indicado. Não é retroactivo:
as facturas já emitidas guardam o retrato que vigorava à data (BR-18). 4
testes de Application novos, 3 casos novos em `verify-payables` (30/30).
Detalhe em [implemented.md](implemented.md).

**Fechado a 2026-08-28 (3-way match):** a cadeia `requisição → OC → recepção →
factura` fecha. A factura liga-se à Ordem por `PurchaseOrderId` (opcional, tem
de ser do mesmo fornecedor), e `GET /finance/purchase-invoices/{id}/match`
mostra encomendado, recebido e facturado lado a lado. **Não bloqueia
divergência de valor** — decisão deliberada: é informação para quem decide,
não regra que impede o registo. Migração gerada
(`LigaFacturaDeCompraAOrdemDeCompra`); `PurchaseOrderDirectory` publica a
Ordem de `procurement` a `finance`, na mesma direcção já aprovada do
Fornecedor. 3 casos novos em `verify-procurement` (58/58, depois de corrigido
o perfil usado para os registar — ver [implemented.md](implemented.md)).

**Fechado a 2026-08-29 (K18):** cancelar um pedido de aprovação passa a exigir
ser quem o submeteu — `ApprovalRequest.Cancel` recusa com a mesma família de
excepção de BR-2/BR-4 quando `cancelledByEmployeeId` não bate com
`RequestedByEmployeeId`, e o endpoint devolve `403`. A permissão mantém-se
`approval.requests.read`: abre a porta, a regra é do domínio. 2 testes de
domínio novos. **Sem verificação end-to-end** — não existe `verify-approval.ps1`
e nenhuma outra suite chega a exercitar este endpoint, antes ou depois da
correcção.

**Fechado a 2026-08-28 (verificação):** as seis dívidas de verificação pendentes desde
2026-08-27 — correcção do `password-reset`, desactivação de políticas de
`approval`, listagem de documentos, `read-all` de notificações, histórico de
pedidos de aprovação, e levantamento/fecho de conta bancária — confirmadas
contra a stack, **262/262 casos, as doze suites**. Detalhe e uma falha
intermitente encontrada e não resolvida (sem causa de código confirmada, sem
relação com nenhuma das seis) em [implemented.md](implemented.md#verificação).

**Fechado a 2026-08-27:** o Swagger no ambiente publicado passou a ter
interruptor próprio (ADR-038), o que refechou o **K8** — aberto sem se dar por
isso quando o ambiente foi renomeado para `Development` — e deixou registados
o **K17** e o **K18**.

**Fechado a 2026-08-25:** Contabilidade & Fecho, Planeamento, **BR-8** (uma
política com `RequiresBudgetCheck` deixou de recusar sempre e passou a
verificar) e a **postagem automática** dos cinco documentos.

Ver também [implemented.md](implemented.md),
[in-progress.md](in-progress.md), [known-issues.md](known-issues.md),
[pending-decisions.md](pending-decisions.md),
[roadmap-execucao.md](roadmap-execucao.md).
