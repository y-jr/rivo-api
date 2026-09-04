# Mapa de Domínios

_Estado verificado contra o código a 2026-09-04._

Destilado de `docs/rivo-arquitetura-global-v1.md` §1. Em caso de dúvida,
esse documento prevalece.

A classificação segue DDD estratégico (core / supporting / generic) e **não**
a lista de 14 módulos funcionais do documento de produto — vários desses
módulos não são bounded contexts (ver §"Não são módulos" abaixo).

## Core domains

Razão de ser competitiva do Rivo.

| Domínio | Bounded contexts | Módulo(s) |
|---|---|---|
| **Financeiro** | Contas a Pagar, Tesouraria, Contas a Receber, Contabilidade & Fecho, Planeamento | [finance](../modules/finance.md) |
| **Procurement** | Requisições & Compras (Procure-to-Pay) | [procurement](../modules/procurement.md) |
| **Comercial / CRM** | Pipeline & Propostas, Contratos Comerciais, Cobranças | [commercial](../modules/commercial.md) |
| **Recursos Humanos** | Ciclo de Vida do Colaborador, Payroll & Compensação | [hr](../modules/hr.md), [payroll](../modules/payroll.md) |

## Supporting domains

Necessários e específicos do negócio, mas não diferenciadores.

| Domínio | Módulo | Nota |
|---|---|---|
| **Governança de Decisões** | [approval](../modules/approval.md) | Reclassificado de core para supporting (R1). O diferenciador é a integração da governança em todos os contextos, não o motor |
| **Fiscal & Compliance** | [fiscal](../modules/fiscal.md) | Motor fiscal angolano — regras próprias suficientes para ser contexto à parte, não feature de Financeiro |
| **Gestão de Projectos** | [projects](../modules/projects.md) | — |
| **Gestão de Frota** | [fleet](../modules/fleet.md) | Auto-contido, baixo acoplamento |
| **Inventário & Armazém** | [inventory](../modules/inventory.md) | ~~Sobreposição parcial com Activos Fixos do Financeiro — a resolver~~ **Resolvida por ADR-039** (2026-08-30): `inventory` dono do activo físico/operacional, `finance` do contabilístico |
| **Conversas e Tickets** | `messaging` | Nasceu a 2026-09-04 (ADR-045/ADR-046). ⚠ **Classificação por inferência** — o ADR-045 fixou-o como bounded context novo, sem lhe atribuir classificação estratégica. Fica em supporting por ter invariantes próprias ligadas à relação comercial (uma conversa aberta por cliente, assunto obrigatório em ticket), ao contrário de `notifications`, que é entrega pura. Se a distinção vier a importar, é decisão de ADR, não desta tabela. **Sem `modules/messaging.md`** — o único módulo com código e sem ficheiro de módulo |

## Generic domains

Commodity — indispensáveis, não diferenciam.

| Domínio | Módulo | Classificação |
|---|---|---|
| **Identidade & Acesso** | [identity](../modules/identity.md) | Split: Autenticação = infraestrutura; Autorização/RBAC = domínio partilhado (ADR-004) |
| **Auditoria** | [audit](../modules/audit.md) | Infraestrutura — sem regras de negócio, só garantia técnica uniforme |
| **Notificações** | [notifications](../modules/notifications.md) | Entrega = infraestrutura; decisão de "quando notificar" fica na origem |
| **Documentos/Anexos** | [documents](../modules/documents.md) | Infraestrutura de storage+metadata; classificação fica na origem |
| **Background Jobs** | — (infraestrutura) | Sem módulo próprio |

## Não são módulos

Ponto explícito de `docs` §1.4: **módulo funcional ≠ bounded context.** Os
seguintes são camadas de leitura/composição ou canais de apresentação. Não
possuem entidades próprias, não têm base de dados própria, e **não devem
gerar módulos**.

**As cinco existem em código desde 2026-09-04**, todas em `src/Composition/`,
todas segundo o padrão do ADR-041 (`Application` + `Api`, sem Domain nem
Infrastructure, sem base de dados própria).

