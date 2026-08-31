# Estado do Projecto

_Última actualização: 2026-08-31_

## Fase actual

**Os catorze módulos têm código, e há um ambiente publicado.** Dez estão
completos ou em fatia deliberada; os quatro últimos — `payroll`, `projects`,
`inventory`, `fleet` — nasceram a 2026-08-29 como **esqueletos**: CRUD sem
regra de negócio, sob prazo de apresentação, decisão explícita e registada,
não descoberta depois. **Todos os quatro ganharam regra de negócio real a
2026-08-30** — Marco/Tarefa/Orçamento, Manutenção/Atribuição/Plano,
Movimento, e por último `payroll` com o motor de cálculo de IRT/INSS (ver a
secção Módulos) — e nenhum continua esqueleto puro.

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
| `fiscal` | ⚠ **Fatia mínima** (ADR-036), mais o motor de IRT/INSS (2026-08-30) e os limiares de isenção de subsídios (2026-08-31). Taxa plana com vigência e determinação (IVA, INSS); `IncomeTaxSchedule` — tabela de escalões progressivos — para o IRT; `SubsidyExemptionSchedule` — limiar em Kwanzas, mesmo padrão de vigência — para Alimentação e Transporte. Continua a não ser o motor fiscal completo: sem SAF-T, sem declarações periódicas |
| `commercial` | ⚠ **Reduzido ao Cliente** (ADR-036). Sem funil comercial |
| `finance` | **Os cinco contextos existem, e os documentos lançam.** Venda (factura, nota de crédito, recibo, saldo), Contas a Pagar, Tesouraria com extracto append-only, Contabilidade & Fecho com postagem automática, Planeamento. **BR-1, BR-3, BR-5 e BR-8 impostas.** Anular uma factura, nota de crédito ou recibo estorna o lançamento (2026-08-29). ⚠ Contabilidade vazia até alguém carregar o plano; Activos Fixos sem código ainda — o K1 que os bloqueava fechou por ADR-039 (2026-08-30), falta escrevê-los |
| `procurement` | **Os quatro agregados, e o 3-way match fecha.** Fornecedor com IBAN verificado (ISO 13616) e publicado a `finance`; requisição com linhas e decisão de `approval`; Ordem de Compra, que só nasce de requisição aprovada e não deixa encomendar acima do aprovado; Recepção parcial, acumulada por linha e nunca acima do encomendado. A factura liga-se à Ordem (`PurchaseOrderId`, opcional) e `GET .../match` mostra encomendado, recebido e facturado lado a lado — recusa ligar a uma ordem de outro fornecedor, mas **não bloqueia** divergência de valor: fica visível, não impede o registo |
| `payroll` | **Folha, itens, subsídios e Recibo, confirmado (2026-08-30/31).** `AddPayrollItem` pergunta a `fiscal` — nunca calcula por si — na ordem do artigo 7.º do CIRT: INSS do trabalhador, isenção de Alimentação/Transporte (até 30.000 Kz/mês cada, excesso tributado), matéria colectável, IRT por escalões; `NetSalary` sai sempre calculado, nunca recebido. Férias e Natal só compõem o recibo, sem isenção. Sem taxa/tabela/limiar em vigor, o item recusa (400) em vez de nascer com campo nulo. Recibo liga-se via `documents` (ADR-009, mesmo desenho de `hr`) a um item de folha Aprovada. Ligado a `approval` (submete pelo bruto). `verify-payroll.ps1` 26 casos. ⚠ A fonte dos valores fiscais continua o utilizador, não fiscalista nem Anexo I da lei |
| `projects` | **Marco, Tarefa, Orçamento e Alocação de Recursos com regra de negócio, confirmado (2026-08-30/31).** Projecto como agregado — fecha, e fechado é facto histórico: nada se altera depois. Tarefa e Alocação verificam o recurso por contrato (ADR-010, BR-18) — Colaborador contra `hr`, Viatura contra `fleet` (`IVehicleDirectory`, novo). Alocação é distinta da atribuição de Tarefa: ao nível do projecto, não da tarefa; o mesmo recurso não se aloca duas vezes em aberto. Orçamento é zero ou um por projecto, moeda fixa na primeira vez (ADR-040). `verify-projects.ps1` 43/43 contra a stack local, sem falha. ⚠ Custos ao nível do projecto continuam de fora — postagem em `finance` é decisão em aberto |
| `fleet` | **Manutenção, Atribuição, Plano de Manutenção, Registo de Viagem, Despesa de Frota e Seguros com regra de negócio, confirmado (2026-08-30/31).** Viatura como agregado — um registo de manutenção aberto de cada vez, uma atribuição aberta de cada vez, vários planos activos ao mesmo tempo (sem exclusão mútua). Viagem e Despesa também pertencem ao agregado, mas sem abrir/fechar — registam-se já concluídas. `VehicleDocument` (Seguros e documentação legal) é ligação autónoma a `documents`, mesmo desenho de `EmployeeDocument` em `hr`. Atribuição e Viagem verificam o Colaborador contra `hr` (ADR-010, BR-18) — na Viagem, opcional. **Primeiro contrato de leitura publicado a 2026-08-31** — `IVehicleDirectory`, consumido por `projects`. `verify-fleet.ps1` 50/50 contra a stack local, sem falha. Fecha a Fase 7 de `fleet` por completo |
| `inventory` | **Movimento, Armazém, Transferência, Contagem e Valorização com regra de negócio, confirmado (2026-08-30/31).** Item como agregado — Recepção, Saída e Ajuste, todos com `WarehouseId` obrigatório (retrofit 2026-08-31); `QuantityOnHand` é o total agregado, `QuantityOnHandAt` a leitura por armazém. `Warehouse` é agregado raiz próprio. Transferência é atómica — sem estado "em trânsito" — e nunca altera o total. `InventoryCount` (agregado raiz próprio) abre num armazém, acumula uma linha por item contado com o esperado congelado no momento em que nasce, e o fecho gera um Ajuste por linha com variância — tudo numa transacção, tudo ou nada. `AverageCost` por item, custo médio ponderado (decisão do utilizador), recalculado só na Recepção; `GET /inventory/valuation` soma o valor movimentado por período. `verify-inventory.ps1` 66/66 contra a stack local, sem falha. Fecha a Fase 7 de `inventory` por completo — nenhuma pergunta de negócio em aberto |

