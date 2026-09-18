# ADR-066: O documento fiscal passa a existir como ficheiro

## Status

Aceite — 2026-09-18. Implementado na mesma data.

Fecha o **K23**.

## Context

Factura, nota de crédito e recibo existiam como **dados**: viam-se no ecrã, no
extracto do cliente e pela API. Não existia ficheiro nenhum. Nada que se
imprimisse, se anexasse a um e-mail, ou se entregasse a quem comprou. Um cliente
que pedisse «mande-me a factura» não tinha resposta possível.

Não foi uma decisão de adiar: a certificação da AGT e o SAF-T dominaram a
conversa sobre facturação (K7, ADR-036) e a **entrega do documento** nunca foi
escrita em lado nenhum — nem em `known-issues`, nem em `pending-decisions`, nem
no documento de produto. Apareceu a 2026-09-16, por pergunta do utilizador.

### O que faltava não era só o compositor

Ao desenhar a composição apareceu uma ausência maior: **o sistema não sabia quem
era.**

O Rivo conhecia em detalhe o cliente de cada documento — `InvoicedParty`,
congelado no momento da emissão, com nome, NIF e endereço — e não tinha em
código nenhum o seu próprio nome, o seu NIF, ou a sua sede. Nem como entidade,
nem como configuração.

Para um documento fiscal isso não é um campo em falta: é a diferença entre um
documento e um rascunho. E o XSD do SAF-T AO exige-o no `Header`
(`CompanyName`, `CompanyID`, `TaxRegistrationNumber`, `CompanyAddress`,
`BusinessName`, `TaxEntity`), secção que `docs/rivo-fiscal-saft-ao-v1.md` §2
atribui a `fiscal`.

## Requirements

1. Factura, nota de crédito e recibo produzem um ficheiro PDF.
2. O documento identifica quem emite, com os campos que o SAF-T exige.
3. **Duas impressões do mesmo documento saem iguais.** Um documento fiscal
   reimpresso não pode diferir do que circulou.
4. A menção fiscal congelada na emissão vai impressa como está — não se
   recalcula.
5. Um documento anulado não pode sair com o aspecto de um documento bom.
6. O documento pode ser enviado ao cliente por correio electrónico.
7. Nada disto acrescenta direcções novas à tabela de dependências entre módulos.

## Alternatives

| Alternativa | Porque não |
|---|---|
| **Compor a cada descarga**, sem guardar | Quebra o requisito 3. Qualquer alteração ao compositor passava a reescrever retroactivamente documentos que já circularam |
| **Guardar o emitente em `finance`** | O `Header` do SAF-T é de `fiscal` pelo mapeamento de `docs`. Duplicá-lo em `finance` criava duas verdades sobre o NIF da empresa |
| **Emitente em configuração** (`appsettings`) | Muda sem auditoria e sem versão. O NIF do emitente decide o que vai impresso em todos os documentos — merece trilha |
| **PDFsharp/MigraDoc (MIT)** | Sem restrição de licença, mas API posicional e muito mais verbosa para o mesmo resultado. Escolha do utilizador, informada pelos termos reais de ambas |
| **Entregar por `notifications`** | Uma notificação dirige-se a um **utilizador** e não leva anexos; isto dirige-se a um **endereço** — o cliente pode não ter conta — e o anexo é o ponto |
| **Aceitar o endereço no corpo do pedido** | Deixava qualquer pessoa com permissão de facturação mandar a factura de um cliente para onde quisesse. Mesma classe de falha que o ADR-057 corrigiu |

## Decision

### 1. `fiscal` ganha a identidade fiscal da empresa

`TaxEntityProfile` — o `Header` do SAF-T, com os nomes dos campos do XSD para
que o mapeamento da exportação seja directo quando chegar.

**Singular, com chave constante.** O ADR-003 fixou empresa única: há uma
identidade fiscal, não um catálogo delas. A chave fixa é o que impede a segunda.

`GET /fiscal/tax-entity` e `PUT /fiscal/tax-entity`. `PUT` e não `POST` porque é
um recurso singular que se declara uma vez e se corrige depois — a mesma leitura
de verbo do ADR-063. Responde `201` na primeira vez e `200` nas seguintes, para
quem configura ver o que fez.

Permissão própria, `fiscal.tax_entity.write`, **só Admin**: quem muda o NIF do
emitente muda o que vai impresso em todos os documentos emitidos a partir desse
momento.

