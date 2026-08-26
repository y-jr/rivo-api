# Rivo Suite — Arquitectura Global (Visão de Sistema Completo)

**Versão:** 1 — primeira entrega conforme o novo enquadramento pedido pelo cliente.
**Objectivo desta entrega:** compreender o Rivo como sistema empresarial completo — domínio, capacidades, ownership, fronteiras, dependências e requisitos — antes de qualquer decisão de arquitectura e antes de aprofundar o Approval Engine.

---

## 0. Nota metodológica 

**Fontes usadas nesta análise:**

1. `Rivo_Suite_Descricao_Modulos.pdf` (Abril 2026) — define o produto: 14 módulos, 7 perfis de acesso, stack (React/Tailwind, PostgreSQL+RLS, REST, WebSockets, IA), multi-tenant.
2. `NDX-DRF001-Documento de Requisitos do SGAP` (09 Jul) — 28 páginas: processo, actores, regras de negócio, RFs, RNFs, modelo de dados conceptual, KPIs, matriz RACI/CRUD.
3. O protótipo actual (`src/`, `supabase/migrations/`) — inspeccionado directamente nesta sessão: **211 tabelas** em 86 migrations, código de `src/lib/*.ts` relevante, componentes de aprovações. Não é especificação nem arquitectura de referência. É usado exclusivamente como **evidência de domínio**: que casos de uso reais emergiram, que conceitos o negócio realmente distingue, que erros de arquitectura já aconteceram quando ninguém decidiu ownership antecipadamente.
4. Duas análises anteriores produzidas nesta mesma linha de trabalho (`rivo-analise-absorcao-sgap-v1.md`, focada em absorção SGAP→Rivo, e os dois documentos sobre o Approval Engine) — usadas aqui como *matéria-prima*, não como conclusões já fechadas. Onde o código evidenciou algo que corrige uma hipótese anterior (por exemplo, "Fornecedor duplicado"), essa correcção está assinalada explicitamente abaixo.

**Regra de leitura do protótipo, reafirmada:** onde o protótipo mostra 5 implementações de aprovação, ou 2 tabelas de auditoria, ou 3 conceitos de "contrato", isso não significa "consolidar estas tabelas". Significa "o domínio tem esta necessidade recorrente e ninguém lhe deu, desde o início, um único dono" — e é assim que é tratado ao longo deste documento.

**Convenção de rotulagem** usada em todas as tabelas:
- **Facto** — está explicitamente no código, no schema ou nos documentos.
- **Inferência** — dedutível com confiança razoável a partir dos factos, mas não afirmado directamente em lado nenhum.
- **Hipótese** — assumido por falta de informação; precisa da sua confirmação.

---

## 1. Domínio: domínios, subdomínios e bounded contexts candidatos

Segue-se um mapa por camada estratégica (core / supporting / generic, na terminologia de DDD), não pela lista de 14 "módulos" do documento funcional — vários desses módulos revelaram-se, à inspecção, **canais de apresentação sobre outros domínios**, não domínios em si (ver secção 1.4).

### 1.1 Core domains (razão de ser competitiva do Rivo)

| Domínio | Bounded contexts candidatos | Responsabilidade central | Estado |
|---|---|---|---|
| **Financeiro** | Contas a Pagar (AP), Tesouraria, Contas a Receber (AR), Contabilidade & Fecho | Ciclo de vida do dinheiro: o que se deve, o que se é devido, o que existe em caixa, e como isso se traduz em livros contabilísticos | Facto: é o domínio mais denso do schema (chart_of_accounts, journal_entries, payables, receivables, budgets, bank_*, petty_cash_*) |
| **Procurement** | Requisições & Compras (Procure-to-Pay) | Do pedido interno de compra à recepção de mercadoria e casamento com a factura (3-way match) | Facto: cadeia `purchase_requisitions → purchase_orders → goods_receipts → purchase_invoices` bem modelada e sequencial |
| **Comercial / CRM** | Pipeline & Propostas, Contratos Comerciais, Cobranças | Captação, negociação, contratação e cobrança de clientes | Facto: leads→deals→proposals→commercial_contracts→receivables/collection_actions é uma cadeia coerente |
| **Recursos Humanos** | Ciclo de Vida do Colaborador, Payroll & Compensação | Tudo o que decorre da relação de trabalho, do onboarding ao offboarding, incluindo o cálculo salarial | Facto: é o domínio com mais tabelas (>40), incluindo um subdomínio de Payroll denso e quase auto-suficiente |

### 1.2 Supporting domains (necessários, diferenciam-se pouco, mas não são commodity)

| Domínio | Bounded context candidato | Observação |
|---|---|---|
| **Governança de Decisões** | Approval / Aprovações | Decidir e registar a autorização de qualquer processo sujeito a aprovação. **Reclassificado de core para supporting — ver Resolução R1 (secção 10).** O diferenciador do Rivo é a *integração* da governança em todos os contextos, não o motor em si. Construído internamente (não adquirido), mas com disciplina de âmbito: sem BPMN, sem designer visual, sem grafos arbitrários |
| **Fiscal & Compliance** | Fiscal Engine (IVA/IRT/INSS, SAF-T AO, declarações AGT) | Depende de dados do Financeiro e da Payroll, mas tem regras próprias (motor fiscal angolano) suficientemente específicas para ser um contexto à parte, não uma feature das Finanças |
| **Gestão de Projectos** | Projects | Consome Employees (recursos), Cost Centers/Budgets (orçamento de projecto), gera facturação (`InvoiceFromExpensesDialog` liga Projects→Invoices) — fronteiras com Financeiro e RH claras via referência, não via tabela partilhada |
| **Gestão de Frota** | Fleet | Auto-contido: viaturas, motoristas (ligados a Employees), despesas de frota, manutenção, seguros — baixo acoplamento com o resto |
| **Inventário & Armazém** | Inventory/Warehouse | Activos, armazéns, contagens, transferências — parcialmente sobreposto com "Activos Fixos" do Financeiro (ver 3.6) |

### 1.3 Generic domains (commodity — não diferenciam o Rivo, mas são indispensáveis)

