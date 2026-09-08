# ADR-060: A cadeia de integridade vive na série

## Status

Aceite (2026-09-08). Fecha o K7 para a factura de venda. Não fecha o K7 para
guias de remessa nem notas de crédito, que ainda não a têm.

## Context

O K7 pedia uma cadeia `Hash`/`HashControl` sobre os documentos emitidos. O
ADR-036 adiou-a, com a justificação de que era «acrescentável depois **sem
reescrever a emissão**, porque a numeração, a ordem e a imutabilidade já lá
estão» — e registou um sinal de alerta: «emissão concorrente sobre a mesma
série».

Esse sinal é o problema inteiro. Uma cadeia precisa de um ponto onde a ordem se
decide. Duas emissões simultâneas que leiam o mesmo «último elo» produzem duas
facturas que apontam ambas para o mesmo antecessor, e a cadeia deixa de ser uma
cadeia — passa a ser uma árvore, em que qualquer ramo pode ser removido sem se
notar.

## Decision

**O último elo vive em `DocumentSeries`**, ao lado do contador de numeração.

```
DocumentSeries.LastDocumentHash  →  o Hash do último documento da série
DocumentSeries.Chain(hash)       →  fecha o elo
SalesInvoice.Hash                →  o elo deste documento
SalesInvoice.PreviousHash        →  o anterior, nulo no primeiro
SalesInvoice.HashMatches()       →  recalcula e compara
```

### Porque na série e não numa tabela de cadeia

**Porque a série já é o ponto de serialização.** É o `Version` dela que impede
duas emissões de receberem o mesmo número — concorrência optimista, ADR-025.
Pendurar a cadeia no mesmo objecto faz o encadeamento herdar essa garantia em
vez de precisar de outra, e uma emissão que perca a corrida não avança nem o
número nem o elo.

Uma tabela à parte precisaria da sua própria serialização, e teria de a
coordenar com a da série. Duas fechaduras para a mesma porta.

### Uma cadeia por série

Duas séries são duas sequências independentes. Encadeá-las juntas faria a ordem
de emissão *entre* séries passar a importar, e não importa — é o que separar
séries significa.

### O que entra no hash

Número, data do documento, instante de registo, total ilíquido e o elo
anterior. Mudar qualquer um numa factura gravada dá outro hash, e a partir daí
a cadeia da série não fecha.

**As linhas entram através do total** e não uma a uma: `GrossTotal` é a soma
delas. Enfiar cada linha tornaria o hash dependente da ordem de iteração, que é
decisão de persistência e não do documento.

## Consequences

### ⚠ Isto não é a assinatura da AGT

`HashControl` continua a ir a `"0"`, que é o que o XSD manda usar «caso o
documento seja gerado por um programa não validado» — e é verdade, o Rivo não
está certificado (ADR-036).

O que a cadeia dá é **detecção de adulteração de documentos já emitidos**, que
é o que o K7 pedia e é independente de certificação. Confundir as duas coisas
seria o erro grave aqui: uma factura do Rivo continua a não ser documento
fiscal válido em Angola.

### As facturas anteriores ficam sem elo, e é deliberado

`Hash` e `PreviousHash` são anuláveis e ficam nulos nas facturas emitidas antes
de 2026-09-08.

**Calcular-lhes um hash agora validaria exactamente o que a cadeia existe para
detectar.** Um elo produzido à posteriori não prova que ninguém lhes tocou
entretanto — prova apenas que os dados de hoje são consistentes consigo
próprios. Nulo é a verdade: «este documento é anterior à cadeia», e é visível.

`HashMatches()` devolve `false` para elas. **Isso não quer dizer adulterada**,
e quem chama tem de distinguir pelo `Hash` nulo.

### Dois campos novos que não se reconstroem

`SystemEntryDate` e `IssuedByUserId` — `SystemEntryDate` e `SourceID` no SAF-T.
São obrigatórios no XSD e nenhum se deduz depois: meses mais tarde ninguém sabe
quem emitiu uma factura nem quando ela entrou no sistema.

Na migração, `SystemEntryDate` das facturas anteriores fica na **data do
documento às 00:00:00**. Não é invenção: é o que o XSD prescreve, «na
exportação de dados relativos a exercícios anteriores em que esta informação
seja desconhecida». `IssuedByUserId` fica nulo — a trilha de auditoria tem o
acto, mas ligá-los aqui seria inferência gravada como facto.

### `Issue` ganhou três parâmetros obrigatórios

Sem omissões, de propósito: o código de produção não os pode esquecer. Os
testes que não são sobre a cadeia passam por um auxiliar
(`FacturaDeTeste.Emitir`) que os preenche.

### Por fazer

- **Nota de crédito e guia de remessa** não têm cadeia. O SAF-T exige `Hash` em
  `MovementOfGoods` também.
- **Nada verifica a cadeia sozinho.** `GET /finance/sales-invoices/chain`
  existe (ver a adenda), mas é preciso alguém pedi-lo. Falta a rotina agendada
  que o corra e avise sem lho pedirem.

## Adenda (2026-09-08) — a travessia

`VerifyInvoiceChain` percorre cada série pela ordem de sequência e reporta três
falhas distintas.

**Só a primeira se apanha documento a documento.** `HashMatches()` diz se
*aquele* documento foi alterado. Um documento **removido** não se apanha assim:
cada um dos que ficam continua consistente consigo próprio, e só a ligação
entre eles denuncia a falta. É por isso que isto é uma travessia.

| Falha | Como se detecta |
|---|---|
| `DocumentAltered` | O conteúdo já não produz o `Hash` gravado |
| `SequenceBroken` | O `PreviousHash` não é o `Hash` do documento anterior |
| `SeriesTailMismatch` | O fim da cadeia não é o que a série guarda |

A terceira existe porque as outras duas não chegam: **remover as últimas
facturas de uma série deixa as que sobram perfeitamente encadeadas entre si.**
É o elo guardado em `DocumentSeries.LastDocumentHash` que sabe que havia mais.

### Uma quebra nomeia um documento, não uma série inteira

Alterar o total de uma factura dá **uma** quebra, não uma cascata: o `Hash`
gravado dela não mudou, só deixou de corresponder ao conteúdo, por isso a
seguinte continua a apontar para o elo certo. Quem lê o relatório sabe onde ir.
Alterar o `Hash` gravado, esse sim, dá as duas.

### As facturas anteriores à cadeia contam-se, não se acusam

`BeforeChain` no relatório. Dizer que 147 documentos legítimos estão
adulterados é como uma verificação morre — o relatório passa a ser ignorado.

### `200` mesmo com quebras

A verificação correu e respondeu; o resultado é conteúdo, não estado do pedido.
Um `409` faria um cliente HTTP tratar «encontrei adulteração» como «o pedido
falhou», e é o contrário.
