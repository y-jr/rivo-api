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

**Fornecedor e Requisição Interna feitos** — 2026-08-27. Ordem de Compra,
Recepção de Mercadoria e 3-way match por fazer.

O que existe:

| Peça | Estado |
|---|---|
| Fornecedor | Feito. Nome, NIF único, IBAN verificado por ISO 13616, contactos, activação/desactivação. Publicado por `ISupplierDirectory` |
| Requisição Interna | Feita. Rascunho com linhas, submissão a `approval`, aplicação da decisão, cancelamento |
| Ordem de Compra | ⚠ Por fazer. É o passo seguinte, e só nasce de requisição aprovada |
| Recepção de Mercadoria | ⚠ Por fazer |
| 3-way match | ⚠ Por fazer. Precisa das duas acima e da factura de compra, que é de `finance` |

### Decisões tomadas ao construir

- **As linhas da requisição são inferência.** `docs` lista para a Requisição
  apenas id, requisitante, departamento, justificação e estado. Sem saber o que
  se pede e por quanto, não há como gerar a Ordem de Compra nem como dar a
  `approval` o valor que selecciona a faixa da alçada. São "atributos
  principais", não lista fechada.
- **A submissão a `approval` é por inversão**, `IProcurementApprovalSubmission`,
  ligada no composition root — o mesmo desenho de `hr` e de `finance`. Aqui não
  havia ciclo a quebrar; mantém-se para o módulo não saber qual é o motor.
- **O IBAN é verificado, o NIF não.** O mod-97 do IBAN é norma ISO 13616,
  internacional e publicada — não é regra fiscal angolana, e por isso não cai na
  proibição do `CLAUDE.md`. E o custo do erro é assimétrico: um NIF errado dá
  uma factura por corrigir, um IBAN errado paga a outra pessoa.
- **`finance` ainda não consome o Fornecedor.** A factura de compra continua a
  guardar nome e NIF em texto. Ligá-la é trabalho seguinte, e **não é
  retroactivo**: as facturas já emitidas guardam o que vigorava à data.

### Ainda por fazer, além dos agregados

- **Suite de verificação end-to-end.** As onze suites PowerShell não cobrem
  `procurement`; a cobertura é de domínio (58 testes) e nada mais.
- **Cobertura de Application.** Os casos de uso não têm teste unitário — mesma
  lacuna dos outros sete módulos.
- Despesa eventual avulsa (lacuna do SGAP, acima).
