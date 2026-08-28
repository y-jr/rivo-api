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

**Os quatro agregados feitos, e o 3-way match fecha** — 2026-08-28.

O que existe:

| Peça | Estado |
|---|---|
| Fornecedor | Feito. Nome, NIF único, IBAN verificado por ISO 13616, contactos, activação/desactivação. Publicado por `ISupplierDirectory` |
| Requisição Interna | Feita. Rascunho com linhas, submissão a `approval`, aplicação da decisão, cancelamento |
| Ordem de Compra | Feita. Só nasce de requisição aprovada, ao preço acordado, e o total encomendado não passa o aprovado. Publicada por `IPurchaseOrderDirectory` |
| Recepção de Mercadoria | Feita. Parcial, acumulada por linha da ordem, e nunca acima do encomendado. Anulável por engano de registo |
| 3-way match | Feito, do lado de `finance`. A factura liga-se à Ordem por `PurchaseOrderId` (opcional, tem de ser do mesmo fornecedor); `GET /finance/purchase-invoices/{id}/match` lê `IPurchaseOrderDirectory` para pôr encomendado, recebido e facturado lado a lado. **Não bloqueia divergência** — informa, não impede |

O terceiro lado do match — a factura — não podia viver aqui: é `finance` que
a possui. `IPurchaseOrderDirectory` segue o mesmo desenho de
`ISupplierDirectory`: a Ordem de Compra é publicada por identificador,
`finance` lê-a, e a comparação em si — o match — corre do lado de quem tem os
três números, não do lado de quem só tem dois.

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
- **O total encomendado não passa o aprovado, e não há tolerância.** Um preço
  acordado acima do estimado acontece, e um limiar de desvio aceitável — 5%?
  10%? — é decisão de negócio que não está em fonte nenhuma. Inventá-lo seria
  abrir a alçada por um número escolhido aqui. Enquanto não houver quem o
  decida, o caminho é uma requisição nova, que volta a passar por decisão.
  **É o ponto de configuração a preencher** quando alguém decidir.
- **A recepção não gere stock, e é fronteira explícita.**
  `modules/procurement.md` proíbe-o: níveis e valorização são de `inventory`, e
  `procurement` publica o facto. Enquanto `inventory` não existir, o facto fica
  registado e ninguém o consome — melhor do que este módulo começar a contar
  existências e nunca mais largar o assunto.
- **Anular uma recepção é corrigir um engano de registo**, não devolver
  mercadoria ao fornecedor. A devolução é outro facto — sai material que
  entrou, e do lado do dinheiro dá nota de crédito. Não existe, e não se finge
  que este cancelamento a cobre.
- **Receber acima do encomendado é recusado, sem tolerância.** Mesma razão da
  ordem: um limiar de excesso aceitável é decisão de negócio sem fonte, e
  aceitar em silêncio faria a empresa dever mais do que encomendou. **É o
  segundo ponto de configuração a preencher.**
- **A guia de remessa é inferência.** Não está em `docs`; fica opcional e em
  texto livre, porque é documento do fornecedor e o Rivo não lhe impõe formato.
- **A Ordem de Compra não tem número próprio.** Uma ordem que sai para o
  fornecedor precisa de uma referência que ele possa citar de volta, e escolher
  o formato — prefixo, reinício anual, se admite saltos — é decisão de negócio
  sem fonte. Fica o identificador, e o número quando houver quem o decida.

### Verificação

**58 testes de domínio** e **`verify-procurement`, 30 casos** (2026-08-27), que
faz de `procurement` o décimo segundo membro do `verify-all` — em 221/221.

O caso que justifica a suite é o das linhas relidas da base: o mapeamento de
uma colecção por campo de apoio é onde o EF Core falha em silêncio, e nenhum
teste de domínio o vê.

### Ainda por fazer, além dos agregados

- **Cobertura de Application.** Os casos de uso não têm teste unitário — mesma
  lacuna dos outros sete módulos.
- Despesa eventual avulsa (lacuna do SGAP, acima).
