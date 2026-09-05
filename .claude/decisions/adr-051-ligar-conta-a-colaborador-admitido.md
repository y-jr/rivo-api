# ADR-051: Ligar uma conta a um Colaborador já admitido

## Status

Aceite (2026-09-05). Acrescenta `POST /hr/employees/{employeeId}/account` e a
permissão `hr.employees.link_account`, deliberadamente **fora do perfil HR**.

Decisões do utilizador: permissão dedicada em vez de reutilizar
`hr.employees.write`; auto-ligação recusada.

## Context

O ADR-050 fechou a falha de personificação em `approval` ao resolver o
colaborador a partir da conta autenticada. Isso tornou o vínculo
conta ↔ colaborador **o único determinante de quem pode decidir**.

E expôs uma lacuna que até aí era inofensiva: o vínculo só se estabelecia na
admissão. `POST /hr/employees` aceita `userId`, mas não havia rota para ligar
um colaborador que já existisse. Quem estivesse admitido sem conta não podia
ser ligado sem ser readmitido — e readmitir cria um segundo registo da mesma
pessoa, que é precisamente o que o BR-14 e a trilha de auditoria existem para
evitar.

O efeito prático era visível no estado local: 41 colaboradores sem conta, e
nem a conta `Admin` conseguia decidir uma aprovação pela interface.

`commercial` já tinha o equivalente desde o ADR-043
(`POST /commercial/customers/{id}/account`). Faltava o de `hr`.

## Decision

### A rota

```
POST /hr/employees/{employeeId}/account
{ "userId": "<conta de identity>" }
→ 204
```

Só o `userId` no corpo: o colaborador vem da rota, e o vínculo é um par, não
uma configuração.

### A permissão é própria, e não `hr.employees.write`

Esta é a divergência deliberada face ao precedente. O ADR-043 justifica usar a
permissão de escrita do Cliente com «não há audiência própria que a distinga de
quem já gere clientes». **Essa justificação não transfere.**

Uma conta ligada a um Cliente dá acesso ao portal do cliente — ver as suas
facturas, submeter comprovativos. Uma conta ligada a um Colaborador dá o que o
**Cargo** desse colaborador confere, incluindo autoridade de aprovação.

Ligar uma conta a um colaborador que tenha Cargo com autoridade é conceder-lhe
essa autoridade **sem passar pela decisão que o BR-20 exigiria** para a
atribuição do Cargo. Quem cria o vínculo escolhe, indirectamente, quem aprova.

Por isso `hr.employees.link_account` fica fora de `ForHumanResources`, pela
mesma razão que `hr.positions.write` já estava fora: RH atribui Cargos, mas não
decide quais conferem autoridade; RH admite pessoas, mas não decide que conta
age em nome de quem.

### Auto-ligação recusada

O actor não pode ligar a conta com que está autenticado. É o caminho de
escalada mais directo — ligo-me a um colaborador com Cargo de aprovação e passo
a decidir — e o mesmo princípio do BR-2: ninguém resolve o seu próprio caso.

A recusa acontece **antes** de se saber se o colaborador existe, para que não
dependa de o atacante ter acertado num identificador válido. Traduz-se em
`403`, não `409`: não é o estado que impede, é quem está a pedir.

### Religar por cima recusa-se

Um colaborador que já tenha conta dá `409`. É aqui que esta rota diverge de
`LinkCustomerAccount`, que sobrepõe em silêncio: no `commercial` a troca
reatribui o acesso ao portal; aqui transferia a identidade com que se aprova.

Repetir a **mesma** ligação é repetível sem erro e sem segundo registo na
trilha — mesma disciplina de `DeactivateApprovalPolicy`.

### Auditado com a conta

`hr.employee.account_linked`, com o `userId` em `NewValue`. Quem investiga uma
decisão de aprovação precisa de saber quando é que aquela conta passou a poder
agir por aquela pessoa, e por ordem de quem.

## Consequences

### Verificado

`403 → 204 → 200`: a conta `gestor@rivo.ao` dava `403` em `/portal/me`, foi
ligada, e passou a resolver o colaborador. É o ciclo do ADR-050 fechado — sem
esta rota, o ecrã de aprovações não tinha como ser usado por ninguém.

`verify-hr` passou de 20 para 28 casos, todos a passar. Nove testes novos na
camada Application, dos quais dois falham se a guarda de auto-ligação for
removida — confirmado a sabotá-la de propósito.

### `hr` ganhou testes de camada Application

Pela mesma razão que `approval` os ganhou no dia anterior:
`Employee.LinkToUser` é um setter, e **nenhuma** das três regras vive no
domínio. Recusar a auto-ligação depende do actor; as duas unicidades dependem
do armazenamento. Um teste de domínio não chegava a nenhuma delas — era
exactamente a forma da falha do ADR-050.

`IHrStore` tem 44 membros, e um caso de uso usa três. A dobra assenta numa base
`HrStoreParcial` que lança em tudo o que não for explicitamente ligado: se um
caso de uso passar a tocar num membro que o teste não previu, o teste falha a
dizer qual, em vez de receber `null` e seguir por um caminho que ninguém quis
exercitar.

### O que fica por resolver

**Não verifica que a conta existe em `identity`.** Fazê-lo exigiria uma
dependência nova de `hr` para `identity`, que a regra de fronteiras não deixa
introduzir sem justificação — e o precedente é explícito nos dois lados: nem
`HireEmployee` nem `LinkCustomerAccount` a verificam. A consequência é um
vínculo para uma conta inexistente: inútil, mas não perigoso — ninguém se
autentica com ela, e ocupa o índice único até ser corrigido.

**Não há como desligar.** Um vínculo errado só se corrige em base de dados. É
uma decisão em aberto e não uma omissão técnica: desligar é tão sensível como
ligar, e o que acontece às decisões já tomadas por aquela conta é uma questão
de domínio, não de implementação.

**A admissão continua a aceitar `userId` com `hr.employees.write`.** A
assimetria é conhecida e deliberada: mexer nisso partia o fluxo de admissão e
as suites que dele dependem. Mas significa que o perfil HR pode criar um
vínculo **na admissão** — o que esta permissão governa é reatribuí-lo depois.
Fica registado como decisão em aberto.

**Observação sobre `commercial`, não corrigida aqui.**
`LinkCustomerAccount` sobrepõe um vínculo existente sem o recusar nem o
distinguir na trilha. Requer `commercial.customers.write`, e a consequência é
menor do que seria em `hr` — mas é a mesma classe de lacuna. Fica como
observação para decisão, fora do âmbito deste ADR.