Ver análise detalhada na secção 4 ("capacidades transversais"), porque para estas é a própria classificação módulo/bounded-context/infraestrutura que está em aberto: **Identidade & Acesso**, **Auditoria**, **Notificações**, **Documentos/Anexos**, **Background Jobs**, **Analytics & IA** (parcialmente — ver 4).

### 1.4 Módulos do documento funcional que NÃO são bounded contexts

Um dos pontos explicitamente pedidos foi não assumir que "módulo funcional = bounded context". Da inspecção resultam três módulos do documento Rivo que são, estruturalmente, **canais de apresentação** ou **camadas de leitura/composição** sobre outros domínios — não donos de dados nem de regras de negócio próprias:

- **Dashboard Executivo** (módulo 1): é uma camada de leitura agregada sobre Financeiro, Comercial e Analytics. Não possui entidades próprias (confirmado — não há tabela `dashboard_*` de negócio, apenas `daily_metrics` como snapshot). É um *read model*, não um domínio.
- **Portal do Colaborador** e **Portal do Cliente** (módulos 11 e 12): são canais de acesso self-service que compõem funcionalidades de RH, Financeiro, Approval, Documentos e Notificações para uma audiência específica (colaborador vs cliente). O schema confirma isto — `client_users` e `client_notifications`/`client_documents` são variantes "por audiência" de conceitos que já existem no lado interno (`profiles`, `notifications`, `employee_documents`), não domínios novos.
- **Configurações & Administração** (módulo 14): é a superfície de administração de Identidade/Tenant/Regras de Aprovação/Importação — não é, em si, um domínio com dados de negócio próprios; é a UI de gestão de outros contextos (Identity & Access, Approval Policy).

**Consequência arquitectural:** estes três "módulos" não devem gerar bounded contexts próprios com base de dados própria. Devem ser implementados como camadas de composição/API que consomem os contextos reais (Financeiro, Comercial, RH, Approval, Documentos, Identity). Tratá-los como domínios geraria duplicação de dados por audiência (exactamente o padrão já visível em `notifications` vs `client_notifications`).

### 1.5 Relações entre contextos (visão de alto nível)

```
Identidade & Acesso (generic/infra) ── usado por TODOS os contextos
Auditoria (generic/infra)           ── escrito por TODOS os contextos
Notificações (generic/infra)        ── accionado por TODOS os contextos
Documentos/Anexos (generic/infra)   ── usado por RH, Financeiro, Comercial, Legal

Governança de Decisões (core, transversal)
 ├── consumido por: Financeiro (Pagamentos), Procurement (Requisições/OCs),
 │                   RH (Férias, Adiantamentos, Payroll), Comercial (Descontos)
 └── lê (não possui): Identidade&Acesso (quem ocupa que cargo), Financeiro (orçamento disponível)

Procurement ──(purchase_invoice_id)──> Financeiro/Contas a Pagar ──> Tesouraria
Comercial ──(client_id, invoice)──> Financeiro/Contas a Receber
RH ──(employee_id, department, cost_center)──> quase todos os outros contextos
Projectos ──(cost_center, budget)──> Financeiro; ──(employee)──> RH; ──> Comercial (facturação)
Fiscal & Compliance ──lê──> Financeiro, Payroll
```

O padrão mais importante deste mapa: **Employee (RH) é o "shared kernel" com maior fan-out do sistema inteiro** — é referenciado por Approval (requester/approver), Procurement (requisitante), Financeiro (cost center responsible), Projectos (owner), Frota (driver), Comercial (sales owner), Departamentos (manager). Isto tem implicações directas na secção 5 (dependências) e na escolha de arquitectura (secção 7).

---

## 2. Capacidades: Rivo existente vs. SGAP

| Capacidade | Rivo hoje | SGAP pede | Avaliação | Tipo |
|---|---|---|---|---|
| Submissão de despesa eventual com cotações | Requisições internas (Procurement) cobrem compras de bens/serviços; não há fluxo dedicado a "despesa eventual avulsa" | Submissão de factura/cotação por Chefe de Departamento | Sobreposição parcial — é conceptualmente uma requisição de despesa, mas com forma mais leve | Expansão de Procurement, não módulo novo |
| Cadastro de fornecedor | **Facto (corrigido nesta versão):** existe uma única tabela `suppliers`, referenciada consistentemente por `payment_requests`, `purchase_orders`, `purchase_invoices`, `payables` — **não há duplicação ao nível dos dados**, apesar do documento funcional listar "cadastro de fornecedores" tanto em Finanças como em Procurement | Cadastro básico de fornecedor (NIF, IBAN) | A ambiguidade é só narrativa/de posicionamento no documento de produto, não técnica | Ownership já resolvido no protótipo — decisão a ratificar: Procurement é o dono natural (é quem qualifica o fornecedor), Financeiro consome |
| Validação de conformidade fiscal/documental (DAF) | Fiscal & Compliance cobre IVA/IRT/INSS/SAF-T; não cobre validação de despesa avulsa | Checklist obrigatório antes de qualquer decisão | Lacuna | Expansão de Fiscal & Compliance como serviço de validação, consumido pelo fluxo de aprovação |
| Alçada, escalonamento, dupla aprovação, delegação, SLA | **Facto:** 5 implementações paralelas fragmentadas (ver secção 3.7) — nenhuma cobre integralmente o pedido | Núcleo do SGAP | Lacuna crítica | Governança de Decisões (core domain) |
| Execução do pagamento + comprovativo | `payment_requests` cobre isto, mas com workflow de aprovação embutido na mesma tabela | Execução apenas após aprovado, com bloqueio técnico | Sobreposição, mas com violação de fronteira (workflow dentro da tabela de execução) | Financeiro/Tesouraria deve executar; decisão fica no Approval Engine |
| Disponibilidade de tesouraria | `bank_accounts`, mas sem conceito de "disponibilidade" ligado à execução | "Aprovado — aguarda tesouraria", dupla barreira estado+saldo | Lacuna | Tesouraria (subdomínio do Financeiro) |
| Planeamento de custos departamentais mensais | `budgets`/`budget_lines` já são mensais e por `cost_center_id` (não por "departamento" nominalmente, mas cost centers mapeiam departamentos) | Ciclo de submissão→consolidação→aprovação mensal ligado ao carregamento do caixa | Sobreposição parcial semântica — **decisão em aberto**: é o mesmo conceito com propósito adicional (forecasting de caixa), ou dois conceitos distintos? | Ver secção 3.5 |
| Anti-fraccionamento (janela 30 dias) | Não existe | Agregação por fornecedor+rubrica em 30 dias | Lacuna nova | Regra de negócio do Approval Engine, alimentada por dados do Financeiro |
| Auditoria append-only, 10 anos, IP | `audit_logs` existe mas **não tem coluna de IP** (facto, confirmado no schema) — não é claro se é append-only ao nível de permissões da BD | Append-only, retenção 10 anos, IP obrigatório | Sobreposição parcial, com lacuna concreta confirmada (falta IP) | Auditoria (generic/infra) |
| RBAC com segregação de funções por processo | `app_role` tem só 4 valores (admin/finance/hr/employee) — **inconsistente com os "7 perfis" do próprio documento de produto** (Admin/Manager/Finance/HR/Sales/Asset Manager/Project Manager) — facto, contradição entre os dois artefactos do protótipo | Segregação por processo individual, sem acumulação de papéis | Lacuna crítica, e também uma inconsistência interna do protótipo a não herdar | Identidade & Acesso |
| MFA obrigatório para aprovadores | Não encontrado no código inspeccionado (pode existir ao nível da plataforma Supabase Auth, não inspeccionado) | Obrigatório para DG/CEO/CFO/Finanças | Hipótese de lacuna — a confirmar | Identidade & Acesso |
| Notificações dashboard+email | `notifications`/`client_notifications` existem; WebSockets confirmados na stack | Notificação a cada mudança de estado | Sobreposição parcial | Notificações (generic/infra) |
| Dashboards/KPIs de processo | Dashboard actual é financeiro geral, não de processo de aprovação | Tempo de ciclo, taxa de rejeição, execução vs planeado | Expansão | Read-model sobre Approval + Financeiro |

