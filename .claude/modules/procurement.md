# procurement — Procurement (Procure-to-Pay)

**Classificação:** core domain.

## Responsabilidade

Do pedido interno de compra à recepção de mercadoria e casamento com a
factura (3-way match). **Dono do Fornecedor.**

A cadeia requisição → ordem de compra → recepção → factura é o exemplo de
fronteira bem definida a replicar noutros contextos.

## Conceitos

| Conceito | Notas |
|---|---|
| Fornecedor | nome, NIF, IBAN, contactos, estado — **dono confirmado** |
| Requisição Interna | requisitante (`hr`), departamento, justificação; submete a `approval` |
| Ordem de Compra | gerada **só após** requisição aprovada |
| Recepção de Mercadoria | alimenta o 3-way match |

## Possui

Fornecedor, Requisição Interna, Ordem de Compra, Recepção de Mercadoria.

## Depende de

`hr` (`ReferenciaColaborador` do requisitante), `approval`, `documents`
(cotações, anexos), `inventory` (recepção de bens), `audit`,
`notifications`.

## Consumido por

`finance` (fornecedor, factura de compra → AP), `inventory` (entrada de
stock), `fiscal` (determinação de imposto na compra).

## Contratos publicados

- Registo de Fornecedor (consumido por `finance`).
- Ordem de Compra e Recepção, para o 3-way match em `finance`.

## Eventos

- `RequisicaoAprovada`
- `OrdemCompraEmitida`
- `MercadoriaRecebida`

## Não pode

- Executar pagamentos a fornecedores — isso é `finance`/Tesouraria.
- Gerir níveis ou valorização de stock — isso é `inventory`; `procurement`
  publica o facto da recepção.
- Ter workflow de aprovação próprio — submete a `approval`.
- Codificar regras fiscais — consulta `fiscal`.

## Regras de negócio

- Ordem de Compra só é gerada após decisão "Aprovado" registada em
  `approval`.
- Alçadas e limiares são configuração de `approval`, não de `procurement`.
- O anti-fraccionamento (BR-7) agrega por fornecedor + rubrica — é regra de
  `approval`, mas alimentada por dados originados aqui.

## Lacuna conhecida

O SGAP prevê **submissão de despesa eventual avulsa** com cotações. O Rivo
cobre compras de bens/serviços por requisição, mas não um fluxo leve de
despesa avulsa. `docs` classifica isto como **expansão de `procurement`**,
não módulo novo.

## Perguntas em aberto

- Forma concreta do fluxo de despesa eventual (mais leve que uma requisição
  completa).
- Validação de conformidade documental antes da decisão — `docs` aponta
  para expansão de `fiscal` como serviço de validação.

## Estado

Não iniciado.
