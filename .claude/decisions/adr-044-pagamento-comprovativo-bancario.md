# ADR-044: Pagamentos do Portal do Cliente — comprovativo bancário, sem gateway

## Status

Aceite (2026-09-03). Decisão do utilizador, primeira das três capacidades
adiadas do Portal do Cliente (ADR-043, `pending-decisions.md` §Domínio e
negócio).

## Context

`pending-decisions.md` registava "Gateway de pagamento (mercado angolano)"
como pendente, com Multicaixa Express como candidato técnico óbvio
(predominante em Angola). A pergunta directa ao utilizador — que gateway
integrar primeiro — foi respondida com uma correcção ao próprio
enquadramento: **não há gateway de pagamento viável para os montantes em
causa.** Os meios de pagamento electrónico em Angola (Multicaixa Express,
referência) têm tectos baixos, pensados para retalho — não para uma factura
B2B de venda de produtos ou serviços.

O fluxo real, tal como acontece hoje fora do Rivo: o cliente paga por
transferência bancária directa, seguindo os dados que a empresa lhe dá;
depois submete o comprovativo emitido pelo banco; e é `finance` — um
humano, não um sistema — que confirma a entrada e dá seguimento.

Isto também respondeu, por arrasto, às duas perguntas seguintes que
`pending-decisions.md` já antecipava:

- **Quem detém o dinheiro até à reconciliação?** A conta bancária da
  própria empresa, directamente — não há intermediário a deter fundos.
- **Como é que a confirmação chega a `finance` e vira `Receipt`?**
  Manualmente — não há webhook de gateway nenhum para receber, porque não
  há gateway.

Ficou ainda por decidir a que é que o comprovativo se refere: uma factura
específica escolhida pelo cliente, ou um valor livre aplicado ao saldo mais
antigo (FIFO). Resposta: **uma factura específica** — é o que o cliente já
vê no portal (`GET /customer-portal/me`), e evita a ambiguidade de aplicar
automaticamente um valor a facturas que o cliente pode não ter intenção de
cobrir.

## Decision

### Não há gateway. É um pedido de confirmação com comprovativo anexado.

Novo agregado em `finance`: `PaymentClaim` — um pedido do cliente para que
uma quantia, contra uma factura específica, seja reconhecida como paga.
Nasce `Pending`; só `finance` (permissão já existente,
`finance.receipts.write`) o move para `Confirmed` ou `Rejected`.

**Confirmar não é um estado novo do dinheiro — é o gatilho do `Receipt` que
já existe.** `ConfirmPaymentClaim` reutiliza `RegisterReceipt` tal como
está: mesma validação de moeda, de cliente, de "não recebe mais do que está
em aberto". O `PaymentClaim` não duplica essas regras — só guarda o pedido
e o rasto do comprovativo até alguém decidir.

**Rejeitar não apaga nada** (BR-14, como sempre): fica `Rejected` com
motivo, e o cliente pode submeter um novo pedido — o mesmo padrão de nunca
reutilizar um registo para significar coisas diferentes ao longo do tempo.

### O comprovativo é um documento de `documents`, ligado como os outros módulos já ligam os seus

Mesmo padrão de `fleet`/`hr`/`payroll` (`AttachDocumentToVehicle` e
equivalentes): o cliente faz upload directo a `POST /documents`
(`documents.write`), e submete o `documentId` devolvido ao criar o
`PaymentClaim`. `finance` não guarda ficheiros — só a referência, resolvida
por `IDocumentCatalogue` como os outros consumidores.

**Consequência directa:** o perfil `Cliente` (ADR-043), que nasceu vazio,
ganha a sua primeira permissão real — `documents.write`. Não é uma
permissão pensada para clientes especificamente; é a mesma que qualquer
módulo já usa para anexar ficheiros a um registo seu.

### A submissão passa pela camada de composição, como o resto do Portal do Cliente

`CustomerPortal` resolve "o próprio Cliente" a partir de `CurrentUser`
(mesma disciplina do ADR-042/043 — nunca aceita `customerId` no pedido) e
delega a `finance` através de um método novo, de escrita, em
`Rivo.Finance.Contracts` — o mesmo padrão que `IApprovalGateway.SubmitAsync`
já estabeleceu para "um módulo submete algo a outro, que decide sozinho o
que fazer com isso". `finance` continua a validar tudo do seu lado
(factura existe, pertence a este cliente, não excede o em aberto) — a
composição não pré-valida nada que `finance` já tenha de validar de
qualquer forma.

**Alternativa rejeitada:** o cliente submeter directamente a
`POST /finance/...`. Quebraria a disciplina de que o Portal do Cliente é
quem resolve identidade externa → registo interno; `finance` teria de
saber ler `CurrentUser` e reimplementar a resolução que já existe uma vez
em `CustomerPortal`.

## Consequences

### O que fica mais fácil

- A segunda das três capacidades adiadas do Portal do Cliente tem desenho
  fechado; falta só código.
- O perfil `Cliente` deixa de estar vazio — primeiro sinal de que o
  catálogo de permissões cresce por capacidade real, não por antecipação.

### O que fica em aberto, e é assumido

- **Sem SLA de confirmação.** `finance` vê os `PaymentClaim` pendentes
  (`GET /finance/receivables/payment-claims?status=Pending`), mas nada
  força prazo nem escala — mesmo estado do `approval` (SLA em aberto,
  `pending-decisions.md` §Approval Engine).
- **Sem notificação ao cliente quando confirmado/rejeitado.** `notifications`
  cobre o padrão; ligar-lhe este evento é trabalho separado, não
  bloqueante — mesma nota já registada para a ligação de conta (ADR-043).
- **Reconciliação bancária automática continua por resolver** — este ADR
  não a substitui. Fecha a metade "cliente diz que pagou, humano confirma";
  a metade "importar o extracto do banco e emparelhar sozinho" continua em
  `pending-decisions.md` §Fornecedores e integrações, à espera do formato
  que cada banco disponibiliza.

## Related

ADR-043 (Portal do Cliente — identidade externa), ADR-041 (padrão de
camada de composição), ADR-009 (documentos e a ligação por FK real fora de
`documents`), ADR-042 (resolver "o próprio" a partir de `CurrentUser`),
`modules/finance.md`, `pending-decisions.md` §Domínio e negócio e
§Fornecedores e integrações.
