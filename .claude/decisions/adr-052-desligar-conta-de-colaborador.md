# ADR-052: Desligar a conta de um Colaborador

## Status

Aceite (2026-09-05). Acrescenta `DELETE /hr/employees/{employeeId}/account`,
com a **mesma permissão** de ligar. Resolve a decisão em aberto que o ADR-051
deixou.

Decisão delegada ao arquitecto pelo utilizador.

## Context

O ADR-051 deu a rota para ligar uma conta a um colaborador já admitido, e
deixou explicitamente por resolver como se desfaz. A pergunta que bloqueava era
de domínio, não de implementação:

> As decisões de aprovação já tomadas por uma conta que se desliga continuam
> válidas, ou passam a estar em questão?

A resposta muda o desenho por inteiro. Se as decisões ficassem em questão,
desligar seria um acto de governança e teria de passar pelo motor de aprovação.

## Decision

### As decisões já tomadas continuam válidas

E isto **não é uma escolha de política — é o que o modelo já dizia.**

`ApprovalDecision` guarda `DecidedByEmployeeId`. Não guarda, e nunca guardou, o
identificador da conta. O facto gravado é «o colaborador X decidiu», e o
ADR-050 apenas mudou *como se descobre* qual é o X — passou a resolvê-lo a
partir da conta em vez de o aceitar do corpo do pedido.

Desligar a conta não altera quem a pessoa era, nem que ela decidiu. Remove a
capacidade de agir **daqui para a frente**, e mais nada. Não há decisão nenhuma
a reabrir, porque nenhuma estava atribuída à conta.

Verificado no código, não assumido: `ApprovalRequest.cs:395`.

### `DELETE`, com a mesma permissão de ligar

`hr.employees.link_account`, a mesma. Duas razões:

- Desligar é uma **perda** de capacidade, não um ganho. A assimetria natural
  seria exigir *menos*, não mais.
- Corrigir um vínculo errado é **resposta a incidente**, não operação de
  rotina. Se a conta errada ficou ligada à pessoa errada, o tempo entre
  descobrir e corrigir é exposição. Exigir mais do que para ligar — ou pior,
  uma aprovação — tornaria a correcção mais lenta do que o erro.

Repetível: desligar quem já está desligado devolve `204` sem auditar. `404`
para um colaborador que não existe — não há estado pretendido a verificar.

### Auto-desligamento é permitido

Ao contrário de ligar, que recusa a própria conta. Desligar-se a si próprio é
estritamente uma perda de capacidade e não encadeia em escalada: para se voltar
a ligar a outro colaborador seria preciso ligar a própria conta, que continua
recusado.

### O que isto custa, dito claramente

**Torna o `409` do ADR-051 contornável em dois passos.** Quem tenha a permissão
pode desligar e voltar a ligar outra conta, obtendo a transferência que numa só
chamada é recusada.

Isto é aceite conscientemente, e a razão é que o `409` nunca foi uma parede — é
o que impede a substituição **silenciosa**. Uma transferência deliberada deixa
**dois** registos na trilha, e o par nomeia a mesma conta dos dois lados:
`PreviousValue` no desligar, `NewValue` no ligar. A transferência fica legível,
que é o que se quer de uma acção legítima e o que denuncia uma ilegítima.

A alternativa — gatilhar o desligar numa aprovação — comprava uma parede à
custa da velocidade de resposta a incidente, que é o caso de uso principal.
Trocar segurança por segurança não é ganho.

### Não verifica pedidos em curso

Um colaborador desligado pode ser aprovador congelado num pedido em curso
(BR-6), e nesse caso o passo fica sem quem o decida.

Não se verifica, por duas razões. `hr` **não referencia**
`Rivo.Approval.Contracts` — define o seu próprio port `IHrApprovalSubmission`
precisamente para o ciclo `hr ↔ approval` não se formar, e introduzir a
dependência para um aviso não passa a barra da regra de fronteiras. E o risco
já existe sem isto: um colaborador pode ser aprovador sem nunca ter tido conta.

Fica registado como risco conhecido, não como omissão.

## Consequences

- `verify-hr`: 28 → **34 casos**. Seis novos, incluindo o par que prova a
  sequência de correcção (desligar → ligar a outro) e o que confirma que a
  transferência fica legível na trilha dos dois lados.
- `Rivo.Hr.Application.Tests`: 9 → **15 testes**.
- O ecrã de Utilizadores deixa de dizer «não há como o desfazer», que era
  verdade quando foi escrito e deixou de ser.
- Fecha a decisão em aberto do ADR-051. As outras duas mantêm-se: a admissão
  continua a aceitar `userId` com `hr.employees.write`, e `LinkCustomerAccount`
  continua a sobrepor vínculos em silêncio.