---

## 3. Ownership de conceitos-chave

| Conceito | Owner candidato | Evidência | Risco de coupling |
|---|---|---|---|
| **Supplier** | Procurement (Vendor/Supplier Registry) | Facto: tabela única, sem duplicação real | Baixo — já resolvido no protótipo |
| **Customer/Client** | Comercial (Client/Account Registry) | Facto: tabela única `clients`, consumida por Financeiro (AR), Portal do Cliente, Projectos | Baixo |
| **Employee** | RH | Facto: tabela única `employees`, mas com o **maior fan-out de FKs de todo o schema** (aprovações, cost centers, departamentos, frota, projectos, comercial) | **Alto** — é o "shared kernel" do sistema; qualquer mudança ao modelo de Employee tem impacto em quase todos os contextos |
| **Department** | RH (`departments`, com `manager_id`) | Facto | Médio — sobrepõe-se parcialmente com Cost Center (ver abaixo) |
| **Cost Center** | Financeiro (`cost_centers`, com `department_id` e `responsible_id` próprios) | Facto: tem o seu próprio "responsável", distinto do gestor do departamento | **Duas noções de "quem é responsável por este centro de custo"** — gestor de departamento (RH) vs. responsável de cost center (Financeiro) podem divergir. Inferência: isto é provavelmente intencional (nem todo cost center corresponde 1:1 a um departamento), mas precisa de confirmação de negócio |
| **Invoice (venda)** vs **Purchase Invoice (compra)** | Comercial/Financeiro-AR vs Procurement/Financeiro-AP | Facto: são tabelas distintas, correctamente separadas (AR vs AP são domínios diferentes) | Baixo — separação correcta, não é duplicação a corrigir |
| **Payment (recebido)** vs **Payment Request (a pagar)** vs **Payroll Payment** | AR (Financeiro) / AP-Approval (Financeiro+Governança) / Payroll (RH) | Facto: três tabelas para três fluxos de dinheiro distintos (entra, sai com aprovação, sai para colaboradores) | Médio — a separação em si é correcta; o problema é que `payment_requests` tem workflow de aprovação embutido, violando a fronteira com Governança de Decisões |
| **Purchase** (Procure-to-Pay) | Procurement (requisição→OC→recepção), com handoff explícito para Financeiro/AP na factura | Facto: cadeia de FKs bem desenhada (`purchase_invoice_id` em `payables` e `payment_requests`) | Baixo — é o melhor exemplo de fronteira bem definida no protótipo inteiro |
| **Budget** | Financeiro | Facto: `budgets`/`budget_lines`, mensal, por cost center | Ver decisão em aberto D-BUD abaixo, quanto à relação com "custos departamentais" do SGAP |
| **Approval** | Governança de Decisões (a construir) | Facto: hoje 5 implementações paralelas — nenhuma é dona única | **Muito alto** hoje; deve descer para baixo depois de unificado, precisamente por passar a ter fronteira e contrato únicos |
| **Document/Anexo** | Fragmentado — sem dono único hoje | Facto: `generated_documents`, `document_templates`, `document_types`, `client_documents`, `employee_documents`, mais colunas ad-hoc (`file_url`, `pdf_path`, `file_path`, `document_url`) espalhadas por outras tabelas | Alto — cada módulo reinventa "guardar um ficheiro" |
| **Audit** | Fragmentado — `audit_logs` genérico + `payroll_audit_logs` quase idêntico | Facto: duplicação confirmada | Médio — duplicação de conceito, mas isolada (não referenciada por FK por outros contextos) |
| **Identity/User** | Fragmentado em três: `profiles`+`user_roles` (utilizador interno autenticado), `employees` (registo organizacional, **sem** FK para `auth.users`), `client_users` (identidade de portal, ligada a `auth.users`+`clients`) | Facto, confirmado directamente no schema | **Muito alto** — "quem está autenticado" e "quem é o colaborador/aprovador" são, hoje, conceitos que podem não coincidir (um `employee` não tem necessariamente um `user_id`). Qualquer regra de segregação de funções ("quem aprova não é quem submete") depende de resolver esta ambiguidade primeiro |
| **Contrato** | Fragmentado em três conceitos distintos e sem relação entre si: `contracts` (contrato de trabalho, RH), `commercial_contracts` (contrato de venda, Comercial), `legal_contracts` (contrato genérico multi-parte — cliente/fornecedor/parceiro/colaborador) | Facto | Baixo-médio — são semanticamente distintos (não é o mesmo erro do Approval), mas `legal_contracts` sobrepõe-se parcialmente aos outros dois pela sua natureza genérica; precisa de decisão sobre se `legal_contracts` é o "documento legal" que acompanha os outros dois ou um quarto conceito a fundir |

