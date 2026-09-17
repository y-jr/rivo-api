# ADR-064: Uma divergência de inventário é uma perda a explicar, não um número a arrumar

## Status

Aceite (2026-09-17). Pedido do utilizador: «resolve o problema da auditoria dos
erros de contagem no inventário» — três problemas distintos, confirmados por ele
depois de os eu ter levantado.

## Context

Fechar uma contagem gerava um Ajuste por cada linha divergente, de imediato, e
com isso corrigia o stock. Três coisas estavam mal, e nenhuma estava registada.

### 1. O motivo do ajuste não explicava nada

A regra do módulo diz que **«um Ajuste exige motivo — uma correcção de contagem
sem explicação não se aceita»**. O fecho passava:

```
"Contagem 01a0b2c3-7f3e-4a11-9c2d-8e5f0a1b2c3d"
```

Cumpre a validação e falha o propósito. Quem vê o movimento — ou a trilha — não
sabe que item era, quanto se esperava, quanto se encontrou, nem se foi falta ou
sobra. Para saber, tinha de ir buscar a contagem pelo identificador.

### 2. Nada passava por decisão

Fechar corrigia o stock fosse a diferença de uma unidade ou de mil. Uma falta
grande de inventário é, em muitos casos, uma perda a investigar — quebra, roubo,
erro de recepção. O sistema tratava-a como um acerto administrativo.

Isto num produto cujo âmbito declara **alçadas e segregação de funções** como
requisitos vinculativos, e onde pagamentos, requisições, folhas salariais e
atribuições de cargo já passam por governança.

### 3. O fecho não dizia o que tinha acontecido

O evento `inventory.count.closed` registava o armazém e quantas linhas
divergiam. Para saber o que divergiu era preciso juntar à mão os eventos de cada
linha com os dos ajustes.

## Requirements

- **Facto** — `approval` resolve a alçada pelo `Amount` da submissão; o limiar é
  configuração, não código.
- **Facto** — `inventory` só depende de `Audit`; não conhece `hr` nem `approval`.
- **Facto** — a quantidade esperada de cada linha fica congelada quando a linha
  nasce, e isso é decisão anterior que este ADR não mexe.
- **Facto** — o custo médio é por item (`AverageCost`), nunca por armazém.
- **Decisão do utilizador** — as três, e agora.

## Alternatives

1. **Um limiar em configuração de `inventory`** (`Inventory:ApprovalThreshold`).
   Rejeitada: duplicaria, em pequeno e pior, o que `approval` já sabe fazer com
   faixas de valor — e criaria um segundo sítio onde a empresa define alçadas.
2. **Aprovar todas as divergências.** Rejeitada: uma diferença de uma unidade num
   parafuso não vale uma decisão de ninguém, e um sistema que pede aprovação para
   tudo ensina as pessoas a aprovar sem ler.
3. **Recusar o fecho quando não há política**, como `payroll` faz. Rejeitada —
   ver a Decisão 3, é a diferença mais importante deste ADR.
4. **Submeter sempre e deixar `approval` decidir se é preciso** (escolhida).

## Decision

### 1. O motivo do ajuste passa a descrever a divergência

```
Contagem de 2026-09-17: esperado 100, contado 60 (falta 40)
```

E a trilha do ajuste passa a levar `expectedQuantity` e `countedQuantity` além da
diferença: quem lê o movimento vê de onde veio o número sem ir buscar a linha.

### 2. A divergência é submetida a decisão, e **o stock não muda até haver uma**

Fechar uma contagem com divergências submete-as a `approval` com o **valor** da
divergência — Σ |variância| × custo médio — e a contagem fica
`PendingApproval`. Responde **202**, não 200: aceite, e nada foi corrigido.

O valor, e não a quantidade, porque é ele que distingue mil parafusos de dez
motores. O limiar vive nas políticas de `approval`, onde a empresa já define as
outras alçadas — **este módulo não tem limiar nenhum escrito**.

Aprovada, `POST /inventory/counts/{id}/decision` aplica os ajustes que o fecho
reteve. Recusada, a contagem fica `Refused` e **não reabre**: reabrir deixaria
alguém acrescentar linhas a uma sessão física já terminada e apresentar ao
aprovador seguinte números diferentes dos que o primeiro recusou. Quem quiser
tentar de novo conta outra vez.

### 3. Não haver política **não** impede o fecho — e é aqui que este processo difere de todos os outros

Em `payroll` e `procurement`, a ausência de política aplicável recusa o pedido:
é configuração em falta. Aqui é o contrário — significa que nenhuma alçada cobre
esta divergência, e a contagem aplica-se como sempre.

A razão é assimétrica e vale escrevê-la: **uma folha por aprovar não paga
ninguém, e esperar não custa nada. Uma contagem por fechar deixa o stock do
sistema a divergir do stock real — que é exactamente a situação que a contagem
existe para terminar.** Bloquear seria escolher o pior dos dois males.

O mesmo desfecho do contrato de `approval` (`NoApplicablePolicy`), lido de duas
maneiras por dois módulos com necessidades diferentes — que é o que um contrato
partilhado deve permitir.