Detalhe com datas e ressalvas em [implemented.md](implemented.md).

**Os marcados com uma ⚠ são fatias deliberadas do produto** (ADR-036, com o
custo do que falta registado em cada `modules/*.md`). **Nenhum módulo
continua marcado ⚠⚠ (esqueleto de prazo)** — categoria que existiu entre
2026-08-29 e 2026-08-30 para `payroll`, `projects`, `inventory` e `fleet`:
sem regra de negócio, sem testes de domínio, feitos para "existir e
responder". Os quatro ganharam regra de negócio real a 2026-08-30, o
último a sair da categoria foi `payroll` (motor de IRT/INSS). **Todos têm
verificação end-to-end**: `verify-projects.ps1` 43 casos, `verify-fleet.ps1`
38, `verify-inventory.ps1` 41, `verify-payroll.ps1` 26 — todas confirmadas
contra a stack local sem falha nova (só o K20, pré-existente e sem causa de
código, em `verify-payroll`).

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
| Código | 14 módulos + 1 camada de composição (`Rivo.Settings`, ADR-041), 73 projectos em `src/`, 368 ficheiros `.cs` |
| Superfície HTTP | 214 endpoints em 15 grupos de rota, mais `/health` |
| ADRs | 41, aceites |
| Testes | **971** em 20 projectos, **todos passam** — incluindo os 4 de integração (Testcontainers). A 2026-08-30, `Rivo.Projects.Domain.Tests` cresceu de 29 (Marco e Tarefa) para 39 (+ Orçamento), `Rivo.Fleet.Domain.Tests` de 25 para 42 (+ Plano de Manutenção), nasceu `Rivo.Inventory.Domain.Tests` com 21 (Movimento), `Rivo.Fiscal.Domain.Tests` cresceu de 18 para 39 (+ `IncomeTaxSchedule`) e nasceu `Rivo.Payroll.Domain.Tests` com 16 (`ApplyCalculation` e o ciclo da folha), depois 22 (+ `PayrollItemDocument`, o Recibo). A 2026-08-31, `Rivo.Fiscal.Domain.Tests` cresceu de 39 para 50 (+ `SubsidyExemptionSchedule`), `Rivo.Payroll.Domain.Tests` de 22 para 30 (+ `PayrollItemAllowanceTests`, os subsídios), `Rivo.Projects.Domain.Tests` de 39 para 55 (+ `ProjectResourceAllocationTests`, a Alocação de Recursos), `Rivo.Inventory.Domain.Tests` de 21 para 43 (+ `WarehouseTests` e o retrofit de `WarehouseId`/`Transfer` em `InventoryItemTests`), depois 64 (+ `InventoryCountTests`, a Contagem), depois 73 (+ nove casos de `AverageCost`, a Valorização), e `Rivo.Fleet.Domain.Tests` de 42 para 63 (+ Viagem/Despesa no agregado Viatura, + `VehicleDocumentTests`). Nasceu `Rivo.Settings.Application.Tests` com 4 (ADR-041, Fase 8) |
| Verificação end-to-end | **18 suites** PowerShell, **480 casos** — a 2026-08-30, `verify-projects` cresceu de 14 para 33 (+ Orçamento), `verify-fleet` de 15 para 38 (+ Plano de Manutenção), `verify-inventory` de 13 para 25 (Movimento), `verify-fiscal` de 12 para 20 (+ motor de IRT/INSS) e `verify-payroll` de 5 para 17 (cálculo real), depois 22 (+ Recibo, mesmo dia). A 2026-08-31, `verify-fiscal` cresceu de 20 para 23 (+ limiares de subsídio), `verify-payroll` de 22 para 26 (+ dois cenários de subsídio ponta a ponta), `verify-projects` de 33 para 43 (+ Alocação de Recursos, confirmado 43/43), `verify-inventory` de 25 para 41 (+ Armazém e Transferência, confirmado 41/41), depois de 41 para 60 (+ Contagem, confirmado 60/60), depois de 60 para 66 (+ Valorização, confirmado 66/66 na segunda corrida), e `verify-fleet` de 38 para 50 (+ Registo de Viagem, Despesa de Frota e Seguros, confirmado 50/50). Nasceu `verify-settings` com 7 casos (ADR-041, Fase 8), confirmado 7/7 na primeira corrida. **Corrida completa (`verify-all.ps1`) confirmada a 2026-08-31, já com Fase 8: 477/480** — as 3 falhas continuam a ser o mesmo K20 (limpeza de política, sem causa de código), nas mesmas três suites de sempre (`verify-ledger`, `verify-payroll`, `verify-procurement`); zero regressão nova, `verify-settings` incluído sem falha. A primeira ronda de `verify-fleet` (26 casos) apanhou dois defeitos reais (400 em vez de 409); a primeira ronda do motor de IRT/INSS apanhou um terceiro (`TaxKind` sem entrada no `switch` de tradução, 500 em vez de determinar); a primeira ronda dos subsídios apanhou um quarto, só visível ao subir a stack — migração de EF esquecida (`PendingModelChangesWarning` fatal no arranque). O Recibo, a Alocação de Recursos e o Armazém/Transferência, sozinhos, não apanharam nenhum defeito de aplicação — só erros na própria suite (contagem de eventos auditados na Alocação; `itemId` aleatório em vez do item real num caso da Contagem, que mascarava 404 por 400). Registo de Viagem/Despesa/Seguros apanhou um defeito real, só visível nos testes de arquitectura: `VehicleDocument` sem a isenção documentada do contador de concorrência (K14/ADR-019) — corrigida antes de subir a stack. Valorização apanhou um quinto defeito real, na resposta da API de Transferência (`averageCost` em falta) — corrigido, confirmado na segunda corrida |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

## O que não existe

- ~~Teste de domínio ou regra de negócio em `payroll` e `inventory`~~ —
  **resolvido a 2026-08-30.** Os quatro esqueletos de 2026-08-29 (`payroll`,
  `projects`, `inventory`, `fleet`) ganharam regra de negócio real e projecto
  de teste próprio; nenhum continua marcado ⚠⚠. Ver a secção Módulos e o
  "Seguimento" que cada `modules/*.md` regista.
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
não publicam), Portal do Colaborador ("o próprio a ver-se a si próprio"),
Portal do Cliente (superfície externa, autenticação de cliente separada) e
Analytics & IA.

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
