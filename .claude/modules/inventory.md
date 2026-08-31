# inventory — Inventário & Armazém

**Classificação:** supporting domain.

## Responsabilidade

Activos físicos, stocks e operações de armazém: itens, armazéns,
movimentos, contagens e valorização.

## Conceitos

| Conceito | Notas |
|---|---|
| Item / Activo | registo e categorização |
| Armazém / Localização | múltiplos |
| Movimento de Stock | recepção, saída, transferência, ajuste |
| Transferência | entre armazéns, com rastreabilidade |
| Contagem de Inventário | inventariação periódica |
| Plano de Manutenção de Activo | preventiva e correctiva |

## Possui

Item, Armazém, Movimento, Transferência, Contagem, Plano de Manutenção de
activo, valorização de stock.

## Depende de

`procurement` (recepção de mercadoria), `finance` (valorização, centro de
custo), `documents` (etiquetas, comprovativos), `audit`, `notifications`.

## Consumido por

`procurement` (entrada de stock), `commercial` (disponibilidade),
`finance` (valorização para contabilidade), `fleet` (peças e consumíveis,
se aplicável).

## Contratos publicados

- Disponibilidade de item.
- **Valorização de stock por período** — implementada a 2026-08-31
  (`GET /inventory/valuation?from=&to=`), mas ainda **não** como contrato de
  leitura entre módulos (`IStockValuationProvider`-like): `finance` continua
  sem a consumir por C#, só por HTTP se algum dia precisar. Publicar como
  contrato fica para quando `finance` tiver mesmo um consumidor real —
  mesma disciplina de não inventar direcção antes de haver quem a peça.

## Eventos

- `StockRecebido`
- `StockSaiu`
- `StockAjustado`

## Não pode

- Determinar preço de venda — isso é `commercial`.
- Criar documentos de compra ou de venda — reage a factos de recepção e
  saída, não origina a transacção comercial.
- Postar directamente no razão — publica valorização para `finance`.

## Regras de negócio

- Movimentos de stock são auditados (BR-9).
- Movimento pertence ao agregado Item — nasce, e nunca se altera nem se
  elimina depois: é a soma dos movimentos que define `QuantityOnHand`, nunca
  o inverso.
- **2026-08-31 — todo o movimento exige Armazém.** `Armazém` é um agregado
  raiz próprio (código único, nome, estado Active/Inactive) — não é filho de
  Item nem de nenhum movimento; um movimento guarda só o `WarehouseId`.
  `QuantityOnHand` mantém-se como o total agregado do item, por conveniência;
  a quantidade por armazém é lida à parte, sempre computada da soma dos
  movimentos desse armazém — nunca escrita directamente, mesma disciplina do
  total.
- Uma Saída ou Ajuste nunca pode levar a quantidade **desse armazém** abaixo
  de zero — recusada, não truncada, e nunca compensada com quantidade que
  exista noutro armazém do mesmo item.
- Um Ajuste exige motivo — uma correcção de contagem sem explicação não se
  aceita.
- Um item inactivo não aceita Recepção, Saída, Ajuste nem Transferência
  novos. Um armazém inactivo também não aceita nenhum dos quatro.
- **Transferência é atómica** (decisão confirmada 2026-08-31): move uma
  quantidade de um armazém de origem para um de destino do mesmo item, num
  só passo — nunca existe um estado intermédio "em trânsito". Gera duas
  pernas ligadas (`TransferOut` na origem, `TransferIn` no destino,
  cada uma apontando para o armazém do outro lado via
  `RelatedWarehouseId`, para rastreabilidade sem precisar de um
  identificador de grupo à parte). O total agregado do item nunca muda com
  uma transferência — só a distribuição por armazém.
- Não há eliminação de Item nem de Armazém (BR-14) — só desactivação.
- **2026-08-31 — Contagem.** `InventoryCount` é um agregado raiz próprio, não
  filho de Item nem de Armazém — cobre muitos itens de um só armazém, o que
  não cabe dentro de um único agregado Item. Âmbito é sempre um armazém: contar
  é um acto físico, num local. Uma linha (`InventoryCountLine`) guarda a
  quantidade esperada **congelada no momento em que nasce** (lida de
  `QuantityOnHandAt`, nunca recalculada no fecho) e a quantidade contada —
  variância é a diferença. O mesmo item não se conta duas vezes na mesma
  sessão.