---

## 4. Capacidades transversais: módulo, bounded context ou infraestrutura?

Nenhuma classificação abaixo foi assumida à partida — resulta da natureza do que cada capacidade decide vs. apenas executa.

| Capacidade | Classificação candidata | Porquê | Estado no protótipo |
|---|---|---|---|
| **Identidade & Acesso** | Split: **Autenticação = infraestrutura** (delegável a um provider, não é lógica de negócio); **Autorização/RBAC = domínio partilhado** (Identity & Access como bounded context próprio) | A autenticação ("é mesmo esta pessoa?") não tem regras de negócio. Mas "que papéis pode esta pessoa acumular no mesmo processo" é uma regra de negócio explícita do SGAP (segregação de funções) — não pode ser tratada como infra pura | Hoje mistura as duas coisas: `auth.users` (Supabase, infra) + `profiles`/`user_roles` (domínio, mas rudimentar — só 4 papéis) |
| **Governança de Decisões (Approval)** | **Supporting domain (R1), implementado como capacidade transversal/partilhada** — não é infraestrutura (tem regras de negócio reais e invariantes próprias), não é "só mais um módulo" (é deliberadamente consumido por vários contextos via contrato estreito) | É exactamente o padrão pedido pelo cliente na "Orientação final": transversal, mas com fronteira de domínio própria | 5 implementações paralelas — ver secção 3.7 abaixo |
| **Auditoria** | **Infraestrutura/generic domain** — a garantia técnica (append-only, imutável, com IP/correlation ID) não carrega regras de negócio específicas de nenhum módulo; deve ser consumida uniformemente por todos | Diferente do Approval: aqui não há "decisão de negócio", só um requisito técnico transversal (RNF-007, RN-012 do SGAP) | 2 implementações quase idênticas (`audit_logs`, `payroll_audit_logs`) — confirma que, tratada como "feature de módulo", duplica-se |
| **Notificações** | **Infraestrutura de entrega + decisão de domínio distribuída** — o mecanismo de envio (dashboard+email) é commodity; a decisão de "quando notificar e quem" pertence ao contexto de origem | Cada contexto decide *quando* disparar; o serviço de notificação só entrega — não deve saber o que é uma "factura" ou uma "aprovação" | `notifications` (interno) e `client_notifications` (portal) — correctamente segmentadas por audiência, não é duplicação a corrigir |
| **Documentos/Anexos** | **Infraestrutura (generic domain)** — armazenamento de ficheiro + metadados é commodity; a *classificação* de um documento (é uma declaração de RH? é um anexo de factura?) pertence ao contexto de origem | Hoje reinventado por módulo (ver secção 3) — candidato claro a capacidade única de storage+metadata, consumida por todos | Fragmentado, sem dono único |
| **Analytics & IA** | **Misto** — funções utilitárias (exportações, importação CSV) são infra; previsões/insights são um *read-model* que consome dados de outros domínios, não um domínio que possui dados primários | O documento de produto trata-o como "módulo 10", mas não possui entidades de negócio próprias — é uma camada de leitura/composição, tal como o Dashboard Executivo (secção 1.4) | `daily_metrics` como snapshot; funções serverless de previsão |
| **Background Jobs** | **Infraestrutura pura** | Exports, digest de notificações, lembretes de SLA, cálculo de payroll, reconciliação bancária, OCR — nenhuma destas operações deve bloquear um pedido HTTP; nenhuma carrega regra de negócio específica de um módulo além da que já lhe pertence | Já existem `supabase/functions` para vários destes casos (`daily-metrics-snapshot`, `check-alerts`, `send-daily-digest`, `sync-bank-transactions`) |
| **File Storage** | Infraestrutura — ver Documentos/Anexos acima | | |

**Consequência prática:** a diferença entre Approval e Auditoria é o ponto mais importante desta secção. Ambas são "transversais", mas por razões diferentes — Approval é transversal *porque é um domínio com regras próprias que serve vários consumidores*; Auditoria é transversal *porque não tem regras de negócio nenhumas, só uma garantia técnica uniforme*. Tratá-las da mesma forma arquitectural seria um erro nos dois sentidos: dar a Approval o tratamento de "biblioteca sem estado" perderia as suas invariantes de domínio (ex.: não auto-aprovação); dar a Auditoria o tratamento de "domínio com regras" criaria acoplamento desnecessário a decidir o que auditar.

### 4.1 O que o protótipo revela sobre o Approval Engine (evidência, sem redesenho aqui)

Como referido, esta análise não aprofunda o desenho do Approval Engine — mas a evidência de ownership recolhida é relevante para o mapa global, por isso fica registada:

| # | Mecanismo | Tabelas | Âmbito |
|---|---|---|---|
| 1 | Motor genérico | `approval_rules`, `approval_rule_levels`, `approval_instances`, `approval_steps` | Despesas, Férias, Compras, Comercial, Adiantamento, Geral |
| 2 | Legado de RH | `approval_steps` ligado a `employee_requests` | Pedidos internos de colaboradores |
| 3 | Payroll | `payroll_approval_steps` | Aprovação de folhas de salário |
| 4 | Pagamentos | `payment_requests`, `payment_request_steps` | Workflow fixo em código (`manager→finance→cfo→treasury`), limiar em `tenants.cfo_approval_threshold` |

Isto confirma, ao nível do mapa global, que **Governança de Decisões é hoje o domínio com o ownership mais fragmentado de todo o sistema** — mais do que Documentos ou Auditoria — e por isso é also o de maior risco se a arquitectura global não lhe der uma fronteira clara desde o início.