Quando isso acontece, **fica escrito na trilha**: o evento de fecho leva
`"approvalRequired":false` e o valor que teria sido submetido. É a diferença
entre não ter sido preciso e ter sido contornado.

O mesmo vale para um ambiente sem motor de governança ligado. Já
`AmbiguousPolicy` e `NoApproversResolved` **bloqueiam** com 409: aí há governança
configurada e ela não foi cumprida.

### 4. O fecho resume a divergência

```json
{"warehouseId":"…","occurredOn":"2026-09-17","linesCounted":12,
 "linesWithVariance":3,"shortfall":40,"surplus":2,"approvalRequired":true}
```

Faltas, sobras e o número de linhas — o que se lê num relance. O detalhe de cada
linha continua no evento do seu ajuste, ligado por `countId`: pôr tudo aqui daria
um JSON gigante numa contagem de quinhentos artigos, e a trilha é
append-only (BR-14) — o que lá entra não se corrige depois.

### 5. Primeiro pergunta-se se há alçada; só depois quem requer

A ordem das duas perguntas é uma decisão, e custou uma corrida de CI a aprender.
A primeira versão exigia o colaborador ligado **antes** de perguntar se havia
sequer política configurada — e assim uma conta administrativa sem ficha de
pessoal, incluindo o Admin de arranque, deixava de conseguir fechar contagens
num sistema onde ninguém tinha configurado governança nenhuma. A CI apanhou-o
num caso que existia desde Agosto.

Sem alçada activa para `inventory.stock_count`, nem se pergunta quem requer:
devolve-se `NoApplicablePolicy` e a contagem aplica-se.

### 6. Quem submete é o colaborador ligado à conta que fechou

Resolvido no composition root, que já conhece `hr` — `inventory` continua a
depender só de `Audit`. Sem colaborador ligado, a submissão é **bloqueada** e não
contornada: sem requerente não há contra quem verificar a segregação de funções
(BR-2).

## Consequences

- **`ApprovalProcessTypes` ganhou `inventory.stock_count`**, o sexto processo, e
  o único em que não haver política aplicável não é impedimento.
- **Dois estados novos** na contagem: `PendingApproval` e `Refused`. Migração
  `AddStockCountApproval` — quatro colunas nulas e um índice; nada a migrar,
  porque uma contagem anterior nunca esteve pendente.
- **`POST /inventory/counts/{id}/close` mudou de contrato**: passa a poder
  responder 202 com o processo de aprovação, em vez de 200 com os ajustes.
- **19 testes novos** (7 de domínio, 12 de aplicação) e `verify-inventory` de 66
  casos para 75.
- **O ecrã de Contagens precisa de mostrar o estado novo** — uma contagem
  pendente não é nem aberta nem fechada, e dizer «fechada» seria repetir no
  ecrã a mentira que este ADR tirou do backend.

## Risks

- **Uma contagem pode ficar pendente indefinidamente**, e enquanto isso o stock
  do sistema continua errado — que é o mal que a contagem ia corrigir. Não há
  alerta nem prazo; depende de alguém decidir. É o mesmo risco das requisições e
  das folhas, e agrava-se com a falta de observabilidade.
- **A governança é opcional por configuração.** Uma empresa que nunca crie uma
  política para `inventory.stock_count` nunca terá aprovação nenhuma — e isso é
  indistinguível, para quem não lê a trilha, de não haver a funcionalidade. O
  `"approvalRequired":false` no evento existe precisamente para essa leitura.
- **O valor da divergência usa o custo médio actual**, não o da data da contagem.
  Se uma recepção alterar o custo médio entre contar e fechar, a alçada é
  escolhida com um número ligeiramente diferente do que se leria no dia. Aceite:
  a alternativa era congelar o custo em cada linha, o que duplicaria em
  `inventory` a valorização que já vive no item.
- **Uma propriedade calculada no agregado quase criou uma coluna.** `LinesWithVariance`
  foi lida pelo EF como navegação, e a primeira migração trazia uma chave
  estrangeira sombra em `inventory_count_line`. Está ignorada explicitamente no
  mapeamento, com a razão escrita lá — mas a classe de problema fica: qualquer
  colecção calculada num agregado mapeado tem este risco.

## Revisit When

- **Se as contagens passarem a ser frequentes** (inventário cíclico semanal), o
  fluxo de decisão torna-se um custo diário e vale rever se a alçada deve subir
  ou se certos armazéns ficam de fora.
- **Se o ajuste passar a lançar na contabilidade** — uma quebra é um custo, e
  hoje o ajuste não produz lançamento. Aí a decisão passa a ter consequência
  financeira directa e o limiar tem de ser revisto com o contabilista.
- **Se surgir contagem parcial por categoria**, o valor da divergência deixa de
  ser comparável entre contagens, e a alçada escolhida por ele também.

## Related

ADR-050 (quem decide resolve-se do vínculo), ADR-057 (o actor nunca se declara),
BR-2 (quem submete não decide), BR-14 (nada se elimina; a trilha é append-only),
BR-20 (o precedente de reter um efeito até haver decisão), e a decisão de
2026-08-31 que fixou a contagem como agregado próprio com o esperado congelado.
