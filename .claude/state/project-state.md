# Estado do Projecto

_Última actualização: 2026-09-04. Números verificados contra o repositório
nesta data, não herdados da versão anterior deste ficheiro._

## Fase actual

**Oito das nove fases do roteiro estão fechadas.** Restam duas coisas, e
nenhuma delas é trabalho de engenharia deste repositório: o **K16** (sem TLS
— falta um domínio apontado à VPS, e o Let's Encrypt não emite para IP) e as
sete verificações que exigem `RIVO_RESTART_COMMAND`, deliberadamente não
configurado para que uma suite de teste não possa reiniciar produção.

**Quinze módulos têm código, mais cinco camadas de composição.** O décimo
quinto, `messaging`, nasceu a 2026-09-04 (ADR-045) e ganhou tickets no mesmo
dia (ADR-046). Nenhum módulo continua esqueleto: os quatro que nasceram assim
a 2026-08-29 — `payroll`, `projects`, `inventory`, `fleet` — ganharam regra de
negócio real a 2026-08-30, e a Fase 7 fechou a 2026-08-31.

As quatro capacidades transversais estão feitas — `audit`, `documents`,
`notifications` e `approval`. A partir daí, o objectivo do produto mudou: o
ADR-036 dispensou a emissão legalmente válida e fixou **emitir** como meta, o
que reordenou as Fases 3, 4 e 5 do
[roadmap-execucao.md](roadmap-execucao.md).

**A Fase 8 fechou a 2026-09-04** — as cinco camadas de composição existem
(`Rivo.Settings`, `Rivo.EmployeePortal`, `Rivo.Dashboard`,
`Rivo.CustomerPortal`, `Rivo.Analytics`), todas segundo o padrão do ADR-041.
O Portal do Cliente está completo (ADR-043 a 046) e o Analytics saiu com
âmbito reduzido por decisão do utilizador (ADR-047): dashboards mais
profundos e importação CSV, **sem alertas e sem previsões de IA**.

**A Fase 6 (`payroll`) fechou no mesmo dia** — não por código, mas por
parecer: o parecer fiscal profissional confirmou os quatro valores de IRT e
INSS que até aqui só tinham a confirmação do utilizador (ADR-049). Nenhuma
linha mudou, precisamente porque o ADR-011 já obrigava a que taxas e escalões
fossem dados com vigência e não código.

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
| `fiscal` | ⚠ **Fatia mínima** (ADR-036), mais o motor de IRT/INSS (2026-08-30) e os limiares de isenção de subsídios (2026-08-31). Taxa plana com vigência e determinação (IVA, INSS); `IncomeTaxSchedule` — tabela de escalões progressivos — para o IRT; `SubsidyExemptionSchedule` — limiar em Kwanzas, mesmo padrão de vigência — para Alimentação e Transporte. Continua a não ser o motor fiscal completo: sem SAF-T, sem declarações periódicas |
| `commercial` | ⚠ **Reduzido ao Cliente** (ADR-036). Sem funil comercial. `Customer.UserId` (ADR-043, 2026-09-03) — Sales/Admin liga uma conta de `identity` já registada a um cliente existente, nunca por auto-declaração |
| `finance` | **Os cinco contextos existem, e os documentos lançam.** Venda (factura, nota de crédito, recibo, saldo), Contas a Pagar, Tesouraria com extracto append-only, Contabilidade & Fecho com postagem automática, Planeamento. **BR-1, BR-3, BR-5 e BR-8 impostas.** Anular uma factura, nota de crédito ou recibo estorna o lançamento (2026-08-29). ⚠ Contabilidade vazia até alguém carregar o plano; Activos Fixos sem código ainda — o K1 que os bloqueava fechou por ADR-039 (2026-08-30), falta escrevê-los |
| `procurement` | **Os quatro agregados, e o 3-way match fecha.** Fornecedor com IBAN verificado (ISO 13616) e publicado a `finance`; requisição com linhas e decisão de `approval`; Ordem de Compra, que só nasce de requisição aprovada e não deixa encomendar acima do aprovado; Recepção parcial, acumulada por linha e nunca acima do encomendado. A factura liga-se à Ordem (`PurchaseOrderId`, opcional) e `GET .../match` mostra encomendado, recebido e facturado lado a lado — recusa ligar a uma ordem de outro fornecedor, mas **não bloqueia** divergência de valor: fica visível, não impede o registo |
| `payroll` | **Folha, itens, subsídios e Recibo, confirmado (2026-08-30/31).** `AddPayrollItem` pergunta a `fiscal` — nunca calcula por si — na ordem do artigo 7.º do CIRT: INSS do trabalhador, isenção de Alimentação/Transporte (até 30.000 Kz/mês cada, excesso tributado), matéria colectável, IRT por escalões; `NetSalary` sai sempre calculado, nunca recebido. Férias e Natal só compõem o recibo, sem isenção. Sem taxa/tabela/limiar em vigor, o item recusa (400) em vez de nascer com campo nulo. Recibo liga-se via `documents` (ADR-009, mesmo desenho de `hr`) a um item de folha Aprovada. Ligado a `approval` (submete pelo bruto). `verify-payroll.ps1` 26 casos. ✅ Parecer fiscal profissional confirmou os valores a 2026-09-04 (ADR-049) — trave de produção da Fase 6 levantada |
| `projects` | **Marco, Tarefa, Orçamento e Alocação de Recursos com regra de negócio, confirmado (2026-08-30/31).** Projecto como agregado — fecha, e fechado é facto histórico: nada se altera depois. Tarefa e Alocação verificam o recurso por contrato (ADR-010, BR-18) — Colaborador contra `hr`, Viatura contra `fleet` (`IVehicleDirectory`, novo). Alocação é distinta da atribuição de Tarefa: ao nível do projecto, não da tarefa; o mesmo recurso não se aloca duas vezes em aberto. Orçamento é zero ou um por projecto, moeda fixa na primeira vez (ADR-040). `verify-projects.ps1` 43/43 contra a stack local, sem falha. ⚠ Custos ao nível do projecto continuam de fora — postagem em `finance` é decisão em aberto |
| `fleet` | **Manutenção, Atribuição, Plano de Manutenção, Registo de Viagem, Despesa de Frota e Seguros com regra de negócio, confirmado (2026-08-30/31).** Viatura como agregado — um registo de manutenção aberto de cada vez, uma atribuição aberta de cada vez, vários planos activos ao mesmo tempo (sem exclusão mútua). Viagem e Despesa também pertencem ao agregado, mas sem abrir/fechar — registam-se já concluídas. `VehicleDocument` (Seguros e documentação legal) é ligação autónoma a `documents`, mesmo desenho de `EmployeeDocument` em `hr`. Atribuição e Viagem verificam o Colaborador contra `hr` (ADR-010, BR-18) — na Viagem, opcional. **Primeiro contrato de leitura publicado a 2026-08-31** — `IVehicleDirectory`, consumido por `projects`. **Custo de manutenção desde 2026-09-04 (ADR-048)** — `MaintenanceRecord.Cost`, opcional, preenchido só ao fechar o registo (é quando se sabe o valor final); nulo é "não registado", não zero, e a soma por período ignora-o em vez de o contar como grátis. `FleetExpenseCategory` **manteve-se com as três categorias** que o documento de produto nomeia — o custo de manutenção não passou a ser uma quarta. `verify-fleet.ps1` 51/51 contra a stack local, sem falha |
| `messaging` | **Conversas e tickets, nascido a 2026-09-04 (ADR-045/046).** `Conversation` e `Message` como agregado — `Kind` distingue mensagem directa de ticket, e é a única diferença estrutural entre os dois. Mensagem directa: **uma conversa aberta por cliente de cada vez**, imposta por índice único filtrado. Ticket: **várias abertas ao mesmo tempo**, cada uma com assunto livre obrigatório. `AddMessage`/`Close` são os mesmos métodos para ambos. O aviso de mensagem nova vai para `Customer.AssignedToEmployeeId` (o vendedor responsável, novo em `commercial`) via `notifications`; **sem vendedor atribuído ninguém é avisado**, e a conversa fica só na fila partilhada. ⚠ **Sem notificação ao cliente quando Sales responde** — o cliente vê ao abrir o portal. ⚠ **Sem `modules/messaging.md`** — é o único módulo com código e sem ficheiro de módulo |
| `inventory` | **Movimento, Armazém, Transferência, Contagem e Valorização com regra de negócio, confirmado (2026-08-30/31).** Item como agregado — Recepção, Saída e Ajuste, todos com `WarehouseId` obrigatório (retrofit 2026-08-31); `QuantityOnHand` é o total agregado, `QuantityOnHandAt` a leitura por armazém. `Warehouse` é agregado raiz próprio. Transferência é atómica — sem estado "em trânsito" — e nunca altera o total. `InventoryCount` (agregado raiz próprio) abre num armazém, acumula uma linha por item contado com o esperado congelado no momento em que nasce, e o fecho gera um Ajuste por linha com variância — tudo numa transacção, tudo ou nada. `AverageCost` por item, custo médio ponderado (decisão do utilizador), recalculado só na Recepção; `GET /inventory/valuation` soma o valor movimentado por período. `verify-inventory.ps1` 66/66 contra a stack local, sem falha. Fecha a Fase 7 de `inventory` por completo — nenhuma pergunta de negócio em aberto |

Detalhe com datas e ressalvas em [implemented.md](implemented.md).

**Os marcados com uma ⚠ são fatias deliberadas do produto** (ADR-036, com o
custo do que falta registado em cada `modules/*.md`). **Nenhum módulo
continua marcado ⚠⚠ (esqueleto de prazo)** — categoria que existiu entre
2026-08-29 e 2026-08-30 para `payroll`, `projects`, `inventory` e `fleet`:
sem regra de negócio, sem testes de domínio, feitos para "existir e
responder". Os quatro ganharam regra de negócio real a 2026-08-30, o
último a sair da categoria foi `payroll` (motor de IRT/INSS). **Todos têm
verificação end-to-end**: `verify-inventory.ps1` 66 casos,
`verify-fleet.ps1` 51, `verify-projects.ps1` 43, `verify-payroll.ps1` 26 —
todas confirmadas contra a stack local sem falha nova (só o K20,
pré-existente e sem causa de código, em `verify-payroll`).

**As cinco camadas de composição** vivem em `src/Composition/`, não em
`src/Modules/`, e não aparecem na tabela acima por não serem módulos
(ADR-041, `domain/domain-map.md` §"Não são módulos"):

| Camada | Estado |
|---|---|
| `Rivo.Settings` | Vista de governança (perfis + regras de aprovação, agrupadas por módulo). **Importação em massa via CSV desde 2026-09-04** (ADR-047) — Clientes, Colaboradores e Fornecedores, cada uma atrás da permissão de escrita que já protege o formulário normal da entidade, sem permissão nova. Escreve através de contratos novos publicados por `commercial`/`hr`/`procurement`. Parser CSV próprio (RFC 4180 mínimo, sem biblioteca). Uma linha malformada não pára o ficheiro. `verify-settings.ps1` 12 casos |
| `Rivo.EmployeePortal` | "Próprio" resolvido por vínculo Identity → Employee (ADR-042). ⚠ **Só isso** — recibos, férias, assiduidade e documentos continuam por fazer. 1 endpoint. `verify-employee-portal.ps1` 8 casos |
| `Rivo.Dashboard` | Os cinco números (receita, despesa, lucro, a receber, a pagar) mais os clientes que mais facturaram. Permissão própria (`dashboard.overview.read`) porque `Manager`, que o documento de produto nomeia, não tem `finance.invoices.read`. `verify-dashboard.ps1` 9 casos |
| `Rivo.CustomerPortal` | **Completo a 2026-09-04.** Resumo financeiro, facturas, extracto de conta corrente, comprovativo de pagamento (ADR-044), mensagens directas (ADR-045) e tickets de suporte (ADR-046). Autorização por vínculo de identidade, não por permissão — excepto `documents.write`, para o comprovativo. `verify-customer-portal.ps1` 26 casos |
| `Rivo.Analytics` | **Nasceu a 2026-09-04** (ADR-047). Tendência mensal de receita/despesa (variante mensal dos contratos que o Dashboard já usava), actividade de `fleet` (despesas, distância, custo de manutenção) e valorização de `inventory`. Primeiros contratos de leitura alguma vez publicados por `fleet` e `inventory`. Permissão própria, mesmo precedente do Dashboard. `verify-analytics.ps1` 8 casos |

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

**Contados contra o repositório a 2026-09-04**, não herdados da versão
anterior deste ficheiro — que já divergia do código em quase todas as
linhas. O histórico de como cada número cresceu está em
[roadmap-execucao.md](roadmap-execucao.md) e [implemented.md](implemented.md);
**não voltar a acumulá-lo aqui**, foi o que tornou esta secção ilegível.

| Área | Estado |
|---|---|
| Código | **15 módulos** em `src/Modules/` + **5 camadas de composição** em `src/Composition/`. 88 projectos em `src/`, 360 ficheiros C# escritos à mão (≈56 800 linhas), mais ≈31 900 linhas geradas pelo EF Core em 54 migrações |
| Superfície HTTP | **247 endpoints** em **20 grupos de rota**. Os maiores: `hr` 40, `finance/ledger` 30, `procurement` 19, `inventory` 19, `finance` 18, `payables` 16, `fleet` 16, `identity` 14, `projects` 13 |
| ADRs | **53**, todos aceites. Os cinco últimos: ADR-049 (parecer fiscal levanta a trave de produção de `payroll`), **ADR-050 (quem decide uma aprovação vem do token, não do corpo — corrige falha de segurança)**, **ADR-051 (ligar uma conta a um Colaborador já admitido, com permissão própria fora do perfil HR)**, **ADR-052 (desligar, e as decisões já tomadas continuam válidas)** e **ADR-053 (histórico do vínculo, sem tocar no caminho que decide)**. Os quatro últimos são uma linha só: o 050 tornou o vínculo conta↔colaborador o único determinante de quem decide, o 051 deu-lhe a rota que faltava, o 052 deu-lhe a saída, e o 053 deu-lhe memória. Cada um foi aberto pelo anterior |
| Entidades de domínio | **64 ficheiros** em 15 módulos — `finance` 17, `hr` 12, `fleet` 7, `inventory` 5, `projects` 5, `procurement` 4, `fiscal` 4, `approval` 2, `payroll` 2, e um cada em `audit`, `commercial`, `documents`, `identity`, `messaging`, `notifications`. Conta entidades, **não raízes de agregado** — `InventoryCountLine` e `StockMovement`, por exemplo, são filhos e não raízes |
| Documentação | ≈19 900 linhas de Markdown em `.claude/` — ADRs, módulos, domínio e estado |
| Perfis de Acesso | **8** — os 7 do documento de produto mais `Cliente` (ADR-043). `Admin` tem **71 permissões**, confirmado em base a 2026-09-05 (a 71.ª é `hr.employees.link_account`, ADR-051). A autorização dos portais continua por vínculo de identidade, não por permissão — as excepções são `documents.write` (comprovativo de pagamento, ADR-044) e as permissões próprias de `Dashboard` e `Analytics`, concedidas a `Manager` porque o documento de produto o nomeia e ele não tem as permissões dos módulos subjacentes |
| Testes automatizados | **1 159** em **28 projectos**, todos passam. Por camada: **824 de domínio**, **301 de Application**, 21 de arquitectura, 9 de API, 4 de integração (Testcontainers). A distribuição por módulo é que é desigual — ver "O que não existe" |
| Verificação end-to-end | **22 suites** PowerShell, **567 casos**, contra a stack real. As maiores: `inventory` 66, `procurement` 58, `fleet` 51, `ledger` 46, `projects` 43. `verify-all.ps1` tolera explicitamente o K20 conhecido (por texto do caso, não por número — já mudou várias vezes) em vez de bloquear o gate inteiro |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

### Dois episódios que explicam o estado actual

Estavam presos dentro da tabela acima, o que a tornava ilegível. Ficam aqui
porque explicam decisões que ainda vigoram — a protecção de `main` e o
desenho da identidade externa.

**2026-09-02/03 — `main` ganhou protecção (PR obrigatório, os dois checks de
CI como obrigatórios).** Até aqui `main` não tinha nenhuma, e foi por essa
porta que sete commits fora do fluxo desta sessão (`lts`…`lts6`, `Abrir
swagger`) chegaram a produção sem build, teste nem `verify-all` a correr —
causaram um deploy partido (migração em falta de
`AccountingRule`/`ChartOfAccountsVersion`) e reabriram o K8 por outra via
(porta 5080 publicada directamente no host, contra o próprio comentário do
`docker-compose.yml`). Os dois, corrigidos. A primeira CI real contra este
código apanhou mais dois defeitos genuínos, também de fora do fluxo:
`LedgerTests.cs` não compilava (API de domínio inventada —
`PostingRule.Create` não existe, é `.Define`) e
`ChartOfAccountsVersion.Version` colidia de nome com o contador de
concorrência reservado — renomeado para `Revision`. `develop` nasceu como
branch de trabalho, e desde então **todo o trabalho entra por PR**: os
PR #7, #8 e #9 (Analytics/CSV, custo de manutenção, parecer fiscal) seguiram
esse caminho a 2026-09-04, com CI verde e deploy confirmado em cada um.

**2026-09-03 — `Customer.UserId` e a ligação de conta (ADR-043).** Decisão do
utilizador: conta própria em `identity`, oitavo Perfil de Acesso (`Cliente`);
ligação a `commercial.Customer` sempre manual por Sales/Admin, **nunca por
auto-declaração do NIF** — o NIF é informação pública, e quem o sabe não
prova que representa a empresa. Mesmo desenho do ADR-042 com os papéis
invertidos: ali a conta existe e o registo de negócio chega depois; aqui o
registo já existe e é a conta que chega depois.

## O que não existe

- ~~Teste de domínio ou regra de negócio em `payroll` e `inventory`~~ —
  **resolvido a 2026-08-30.** Os quatro esqueletos de 2026-08-29 (`payroll`,
  `projects`, `inventory`, `fleet`) ganharam regra de negócio real e projecto
  de teste próprio; nenhum continua marcado ⚠⚠. Ver a secção Módulos e o
  "Seguimento" que cada `modules/*.md` regista.
- **Cobertura de Application em dez dos quinze módulos.** Só `finance`
  (162), `identity` (28), `messaging` (23), `hr` (25) e `approval` (8) a têm.
  Os restantes dez — incluindo `procurement`, que é dos maiores — só têm
  testes de domínio e verificação caixa-preta.

  ⚠ **Os dois últimos foram ganhos a reboque de um problema, não por
  disciplina.** `approval` só a ganhou depois de a lacuna custar uma falha de
  segurança (ADR-050, K21): a decisão de quem aprova vinha do corpo do pedido
  e não era confrontada com o token. Os testes de domínio passavam, porque o
  domínio recebia o identificador já escolhido e aplicava-lhe as regras
  correctamente — **o defeito estava em quem escolhia**, que é orquestração.

  `hr` ganhou-a no dia seguinte (ADR-051) exactamente pela mesma forma:
  `Employee.LinkToUser` é um setter, e nenhuma das regras do vínculo vive no
  domínio. É o argumento mais concreto que este projecto tem para fechar os
  outros dez — a falha aparece sempre na camada que não está coberta.
- **Testes de integração** em catorze dos quinze módulos. Só
  `notifications` os tem.
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
  trabalho de outra sessão.

  ~~O contrato que esse trabalho consome está desactualizado.~~
  **Regenerado a 2026-09-04.** [API-FRONTEND.md](../../API-FRONTEND.md)
  documentava 119 rotas e não mencionava `analytics`, `messaging` nem
  `customer-portal` — a Fase 8 inteira estava fora dele. Passou a ter as
  **244 rotas**, geradas do `/openapi/v1.json` da aplicação a correr e
  cruzadas rota a rota com as permissões declaradas no código.

  **O OpenAPI sozinho não chegava**, e é a razão de o documento continuar a
  existir em vez de se remeter para o Swagger: os minimal APIs devolvem
  `IResult` sem `.Produces<T>()`, por isso o documento gerado declara `200`
  para tudo — falso para as **121 rotas** que devolvem `201`, `202` ou
  `204`, metade da superfície. A
  permissão exigida também não aparece em lado nenhum do OpenAPI. Essas duas
  colunas vêm do código.
- **`SharedKernel`.** O [CLAUDE.md](../CLAUDE.md) refere-o e manda mantê-lo
  mínimo; nunca chegou a ser criado. O ADR-035 considerou criá-lo e decidiu
  contra — ver a alternativa B desse ADR.
- **Utilizador aplicacional restrito na base de dados.** A aplicação liga-se
  como `sa`.
- ~~Regras fiscais angolanas de cálculo — IRT, INSS, códigos de isenção.~~
  **Parcialmente resolvido a 2026-09-04.** IRT e INSS estão implementados e
  o parecer fiscal profissional confirmou os valores (ADR-049). **Os
  códigos de isenção continuam por obter** — e são a única lacuna desta
  faixa que bloqueia algo hoje: emitir com `ISE` ou `NS` devolve 501, e
  `CLAUDE.md` proíbe inventar o código em falta.
- ~~`modules/messaging.md`.~~ **Escrito a 2026-09-04.** Os quinze módulos
  têm agora ficheiro de módulo. O de `messaging` regista também que a sua
  classificação estratégica é **inferência e não decisão** — o ADR-045
  fixou-o como bounded context novo sem lhe atribuir classificação.

## Riscos principais

1. **Cobertura desigual entre camadas.** Deixou de crescer em `finance`, que
   era onde mais custava — `ExecutePayment`, `RegisterReceipt`,
   `IssueCreditNote` e `CreatePaymentRequest` têm teste unitário, e a
   ordem das verificações de BR-5 está fixada por um teste que falha se
   alguém a inverter. **Doze dos quinze módulos continuam sem.** O CI apanha
   regressões de domínio e violações de fronteira; um caso de uso errado que
   compile continua a passar em `hr`, `approval`, `procurement` e nos
   restantes. O contraste agravou-se com a Fase 8: as camadas de composição
   nasceram todas com testes de Application, e os módulos antigos que os não
   têm continuam a não os ter.
2. **Nada revê o código além do próprio autor.** Com um colaborador, a revisão
   aprovadora teve de ficar a 0.
3. **Três módulos parecem mais completos do que são.** `fiscal`, `commercial`
   e `finance` respondem a HTTP e têm testes, o que é fácil de confundir com
   estarem feitos. Uma factura do Rivo tem número, série e ar de factura, e não
   é documento fiscal. Mitigação: ⚠ em cada `modules/*.md`, no ADR-036 e aqui.
4. ~~Um módulo é CRUD sem regra nenhuma, e responde tão bem quanto os
   feitos.~~ **Fechado a 2026-08-30.** `payroll` (nascido 2026-08-29, sob
   prazo de apresentação, decisão explícita) foi o último dos quatro
   esqueletos a sair deste risco — ganhou o motor de cálculo de IRT/INSS.
   `projects`, `fleet` e `inventory` já tinham saído no mesmo dia
   (Marco/Tarefa/Orçamento, Manutenção/Atribuição/Plano, Movimento). Nenhum
   módulo continua marcado ⚠⚠.
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

### Estado a 2026-09-04: o roteiro acabou

**Não há próxima fase.** As Fases 0 a 8 estão fechadas ou cumpridas, com
duas excepções que não são trabalho de engenharia deste repositório (K16,
que precisa de domínio; e as sete verificações que precisam de um comando de
reinício deliberadamente não configurado). O que resta divide-se em três
categorias, e vale a pena não as confundir:

| Categoria | O que lá está |
|---|---|
| **Depende de terceiros** | Domínio para a VPS (desbloqueia TLS, K16 e K17). Certificação AGT. Lista oficial de códigos de isenção — a única que bloqueia algo hoje. DS.120 v1.4 oficial. Plano de contas real, que é do contabilista |
| **Depende de escolha do utilizador** | Provider de e-mail transaccional (sem ele, `notifications` escreve em log e não envia). Provider de modelos de IA (bloqueia as previsões que o ADR-047 deixou de fora). Fonte da taxa de câmbio. Object storage para `documents`. Mecanismo de reconciliação bancária |
| **Depende só de código** | Regenerar o `API-FRONTEND.md` (ver "O que não existe" — é a mais accionável). Escrever `modules/messaging.md`. Utilizador de base de dados restrito em vez de `sa`. Observabilidade. Cobertura de Application nos dez módulos sem ela. Activos Fixos em `finance`, desbloqueados pelo ADR-039 e por escrever. Funil comercial (lead, oportunidade, proposta), fora de âmbito desde o ADR-036 |

**O que esta lista não tem** vale tanto como o que tem: nenhuma reescrita,
nenhuma fronteira por corrigir, nenhum módulo por refazer. O custo de
retrofit que o roteiro temia — numeração fiscal, imutabilidade, concorrência
optimista, ownership de dados — foi pago à cabeça.

### Itens anteriores, com o estado de hoje

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
4. **Cobertura de Application nos outros módulos** — `finance` tem 162 testes
   de Application, `identity` 28, `messaging` 23, `hr` 25 e `approval` 8; os
   outros dez módulos, nenhum.

   Os dois últimos da lista entraram a reboque de problemas concretos
   (ADR-050 e ADR-051), não por plano — e é esse o padrão a inverter. O
   próximo que mais custa é `procurement`: é dos maiores, submete pedidos a
   `approval`, e o 3-way match é orquestração pura sobre três agregados que o
   domínio não vê em conjunto.
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
8. ~~`payroll` é o único esqueleto de prazo que resta sem regra de
   negócio.~~ **Fechado a 2026-08-30** — motor de IRT/INSS, e depois Recibo
   (via `documents`), ver abaixo. `projects`, `fleet` e `inventory` saíram
   da lista de esqueletos no mesmo dia (Orçamento de Projecto/ADR-040, Plano
   de Manutenção, Movimento). **A Fase 7 fechou por completo a 2026-08-31**
   — Alocação de Recursos em `projects`, Armazém/Transferência/Contagem em
   `inventory`, e Registo de Viagem/Despesa de Frota/Seguros em `fleet`
   saíram todos da lista de trabalho de engenharia sem decisão à espera (ver
   "Fechado" abaixo) — a primeira com Colaborador e Viatura feitos e custos
   de fora por decisão explícita (postagem em `finance` depende de "tempo
   real ou em lote?"); a segunda e a terceira com retrofit de `WarehouseId`,
   transferência atómica, contagem que gera Ajuste no fecho, e Viagem/Despesa
   sem abrir/fechar. **A única decisão de negócio que a Fase 7 ainda tinha
   pendente — o método de valorização de stock — foi respondida pelo
   utilizador no mesmo dia** (custo médio ponderado) e implementada de
   imediato: `AverageCost` por item, recalculado só na Recepção. Fecha a
   Fase 7 de `inventory` por inteiro, e com ela a Fase 7 inteira sem nenhuma
   pergunta de negócio por resolver. Ver o "Seguimento" em cada
   `modules/*.md`.
9. **A Fase 8 arrancou no mesmo dia, 2026-08-31** — Configurações &
   Administração, a primeira das cinco camadas de composição. Sem forma de
   código nenhuma antes disto; ADR-041 fixou o padrão (`Application` + `Api`,
   sem Domain nem Infrastructure, em `src/Composition/`) e `Rivo.Settings` é
   a primeira aplicação concreta. Ver "Fechado" abaixo.
10. **Portal do Colaborador, mesmo dia — a segunda.** O utilizador escolheu
    directamente por onde continuar a Fase 8 e registou a decisão:
    "próprio" resolve-se pelo vínculo Identity → Employee
    (`IEmployeeDirectory.FindByUserIdAsync`, novo), nunca por permissão
    nova — é regra de contexto (ADR-042). Também fixou a ordem para o
    resto da Fase 8 (contratos de leitura Finance/Commercial → Dashboard
    Executivo → decisão de identidade externa → Portal do Cliente →
    Analytics & IA) e confirmou não voltar ao plano de contas do PGC
    enquanto nada da Fase 8 depender directamente dele. Ver "Fechado"
    abaixo.
11. **Contratos de leitura de `finance`, mesmo dia — o primeiro passo da
    ordem que o utilizador fixou no item anterior.** `IReceivablesOverview`
    e `IPayablesOverview`, separados um do outro (mesma fronteira interna
    de `ISalesInvoiceStore`/`IPayablesStore`), moeda sempre explícita
    (mesma disciplina de `BudgetCheck`), só saldos correntes (mesma
    fronteira de `GET /inventory/valuation`). `commercial` não precisou de
    nada novo — `ICustomerDirectory` já resolvia o nome do cliente. Só o
    desenho e os contratos ficam feitos; o Dashboard Executivo em si
    continua por construir. Ver "Fechado" abaixo.
12. **Dashboard Executivo, mesmo dia — item 1 do documento de produto.** O
    utilizador confirmou o âmbito: os cinco números
    (receita/despesa/lucro/AR/AP + top clientes), um só
    `GET /dashboard/overview`. Primeira camada de composição a ganhar
    `Contracts` próprio — não por ter consumidor, mas para `identity`
    conceder `dashboard.overview.read` a `Manager`, que
    `docs/rivo-suite-descricao-modulos.md` nomeia e que não tem
    `finance.invoices.read`. Apanhou um defeito real (`GroupBy` para
    registo posicional sem tradução SQL) só visível ao subir a stack. Ver
    "Fechado" abaixo.

**Fechado a 2026-08-30 (payroll: os dois pontos de IRT/INSS que bloqueavam
produção):** o utilizador confirmou, depois de reafirmar mais cedo no mesmo
dia que não decidiria por via de arquitectura, que não havia fonte fiscal
disponível e que respondia directamente.

- **Parcela fixa do escalão 150.001–200.000 = 12.500 Kz.** O salto de
  12.500 Kz na fronteira da isenção é real, não erro de transcrição — mesmo
  padrão do escalão equivalente na tabela histórica (isenção 70.000).
- **Parcela fixa do escalão 1.500.001–2.000.000 = 292.250 Kz** (valor da
  Angolex; a contribuição do cliente, 292.000, fica descartada como
  provável erro de OCR).
- **INSS sem tecto contributivo** — os 3%/8% incidem sobre o salário bruto
  inteiro, sempre.

**A fonte destes três valores é o utilizador, não o Anexo I da Lei n.º 14/25
nem parecer de fiscalista** — essa fonte primária continua por obter, e a
distinção fica registada em `pending-decisions.md`, `modules/fiscal.md` e
`modules/payroll.md` para não se perder da próxima vez que o valor for
citado.

> **Actualização de 2026-09-04 (ADR-049): a reserva acima foi levantada.** O
> parecer fiscal profissional confirmou os mesmos três valores, mais as
> isenções de subsídio de 2026-08-31. Nenhum valor mudou e nenhuma linha de
> código foi precisa. O parágrafo fica como estava porque descreve
> correctamente o que se sabia a 2026-08-30 — o registo é do que era
> verdade nesse dia, não do que veio a ser.

**Isto não implementa o cálculo de IRT/INSS.** Resolve a incógnita fiscal
que faltava; o que faltava a seguir era engenharia — ver o fecho imediatamente
abaixo, na mesma sessão.

**Fechado a 2026-08-30 (`fiscal`+`payroll`: motor de cálculo de IRT/INSS):**
com a incógnita fiscal resolvida (acima), `fiscal` ganhou um agregado novo,
`IncomeTaxSchedule` — série de versões de uma **tabela** de escalões
progressivos, mesmo padrão de vigência/`InForceOn` de `TaxRateSchedule`, mas
cada versão guarda vários `IncomeTaxBracket` (Parcela Fixa + Taxa × Excesso
de) em vez de um único número. `SelectBracket` escolhe o escalão de maior
"excesso de" que a matéria colectável ainda ultrapassa — nunca iguala — o
que reproduz correctamente tanto 150.000 (isenção) como 150.001 (já no
escalão seguinte, com o salto de 12.500 Kz confirmado).

`TaxKind` ganhou `EmployeeSocialSecurity`/`EmployerSocialSecurity`, e o INSS
carrega-se pelo mecanismo já existente de `TaxRateSchedule` — é uma taxa
plana como o IVA, não precisou de desenho novo. `payroll.AddPayrollItem`
pergunta a `fiscal` na ordem do artigo 7.º do CIRT: determina o INSS do
trabalhador à data do fim do período (`PayrollRun.PeriodEndDate`, não a data
corrente — ADR-011 §3), deduz-o do bruto, pede o IRT sobre a matéria
colectável resultante, e só então `PayrollItem.ApplyCalculation` grava os
três campos — o líquido é sempre `bruto − IRT − INSS`, calculado dentro do
método, nunca recebido como parâmetro solto que pudesse discordar da soma.
Sem taxa de INSS ou tabela de IRT em vigor à data, o item recusa (400) em
vez de nascer com um campo a fingir "por calcular" — mesmo padrão de
`IssueSalesInvoice` perante `TaxDeterminationOutcome.NoRateInForce`.

**Um defeito real, apanhado só na verificação end-to-end**: o `switch`
exaustivo que traduz `TaxKind` entre `Domain` e `Contracts`
(`ListTaxRates.ToDomain`/`ToContract`, padrão do ADR-010) não tinha entrada
para os dois valores novos — `dotnet test` não apanhou, porque nenhum teste
de domínio ou de aplicação passava um `TaxKind` de INSS por esse caminho; só
apareceu como 500 ao pedir a determinação de INSS contra a API a correr.
Corrigido no mesmo dia.

Testes: `Rivo.Fiscal.Domain.Tests` cresceu de 18 para 39 (`IncomeTaxSchedule`,
incluindo o exemplo documentado bruto 250.000 → IRT 38.900); nasceu
`Rivo.Payroll.Domain.Tests` com 16 (`ApplyCalculation`, incluindo o mesmo
exemplo ponta-a-ponta, líquido 203.600). `verify-fiscal.ps1` cresceu de 12
para 20 casos — semeia o INSS real (3%/8%) e a Tabela B completa de forma
**idempotente** (por códigos e vigência reais, não por código único de
corrida, ao contrário dos casos 1-12 da mesma suite) — e `verify-payroll.ps1`
de 5 para 17, com o cálculo real a substituir a verificação de "campos ficam
nulos", mais um caso novo para a recusa por falta de dados fiscais.
`verify-all.ps1` completo: 395/398, as 3 falhas todas o K20 pré-existente
(limpeza de política), zero regressão nova.

**A reserva de fonte não muda.** O mecanismo está pronto e testado; os
valores continuam a depender do utilizador, não de fiscalista nem do Anexo I
da Lei n.º 14/25. ~~Tratamento de subsídios em IRT continua sem resposta, e
`PayrollItem` não distingue componentes do salário bruto~~ — **resolvido a
2026-08-31**, ver abaixo.

**Fechado a 2026-08-30 (`payroll`: Recibo, ligado a `documents`):** com o
motor de IRT/INSS fechado (acima), no mesmo dia `payroll` ganhou
`PayrollItemDocument` — a ligação entre um Item de Folha e o ficheiro do
recibo. Entidade independente, não filha do agregado da folha (mesma razão
de `Rivo.Hr.Domain.EmployeeDocument`: anexar um documento não é decisão do
agregado, é registo à parte, feito depois de a folha já ter dito o que tem a
dizer): FK real para `payroll.payroll_item(id)`, e por SQL entre schemas
numa migração própria (`AddCrossSchemaDocumentForeignKey`, mesmo nome e
desenho da versão de `hr`), para `documents.document(id)`. `documents`
guarda o ficheiro e o hash; `payroll` guarda a categoria e sabe o que ela
significa (BR-15, retenção legal — **10 anos, confirmado pelo utilizador a
2026-08-31**; só documentado, sem mecanismo novo — BR-14 já bloqueia
eliminação física em todo o sistema, ver "Fechado" abaixo).

**Upload e anexar continuam passos separados**, mesma disciplina de `hr`:
upload exige `documents.write`, anexar exige `payroll.runs.write` porque
está a alterar-se o registo do item. **Só se anexa a um item de uma folha
Aprovada** (409 antes disso) — a única regra desta funcionalidade que é
inferência da sessão e não requisito confirmado: um recibo é prova do que
foi autorizado, e os valores de um item podem mudar em Draft ou
PendingApproval. Registada como tal em `modules/payroll.md`, revisável.

Testes: `Rivo.Payroll.Domain.Tests` cresceu de 16 para 22
(`PayrollItemDocumentTests`, mesmo desenho de `EmployeeDocumentTests`).
`verify-payroll.ps1` cresceu de 17 para 22 casos — recusa antes de Aprovada,
anexar com sucesso, listar com os metadados de `documents` juntados em
memória, documento inexistente devolve 404, e o anexo fica na trilha com
actor. Todos passaram à primeira corrida — zero defeito apanhado nesta
ronda, ao contrário das duas anteriores do mesmo dia (`fleet` e o motor de
IRT/INSS).

**Fechado a 2026-08-31 (`fiscal`+`payroll`: subsídios com tratamento
fiscal):** última incógnita do IRT por resolver (`docs/rivo-fiscal-regras-angola-v1.md`
§1.9). O utilizador confirmou directamente, sem fonte fiscal profissional:
Subsídio de Alimentação e Subsídio de Transporte isentos até **30.000
Kz/mês cada**, excesso soma-se à matéria colectável; Subsídio de Férias e
Subsídio de Natal **sem isenção nenhuma**, tributados normalmente.

`fiscal` ganhou `SubsidyExemptionSchedule` — mesmo padrão série+versão de
`TaxRateSchedule`, mas guarda um `Amount` (Kwanzas) em vez de uma
`Percentage`; não coube no agregado existente sem forçar o campo. Uma série
por `SubsidyKind` (só `FoodAllowance`/`TransportAllowance` — Férias e Natal
não têm limiar nenhum, por isso nem entram no enum). `payroll` ganhou
`PayrollItem.FoodAllowance`/`TransportAllowance`/`VacationAllowance`/`ChristmasAllowance`
— **componentes do bruto, não uma soma a ele** (`Sum(subsídios) ≤
GrossSalary` é invariante nova). `AddPayrollItem` só pergunta o limiar a
`fiscal` quando o subsídio é declarado (> 0), para não obrigar um item sem
subsídios a depender de configuração que não usa.

**Um defeito real, só visível ao subir a stack**: a migração de EF para as
quatro colunas novas de `payroll_item` nunca foi gerada — `dotnet test`
passou inteiro sem o apanhar, porque nenhum teste sobe o container Docker;
a API entrou em crash loop no arranque com `PendingModelChangesWarning`
promovido a excepção fatal. Corrigido no mesmo dia: migração
`AddPayrollItemAllowances` gerada e aplicada, com `defaultValue: 0m` para as
linhas já existentes.

Testes: `Rivo.Fiscal.Domain.Tests` cresceu de 39 para 50
(`SubsidyExemptionScheduleTests`); `Rivo.Payroll.Domain.Tests` de 22 para 30
(`PayrollItemAllowanceTests`). `verify-fiscal.ps1` cresceu de 20 para 23
(semeia os dois limiares, idempotente); `verify-payroll.ps1` de 22 para 26
— dois cenários ponta a ponta (subsídios dentro do limiar, isenção total;
acima do limiar, excesso tributado, Férias/Natal sem isenção) mais duas
recusas de validação (subsídio negativo, soma acima do bruto), todas 400.

**A trave de produção não muda**: o mecanismo está pronto e testado, a
fonte dos valores continua o utilizador, não fiscalista nem texto legal
primário.

**Corrida completa (`verify-all.ps1`) confirmada a 2026-08-31, já com
subsídios: 407/410** — as 3 falhas continuam a ser o mesmo K20, nas mesmas
três suites de sempre (`verify-ledger`, `verify-payroll`,
`verify-procurement`); zero regressão nova.

**Fechado a 2026-08-31 (`payroll`: prazo de retenção do recibo, BR-15):**
o utilizador confirmou directamente — **10 anos**, mesma reserva de fonte
das entradas fiscais (não é texto legal primário). Registado só como
documentação (`modules/payroll.md`, `pending-decisions.md`): BR-14 já
bloqueia eliminação física em todo o sistema — nenhum módulo publica rota
`DELETE` — por isso o prazo já está estruturalmente satisfeito sem
mecanismo novo. Um campo explícito de "retido até" ficaria sem consumidor
e seria especulativo.

**Fechado a 2026-08-31 (`projects`+`fleet`: Alocação de Recursos):**
`Project` ganhou `ProjectResourceAllocation` — Colaborador ou Viatura, com
início e fim opcional — mesmo desenho de `Rivo.Fleet.Domain.VehicleAssignment`
(`Assign`/`End`), com a diferença de que um projecto tem vários recursos
alocados em simultâneo, ao contrário de uma viatura (um motorista de cada
vez). O mesmo recurso (mesmo tipo+identificador) não se aloca duas vezes em
aberto — termina a alocação actual primeiro.

**Distinta da atribuição de Tarefa**: alocar é ao nível do projecto,
"quem/o que está afecto", para planeamento de capacidade — não tem relação
estrutural com nenhuma Tarefa concreta. `fleet` ganhou o seu primeiro
contrato de leitura publicado, `IVehicleDirectory` (mesmo padrão de
`IEmployeeDirectory` em `hr`, ADR-010), para `projects` verificar a Viatura
sem lhe possuir o registo — a segunda direcção de dependência prevista em
`modules/projects.md` desde 2026-08-29 e nunca antes ligada.

**Custos ficam de fora, deliberadamente.** `modules/projects.md`
§Conceitos lista "pessoas, viaturas, custos", mas atribuir um custo directo
ao projecto implica postar em `finance`, e o mecanismo de postagem (tempo
real ou em lote) é decisão em aberto (`state/pending-decisions.md`).
Construir sem essa decisão seria especulativo — mesma disciplina do
ADR-040 perante a validação cruzada com o orçamento de `finance`.

Testes: `Rivo.Projects.Domain.Tests` cresceu de 39 para 55
(`ProjectResourceAllocationTests`, 16 casos). `verify-projects.ps1` cresceu
de 33 para 43 — colaborador e viatura alocados e verificados contra `hr`/
`fleet`, recusa por data anterior ao início do projecto, recusa do mesmo
recurso duas vezes em aberto, terminar alocação, recusa de terminar duas
vezes, projecto fechado não aceita alocação nova, tudo auditado com actor.
Um único defeito apanhado, na própria suite (não na aplicação): a
contagem esperada de eventos auditados incluía tentativas recusadas, que
correctamente não deixam registo — corrigido no mesmo dia. `verify-fleet.ps1`
confirmado sem regressão, 38/38.

**Fechado a 2026-08-31 (`inventory`: Armazém e Transferência, retrofit):**
duas perguntas de arquitectura/processo, ambas explicitamente em aberto em
`modules/inventory.md` (a segunda) ou descobertas ao ler o código já enviado
(a primeira) — resolvidas pelo utilizador, escolhendo em ambas a opção
recomendada:

- **Alcance — retrofit, não convivência.** O Movimento já enviado a
  2026-08-30 (`RegisterReceipt`/`RegisterIssue`/`RegisterAdjustment`) passou
  a exigir `WarehouseId`, em vez de ficar por fazer ao lado de um Armazém
  novo e independente. `QuantityOnHand` mantém-se como o total agregado do
  item; `QuantityOnHandAt(warehouseId)` lê a quantidade por armazém — a
  mesma soma assinada dos movimentos, só filtrada. Uma Saída ou Ajuste nunca
  pode "emprestar" quantidade doutro armazém do mesmo item — recusada (409)
  mesmo que o total agregado chegasse.
- **Transferência — atómica.** Move uma quantidade entre dois armazéns do
  mesmo item num só passo, sem estado intermédio "em trânsito". Gera duas
  pernas ligadas (`StockMovementType.TransferOut`/`TransferIn`, cada uma
  apontando para o armazém do outro lado via `RelatedWarehouseId`) —
  rastreabilidade sem precisar de um identificador de grupo à parte. O total
  agregado do item nunca muda com uma transferência.

`Warehouse` nasceu como agregado raiz próprio de `inventory` (código único,
nome, estado Active/Inactive) — não filho de `InventoryItem`, referenciado
só por `WarehouseId` nos movimentos, mesma disciplina inter-agregado usada
em todo o Rivo (ADR-010), aqui dentro do mesmo módulo. Um armazém inactivo
recusa movimento novo (409), mesma semântica de item inactivo. Não há
eliminação de armazém (BR-14), só desactivação.

**Migração fez *backfill*, não bloqueou dados existentes.** O retrofit torna
`WarehouseId` obrigatório numa tabela (`stock_movement`) que já tinha linhas
na base local. A migração cria um armazém "Principal" e associa-o a todos os
movimentos pré-existentes antes de impor a restrição `NOT NULL` — artefacto
da migração, documentado como tal, não escolha de negócio sobre dados
históricos.

Testes: `Rivo.Inventory.Domain.Tests` cresceu de 21 para 43 (`WarehouseTests`
novo, 9 casos; `InventoryItemTests` ganhou `WarehouseId` em todos os casos
de Movimento, mais os de `Transfer` e `QuantityOnHandAt`).
`verify-inventory.ps1` cresceu de 25 para 41 — Armazém (registar, código
único, campos obrigatórios, listar/obter), movimento recusado em armazém
inexistente (404) e inactivo (409), saída recusada num armazém com
quantidade global disponível mas nada nesse armazém em concreto,
transferência com todos os casos de recusa (quantidade insuficiente na
origem, mesmo armazém dos dois lados, quantidade não positiva, armazém
inexistente), e sobrevivência ao reinício da stack. **Confirmado 41/41
contra a stack local, sem nenhuma falha, primeira corrida** — nenhum defeito
de aplicação apanhado. Detalhe em [modules/inventory.md](../modules/inventory.md).

**Fechado a 2026-08-31 (`inventory`: Contagem):** `InventoryCount` nasceu —
agregado raiz próprio, não filho de Item nem de Armazém. Nenhuma pergunta
ficou em aberto para o utilizador: as decisões de forma (âmbito por armazém,
esperado congelado no momento em que a linha nasce, fecho atómico) foram
inferidas por precedente já estabelecido no próprio módulo (mesma disciplina
do retrofit de Armazém/Transferência do dia anterior), não fixadas por
decisão de negócio.

- **Âmbito é sempre um armazém** — contar é um acto físico, num local.
- **Linha por item, esperado congelado no momento em que nasce.** A
  quantidade esperada de cada linha é lida de `QuantityOnHandAt` no
  instante em que a linha é acrescentada — nunca recalculada no fecho.
  Recalcular absorveria em silêncio qualquer movimento acontecido durante a
  contagem, escondendo exactamente a divergência que a contagem existe para
  apanhar. O mesmo item não se conta duas vezes na mesma sessão.
- **Fechar gera Ajuste por linha com variância, na mesma transacção do
  próprio fecho — tudo ou nada.** `CloseInventoryCount` toca dois agregados
  (`InventoryCount` e, por cada linha com variância, `InventoryItem`) e só
  grava tudo junto no fim; se um item recusar o ajuste (por exemplo, ficou
  inactivo entretanto), nada fica gravado, nem sequer o fecho da contagem —
  mesma disciplina de "emitir passa a lançar, na mesma transacção" já usada
  em `finance`. Fechar sem nenhuma linha é recusado (409) — não há o que
  confirmar.
- **Cancelar exige motivo** (mesma disciplina de Ajuste sem explicação);
  fechada é facto histórico (BR-14) — não se cancela nem aceita linha nova.
- **Múltiplas contagens simultaneamente abertas no mesmo armazém são
  permitidas** — simplificação deliberada e documentada, não invariante
  esquecida: o mesmo item podia em teoria ser contado em duas sessões
  concorrentes sem que uma soubesse da outra. Aceite por agora; revisitar se
  se tornar um problema real.

Testes: `Rivo.Inventory.Domain.Tests` cresceu de 43 para 64
(`InventoryCountTests`, 21 casos). `verify-inventory.ps1` cresceu de 41 para
60 — abrir contagem (armazém inexistente 404, inactivo 409), acrescentar
linha (item duplicado 409, quantidade negativa 400, contagem/item
inexistente 404), fechar (sem linhas 409, gera ajuste e actualiza
`quantityOnHandAt`, segundo fecho 409, linha nova depois de fechada 409),
cancelar (com motivo, sem motivo 400, já fechada 409, duas vezes 409),
listagem filtrada por armazém, sem eliminação, tudo auditado com actor.
**Confirmado 60/60 contra a stack local, sem nenhuma falha** — um defeito
apanhado, mas na própria suite, não na aplicação: um caso usava um `itemId`
aleatório para testar quantidade negativa, e a aplicação (correctamente)
verificava a existência do item antes da quantidade, devolvendo 404 em vez
do 400 esperado — corrigido para reutilizar o item real da suite, que já
tinha uma linha, provando que a validação de quantidade acontece antes da
verificação de duplicado. Detalhe em [modules/inventory.md](../modules/inventory.md).

**Fechado a 2026-08-31 (`inventory`: Valorização de stock por custo médio
ponderado) — fecha a Fase 7 de `inventory` por inteiro:** decisão de negócio
do utilizador ("Custo médio ponderado (Recomendado)"), última pergunta em
aberto do módulo — sem fonte fiscal a verificar, é escolha de gestão.

`InventoryItem.AverageCost` (por item, nunca por armazém) recalculado só na
Recepção — `(QuantityOnHand × AverageCost + quantity × unitCost) /
(QuantityOnHand + quantity)`, depois de o movimento entrar; auto-corrige-se
sem caso especial quando o item estava esgotado (0 × qualquer coisa = 0).
Saída, Ajuste e Transferência congelam o custo corrente no próprio
`StockMovement.UnitCost`, sem o alterar — snapshot, não recálculo. Custo
negativo recusado (400); zero permitido (amostra, doação).

`GET /inventory/valuation?from=&to=` soma `Value` (`Quantity × UnitCost`,
assinado) dos movimentos no período — "quanto valor se moveu nesta janela",
deliberadamente sem reconstruir quantidade/valor num ponto no tempo
passado. Janela invertida recusada (400).

**Migração fez *backfill* honesto a zero**, mesmo padrão das anteriores:
`unit_cost` e `average_cost` nascem a zero para dados já existentes na base
local — sem custo de compra capturado antes da migração.

Testes: `Rivo.Inventory.Domain.Tests` cresceu de 64 para 73 (nove casos
novos de `AverageCost`). `verify-inventory.ps1` cresceu de 60 para 66 e
**confirmou 66/66 contra a stack local, na segunda corrida** — a primeira
apanhou um defeito real: a resposta da API de Transferência esquecia
`averageCost` (só o helper de Recepção/Saída/Ajuste o tinha), corrigido no
mesmo dia. Fecha a Fase 7 de `inventory` por inteiro, sem nenhuma pergunta
de negócio por resolver. Detalhe em [modules/inventory.md](../modules/inventory.md).

**Fase 8 iniciada a 2026-08-31 — Configurações & Administração
(`Rivo.Settings`), primeira camada de composição a ganhar código:**
`domain/domain-map.md` §Read models e `docs/rivo-arquitetura-global-v1.md`
§1.4 já resolviam em prosa que Dashboard, Portais, Configurações e
Analytics não são bounded contexts — faltava a forma concreta.

**ADR-041 fixa o padrão**, para as outras quatro áreas seguirem sem reabrir
o desenho: `Application` + `Api`, sem Domain nem Infrastructure, sem base
de dados própria; vive em `src/Composition/<Nome>/`, não `src/Modules/`;
depende de outros módulos só pelos seus contratos, exactamente a mesma
regra de sempre; regista-se no host como qualquer módulo
(`AddXModule`/`MapXModule`).

`Rivo.Settings` compõe dois contratos num único `GET /settings/overview`:

- **`IAccessProfileCatalogue`** de `identity` — primeiro consumidor externo
  de `identity`, desde sempre. `Rivo.Identity.Contracts` nasceu para isto —
  até este dia, `identity` era o único módulo implementado sem assembly de
  `Contracts` (ADR-017: "criado quando o módulo tem consumidor"). O
  catálogo de permissões (`Permissions`) mudou-se para lá como
  `IdentityPermissions`, mesmo lugar de `HrPermissions` e todos os outros —
  refactor mecânico em seis ficheiros, sem alterar comportamento nenhum.
- **`IApprovalPolicyCatalogue`** de `approval` — segundo contrato de
  leitura do módulo, devolve um resumo por política (processo,
  activa/inactiva, nº de passos, se exige verificação orçamental), não os
  passos nem os aprovadores.

A vista agrupa as regras de aprovação pelo prefixo do `processType` e
ordena tudo por nome. **Uma política desactivada continua a aparecer, com
`isActive:false`** — mesma disciplina de "não se elimina, desactiva-se"
(BR-14), aplicada agora a uma vista de leitura. **Admin-only sem permissão
nova**: as duas permissões que a vista soma já só pertenciam a `Admin`.

`Rivo.Architecture.Tests` ajustado: `ProjectReferenceTests` ganhou uma
excepção nomeada (`CamadasDeComposicao`) para a asserção que assumia Domain
em todo o módulo declarado — uma camada de composição não tem, por
desenho. 21/21, sem regressão.

`Rivo.Settings.Application.Tests` (novo, 4 casos, fakes escritos à mão).
`scripts/verify-settings.ps1` (novo, 7 casos) **confirmou 7/7 contra a
stack local, sem nenhuma falha na primeira corrida**. Detalhe em
[decisions/adr-041](../decisions/adr-041-camada-de-composicao-padrao.md).

**Fica por fazer, cada uma com decisão própria em aberto:** Dashboard
Executivo (precisa de contratos de leitura que `finance`/`commercial` ainda
não publicam), Portal do Cliente (superfície externa, autenticação de
cliente separada) e Analytics & IA. Portal do Colaborador ganhou a sua
decisão no mesmo dia — ver abaixo.

**Fechado a 2026-08-31 (`Rivo.EmployeePortal`: Portal do Colaborador,
"próprio" — ADR-042):** segunda camada de composição da Fase 8, decisão
directa do utilizador em resposta à escolha de por onde continuar.

- **"Próprio" é regra de contexto, nunca permissão.** `CurrentUser` (a
  identidade autenticada) é a fonte de verdade; o colaborador resolve-se
  pelo vínculo Identity → Employee. Não existe `hr.employees.read_own` nem
  equivalente — a pergunta "que módulos este perfil pode usar" não é a
  pergunta certa para "ver os meus próprios dados".
- **Sem colaborador ligado, 403 — nunca adivinha.** Testado com o próprio
  Admin do bootstrap, que não tem `hr.Employee` ligado nenhum.
- **Sem `employeeId` no pedido.** `GET /portal/me` devolve sempre e só o
  colaborador do chamador — estruturalmente impossível pedir o de outro,
  não por validação, por não existir parâmetro para isso.
- **Admin continua pelos fluxos administrativos existentes** — o portal
  não é atalho de RBAC.

`Rivo.Hr.Contracts` ganhou `IEmployeeDirectory.FindByUserIdAsync`.
**Consequência necessária, não pedida directamente:** `Employee.UserId`
passou a único quando preenchido (índice filtrado + verificação em
`HireEmployee`, 409) — o campo existia desde a Fase 0 de `hr` (ADR-004),
mas nunca tinha tido consumidor a confiar em "no máximo um colaborador por
conta"; resolver "o próprio" sobre um campo que tolerava duplicados
exporia dados por acidente.

**A mesma resposta fixou a ordem do resto da Fase 8:** contratos de
leitura Finance/Commercial → Dashboard Executivo → decisão de identidade
externa → Portal do Cliente → Analytics & IA — e confirmou não voltar ao
plano de contas do PGC agora, por não haver nada na Fase 8 que dependa
directamente dele.

Testes: `Rivo.EmployeePortal.Application.Tests` (novo, 4 casos).
`scripts/verify-employee-portal.ps1` (novo, 8 casos) **confirmou 8/8 contra
a stack local, sem nenhuma falha na primeira corrida**; `verify-hr.ps1`
cresceu de 18 para 20 (conta duplicada recusada, índice único confirmado
por SQL), sem regressão. Detalhe em
[decisions/adr-042](../decisions/adr-042-portal-colaborador-proprio.md).

**Fechado a 2026-08-31 (`finance`: contratos de leitura de AR/AP para a
Fase 8):** primeiro passo da ordem que o utilizador fixou depois do Portal
do Colaborador — sem isto, o Dashboard Executivo não tinha o que compor.

`Rivo.Commercial.Contracts` já resolvia o que faltava do lado de
`commercial` (`ICustomerDirectory`, nome do cliente) — a lacuna real era
inteiramente de `finance`, que só publicava `IBudgetAvailability`.

- **`IReceivablesOverview`** — receita líquida do período (facturas menos
  notas de crédito, ambas não anuladas, valor sem imposto — imposto
  cobrado é passivo perante o Estado, não é receita da empresa), saldo
  corrente de Contas a Receber (valor bruto — o cliente deve o total),
  top clientes por facturado (nome resolvido ao vivo por `commercial`,
  não o retrato congelado na factura — BR-18 protege o documento fiscal,
  não um KPI de gestão; Consumidor Final fica fora do ranking).
- **`IPayablesOverview`** — despesa líquida do período (facturas de
  compra registadas, não anuladas — regime de compromisso, simétrico ao
  da receita: se um lado fosse por competência e o outro por caixa,
  "lucro" misturaria regimes sem ninguém reparar), saldo corrente de
  Contas a Pagar (só desconta o **executado** — diferente de
  `CommittedAsync`, que também conta pedidos submetidos mas ainda não
  pagos, porque o dinheiro só sai na execução).

**Separados um do outro**, mesma fronteira interna de
`ISalesInvoiceStore`/`IPayablesStore` ("dois contextos distintos, juntá-los
daria uma interface que ninguém implementa sem conhecer tudo"). **Moeda
sempre parâmetro explícito**, nunca somada entre moedas — mesma disciplina
de `BudgetCheck`. **Só saldos correntes**, nunca uma reconstrução a uma
data passada — mesma fronteira que `GET /inventory/valuation` já tinha
traçado na Fase 7. Nota de crédito reduz a receita do período em que é
**emitida**, não do período da factura original.

`ISalesInvoiceStore` e `IPayablesStore` ganharam agregações de base de
dados dedicadas (`SumOutstandingAsync`, `SumNetInvoicedAsync`,
`SumNetCreditedAsync`, `TopCustomersByInvoicedAsync`,
`SumNetExpensesAsync`, `SumOutstandingPayablesAsync`) — mesma disciplina
de `OutstandingAsync` evitar `join` (documentada ali), aplicada agora ao
conjunto em vez de por factura.

**Só o desenho e os contratos ficam feitos — sem endpoint novo, sem suite
de verificação end-to-end nova.** Os dois contratos são consumidos por
C#, sem consumidor real ainda (o Dashboard Executivo continua por
construir) — mesma disciplina de não publicar superfície antes de haver
quem a peça.

Testes: `Rivo.Finance.Application.Tests` cresceu de 119 para 133
(`ReceivablesOverviewTests`, `PayablesOverviewTests`). `verify-finance.ps1`
(29/29) e `verify-payables.ps1` (30/30) confirmados sem regressão — as
stores que mudaram são as mesmas que essas suites já exercitam. Detalhe
completo em [modules/finance.md](../modules/finance.md).

**Fechado a 2026-08-31 (`Rivo.Dashboard`: Dashboard Executivo — item 1 do
documento de produto):** o utilizador confirmou o âmbito directamente —
os cinco números que o documento de produto pede, num só
`GET /dashboard/overview`: receita, despesa, lucro, Contas a Receber,
Contas a Pagar, mais os clientes que mais facturaram no período.

`Rivo.Dashboard` compõe `IReceivablesOverview`/`IPayablesOverview`
(contratos da ronda anterior, no mesmo dia). **Lucro é `Receita − Despesa`,
calculado aqui — não um contrato à parte.** Os dois lados já vinham no
mesmo regime de compromisso, simétricos de propósito; subtrair os dois
números já publicados é a conta inteira, sem inventar semântica de lucro
contabilístico que o PGC (por carregar) ainda não sustenta.

**Primeira camada de composição a ganhar `Contracts` próprio** — não por
ter consumidor (ninguém compõe o Dashboard), mas porque `identity`
precisa do catálogo de permissões, exactamente como precisa de qualquer
módulo. `docs/rivo-suite-descricao-modulos.md` nomeia `Manager` para ver
o Dashboard, e `Manager` não tem `finance.invoices.read` (só `Finance`
tem, via `ForTreasury`) — exigir os contratos subjacentes, mesmo padrão
de `Rivo.Settings`, excluiria a audiência que o documento de produto
nomeia. `dashboard.overview.read` é permissão própria, publicada em
`Rivo.Dashboard.Contracts`, concedida a `Admin` (via `All`) e `Manager`.

**Um defeito real, só visível ao subir a stack:** `TopCustomersByInvoicedAsync`
(`ISalesInvoiceStore`, criado na ronda anterior) projectava `GroupBy`
directamente para um registo de construtor posicional — o EF Core
recusa-se a traduzir isso para SQL e lança em runtime, mesmo sendo um
padrão que parece inócuo. Os 133 testes de `Rivo.Finance.Application.Tests`
não apanharam porque os fakes fazem LINQ-to-Objects, sem essa restrição —
é exactamente a classe de defeito que a verificação end-to-end existe
para apanhar. Corrigido projectando primeiro para um tipo anónimo,
materializando com `ToListAsync`, e só depois mapeando para
`CustomerInvoicedTotal` em memória.

**Um bug na própria suite, também descoberto ao correr:**
`scripts/verify-dashboard.ps1` usava uma moeda de teste isolada (`ZZZ`,
que nenhuma outra suite usa) mas assumia que essa moeda começava sempre a
zero — falso na segunda corrida do dia, porque a primeira (que apanhou o
defeito acima antes de crashar) já lhe tinha deixado facturas. Corrigido
para asserções por **delta** (o que muda, não um estado inicial que
ninguém garante) — mesma disciplina de "re-executável" que
`verify-finance.ps1` já documentava para séries e códigos de taxa.

Testes: `Rivo.Dashboard.Application.Tests` (novo, 5 casos).
`scripts/verify-dashboard.ps1` (novo, 9 casos) **confirmou 9/9 contra a
stack local**, incluindo duas corridas seguidas para provar a
re-executabilidade: 401 sem autenticação, 403 para quem não tem a
permissão (`Sales`), 200 para `Manager`, receita/a-receber a subir
exactamente o esperado ao facturar, ordem correcta no topo de clientes,
despesa/a-pagar/lucro a moverem-se correctamente ao registar factura de
compra, janela invertida recusada (400), moeda e contagem de clientes com
omissão (`AOA`, 5), sobrevivência ao reinício da stack. `verify-bootstrap`
confirma Admin com 67 permissões (66 + `dashboard.overview.read`), sem
regressão em `verify-finance`, `verify-payables`, `verify-settings` nem
`verify-employee-portal`.

**Fica por fazer da Fase 8:** Portal do Cliente (bloqueado pela decisão
de identidade externa que o utilizador já registou como pré-requisito) e
Analytics & IA (adiado até os módulos produtores terem contratos
estáveis).

**Fechado a 2026-08-31 (`fleet`: Registo de Viagem, Despesa de Frota,
Seguros):** os três últimos itens de engenharia da Fase 7, todos sem
pergunta de negócio em aberto — as decisões de forma vieram do precedente
já estabelecido no módulo.

- **Viagem e Despesa pertencem ao agregado Viatura**, mesma disciplina de
  Manutenção/Atribuição/Plano: uma viatura inactiva não aceita nenhum dos
  dois. **Ao contrário de Manutenção e Atribuição, não têm abrir/fechar** —
  registam-se já como facto concluído, mesma disciplina de `StockMovement`
  em `inventory`, e nunca se alteram nem se eliminam depois (BR-9, BR-14).
- Viagem: motorista **opcional** (ao contrário da Atribuição), verificado
  contra `hr` quando indicado. Distância é a diferença entre os dois
  odómetros, computada, nunca escrita directamente.
- Despesa: exactamente as três categorias que
  `docs/rivo-suite-descricao-modulos.md` nomeia — combustível, portagem,
  estacionamento — sem campo de moeda (sempre AOA, mesma simplificação de
  `NetSalary` em `payroll`). Sem postagem automática no razão — facto
  operacional, mesma decisão que manteve Custos de fora da Alocação de
  Recursos (postagem em `finance` depende de "tempo real ou em lote?").
- **Seguros e documentação legal não são filhos do agregado** — vivem em
  `VehicleDocument`, ligação autónoma a `documents` (ADR-009), mesmo desenho
  de `EmployeeDocument` em `hr`: sem invariante que dependa dos outros
  filhos da viatura, não precisam do limite de consistência do agregado, e
  sem guarda de estado — uma viatura inactiva continua a aceitar documento
  novo.

Testes: `Rivo.Fleet.Domain.Tests` cresceu de 42 para 58 no agregado Viatura
(Viagem/Despesa), mais `VehicleDocumentTests` novo (5 casos) — 63 no total.
`verify-fleet.ps1` cresceu de 38 para 50 — viagem com/sem motorista,
motorista inexistente (404), datas e odómetros inconsistentes (400),
despesa nas três categorias, categoria desconhecida (400), valor não
positivo (400), documento anexado com metadados de `documents`, documento
inexistente (404), viatura inactiva recusa os dois novos tipos, tudo
auditado com actor, sobrevivência ao reinício da stack. **Confirmado 50/50
contra a stack local, sem nenhuma falha na primeira corrida** — um defeito
apanhado, mas nos testes de arquitectura, não na suite E2E: `VehicleDocument`
não tinha a isenção documentada do contador de concorrência (K14/ADR-019,
mesma razão de `EmployeeDocument`) — corrigida antes de subir a stack.
Fecha a Fase 7 de `fleet`, e a Fase 7 inteira, por completo. Detalhe em
[modules/fleet.md](../modules/fleet.md).

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

**Fechado a 2026-08-30 (`projects`: Orçamento):** `Project` ganhou um
terceiro filho no agregado, `ProjectBudget` — **zero ou um por projecto**,
ao contrário de Marco e Tarefa: não há "vários orçamentos", há um, revisto
ao longo do tempo. A moeda fixa-se na primeira vez — uma revisão para outra
moeda é recusada (409), não convertida: decidir a taxa de câmbio não é
decisão deste método. Nem definir nem rever é possível depois de o projecto
fechar.

Distinto do orçamento por centro de custo de `finance` (ADR-040, ADR-037) —
os dois nunca se fundem, e a validação cruzada (uma despesa de projecto
contra o disponível de `finance`) continua por desenhar; este fecho só
implementa a entidade e a regra dentro de `projects`.

10 testes de domínio novos (`Rivo.Projects.Domain.Tests` cresceu de 29 para
39). `verify-projects.ps1` cresceu de 28 para 33 casos e **confirmou 33/33
contra a stack local no mesmo dia, sem nenhuma falha na primeira corrida**.
~~Alocação de Recursos continua por fazer, sem decisão própria.~~
**Feita a 2026-08-31** — ver "Fechado" mais abaixo. Detalhe em
[modules/projects.md](../modules/projects.md).

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