---

## 5. Dependências entre fronteiras candidatas

| De → Para | Natureza | Ownership dos dados | Risco |
|---|---|---|---|
| Approval Engine ← consumido por (Despesas, Férias, Procurement, Comercial, Payroll, Pagamentos) | Muitos-para-um; Approval não depende deles, só recebe pedidos | Approval possui as suas próprias entidades (política, pedido, atribuição, decisão); nunca possui a entidade de negócio de origem | Baixo, **se** o contrato de submissão for estreito e estável (risco alto se o motor começar a ler directamente tabelas de outros módulos — "God Module", já identificado nas análises anteriores) |
| Approval Engine → lê Identidade/RH (cargo → pessoa actual) | Leitura estreita, síncrona | Identidade/RH possui | Médio — é o ponto onde o God Module tende a nascer; precisa de contrato explícito e versionado |
| Approval Engine → lê Financeiro/Budget (verificação orçamental, RN-017 do SGAP) | Leitura, provavelmente síncrona no momento da decisão | Financeiro possui | Médio — mesma natureza do ponto anterior; outro caso de "leitura estreita de domínio alheio", a desenhar com o mesmo cuidado |
| Approval (decisão "Aprovado") → Tesouraria (execução "Pago") | **Ponto de consistência forte** | Approval possui a decisão; Tesouraria possui a execução | **Alto** — o SGAP exige revalidação do estado + verificação de saldo no momento da execução (RN-020); se Approval e Tesouraria acabarem em processos/serviços separados, isto implica lidar com concorrência distribuída; se ficarem no mesmo processo, é uma transacção local trivial. **Esta é uma das decisões mais influentes para a escolha MM vs. Microservices (secção 7)** |
| Procurement → Financeiro/AP (purchase_invoice_id) | Handoff explícito por referência | Procurement possui requisição/OC/recepção; Financeiro possui factura/pagável | Baixo — já modelado correctamente com FK; é o exemplo a replicar |
| Comercial → Financeiro/AR (invoice, receivable) | Handoff por referência | Comercial possui cliente/proposta/contrato; Financeiro possui factura/recebível | Baixo |
| RH (Employee) → quase todos os contextos | Leitura de referência (fan-out muito alto) | RH possui | **Alto**, não pela natureza da dependência (é só leitura de referência), mas pelo volume — qualquer mudança ao modelo de Employee tem raio de impacto no sistema inteiro |
| Auditoria ← escrito por todos os contextos | Fan-in, write-only do ponto de vista dos domínios | Auditoria possui o log; nunca possui os dados de negócio que descreve | Baixo, se o contrato for um simples "registar evento" |
| Notificações ← accionado por todos os contextos | Fan-in | Notificações possui a entrega; a decisão de "quando" fica na origem | **Achado concreto de risco:** o protótipo tem um *trigger* que insere até 20 notificações **dentro da mesma transacção** que muda o estado de um pedido de pagamento — é o tipo de acoplamento síncrono que aumenta o raio de impacto de uma falha, independentemente da arquitectura escolhida |
| Documentos ← usado por RH, Financeiro, Comercial, Legal | Fan-in | Documentos possui storage+metadata; classificação fica na origem | Baixo, se unificado |
| Portais (Colaborador/Cliente) → tudo | Só leitura/composição, nunca ownership | Nenhum — são camadas de apresentação (secção 1.4) | Baixo, desde que não lhes seja dado ownership de dados próprios |
| Identidade & Acesso ← dependência de TODOS | Fundacional | Identidade possui | Se falhar, falha tudo — maior requisito de disponibilidade do sistema inteiro, independentemente de qualquer outra decisão |

### 5.1 Potenciais transações distribuídas identificadas

1. **Aprovação → Execução do pagamento** (Approval→Tesouraria): exige estado consistente no momento da execução (revalidação + saldo). É o ponto mais sensível do sistema todo.
2. **Aprovação → Verificação/actualização orçamental** (Approval↔Budget): a verificação antes da decisão é uma leitura (pode ser síncrona sem custo); a actualização de "execução vs. planeado" depois da decisão pode tolerar consistência eventual (é reporting, não controlo).
3. **Aprovação de folha de pagamento → Geração de recibos → Pagamento a colaboradores**: mesma família de risco que (1), hoje implementada de forma paralela e não convergida com o motor genérico.

Nenhuma destas foi ainda decidida como "transacção ACID local" vs "saga/outbox" — depende directamente da decisão Modular Monolith vs. Microservices (secção 7), pelo que fica registada aqui como decisão em aberto, não resolvida.

---

## 6. Requisitos arquitecturais consolidados

