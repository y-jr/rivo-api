# ADR-048: Custo de manutenção — campo em `MaintenanceRecord`, não uma quarta categoria de Despesa

## Status

Aceite (2026-09-04). Decisão do utilizador, resolvendo a lacuna de domínio
registada em `pending-decisions.md` ao construir Analytics & IA (ADR-047).

## Context

`docs/rivo-suite-descricao-modulos.md` §7 nomeia "Despesas de frota
(combustível, portagens, estacionamento)" como um item, e "Plano de
manutenção com alertas de revisão" como outro, distinto — sem ligar os
dois. `FleetExpenseCategory` reflecte isso deliberadamente: "exactamente as
três categorias que o documento nomeia, nenhuma outra" (`modules/fleet.md`,
decisão já tomada e documentada). `MaintenanceRecord`, por outro lado,
nunca teve campo de valor nenhum — nasceu só como registo do que aconteceu
(tipo, descrição, datas de abertura/fecho), não do que custou.

O utilizador pediu "custos de manutenção" como métrica de Frota ao definir
o âmbito de Analytics & IA (ADR-047). Construir a métrica exigia primeiro
decidir onde o custo vive — dois caminhos possíveis, e mutuamente
exclusivos na leitura do domínio actual:

1. Campo novo em `MaintenanceRecord`.
2. Categoria nova (`Maintenance`) em `FleetExpenseCategory` — que reabriria
   a decisão "exactamente três categorias" já registada.

Perguntado directamente ao utilizador.

## Decision

### Custo é um atributo do registo de Manutenção, não uma Despesa de Frota

`MaintenanceRecord` ganha `Cost` (`decimal?`, opcional). `Despesa de Frota`
mantém-se exactamente como estava — três categorias, sem `Maintenance` — a
decisão de 2026-08-31 não foi reaberta.

**Porquê esta e não a outra:** o registo de Manutenção já existe
especificamente para capturar "o que aconteceu" a uma intervenção — tipo,
descrição, quando começou, quando acabou. O custo é mais um atributo dessa
mesma intervenção, não um facto solto que precise do mecanismo de Despesa
(que serve para lançamentos avulsos sem abertura/fecho — combustível,
portagem, estacionamento, nenhum dos quais tem "início" e "fim").
Duplicar a intervenção como Despesa também obrigaria a manter os dois
sincronizados, ou a aceitar que um trabalho de manutenção apareça duas
vezes na frota — nenhuma das duas é melhor do que um campo no sítio onde o
resto da intervenção já vive.

### Opcional, preenchido só ao fechar

`Cost` só se define em `CloseMaintenance` (`Vehicle.CloseMaintenance` →
`MaintenanceRecord.Close`), nunca à abertura — é quando se sabe o valor
final da intervenção, não uma estimativa que depois teria de ser corrigida.
Continua opcional mesmo ao fechar: nem toda a manutenção tem custo a
registar (trabalho interno, garantia), e nulo significa "não registado",
não "grátis" — a soma agregada (`GetPeriodMaintenanceCostAsync`) ignora as
que não têm custo em vez de as contar como zero.

**Sem coluna de moeda**, mesma simplificação de `FleetExpense.Amount` e de
`NetSalary` em `payroll` — é sempre AOA.

**Migração sem risco de backfill.** Ao contrário do incidente evitado no
ADR-046 (coluna não-nula com `defaultValue` errado), esta coluna nasce
nula: todo o histórico anterior a 2026-09-04 fica correctamente `NULL`
("custo não registado", que é exactamente o que essas linhas são), sem
precisar de nenhum valor de substituição.

### Publicado em `IFleetActivityOverview`

`GetPeriodMaintenanceCostAsync(from, to)` — soma de `Cost` das manutenções
**fechadas** no período (filtra por `EndedOn`, não `StartedOn` — é quando o
custo passa a existir). Consumido por `Rivo.Analytics`
(`AnalyticsOverviewView.FleetPeriodMaintenanceCost`), fechando a lacuna que
`IFleetActivityOverview` documentava desde o ADR-047.

## Consequences

### O que fica mais fácil

- Analytics & IA mostra a métrica de custo de manutenção que o utilizador
  pediu, sem inventar dado nenhum — só o que ficar registado a partir de
  agora (e, retroactivamente, nada do histórico, que é honesto: não havia
  custo capturado antes de hoje).
- `Despesa de Frota` continua exactamente como o documento de produto a
  descreve — nenhuma decisão anterior foi silenciosamente alargada.

### O que fica em aberto, e é assumido

- **Histórico sem custo.** Toda a manutenção fechada antes de 2026-09-04
  tem `Cost = null` — não há como reconstruir o que não foi capturado.
- **Sem obrigatoriedade.** Uma manutenção pode fechar sem custo para
  sempre — não há validação que force o preenchimento, por desenho.

## Related

ADR-047 (Analytics & IA — onde a lacuna foi descoberta e onde a métrica é
consumida), ADR-046 (a mesma lição de migração — coluna nova nunca com
`defaultValue` que inventa um facto, aqui evitado à partida por a coluna
nascer nula), `modules/fleet.md` §Regras de negócio (a decisão de 2026-08-31
sobre as três categorias, mantida sem alteração).