`SoftwareValidationNumber` existe como campo e **existir não é estar
certificado** — a certificação é o K7 e depende de terceiros. Enquanto estiver
vazio, o documento não o menciona: melhor omitir do que imprimir um número falso.

### 2. O papel compõe-se uma vez e congela

`FiscalDocumentFile`, em `finance`, liga o documento ao ficheiro guardado em
`documents`. Na primeira descarga compõe-se, guarda-se e devolve-se; daí em
diante devolve-se o mesmo ficheiro.

**Não é optimização, é o requisito 3.** É o que permite ao cliente e à empresa
compararem o que cada um tem na mão.

A linha guarda o SHA-256 que `documents` calculou. Serve para responder «é este e
não outro» sem ir buscar o ficheiro.

### 3. A anulação é o caso que obriga a mais do que um ficheiro

Uma factura anulada não pode ser entregue com o aspecto de uma boa — a anulação
tem de aparecer impressa, e aparece em destaque no topo. Mas a versão emitida
antes da anulação também não se pode reescrever, porque **foi ela que circulou**.

Logo há no máximo dois ficheiros por documento: antes e depois da anulação.
`ReflectsCancellation` distingue-os e faz parte da chave de procura. Nada se
apaga (BR-14).

### 4. Um ficheiro órfão recompõe-se

A linha em `finance` e o conteúdo em `documents` vivem em sítios diferentes, e o
K12 registra o caso do ficheiro órfão. Quando o conteúdo falta, compõe-se de
novo em vez de responder um erro que ninguém sabe ler.

### 5. Uma projecção para os três documentos

`FiscalDocumentContent` serve factura, nota de crédito e recibo. Os três
partilham quase tudo — número, cliente congelado, moeda, linhas, totais, menção
fiscal — e diferem em pormenores que cabem em campos opcionais. Três projecções
paralelas davam três composições a divergir com o tempo.

O recibo não tem imposto: liquida valores já tributados. O compositor esconde as
colunas de imposto quando não há nenhuma, em vez de imprimir zeros.

### 6. Sem emitente declarado, não há documento

`501 Not Implemented` — a capacidade existe, falta configurar quem emite. Não é
`4xx` porque o cliente não errou, nem `500` porque nada falhou. Mesma leitura do
`501` em `hr` e `procurement`.

**Recusar é mais honesto do que compor um papel sem cabeçalho** que alguém possa
confundir com uma factura.

### 7. A entrega é permissão própria e endereço lido, não declarado

`POST /finance/<documento>/{id}/delivery`, com `finance.documents.deliver`.

Permissão separada da de leitura: descarregar é acto interno, enviar faz sair
correio para uma pessoa de fora com o documento anexado. É a mesma distinção
entre `payments.request` e `payments.execute` — ver não é fazer acontecer.

Fica em `ForBilling`: quem emite a factura é quem a manda. Separá-las obrigava a
interromper outra pessoa para concluir o acto comercial mais corrente que existe.

O destinatário lê-se do Cliente pelo contrato de `commercial` —
`CustomerReference` ganhou `Email` para isso. **Nunca vem do pedido.**

Sem endereço registado, ou em documento a consumidor final, responde `409` com a
razão: o documento está bom, o que falta é a quem enviar.

A trilha registra a tentativa **com o destino, tenha corrido bem ou mal**. Um
envio falhado é informação: diz que alguém tentou e que o cliente não recebeu.

### 8. Duas inversões, zero direcções novas

A tabela de dependências já permitia `Finance → Fiscal, Commercial, Documents`.
As duas capacidades que faltavam invertem-se e ligam-se no composition root, como
`IPaymentApproval`:

- `IFiscalDocumentArchive` — guardar o ficheiro. Não foi acrescentado a
  `Rivo.Documents.Contracts` porque esse assembly não tem dependências por
  decisão do ADR-017, e guardar precisa de `AuditContext`.
- `IFiscalDocumentDelivery` — o servidor de correio, que vive no host.

### 9. QuestPDF, com a licença Community declarada em código

Escolha do utilizador entre QuestPDF e PDFsharp/MigraDoc, feita com os termos
reais de ambas em mão.

A licença Community (versão 3.0, 6 de Julho de 2026) é gratuita para
organizações com **receita anual bruta inferior a 1 000 000 USD**, em base
consolidada, que não sejam do sector público nem cotadas em bolsa — e permite
explicitamente aplicações comerciais e a redistribuição da biblioteca compilada.