- Fechar uma contagem sem nenhuma linha não tem o que confirmar — recusado.
  Fechar uma contagem com linhas gera, **na mesma transacção**, um Ajuste por
  cada linha com variância diferente de zero (mesma disciplina de "emitir
  passa a lançar" em `finance`) — tudo ou nada: se um item recusar o ajuste
  (por exemplo, ficou inactivo entretanto), nada fica gravado, nem o fecho da
  contagem.
- Cancelar uma contagem aberta exige motivo, mesma disciplina de um Ajuste
  sem explicação. Uma contagem fechada é facto histórico (BR-14) — nunca se
  cancela nem aceita linha nova depois de fechada.
- **2026-08-31 — custo médio ponderado** (decisão de negócio do utilizador,
  sem fonte fiscal verificada para decidir por conta própria). `AverageCost`
  é por item, nunca por armazém — o mesmo item vale o mesmo, esteja onde
  estiver. **Recalculado só na Recepção**, o único movimento que traz custo
  de compra novo; Saída, Ajuste e Transferência consomem ao custo médio
  corrente, sem o alterar, e ficam com esse custo congelado no próprio
  movimento (`StockMovement.UnitCost`, nunca recalculado depois). Custo
  unitário zero é permitido (amostra, doação) — negativo não.

## Sobreposição conhecida — resolvida (ADR-039)

**Activos Fixos** (`finance`, com depreciação) e **Activos** (`inventory`)
**coexistem**, cada um dono de uma faceta do mesmo bem físico: `inventory` é
dono do activo físico/operacional (localização, responsável, estado,
movimentos); `finance` é dono do activo contabilístico (valor, capitalização,
depreciação, abate). Deve existir uma relação explícita entre os dois,
idealmente 1:1 quando representam o mesmo bem — nem todo item de `inventory`
é Activo Fixo (mercadorias e consumíveis podem existir só aqui). O mecanismo
concreto da ligação (o campo, o sentido) fica por desenhar para quando
`finance` tiver Activos Fixos com código, o que ainda não tem. Fecha o
**K1**. Detalhe em [decisions/adr-039](../decisions/adr-039-inventory-vs-activos-fixos.md).

## Perguntas em aberto

Nenhuma pergunta em aberto neste momento.

## Estado

**Movimento, com regra de negócio real — 2026-08-30.** `InventoryItem`
(SKU único, nome, unidade) nasceu esqueleto a 2026-08-29; ganhou Movimento
(Recepção, Saída, Ajuste) como parte do mesmo agregado, desbloqueado por
ADR-039.

**2026-08-31 — Armazém e Transferência (retrofit).** Decisão confirmada com
o utilizador: (1) o Movimento já existente ganhou `WarehouseId` obrigatório
em vez de conviver com um desenho sem armazém — `QuantityOnHand` passou a
ter uma leitura por armazém (`QuantityOnHandAt`) além do total agregado; (2)
Transferência é **atómica**, sem estado "em trânsito". `Warehouse` nasceu
como agregado raiz próprio (código único, nome, estado). Migração faz
*backfill*: os movimentos já existentes na base local ganharam um armazém
"Principal" gerado pela própria migração — artefacto de dados históricos,
não escolha de negócio. Resolve a pergunta em aberto "Semântica de
transferência entre armazéns".

**2026-08-31 — Contagem.** `InventoryCount` (agregado raiz próprio) +
`InventoryCountLine` (filho, esperado congelado no momento em que a linha
nasce). Abre-se num armazém, acumula uma linha por item contado, e o fecho
gera um Ajuste por linha com variância — numa só transacção com o próprio
fecho, tudo ou nada. Cancelar exige motivo; fechada é facto histórico
(BR-14).

**2026-08-31 — Valorização de stock por custo médio ponderado.** Decisão de
negócio do utilizador ("Custo médio ponderado (Recomendado)"), sem fonte
fiscal verificada para decidir por conta própria. `InventoryItem.AverageCost`
recalculado só na Recepção; Saída/Ajuste/Transferência congelam o custo
corrente no próprio `StockMovement.UnitCost`, sem o alterar.
`GET /inventory/valuation?from=&to=` soma o `Value` (`Quantity × UnitCost`)
dos movimentos no período — deliberadamente não reconstrói quantidade/valor
num ponto no tempo passado, só o que se moveu na janela. *Retrofit* com
backfill honesto a zero para movimentos e itens já existentes (sem custo de
compra capturado antes desta migração).

73 testes de domínio (`Rivo.Inventory.Domain.Tests` — 43 do agregado Item,
9 de `Warehouse`, 21 de `InventoryCount`); `scripts/verify-inventory.ps1` —
**66/66 confirmados contra a stack local a 2026-08-31**, sem nenhuma falha,
segunda corrida (a primeira apanhou um defeito real: `averageCost` em falta
na resposta da API de Transferência, corrigido).

Permissões atribuídas a `AssetManager`, que deixou de estar vazio a
2026-08-29.
