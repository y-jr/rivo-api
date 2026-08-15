# notifications — Notificações

**Classificação:** generic domain. Entrega = infraestrutura; a decisão de
*quando* notificar fica na origem.

## Responsabilidade

Entregar notificações aos destinatários (dashboard, e-mail e futuros
canais). **Não decide quando uma notificação é justificada** — o contexto de
origem decide e publica; `notifications` entrega.

## Conceitos

| Conceito | Notas |
|---|---|
| Notificação | destinatário, tipo, título, mensagem, lida, criado em |
| Preferência de Notificação | destinatário, canal, frequência |

## Possui

Notificação, Preferência de Notificação, e o estado de entrega.

## Depende de

`identity` (resolução do destinatário). Provider de e-mail
(infraestrutura).

## Consumido por

Todos os módulos, por evento — não por chamada directa que acople o módulo
de origem a um canal concreto.

## Contratos publicados

- Entregar notificação (destinatário, tipo, conteúdo).

## Não pode

- Decidir se uma notificação é justificada pelo negócio.
- Conhecer o significado de "factura" ou "aprovação" — recebe um evento
  genérico com o conteúdo já preparado.
- Duplicar-se por audiência. Segmentar por audiência (interno vs. portal de
  cliente) é aceitável; criar um segundo módulo de notificações não é.

## Regras de negócio

- **A entrega corre fora da transacção de negócio.** Corrige directamente o
  anti-padrão encontrado no protótipo: um trigger inseria até 20
  notificações dentro da mesma transacção que mudava o estado de um pedido
  de pagamento.
- Envio via background job, com retries e backoff.
- Idempotência no reenvio.

## Perguntas em aberto

- Provider de e-mail transaccional.
- Canais além de dashboard e e-mail (SMS? push?).

## Estado

**Implementado.** Fila com estado, worker de entrega com recuo exponencial,
leitura e marcação como lida. `identity` notifica na atribuição de perfil.

Verificado em `scripts/verify-notifications.ps1` (13 casos).

### O anti-padrão que isto corrige

O protótipo tinha um *trigger* que inseria **até 20 notificações dentro da
transacção** que mudava o estado de um pedido de pagamento (A4). Um problema
no envio derrubava a operação de negócio.

Aqui:

```
Módulo de origem → INotifier.QueueAsync()  → grava e devolve. Transacção própria.
                                             ↓
BackgroundService (sondagem) → canal → Delivered | Failed + recuo
```

**Uma falha de entrega nunca afecta a operação de negócio.** Contraste
deliberado com `audit`, onde as falhas propagam: perder auditoria é pior do
que falhar; perder uma notificação não é.

### Dois estados independentes

| Estado | Significado |
|---|---|
| `ReadAt` | Leitura na aplicação. A notificação é visível assim que criada |
| `DeliveryStatus` | Entrega externa: `NotRequired`, `Pending`, `Delivered`, `Abandoned` |

Separá-los evita que um problema no e-mail esconda a notificação do
destinatário. Sem canal externo pedido, nasce `NotRequired` em vez de ficar
eternamente pendente.

Recuo exponencial: 2, 4, 8, 16 minutos; ao 5.º insucesso passa a
`Abandoned` — insistir mais só mascara a avaria.

### Sem permissões

Ler notificações não exige permissão, exige **ser o destinatário**. É
invariante de propriedade do agregado, verificada no domínio — não é política
configurável, e por isso não entra no modelo de ADR-014.

Marcar notificação alheia devolve **404, não 403**: distinguir revelaria que
existe.

### Fora do implementado

| Omitido | Porquê |
|---|---|
| **Preferências de notificação** | Só há um canal externo; preferências sobre um canal só são especulação |
| **Provider de e-mail** | Decisão pendente. O port existe; a implementação de dev regista em log |
| **Templates** | Nada os gera |
