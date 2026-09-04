# ADR-046: Tickets de suporte — reaproveitando o motor de mensagens

## Status

Aceite (2026-09-04). Decisão do utilizador, terceira e última das capacidades
adiadas do Portal do Cliente (ADR-043) — pagamentos (ADR-044) e mensagens
(ADR-045) fecharam antes.

## Context

`docs/rivo-suite-descricao-modulos.md` §12 diz só "abertura e acompanhamento
de tickets de suporte" — sem categorias, sem SLA, sem dizer quem resolve.
Mesmo enquadramento das duas capacidades anteriores: perguntado directamente
ao utilizador, com as recomendações registadas em `pending-decisions.md`
como ponto de partida.

## Decision

### 1. Sem categorias fixas — assunto livre

Não há taxonomia de categorias no repositório, e inventar uma seria o
mesmo erro que ADR-036 já recusou para o plano de contas: um catálogo
plausível seria pior do que nenhum, porque ninguém o reveria. O ticket
tem um **assunto em texto livre** (`Conversation.Subject`), escrito pelo
cliente ao abrir — é a categorização, feita por quem sabe o que precisa.

### 2. SLA adiado

Mesmo estado do `approval` (SLA nunca implementado, gap registado) e do
`PaymentClaim` (ADR-044, "sem SLA de confirmação"). O ticket aparece na
fila de Sales sem prazo nem escalonamento automático.

### 3. Sales resolve — mesma audiência das mensagens directas

Não há evidência de uma equipa de suporte distinta da comercial. `Sales`
já tem `messaging.conversations.read`/`.write` desde o ADR-045; nenhuma
permissão nova.

### 4. Reaproveita `messaging` — não é módulo novo

Um ticket é a mesma coisa que uma mensagem directa, só que com assunto e
sem a invariante de conversa única: `Conversation` ganha `Kind`
(`Message` | `Ticket`) e `Subject` (`string?`, obrigatório só em
`Ticket`).

**O que muda entre os dois:**

- **Mensagem directa (`Kind = Message`):** sem assunto, uma conversa
  aberta por cliente de cada vez — é o que ADR-045 já fixou, sem alteração.
- **Ticket (`Kind = Ticket`):** com assunto, **várias abertas ao mesmo
  tempo por cliente** — cada ticket rastreia um assunto diferente, e a
  UX esperada de "abertura e acompanhamento" é ver vários em curso, não
  um só. O índice único filtrado de `Conversation` (uma aberta por
  cliente) passa a aplicar-se só a `Kind = Message`.

**O que não muda:** `AddMessage`/`Close` são exactamente os mesmos
métodos, para os dois `Kind` — abrir, responder e fechar continuam a ser
a mesma máquina de estados. A fila de Sales
(`GET /messaging/conversations`) ganha um filtro por `Kind`, mas é a
mesma fila.

**Alternativa rejeitada:** módulo novo (`support`), com o `Conversation`/
`Message` duplicados. Só se justificaria se tickets precisassem de algo
que a forma actual estruturalmente não permite — por exemplo, alçada de
`approval`, que não foi pedida aqui e que, se vier a ser pedida, o
próprio `pending-decisions.md` já previa seguir "o motor já feito" (ou
seja, entraria como extensão de `messaging`, não como razão para a ter
duplicado agora).

### O que muda na API

- `POST /customer-portal/me/tickets` — abre, com assunto e primeira
  mensagem.
- `POST /customer-portal/me/tickets/{id}/messages` — o cliente responde a
  **um** ticket seu — ao contrário de mensagens directas, que nunca
  aceitam identificador porque só há uma conversa possível, aqui o
  cliente escolhe a qual dos vários tickets está a responder. `messaging`
  verifica que o ticket é do cliente e que é mesmo um `Ticket` antes de
  aceitar — a mesma resposta (404) serve "não existe" e "não é teu", sem
  revelar qual.
- `GET /customer-portal/me/tickets` — lista, separada de
  `GET /customer-portal/me/messages` (o mesmo endpoint de mensagens
  directas, agora filtrado por `Kind` por baixo).
- `GET /messaging/conversations?kind=Ticket` — a fila de Sales, com o
  filtro novo.

## Consequences

### O que fica mais fácil

- As três capacidades adiadas do Portal do Cliente (ADR-043 §12) estão
  todas fechadas. O único item que falta na Fase 8 é Analytics & IA,
  deliberadamente adiado.
- Uma futura alçada de tickets (se vier a ser pedida) estende
  `Conversation`, não cria um segundo agregado a manter a par do
  primeiro.

### O que fica em aberto, e é assumido

- **Sem prioridade.** Um ticket não se marca urgente/normal — mesma
  disciplina de não inventar o que ninguém pediu. Adicionável mais tarde
  sem mudar a forma.
- **Sem reatribuição de ticket a um vendedor específico.** A notificação
  de um ticket novo segue exactamente a mesma regra das mensagens
  directas — vai para `Customer.AssignedToEmployeeId`, se houver; sem
  vendedor atribuído, ninguém é avisado e o ticket fica só na fila
  partilhada.
- **Sem estado intermédio** ("em curso", diferente de "aberto"). Dois
  estados — `Open`/`Closed` — chegam para "abertura e acompanhamento";
  um terceiro estado é extensão futura, não recorte deste ADR.

## Related

ADR-045 (mensagens directas — o motor que este ADR reaproveita), ADR-043
(Portal do Cliente — identidade externa), ADR-044 (mesmo padrão de "sem
SLA agora"), ADR-036 (não inventar taxonomias sem fonte),
`pending-decisions.md` §Domínio e negócio.