| Item do documento de produto | O que é realmente | Estado |
|---|---|---|
| Dashboard Executivo | Read model agregado sobre Financeiro, Comercial e Analytics | **Implementado 2026-08-31** — `Rivo.Dashboard`, ADR-041 |
| Portal do Colaborador | Canal self-service que compõe RH, Financeiro, Approval, Documentos, Notificações | **Iniciado 2026-08-31** — `Rivo.EmployeePortal`, ADR-042. "Próprio" resolvido; recibos/férias/assiduidade/documentos ainda por fazer |
| Portal do Cliente | Canal self-service que compõe Comercial, Financeiro/AR, Documentos, Mensagens | **Completo 2026-09-04** — `Rivo.CustomerPortal`. Identidade externa (ADR-043), resumo financeiro, facturas, extracto, comprovativo de pagamento (ADR-044), mensagens directas (ADR-045) e tickets de suporte (ADR-046) |
| Configurações & Administração | UI de gestão de Identity & Access e de políticas de aprovação | **Implementado 2026-08-31** — `Rivo.Settings`, ADR-041. Ganhou a importação em massa via CSV a 2026-09-04 (ADR-047) |
| Analytics & IA | Misto: exportações/importações = infraestrutura; previsões/insights = read model | **Âmbito reduzido, implementado 2026-09-04** — `Rivo.Analytics`, ADR-047: tendência mensal de `finance`, actividade de `fleet`, valorização de `inventory`. **Sem alertas** (exclusão deliberada) e **sem previsões de IA** (bloqueadas — provider por decidir) |

Tratá-los como domínios geraria duplicação de dados por audiência —
exactamente o padrão que o protótipo já exibia (`notifications` vs
`client_notifications`).

**Consequência:** implementam-se como camadas de composição/API que consomem
os contextos reais. Nunca com ownership de dados próprio.

## Relações entre contextos

```
Identidade & Acesso  ── usado por TODOS
Auditoria            ── escrito por TODOS
Notificações         ── accionado por TODOS
Documentos/Anexos    ── usado por RH, Financeiro, Comercial

Governança de Decisões (transversal)
 ├── consumido por: Financeiro (Pagamentos), Procurement (Requisições/OCs),
 │                  RH (Férias), Payroll (Folhas), Comercial (Descontos)
 └── lê (não possui): hr (Cargo → pessoa actual), finance (disponível orçamental)

Procurement ──(factura de compra)──> Financeiro/AP ──> Tesouraria
Comercial   ──(cliente, factura)───> Financeiro/AR
hr          ──(ReferenciaColaborador)──> quase todos os contextos
Projects    ──> finance (centro de custo), hr (recursos), commercial (facturação)
Fiscal      ──lê──> Financeiro, Payroll
Messaging   ──lê──> commercial (vendedor responsável), hr (conta do vendedor)
            ──escreve──> notifications (aviso de mensagem/ticket novo)

Camadas de composição (src/Composition/, ADR-041) — só consomem contratos:
 Settings       ──> identity, approval, commercial, hr, procurement
 EmployeePortal ──> hr
 Dashboard      ──> finance
 CustomerPortal ──> commercial, finance, messaging
 Analytics      ──> finance, fleet, inventory
```

**Padrão mais importante:** `hr.Colaborador` tem o maior fan-out do sistema.
É referenciado por Approval, Procurement, Financeiro, Projectos, Frota e
Comercial. Por isso o acesso é feito **exclusivamente pelo contrato
`ReferenciaColaborador`** (ADR-010), nunca por leitura directa.

## Ownership de conceitos-chave

| Conceito | Dono | Nota |
|---|---|---|
| Fornecedor | procurement | Consumido por finance |
| Cliente | commercial | Consumido por finance (AR) e Portal do Cliente |
| Colaborador | hr | Maior fan-out — ver ADR-010 |
| Departamento | hr | Distinto de Centro de Custo (ADR-005) |
| Cargo | hr | Distinto de Perfil de Acesso (ADR-005) |
| Perfil de Acesso | identity | Distinto de Cargo (ADR-005) |
| Centro de Custo | finance | Mapeamento a Departamento é opcional (ADR-005) |
| Orçamento | finance | Distinto de Previsão de Custos Departamentais (ADR-006) |
| Previsão de Custos Departamentais | finance | Input mensal ao carregamento de caixa (ADR-006) |
| Factura de Venda | finance (AR) | Cliente pertence a commercial |
| Factura de Compra | finance (AP) | Fornecedor pertence a procurement |
| Pedido/Execução de Pagamento | finance (Tesouraria) | Sem passos de aprovação embutidos — a decisão vive em approval |
| Aprovação (política, pedido, decisão) | approval | Nunca possui a entidade de negócio de origem |
| Documento | documents | Ligação e retenção ficam no contexto de origem (ADR-009) |
| Contrato de Trabalho | hr | ADR-009 |
| Contrato Comercial | commercial | ADR-009 |
| Conversa e Ticket | messaging | Distinguidos por `Conversation.Kind`, não por agregado separado (ADR-046) |
| Vendedor responsável por cliente | commercial | `Customer.AssignedToEmployeeId`. **Controla só para quem vai o aviso**, nunca o acesso — qualquer utilizador com permissão em conversas vê e responde a qualquer uma (ADR-045) |
| Custo de manutenção de viatura | fleet | Campo de `MaintenanceRecord`, não uma quarta categoria de Despesa de Frota (ADR-048) |