| Categoria | Requisito | Fonte | Tipo |
|---|---|---|---|
| Multi-tenancy | Toda a plataforma é multi-tenant, isolamento por `tenant_id` + RLS | Doc. Rivo + confirmado no schema (965 ocorrências de `tenant_id`, funções `is_tenant_member`/`has_tenant_role`) | **Facto** |
| Multi-tenancy do SGAP | O documento SGAP não menciona multi-tenancy nenhuma vez — é escrito como se fosse para uma única organização | Leitura directa do SGAP | **Facto** (ausência) |
| Multi-tenancy — consequência | Todas as capacidades absorvidas do SGAP (alçadas, limiares, departamentos) têm de ser modeladas por tenant desde o início | Combinação dos dois factos acima; já parcialmente confirmado no protótipo (`tenants.cfo_approval_threshold`) | **Inferência**, já parcialmente validada |
| Volumetria | O SGAP indica ~500 processos/dia e ~10.000 pagamentos/mês — **isto descreve a carga esperada de UM tenant**, não da plataforma inteira | SGAP + nota de correcção desta análise | **Facto** (SGAP) + **Inferência** (não é a carga agregada do Rivo multi-tenant) |
| Disponibilidade | SGAP pede 99,5% em horário alargado (07h-20h, dias úteis) | SGAP RNF-004 | **Facto**, mas só para o processo de pagamentos |
| Disponibilidade — Rivo global | Não há um número de disponibilidade definido para a plataforma inteira; é razoável assumir pelo menos o mesmo piso, possivelmente 24/7 dado o Portal do Cliente ser externo e não estar limitado a horário de expediente interno | — | **Hipótese a confirmar** |
| Performance | ≤2s para 95% dos pedidos, 100 utilizadores concorrentes (SGAP) | SGAP RNF-003 | **Facto**, âmbito Pagamentos; extensão a todo o Rivo é **inferência** |
| Segurança | MFA obrigatório para aprovadores/Finanças; RBAC; TLS 1.2+; AES-256 em repouso | SGAP RNF-001/002 | **Facto** (SGAP); presença efectiva no Rivo — **hipótese**, não confirmada no código inspeccionado |
| Auditoria | Append-only, retenção mínima 10 anos, IP obrigatório | SGAP RNF-007 + RN-012 | **Facto**; `audit_logs` actual não tem coluna de IP — **gap confirmado** |
| Consistência | Bloqueio técnico de pagamento fora do estado Aprovado; revalidação de estado + saldo na execução; controlo de concorrência optimista | SGAP RN-001, RN-020, RNF-011 | **Facto** — implica consistência forte no ponto Approval→Tesouraria (ver secção 5.1) |
| Escalabilidade | Arquitectura preparada para ≥5.000 processos/ano e novos departamentos sem redesenho | SGAP RNF-005 | **Facto**, mas é um número modesto — não sugere, por si só, necessidade de escalar componentes de forma independente |
| Isolamento de falhas | Não explicitado directamente no SGAP nem no doc. Rivo como requisito numérico, mas decorre da natureza multi-módulo da plataforma: uma falha em Pagamentos não deve indisponibilizar Projectos, Frota, Portal do Colaborador | Inferência a partir da estrutura de domínio (secção 1) | **Inferência** |
| Recuperação | RPO ≤24h, RTO ≤8h, testes de restauro semestrais | SGAP RNF-008 | **Facto** (âmbito Pagamentos); extensão a toda a plataforma — **hipótese** |
| Manutenibilidade | Não quantificada; decorre indirectamente do objectivo declarado de "absorver progressivamente outros sistemas" sem se tornar um "monólito desorganizado" | Prompt do cliente, secção 27 | **Facto** (intenção declarada), **inferência** (implica fronteiras internas fortes independentemente da topologia de deployment escolhida) |

---

## 7. Modular Monolith vs. Microservices vs. híbrido

Esta comparação usa os requisitos e fronteiras apurados acima — não critérios genéricos. É apresentada como **recomendação preliminar informada**, sujeita às decisões em aberto da secção 8, e não fecha a discussão do Approval Engine, que fica para depois.

### 7.1 O que os requisitos e o domínio dizem

- **Não há evidência de necessidade de escalar módulos de forma independente.** A volumetria do SGAP (500/dia, 10k/mês) é modesta; o requisito de escalabilidade do Rivo (≥5.000 processos/ano) também é modesto. Nenhum documento indica, por exemplo, que Frota ou Inventário precisem de 100× a capacidade de Financeiro.
- **Há um ponto de consistência forte real** (Approval→Tesouraria, secção 5.1) que é trivial dentro de uma transacção local e passa a exigir saga/outbox/eventual consistency se for distribuído — sem nenhum requisito que obrigue à distribuição.
- **Employee é um "shared kernel" com fan-out muito alto** (secção 1.5, secção 3). Fronteiras de serviço construídas cedo à volta de RH/Identidade tenderiam a gerar chamadas de rede constantes a partir de quase todos os outros contextos — exactamente o tipo de acoplamento que o cliente pediu para evitar.
- **Não há evidência de múltiplas equipas independentes** a exigir deployabilidade e ciclos de release separados (o driver organizacional clássico para microservices). Isto é uma **hipótese a confirmar** — não sabemos ainda a dimensão da equipa de engenharia prevista.
- **RLS multi-tenant e RBAC/segregação de funções são muito mais simples de garantir de forma consistente a partir de um único ponto de aplicação** (uma base de dados, uma camada de autorização) do que replicados/sincronizados entre serviços.
- **O problema real do protótipo nunca foi "o monólito não escala".** Foi a ausência de fronteiras internas e ownership — 5 implementações de Approval, 2 de Auditoria, RBAC inconsistente entre documento e código. Isto é um problema de **modularidade e governança**, resolúvel dentro de um monólito bem desenhado; não é, por si, argumento para distribuição.

### 7.2 Comparação nos eixos pedidos

| Eixo | Modular Monolith (bem estruturado) | Microservices |
|---|---|---|
| Deployment | Um deployável; simples, mas qualquer alteração exige novo deploy do todo | Deploy independente por serviço; útil só se houver necessidade real de o fazer |
| Transacções/consistência | Transacção local trivial no ponto Approval→Tesouraria | Exige saga/outbox só para satisfazer um requisito que hoje é local |
| Comunicação entre módulos | Chamada em processo, via interface interna estável (contract-first) | Chamada de rede, exige idempotência, retries, versionamento de API |
| Isolamento de falhas | Alcançável por disciplina de dependências (nenhum módulo independente depende do caminho crítico do outro) — não é automático, mas é possível sem distribuição física | Isolamento físico "de fábrica", mas ao custo de toda a complexidade operacional acima |
| Escalabilidade | Escala o processo inteiro; suficiente para a volumetria conhecida | Permite escalar componentes isoladamente — sem requisito conhecido que o justifique hoje |
| Ownership de dados | Depende de disciplina interna (schemas/tabelas por módulo, sem joins cruzados fora de contrato) | Forçado pela separação física de bases de dados |
| Custo operacional | Baixo — uma base de dados, um runtime | Alto — service discovery, mensageria, observabilidade distribuída, múltiplos pipelines |
| Extracção futura | Possível, **se** desenhado com fronteiras internas fortes desde o início (schema por módulo, sem FKs cruzadas fora de contrato, comunicação só por interface) | N/A — já está distribuído |
| Complexidade de equipa | Baixa para equipas pequenas/médias | Só compensa com equipas grandes e independentes por serviço |

