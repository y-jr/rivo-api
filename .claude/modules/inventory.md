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

## Sobreposição conhecida

**Activos Fixos** (`finance`, com depreciação) sobrepõe-se parcialmente a
**Activos** (`inventory`). `docs` §1.2 assinala a sobreposição mas **não a
resolve**. É decisão em aberto — ver
[state/pending-decisions.md](../state/pending-decisions.md).

Enquanto não for resolvida, não assumir nenhum dos lados como dono.

## Perguntas em aberto

- Método de valorização (FIFO / custo médio ponderado / outro) — decisão de
  negócio, não assumir por omissão.
- Fronteira com Activos Fixos de `finance`.
- Semântica de transferência entre armazéns.

## Estado

⚠ **Esqueleto** — 2026-08-29. `InventoryItem` (SKU único, nome, unidade),
CRUD. **Sem movimento nenhum** — `QuantityOnHand` nasce e fica a zero até
`Movimento` existir. Sem Armazém, Transferência, Contagem, valorização de
stock — sem testes, sem verificação end-to-end. Permissões atribuídas a
`AssetManager`.
