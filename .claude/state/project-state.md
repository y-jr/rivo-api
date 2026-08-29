# Estado do Projecto

_Última actualização: 2026-08-29_

## Fase actual

**Os catorze módulos têm código, e há um ambiente publicado.** Dez estão
completos ou em fatia deliberada; os quatro últimos — `payroll`, `projects`,
`inventory`, `fleet` — nasceram a 2026-08-29 como **esqueletos**: CRUD sem
regra de negócio, sob prazo de apresentação, decisão explícita e registada,
não descoberta depois. Detalhe na secção Módulos.

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
| `finance` | **Os cinco contextos existem, e os documentos lançam.** Venda (factura, nota de crédito, recibo, saldo), Contas a Pagar, Tesouraria com extracto append-only, Contabilidade & Fecho com postagem automática, Planeamento. **BR-1, BR-3, BR-5 e BR-8 impostas.** ⚠ Contabilidade vazia até alguém carregar o plano; a anulação não estorna; activos fixos bloqueados por K1 |
| `procurement` | **Os quatro agregados, e o 3-way match fecha.** Fornecedor com IBAN verificado (ISO 13616) e publicado a `finance`; requisição com linhas e decisão de `approval`; Ordem de Compra, que só nasce de requisição aprovada e não deixa encomendar acima do aprovado; Recepção parcial, acumulada por linha e nunca acima do encomendado. A factura liga-se à Ordem (`PurchaseOrderId`, opcional) e `GET .../match` mostra encomendado, recebido e facturado lado a lado — recusa ligar a uma ordem de outro fornecedor, mas **não bloqueia** divergência de valor: fica visível, não impede o registo |
| `payroll` | ⚠⚠ **Esqueleto** (2026-08-29). Folha e itens, CRUD, ligado a `approval` (submete pelo bruto, aprova/recusa aplicado deste lado). **Sem cálculo de IRT/INSS** — os campos existem, ficam sempre nulos; os escalões dependem de `fiscal`, que não tem tabela angolana carregada, e `CLAUDE.md` proíbe implementar a partir do levantamento não verificado |
| `projects`, `inventory`, `fleet` | ⚠⚠ **Esqueletos** (2026-08-29). CRUD simples — Projecto; Item com SKU único; Viatura com matrícula única. Sem nenhuma das regras que `modules/*.md` descreve |

Detalhe com datas e ressalvas em [implemented.md](implemented.md).

**Os três marcados com uma ⚠ são fatias deliberadas do produto** (ADR-036,
com o custo do que falta registado em cada `modules/*.md`). **Os quatro
marcados com ⚠⚠ são esqueletos de prazo** — categoria diferente: sem regra de
negócio, sem testes de domínio, feitos para "existir e responder", não para
estarem correctos. Não confundir os dois. **Têm, desde 2026-08-29, verificação
end-to-end** (`scripts/verify-payroll.ps1`, `verify-projects.ps1`,
`verify-inventory.ps1`, `verify-fleet.ps1`) — o que confirmam é o CRUD e a
sua superfície HTTP (contrato, permissão, auditoria, persistência), nunca
regra de negócio que não existe.

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
| Código | 14 módulos, 70 projectos em `src/`, 287 ficheiros `.cs` |
| Superfície HTTP | 169 endpoints em 14 grupos de rota, mais `/health` |
| ADRs | 38, aceites |
| Testes | **706** em 15 projectos, **todos passam** — incluindo os 4 de integração (Testcontainers). **Zero** nos quatro módulos esqueleto (`payroll`, `projects`, `inventory`, `fleet`) — nenhum projecto de teste existe para eles ainda |
| Verificação end-to-end | **17 suites** PowerShell, **335 casos** — as 4 dos esqueletos e `verify-approval` (cancelamento, K18) escritas e corridas a 2026-08-29. Última corrida de cada, isolada: `verify-projects` 14/14, `verify-inventory` 13/13, `verify-fleet` 15/15, `verify-approval` 10/10, `verify-payroll` 15/16. O único caso que falha em cada corrida (2 antes, 3 agora) é a mesma falha intermitente na limpeza final de uma política, sem causa de código confirmada em quatro investigações — **K20** em [known-issues.md](known-issues.md) |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

## O que não existe

- **Teste de domínio ou regra de negócio nos quatro módulos esqueleto**
  (`payroll`, `projects`, `inventory`, `fleet`). Têm, desde 2026-08-29,
  verificação end-to-end (ver Números) — o que confirma é o CRUD e a sua
  superfície HTTP, nunca regra de negócio que não existe. Ver a nota ⚠⚠ na
  secção Módulos e o "Seguimento" que cada `modules/*.md` regista.
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
4. **Quatro módulos são CRUD sem regra nenhuma, e respondem tão bem quanto os
   feitos.** `payroll`, `projects`, `inventory`, `fleet` (2026-08-29) — sob
   prazo de apresentação, decisão explícita. Ao contrário do risco 3, aqui não
   há sequer uma regra reduzida por trás: `POST /fleet/vehicles` aceita
   qualquer matrícula, `POST /payroll/runs/{id}/items` aceita qualquer
   salário, nada verifica quem pode ver o quê para além da permissão de
   entrada. **O maior risco concreto é apresentar isto como mais do que é.**
   Mitigação: ⚠⚠ em cada `modules/*.md` e na secção Módulos acima, distinta da
   ⚠ dos três reduzidos de propósito.
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
2. **Estorno automático.** Anular uma factura, uma nota de crédito ou um recibo
   **não gera lançamento inverso** — o original fica e corrige-se à mão. É a
   lacuna mais visível da postagem.
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
8. **Dar regra de negócio real aos quatro esqueletos, ou decidir que ficam
   CRUD por agora.** `payroll` precisa de Recibo e da ligação a `fiscal`
   quando houver tabela real; `projects` precisa de Marco, Tarefa e Orçamento;
   `inventory` precisa de Movimento — sem ele `QuantityOnHand` nunca sai de
   zero; `fleet` precisa de Manutenção e Atribuição. Têm suite end-to-end
   desde 2026-08-29 (confirma o CRUD, não regra que não existe); continuam
   sem teste de domínio. Ver o "Seguimento" em cada `modules/*.md`.

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