### 7.3 Recomendação preliminar

**Modular Monolith bem estruturado, com fronteiras internas fortes desenhado para permitir extracção futura**, e não Microservices nem Modular Monolith "solto". Razões, ancoradas nos achados acima:

1. Nenhum requisito de volumetria, disponibilidade ou performance encontrado exige isolamento físico.
2. O ponto de maior exigência de consistência do sistema inteiro (Approval→Tesouraria) é **mais simples e mais seguro** dentro de uma transacção local.
3. O "shared kernel" Employee/RH tem fan-out demasiado alto para ser cedo colocado atrás de uma fronteira de rede sem introduzir latência e falhas parciais em quase todos os fluxos do sistema.
4. O problema histórico do protótipo (5 implementações de Approval, 2 de Auditoria) foi falta de modularidade disciplinada, não falta de distribuição — microservices não resolveria isto; agravaria, porque cada serviço tenderia a reinventar o que não estivesse claramente centralizado.
5. Não há, nos documentos analisados, evidência de múltiplas equipas independentes que justifiquem deployabilidade separada.

**O que isto implica no desenho, desde o dia um** (para que a extracção futura de um módulo — nomeadamente Approval, se algum dia se justificar — não exija reescrever o sistema):
- Ownership de dados por módulo estritamente respeitado — sem tabelas partilhadas fora dos casos já identificados como correctos (Supplier, Client), e mesmo esses acedidos por contrato, não por join livre entre módulos.
- Comunicação entre módulos através de interfaces internas estáveis (o padrão já esboçado em `approval-integration.ts` vai nesta direcção, mas precisa de generalizar-se aos outros contextos, não só ao Approval).
- Nenhuma dependência de runtime de um módulo independente (Projectos, Frota, Portal) sobre o caminho crítico de Governança de Decisões ou Financeiro.
- Notificações e efeitos secundários não-críticos fora da transacção que regista a decisão de negócio (corrige o achado da secção 5 sobre o *trigger* de 20 notificações síncronas).

**Esta recomendação é preliminar** — depende directamente das respostas às perguntas da secção 8, nomeadamente dimensão da equipa e trajectória de crescimento multi-tenant, que podem alterar o balanço.

---

## 8. Onde fica o Approval Engine (nota de enquadramento, não aprofundada)

Conforme pedido, este documento não desenha o Approval Engine em detalhe. Fica registado apenas o seu lugar no mapa:

- É um **supporting domain** (Governança de Decisões) — ver R1 —, não uma feature de nenhum módulo.
- É implementado como **capacidade transversal/partilhada**, consumida por vários bounded contexts através de um contrato estreito (pedido de aprovação → decisão), nunca acedendo directamente aos dados desses contextos.
- Tem duas dependências de leitura legítimas e estreitas a desenhar com cuidado: Identidade/RH (cargo → pessoa) e Financeiro/Budget (verificação orçamental) — ambas identificadas na secção 5 como o ponto onde o risco de "God Module" nasceria se não forem contratualizadas.
- Tem um ponto de consistência forte com Tesouraria (Approval→Pago) que pesa directamente na recomendação da secção 7.

O desenho detalhado (modelo de dados definitivo, semântica de SLA, invariantes obrigatórias, etc.) fica para a fase seguinte, depois de validado este mapa global.

---

## 9. Decisões em aberto — preciso da sua confirmação

| # | Decisão | Depende de |
|---|---|---|
| D1 | Identidade & Acesso: confirma a classificação split (Autenticação=infra, Autorização/RBAC=domínio partilhado)? | Alinhamento com a visão de segurança que quer para o Rivo |
| D2 | "Cargo" organizacional (ex.: "Director Financeiro", "Chefe de Departamento") — é um conceito novo a modelar, distinto dos actuais 7 perfis de acesso? | Necessário antes de fechar Identidade & Acesso e, mais tarde, o Approval Engine |
| D3 | Budget (Financeiro, anual/mensal por cost center) vs. "custos departamentais mensais" (SGAP, ligado ao carregamento de caixa) — é o mesmo conceito com um propósito adicional (forecasting de tesouraria), ou dois conceitos distintos que devem coexistir? | Desenho de Financeiro/Tesouraria |
| D4 | Department (RH) vs. Cost Center (Financeiro) — a divergência entre gestor de departamento e responsável de cost center é intencional (nem todo cost center mapeia 1:1 um departamento) ou é uma inconsistência do protótipo a não herdar? | Modelo organizacional do Rivo |
| D5 | Documento/Anexo como capacidade transversal única — concorda em tratá-la como infraestrutura de storage+metadata desde o início, com classificação a ficar em cada contexto de origem? | Prioriza-se cedo ou fica para depois? |
| D6 | Volumetria e trajectória multi-tenant esperada (quantos tenants, ritmo de crescimento, equipa de engenharia prevista) — necessário para validar (ou refutar) a recomendação Modular Monolith da secção 7 | Informação de negócio que só o cliente tem |
| D7 | `legal_contracts` (protótipo) — é um "documento legal" que acompanha contratos de trabalho e comerciais, ou um quarto conceito de contrato a fundir com os outros dois? | Desenho de Comercial/RH/Legal |

---

## 10. Resoluções arquitecturais (R1–R5)

Estas resoluções fecham objecções levantadas em revisão de arquitectura sobre a v1 deste documento e sobre `rivo-dados-integracoes-seguranca-v1.md`. São vinculativas e prevalecem sobre qualquer texto anterior que as contradiga.

### R1 — Governança de Decisões é *supporting*, não *core*

**Objecção:** a classificação como core domain assentava num argumento de posicionamento comercial ("é a proposta de valor face ao SGAP"), não numa análise de domínio.

**Resolução:** reclassificado como **supporting domain**.

Fundamento: um motor de aprovações é um problema bem compreendido, com soluções disponíveis no mercado. O que diferencia o Rivo não é o motor, é a **integração da governança de decisões em todos os contextos de negócio** — aprovação ligada nativamente a Procurement, Payroll, Tesouraria, Comercial, com acesso aos dados que tornam possíveis regras como anti-fraccionamento e verificação orçamental. Essa integração é que é difícil de replicar; o motor não é.