`QuestPDF.Settings.License = LicenseType.Community` fica no construtor estático
do compositor, com a nota do limiar: é a linha de código que faz a afirmação
contratual, e quem a ler daqui a dois anos tem de saber disso. **Se a receita
passar o limiar, a licença passa a paga.**

### 10. A fonte é a que o QuestPDF traz

Pedir Calibri **rebentava no contentor**: a imagem de runtime do .NET é Linux e
não traz fontes da Microsoft, e o QuestPDF lança em vez de substituir em
silêncio. O endpoint respondia `200` na máquina de quem o escreveu e `500` em
produção.

Sem `FontFamily`, usa a fonte embutida no pacote. Para um documento fiscal isto
vale mais do que a escolha tipográfica: **o papel sai igual em todos os
ambientes**.

## Consequences

- Factura, nota de crédito e recibo imprimem-se e entregam-se. O K23 fecha.
- A composição e deterministica **depois de uma correccao**, e ha um teste com
  um segundo de espera a guarda-la. A primeira versao nao era: o QuestPDF grava
  `/CreationDate` e `/ModDate` com o instante da composicao, e o teste que devia
  apanhar isso passava localmente por as duas composicoes cairem no mesmo
  segundo. Falhou na CI. As datas dos metadados passaram a vir da data do
  documento — o que tambem e mais correcto: a data de criacao do ficheiro nao tem
  significado num documento fiscal reimpresso.
- `fiscal` passa a saber quem a empresa é — primeiro passo real para a exportação
  SAF-T, que precisa exactamente destes campos no `Header`.
- Os documentos ficam arquivados em `documents` com categoria própria
  (`documento-fiscal`), logo `GET /documents?category=documento-fiscal` lista os
  papéis emitidos — que é a pergunta que uma inspecção faz.
- Um sistema recém-instalado responde `501` ao primeiro pedido de documento até
  alguém declarar a identidade fiscal. É intencional e a mensagem diz o que
  fazer.
- 33 testes novos. `verify-fiscal` de 23 para 28 casos, `verify-finance` de 29
  para 34.

## Risks

- **Um PDF sem certificação continua a ser um documento sem validade fiscal.**
  Isto resolve a entrega, não a conformidade (K7, K2). A menção congelada
  continua a ser a que a lei exigia à data, e nada aqui a valida.
- **A identidade fiscal não é versionada.** Corrigir a sede hoje não reescreve
  documentos antigos — porque os ficheiros deles estão congelados —, mas um
  documento cujo papel ainda não foi composto sairá com o cabeçalho de hoje. Se a
  empresa mudar de NIF ou de nome, o `Header` do SAF-T por período vai precisar
  de versões. Fica assinalado, não resolvido.
- **O envio não tem repetição automática.** Uma falha de correio devolve `503`
  com a mensagem do servidor e fica na trilha; quem clicou volta a tentar. Um
  worker com nova tentativa é desenho diferente e não se justificou ainda.
- **O aspecto do documento não está validado por ninguém do negócio.** Os testes
  provam que sai um PDF, que sai igual duas vezes e que a anulada difere da boa.
  Não provam que a disposição é a que um contabilista angolano espera.

## Revisit When

- A certificação da AGT avançar (K7) — o `SoftwareValidationNumber` passa a ter
  valor e a cadeia de `Hash` entra no documento.
- A exportação SAF-T for feita — o `Header` por período pode exigir versões da
  identidade fiscal.
- O envio passar a precisar de repetição, ou de mais do que um destinatário.
- A receita da empresa aproximar-se de 1 000 000 USD — a licença do QuestPDF
  muda de categoria.

## Related

- Fecha **K23** (`state/known-issues.md`).
- **ADR-003** — empresa única, o que torna a identidade fiscal singular.
- **ADR-010** — `finance` lê `fiscal` e `commercial` por contrato.
- **ADR-017** — assembly de contratos sem dependências, razão da inversão do
  arquivo.
- **ADR-034** — inversão para o módulo não conhecer o motor; mesmo desenho aqui.
- **ADR-036** — a facturação e a ordem de execução.
- **ADR-057** — o actor resolvido, nunca declarado; mesma razão para o
  destinatário.
- **ADR-063** — `PUT` para corrigir.
- **K7, K2** — certificação e regras de cálculo, independentes disto.
- **K12** — ficheiro órfão, tratado por recomposição.
- `docs/rivo-fiscal-saft-ao-v1.md` §2 — o `Header` pertence a `fiscal`.
