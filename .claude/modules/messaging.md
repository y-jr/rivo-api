# messaging — Conversas e Tickets

**Classificação:** supporting domain.

⚠ **A classificação é inferência, não decisão registada.** O ADR-045 fixou
`messaging` como bounded context novo mas nunca lhe atribuiu classificação
estratégica. Fica em supporting por ter invariantes próprias ligadas à
relação comercial, ao contrário de `notifications`, que é entrega pura. Se a
distinção vier a importar, é decisão de ADR.

## Responsabilidade

Conversas entre o cliente e a equipa comercial, e tickets de suporte. As
duas coisas são o mesmo agregado, distinguidas por `Conversation.Kind`.

**Não possui o Cliente** (é de `commercial`) nem o Colaborador (é de `hr`).
Referencia os dois por identificador, e lê-lhes os atributos pelos contratos
publicados (ADR-010, BR-18).

## Porquê módulo novo e não `notifications`

`notifications` é fire-and-forget de um só sentido — `INotifier.QueueAsync`,
sem thread, sem resposta, sem estado de leitura. Forçá-lo a servir uma
conversa a duas vias mudaria o que ele significa para todos os outros
consumidores (`identity`, `approval`, `hr`, …). O contexto novo mantém a
fronteira limpa e continua a **usar** `notifications` para o único aviso que
faz sentido aqui: "tens mensagem nova", enviado ao vendedor responsável.

## Conceitos

| Conceito | Notas |
|---|---|
| Conversa (`Conversation`) | Raiz de agregado. Pertence a um Cliente, tem `Kind` (`Message`/`Ticket`), estado (`Open`/`Closed`) e, só em ticket, `Subject` |
| Mensagem (`Message`) | Filha da Conversa. Nasce com autor e corpo; **nunca se altera nem se elimina** |

## Possui

Conversa e Mensagem. Nada mais.

## Depende de

`commercial` (`ICustomerDirectory` — resolve o Cliente e o vendedor
responsável), `hr` (`IEmployeeDirectory` — resolve a conta do vendedor para
o notificar), `notifications` (`INotifier` — o aviso), `audit`.

**Nenhuma dependência a `approval`.** Sem SLA e sem alçada, é fila simples.

## Consumido por

`Rivo.CustomerPortal`, pelo contrato `ICustomerMessaging` — é o único
consumidor, e é ele que resolve "o próprio cliente" antes de chamar.

## Contratos publicados

**`ICustomerMessaging`** — enviar mensagem, abrir ticket, responder a
ticket, listar as próprias conversas e os próprios tickets.

O `customerId` é sempre resolvido pela composição a partir do token e
**nunca vem do pedido do cliente**. O contrato recebe-o já resolvido, mais
um `Guid actorId` cru — nunca um `AuditContext`, porque um assembly de
contratos não depende de nada (ADR-017); é `messaging` que constrói o seu.

## Não pode

- Possuir o Cliente ou o Colaborador. Referencia por identificador e lê pelo
  contrato (BR-18).
- Aceitar `customerId` vindo do pedido do cliente. Quem resolve "o próprio"
  é a camada de composição.

## Regras de negócio

- **Mensagem directa: uma conversa aberta por cliente de cada vez.** O
  cliente não escolhe conversa — escreve, e cai na que já tiver aberta, ou
  abre uma nova se não houver nenhuma. Imposto por índice único filtrado
  (`ux_conversation_open_message_per_customer`), não só pela camada
  Application.
- **Ticket: vários abertos ao mesmo tempo por cliente.** Cada um rastreia um
  assunto diferente, e "abertura e acompanhamento" pressupõe ver vários em
  curso. É a única diferença estrutural face à mensagem directa, e é por
  isso que o índice único acima é filtrado por `kind = 'Message'`.
- **Assunto obrigatório em ticket, ausente em mensagem directa.** Texto
  livre, no máximo 200 caracteres. **Sem taxonomia de categorias** — inventar
  uma seria o mesmo erro que o ADR-036 recusou para o plano de contas: um
  catálogo plausível que ninguém reveria. O assunto escrito pelo cliente é a
  categorização, feita por quem sabe o que precisa.
