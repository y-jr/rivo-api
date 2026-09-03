# ADR-045: Mensagens directas com a equipa comercial

## Status

Aceite (2026-09-03). Decisão do utilizador, segunda das três capacidades
adiadas do Portal do Cliente (ADR-043, `pending-decisions.md` §Domínio e
negócio) — a primeira foi pagamentos, ADR-044.

## Context

`docs/rivo-suite-descricao-modulos.md` §12 só diz "mensagens directas com a
equipa comercial" — sem modo, sem destinatário, sem forma. Três perguntas
bloqueavam o desenho, pela mesma razão que bloquearam pagamentos: nenhuma
delas se infere do código nem dos documentos.

## Decision

### 1. Assíncronas

O cliente escreve, a mensagem fica em fila, Sales responde quando puder —
o mesmo regime que `notifications` já usa noutro contexto. Sem chat em
tempo real, sem WebSocket/SignalR: infra-estrutura que a plataforma não
tem e que esta funcionalidade, por si só, não justifica trazer.

### 2. Vendedor responsável por cliente

`commercial.Customer` ganha `AssignedToEmployeeId` (`Guid?`, referência a
`hr.Employee` por identificador — ADR-010, sem chave estrangeira entre
schemas). Sales/Admin atribui por `POST /commercial/customers/{id}/owner`,
mesma permissão de escrever no cliente (`commercial.customers.write`) que
já cobre `LinkCustomerAccount` — não há audiência própria que a distinga.

**O que isto controla, e só isto: para quem vai a notificação de uma
mensagem nova.** Não é controlo de acesso — qualquer utilizador com
`messaging.conversations.write` continua a poder ver e responder a
qualquer conversa (caixa partilhada). Restringir a resposta ao vendedor
exclusivo trancaria o cliente fora de resposta sempre que essa pessoa
estivesse ausente, e nada na pergunta original pedia essa troca. Fica
registado aqui como leitura, revisível se um caso de uso concreto vier a
exigir o oposto.

**Alternativa rejeitada:** caixa partilhada sem dono nenhum. Foi a opção
recomendada — mais simples, sem campo novo — mas o utilizador escolheu
directamente a atribuição por cliente, que é o padrão que a maioria dos
CRMs segue e que abre caminho para outros usos do mesmo campo (relatórios
por vendedor, por exemplo) sem se comprometer com nenhum deles agora.

### 3. Módulo novo — `messaging`

`notifications` é fire-and-forget de um só sentido (`INotifier.QueueAsync`,
sem thread, sem resposta, sem estado de leitura) — forçá-lo a servir uma
conversa a duas vias mudaria o que ele significa para todos os outros
consumidores (`identity`, `approval`, …). Um bounded context novo
(`Conversation`, `Message`) mantém a fronteira limpa, e continua a usar
`notifications` para o único aviso que faz sentido aqui: "tens mensagem
nova", enviado ao vendedor responsável quando o cliente escreve.

**Sem notificação ao cliente quando Sales responde** — mesma nota já
registada em ADR-043 (ligação de conta) e ADR-044 (confirmação de
pagamento): o cliente vê a resposta ao abrir o portal; ligar `notifications`
a esse lado é trabalho separado, não bloqueante.

### Uma conversa aberta por cliente, não uma por assunto

O cliente não escolhe "conversa" — escreve, e cai na conversa aberta que já
tiver, ou abre uma nova se não houver nenhuma. Fecha quando Sales resolve
(`POST /messaging/conversations/{id}/closure`); a próxima mensagem do
cliente abre outra. **Categorização por assunto é o que "tickets de
suporte" (a terceira capacidade adiada) já promete separadamente** —
inventar aqui uma segunda forma de agrupar seria antecipar essa decisão,
ainda por tomar.

## Consequences

### O que fica mais fácil

- A terceira e última capacidade adiada do Portal do Cliente (tickets de
  suporte) tem agora um precedente de forma a seguir ou a rejeitar
  explicitamente — categorias/SLA são exactamente o que "uma conversa por
  cliente, sem assunto" deixou de fora.
- `AssignedToEmployeeId` fica disponível para outros consumidores que
  venham a precisar de "quem é o vendedor deste cliente", sem se
  comprometerem com mensagens.

### O que fica em aberto, e é assumido

- **Sem SLA de resposta.** Mesmo estado do `approval` e do `PaymentClaim`
  (ADR-044) — visível na fila (`GET /messaging/conversations`), sem nada a
  forçar prazo.
- **Sem reatribuição em lote.** Trocar o vendedor responsável de vários
  clientes de uma vez (saída de um comercial, por exemplo) não tem
  endpoint próprio — atribui-se um de cada vez, como hoje.
- **Sem anexos.** Uma mensagem é só texto; ligar `documents` a uma
  conversa, se vier a fazer falta, é extensão separada, mesmo padrão de
  ADR-044 com o comprovativo.

## Related

ADR-043 (Portal do Cliente — identidade externa), ADR-044 (pagamentos —
mesmo padrão de contrato de escrita e de notificação diferida), ADR-010
(referência por identificador entre contextos), ADR-017 (assemblies de
contratos sem dependências), `modules/commercial.md`,
`pending-decisions.md` §Domínio e negócio.