**Consequências:**

- **Construir internamente, não adquirir.** Um motor externo não consegue impor invariantes que dependem de dados de outros contextos do Rivo (anti-fraccionamento agrega por fornecedor+rubrica; verificação orçamental lê Financeiro). Adquirir fragmentaria o modelo de domínio.
- **Disciplina de âmbito.** Sem BPMN, sem designer visual de workflows, sem grafos de workflow arbitrários. O workflow é definido pelo Rivo; só as regras (alçadas, cargos, departamentos, faixas de valor) são configuráveis. Funcionalidade genérica de workflow engine é complexidade sem requisito.
- O nível de investimento e de sofisticação vai para a **integração e para as invariantes**, não para a generalidade do motor.

**Rever quando:** surgir requisito de workflows definidos pelo cliente em runtime, ou de processos de aprovação fora do âmbito do Rivo.

### R2 — Segregação de funções: a invariante vive no domínio; RLS é defesa em profundidade

**Objecção:** contradição interna entre `rivo-dados-integracoes-seguranca-v1.md` §3.2 ("regra de código no domínio `approval`, não configuração") e §3.6 ("RLS por atribuição de aprovação"). Não fica definido onde vive a invariante.

**Resolução:** hierarquia explícita, sem ambiguidade.

1. **O domínio `approval` é a fonte de verdade** de toda a regra de segregação de funções. É lá que a invariante é expressa, testada e imposta.
2. **RLS é uma segunda linha de defesa**, não a regra. Existe para que um erro na camada de aplicação não permita escrever uma decisão que não pertence ao autor.
3. **Regra vinculativa:** nenhuma regra de negócio pode existir *apenas* em RLS. Toda a política RLS tem de ser o reflexo de uma invariante já expressa e testada no domínio. Se uma regra só existe em SQL, é um defeito de arquitectura.

Fundamento: invariantes como "quem submete não decide" ou alçadas por cargo precisam de contexto de domínio que uma política SQL exprime mal e que não é testável ao nível do domínio. O requisito do SGAP ("bloqueio técnico, não apenas na interface") fica satisfeito pela imposição no servidor — RLS acrescenta profundidade, não substitui.

### R3 — Ligação de documentos por tabela de ligação, não por FK polimórfica

**Objecção:** `documents` com `entidade_tipo`+`entidade_id` genérico perde integridade referencial, precisamente num domínio com requisitos fortes de retenção legal (10 anos de auditoria, prazos fiscais angolanos).

**Resolução:** desenho híbrido.

- **`documents` continua a ser capacidade transversal única** (D5 mantém-se): possui o ficheiro, os metadados, o ponteiro de storage, o hash e o versionamento.
- **A ligação a um registo de negócio passa a viver no contexto de origem**, numa tabela de ligação própria com FKs reais: para o registo do próprio contexto e para `documents.documento(id)`.
- Exemplo: `hr.colaborador_documento(colaborador_id → hr.colaborador, documento_id → documents.documento, categoria)`.

**Consequências:**

- Integridade referencial restaurada nos dois sentidos.
- A direcção de dependência mantém-se correcta: o contexto consumidor depende de `documents`; `documents` não depende de ninguém.
- A **classificação e a retenção legal ficam no contexto de origem**, que é o único que sabe o prazo aplicável a *aquele* tipo de documento — `documents` não pode saber isso genericamente.
- Custo: uma tabela de ligação pequena por contexto consumidor. Aceite.

**Excepção deliberada — `audit` mantém a referência polimórfica** (`entidade_tipo`+`entidade_id`, sem FK). Justificação: o log é append-only e tem de sobreviver à eliminação lógica do registo que descreve, incluindo registar acções sobre entidades que já não existem. Uma FK real impediria exactamente o que a auditoria precisa de garantir. Trade-off aceite explicitamente.

### R4 — Contrato publicado por `hr` para a referência a Colaborador

**Objecção:** o documento identifica Employee como o *de facto* shared kernel com maior fan-out do sistema, e usa isso como argumento contra microservices — mas não propõe como impedir que se torne uma god-entity dentro do monólito.

**Resolução:** `hr` publica um contrato de leitura estreito. Nenhum outro contexto lê tabelas de `hr`.

**`ReferenciaColaborador`** (read model publicado por `hr`):

| Campo | Nota |
|---|---|
| `colaborador_id` | identificador estável |
| `nome_exibicao` | — |
| `estado` | activo / inactivo |
| `departamento_id` | — |
| `cargo_actual` | id + nome, resolvido à data pedida (usa Atribuição de Cargo, que é histórica) |
| `utilizador_id` | opcional — nem todo colaborador tem login |

**Regras vinculativas:**

- Os contextos consumidores guardam **apenas `colaborador_id`**. Nunca copiam nome, departamento ou cargo para as suas próprias tabelas — essa cópia fica obsoleta silenciosamente.
- Excepção única e deliberada: o **snapshot de submissão** em `approval` (Pedido de Aprovação e Atribuição congelam requisitante, departamento e aprovador resolvido). Aí a cópia é intencional — o processo não pode mudar porque a organização mudou a meio.
- Se um consumidor precisa de mais do que os campos acima, isso é sinal de que (a) o caso de uso pertence a `hr`, ou (b) o contrato precisa de extensão explícita e versionada. Nunca de leitura directa às tabelas de `hr`.

### R5 — Chaves estrangeiras entre schemas

Decorre de R4 e clarifica um ponto que ficava implícito.

**Resolução:** é permitida FK entre schemas **exclusivamente para a chave primária do contexto dono** (ex.: `fleet.viatura.motorista_id → hr.colaborador(id)`), com o único propósito de garantir integridade referencial.

**Proibido:**

- FK para colunas que não sejam a chave primária do dono.
- `JOIN` para outras tabelas do contexto dono. Ler um atributo de Colaborador faz-se pelo contrato R4, não por SQL.
- FK que atravesse o schema no sentido inverso ao da dependência declarada.

Numa eventual extracção futura de um contexto, estas FKs degradam-se para identificadores simples — é uma alteração localizada, não uma remodelação.

---