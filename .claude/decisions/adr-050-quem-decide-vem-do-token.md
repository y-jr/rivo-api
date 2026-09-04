# ADR-050: Quem decide vem do token, nunca do corpo do pedido

## Status

Aceite (2026-09-04). **Corrige uma falha de segurança**, e altera o contrato
HTTP de `approval` — `POST /requests/{id}/decisions` e
`POST /requests/{id}/cancellation` deixam de aceitar o identificador do
colaborador no corpo.

Decisão do utilizador: corrigir o backend antes de construir qualquer ecrã
de aprovações.

## Context

A falha apareceu ao ligar o frontend. A pergunta era banal — «de onde vem o
`decidedByEmployeeId` que o corpo do pedido exige?» — e a resposta era que
vinha de quem chamasse, sem ninguém o confrontar com o token.

```
POST /approval/requests/{id}/decisions
{ "decidedByEmployeeId": "<identificador de outra pessoa>", "action": "Approved" }
→ 200 OK
```

**Verificado empiricamente contra a API a correr**, não inferido do código: a
conta `Admin` — que não estava sequer associada a nenhum colaborador —
registou uma decisão em nome de um colaborador atribuído ao passo, e o pedido
passou a `Approved`.

### O que isto significava

`approval` é o módulo cuja razão de existir é impor quem pode fazer o quê. A
BR-2 diz que **quem submete nunca decide sobre o próprio pedido**, e
`modules/approval.md` descreve-a como imposta «em código, não configuração».

Só que as regras eram avaliadas contra o colaborador **declarado no corpo**,
não contra o **autor da chamada**. Consequências, por ordem de gravidade:

| Regra | Como se contornava |
|---|---|
| **BR-2** | Quem submeteu, tendo `approval.requests.decide`, aprovava o seu próprio pedido indicando o identificador de outra pessoa |
| **BR-4** | A mesma pessoa intervinha duas vezes em papéis conflituantes, declarando-se outra na segunda |
| **K18** | Qualquer conta com `approval.requests.read` cancelava o pedido alheio declarando-se o requisitante |

O domínio estava certo. `SegregationOfDutiesTests` cobria BR-2 e BR-4, e
passava — porque recebia o identificador **já escolhido** e aplicava-lhe as
regras correctamente. O defeito estava em **quem escolhia**, que é
orquestração, e `approval` não tinha testes de camada Application.

### Não foi descuido — foi um raciocínio errado

O comentário que justificava o campo dizia:

> Quem decide, como Colaborador de `hr`. É contra este identificador que BR-2
> e BR-4 são verificadas — e não contra o utilizador autenticado, **porque
> nem todo o utilizador é colaborador** (ADR-004).

O facto está certo: nem todo o utilizador é colaborador, e o ADR-004 fixou
isso. A conclusão é que está errada. A resposta a essa verdade é **resolver o
colaborador a partir da conta e recusar quando não há vínculo** — não deixar
quem chama declarar quem é.

Vale registá-lo assim porque o modo de falha é instrutivo: uma premissa
verdadeira levou a um desenho inseguro, e nenhuma revisão de código que
aceitasse a premissa teria dado por isso.

## Decision

**O decisor e o cancelador resolvem-se da conta autenticada.**

### 1. A resolução vive no caso de uso, não na camada Api

`DecideOnRequest` e `CancelRequest` passam a receber o **identificador da
conta** e a resolver o colaborador por
`IEmployeeDirectory.FindByUserIdAsync` — o mesmo contrato que o Portal do
Colaborador já usava para resolver «o próprio» (ADR-042). `approval` já
dependia de `Rivo.Hr.Contracts` desde o ADR-034; não há fronteira nova.

Fica no caso de uso e não no endpoint de propósito: assim **não sobra nenhum
caminho de código que aceite um decisor arbitrário**, nem para um consumidor
interno futuro.

### 2. Sem vínculo a Colaborador não se decide

Uma conta sem colaborador associado recebe **403**, com mensagem que diz
porquê. Não é 404 — a pessoa existe; o que falta é o vínculo. É a aplicação
directa do ADR-005: quem decide é um Colaborador, porque é o **Cargo** que
confere autoridade, e contas não têm cargos.

### 3. O corpo do pedido perde os campos

| Antes | Agora |
|---|---|
| `POST .../decisions` com `{ decidedByEmployeeId, action, notes }` | `{ action, notes }` |
| `POST .../cancellation` com `{ cancelledByEmployeeId }` | sem corpo |

É alteração de contrato, e está reflectida em `API-FRONTEND.md`.

## Consequences

### O que fica mais fácil

- As regras de segregação passam a valer contra quem realmente chama. BR-2,
  BR-4 e o K18 deixam de depender da honestidade do cliente.
- O ecrã de aprovações fica com um desenho óbvio: não há nada a escolher, e
  portanto nada a proteger na interface.
- Nasceu `Rivo.Approval.Application.Tests` — o projecto que
  `project-state.md` já apontava como o mais valioso em falta, precisamente
  por BR-2/BR-4/BR-6 viverem em `DecideOnRequest` sem cobertura.

### O que fica mais difícil, e é o custo aceite

- **As suites de verificação tiveram de mudar.** Seis delas aprovavam
  autenticadas como `Admin`, indicando o aprovador no corpo — ou seja,
  exercitavam a falha. Agora cada uma cria o aprovador **com conta ligada** e
  age com os cabeçalhos dele (`New-RivoColaboradorComConta`, em
  `scripts/_ambiente.ps1`).
- **Decidir exige que a conta tenha colaborador.** Um administrador de
  sistema sem vínculo já não aprova nada — o que é a regra correcta, mas é
  uma porta que se fecha e que alguém vai notar.
- **A ligação conta ↔ colaborador só existe na admissão.**
  `POST /hr/employees` aceita `userId`; não há rota para ligar um colaborador
  que já exista. Fica registado em `pending-decisions.md`: quem já está
  admitido sem conta precisa de uma.

### O que a auditoria já fazia bem

O rasto registava sempre o `actorId` verdadeiro, mesmo quando a decisão era
atribuída a outra pessoa. **A detecção funcionava; o que falhava era a
prevenção.** Um investigador conseguia reconstruir o que aconteceu — mas
depois de ter acontecido.

## Risks

- **Contas de serviço.** Se algum consumidor automático vier a precisar de
  decidir, precisará de um colaborador associado, ou de uma decisão nova
  sobre como representá-lo. Não existe nenhum hoje.
- **Cargos com autoridade continuam a resolver-se por `hr`.** Esta correcção
  fecha «quem diz que é», não «quem tem autoridade» — essa parte já estava
  certa (BR-20) e não foi tocada.

## Related

ADR-004 (utilizador ≠ colaborador — a premissa verdadeira que levou ao
desenho errado), ADR-005 (é o Cargo que confere autoridade), ADR-008
(segregação é invariante de domínio, RLS é defesa em profundidade),
ADR-034 (desenho do motor de aprovação), ADR-042 (o mesmo padrão de resolver
«o próprio» pelo vínculo, no Portal do Colaborador), K18 em
`state/known-issues.md`.
