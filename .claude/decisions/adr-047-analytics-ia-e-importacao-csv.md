# ADR-047: Analytics & IA — âmbito reduzido, e importação CSV em `Rivo.Settings`

## Status

Aceite (2026-09-04). Último item da ordem fixa da Fase 8. Decisão do
utilizador, em duas rondas de `AskUserQuestion`.

## Context

`docs/rivo-suite-descricao-modulos.md` §10 "Analytics & Inteligência
Artificial" lista cinco funcionalidades:

1. Dashboards analíticos interactivos.
2. Alertas inteligentes baseados em regras configuráveis.
3. Previsões financeiras com modelos de IA.
4. Log de auditoria completo.
5. Importação em massa via CSV.

Cruzando com `pending-decisions.md`/`domain-map.md` antes de propor âmbito:

- **(4) já está feito.** O módulo `audit` (`GET /audit/entries`) entrega
  isto — não é trabalho de Analytics, é reutilização de um módulo que já
  existe.
- **(3) está bloqueada.** `pending-decisions.md` regista "Provider de
  modelos de IA" como decisão em aberto, não escolhida. Construir uma
  integração de IA agora seria inventar a decisão que falta, não
  implementá-la.
- **(5) está duplicada no documento de produto.** A mesma "importação em
  massa via CSV" aparece também em §14 "Configurações & Administração".
  Não é claro qual dos dois módulos a possui — sinalizado ao utilizador
  em vez de resolvido em silêncio.
- **(1) e (2) não têm âmbito concreto.** Nem o documento de produto nem
  `pending-decisions.md` dizem que dashboards ou que regras.

Perguntado directamente ao utilizador, em duas rondas.

## Decision

### 1. Âmbito: só (1) e (5). Sem alertas, sem previsões de IA

Primeira ronda (`multiSelect`): utilizador escolheu **"Dashboards
analíticos mais profundos"** e **"Importação em massa via CSV"** —
excluindo explicitamente alertas inteligentes e previsões de IA. (4) não
entra por já estar feito.

**Alertas de regras configuráveis ficam de fora**, não por bloqueio de
domínio (ao contrário de (3)), mas por decisão explícita de âmbito — não
registado como pendência, porque não foi pedido, é uma exclusão
deliberada.

### 2. Dashboards: tendência mensal, três domínios

Segunda ronda fixou:

- **Tendência mensal** das métricas que já existem (Finance:
  receita/despesa), como série mensal em vez de só o total do período —
  é o que distingue Analytics do Dashboard Executivo (ADR-041), que já
  mostra os cinco números agregados.
- **Frota**: despesas do período e distância percorrida.
- **Inventário**: valor de stock agora e valorização no período.
- **Sem HR** — descartado explicitamente pelo utilizador na mesma ronda.

Implementado como camada de composição `Rivo.Analytics` (`Application` +
`Api` + `Contracts` mínimo, ADR-041): `GetAnalyticsOverview` compõe
`IReceivablesOverview.GetMonthlyNetRevenueAsync`/
`IPayablesOverview.GetMonthlyNetExpensesAsync` (novos, adicionados aos
contratos já publicados de `finance`), mais dois contratos novos —
primeiros publicados por `fleet` e `inventory`:
`IFleetActivityOverview` e `IInventoryValuationOverview`. `GET
/analytics/overview`, atrás de `analytics.overview.read`.

**Permissão à parte, não composta.** Mesmo raciocínio do
`dashboard.overview.read` (ADR-041): `docs/rivo-suite-descricao-modulos.md`
nomeia `Manager` para "Dashboard, Frota, Projectos, Analytics,
Aprovações", mas `Manager` não tem as permissões de leitura subjacentes
de `finance`/`fleet`/`inventory`. Exigir os contratos subjacentes
excluiria a audiência que o documento nomeia — ver
`Rivo.Analytics.Contracts`. Concedida a `Manager` e `Admin`.

**Frota não tem "custos de manutenção".** O utilizador pediu essa
métrica nomeadamente, mas `MaintenanceRecord` não tem campo de valor e
`FleetExpenseCategory` não distingue manutenção das outras despesas.
`IFleetActivityOverview` expõe só o que o domínio suporta hoje (despesas
das três categorias existentes, distância) — o vazio fica documentado no
próprio contrato e registado em `pending-decisions.md`, não inventado
aqui. É decisão de `fleet`, fora do âmbito deste ADR.

### 3. Importação CSV: Clientes, Colaboradores, Fornecedores — vive em `Rivo.Settings`

Segunda ronda, duas perguntas:

- **Entidades**: Clientes, Colaboradores, Fornecedores. **Não** itens de
  inventário — descartado explicitamente.
- **Localização**: `Rivo.Settings` (módulo 14), não um novo módulo
  Analytics — resolvendo a duplicação do documento de produto a favor de
  Configurações & Administração, onde `Rivo.Settings` (ADR-041) já vive
  como camada de composição sobre `identity`/`approval`.

Cada importação escreve através do contrato de escrita já publicado da
entidade — sem permissão nova, reaproveitando a que já existe:

| Entidade | Contrato de escrita | Permissão |
|---|---|---|
| Clientes | `commercial` | `CommercialPermissions.CustomersWrite` (Sales) |
| Colaboradores | `hr` | `HrPermissions.EmployeesWrite` (HumanResources) |
| Fornecedores | `procurement` | `ProcurementPermissions.SuppliersWrite` (Admin) |

**Não implementado neste ADR** — fixa o âmbito e a localização; a
implementação (parser CSV, validação linha-a-linha, relatório de
erros/sucessos) é o próximo passo dentro do mesmo mandato.

## Consequences

### O que fica mais fácil

- A Fase 8 fecha com todos os itens da ordem fixa tratados —
  incluindo os dois que o documento de produto lista sob Analytics sem
  bloqueio (dashboards, CSV) e os dois que ficam correctamente adiados
  (alertas: por âmbito; previsões de IA: por decisão em aberto).
- `fleet` e `inventory` publicam o seu primeiro contrato de leitura
  agregada — precedente directo para qualquer composição futura que
  precise de números desses dois módulos, sem repetir a descoberta de
  "que tabela agregar" feita aqui.

### O que fica em aberto, e é assumido

- **Sem alertas de regras configuráveis.** Exclusão deliberada, não
  pendência.
- **Sem previsões de IA.** Bloqueado por decisão em aberto
  (`pending-decisions.md` — provider de modelos de IA). Não implementável
  sem essa escolha.
- **Custos de manutenção da Frota não aparecem em lado nenhum** — o
  domínio não os regista. Registado em `pending-decisions.md`.
- **Importação CSV ainda não implementada** — só o âmbito e a
  localização estão decididos.

## Related

ADR-041 (padrão de camada de composição — `Rivo.Analytics` segue-o à
letra, mesmo precedente de `Rivo.Dashboard`), ADR-036 (não inventar
taxonomias/integrações sem fonte — aplicado aqui à recusa de inventar o
provider de IA), `pending-decisions.md` §Domínio e negócio e §Integrações
externas.
