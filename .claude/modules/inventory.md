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
- Valorização de stock por período.

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

- Método de valorização (FIFO / custo médio ponderado / outro) — decisão de
  negócio, não assumir por omissão.
- Semântica de transferência entre armazéns.

## Estado

⚠ **Esqueleto** — 2026-08-29. `InventoryItem` (SKU único, nome, unidade),
CRUD. **Sem movimento nenhum** — `QuantityOnHand` nasce e fica a zero até
`Movimento` existir. Sem Armazém, Transferência, Contagem, valorização de
stock — sem testes, sem verificação end-to-end. Permissões atribuídas a
`AssetManager`.