- **`AddMessage` e `Close` são os mesmos métodos para os dois `Kind`.**
  Abrir, responder e fechar são a mesma máquina de estados.
- **Uma mensagem nunca se altera nem se elimina** (BR-9, BR-14) — mesma
  disciplina de `StockMovement` em `inventory`. Por isso `Message` está na
  lista de isenções documentadas do contador de concorrência
  (`ConcurrencyTokenTests`): é imutável desde que escrita.
- **Responder a um ticket exige dizer a qual.** É o único fluxo de "o
  próprio" que aceita um identificador vindo do cliente — porque há vários
  tickets possíveis. `messaging` verifica que o ticket é daquele cliente e
  que é mesmo um `Ticket` antes de aceitar, e **devolve 404 tanto para "não
  existe" como para "não é teu"**, sem revelar qual (mesma disciplina do
  `PaymentClaim`, ADR-044).
- **Fechar é de quem tem `messaging.conversations.write`**, não do cliente.
  A próxima mensagem do cliente abre outra conversa.

## O vendedor responsável controla o aviso, não o acesso

`commercial.Customer.AssignedToEmployeeId` decide **para quem vai a
notificação** de mensagem ou ticket novo. Não é controlo de acesso:
qualquer utilizador com `messaging.conversations.read`/`.write` continua a
ver e a responder a qualquer conversa — a fila é partilhada.

**Sem vendedor atribuído, ninguém é avisado** e a conversa fica só na fila.
É comportamento aceite, não defeito.

## Perguntas em aberto

- **Sem notificação ao cliente quando a equipa responde.** O cliente vê ao
  abrir o portal. Ligar `notifications` a esse lado é trabalho separado —
  mesma nota já registada no ADR-043 e no ADR-044.
- **Sem SLA e sem escalonamento.** Adiado explicitamente pelo ADR-046, mesmo
  estado do `approval` e do `PaymentClaim`.
- **Sem prioridade e sem estado intermédio.** Um ticket não se marca urgente,
  e só tem `Open`/`Closed` — "em curso" seria um terceiro estado, extensão
  futura e não recorte do ADR-046.
- **Sem reatribuição de ticket a um vendedor específico.** A notificação
  segue sempre `AssignedToEmployeeId`.

## Estado

**Nascido a 2026-09-04 (ADR-045), com tickets no mesmo dia (ADR-046).**
`Conversation`/`Message` como agregado; `Kind` e `Subject` acrescentados
horas depois pelo ADR-046, em vez de um módulo `support` à parte — um ticket
é a mesma coisa que uma mensagem, com assunto e sem a invariante de conversa
única.

**Um quase-incidente apanhado antes de produção.** A migração gerada pelo EF
pôs `kind` a `defaultValue: ""` para as linhas já existentes — e `messaging`
já estava em produção desde o ADR-045, o que teria deixado toda a conversa
antiga com um valor que a conversão string→enum não reconhece. Corrigido
para `"Message"` (o que essas linhas sempre foram) e **verificado ao vivo**:
base recuada, linha antiga inserida à mão, migração reaplicada, valor
confirmado por SQL.

11 testes de domínio (`Rivo.Messaging.Domain.Tests`) e 23 de Application
(`Rivo.Messaging.Application.Tests`). Verificação end-to-end pelo lado do
Portal do Cliente (`verify-customer-portal.ps1`, 26 casos) — `messaging` não
tem suite própria porque o envio não tem endpoint próprio: passa sempre pelo
Portal, que resolve o cliente primeiro.

Permissões (`messaging.conversations.read`/`.write`) atribuídas a `Sales` e
`Admin`. **`Admin` ficou sem elas na primeira versão** — falha apanhada por
`verify-bootstrap` (67 permissões em vez das 69 esperadas) e corrigida no
mesmo dia.
