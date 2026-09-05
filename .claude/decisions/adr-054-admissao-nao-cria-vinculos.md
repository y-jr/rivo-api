# ADR-054: A admissão não cria vínculos com contas

## Status

Aceite (2026-09-05). **Alteração de contrato:** `POST /hr/employees` deixa de
aceitar `userId`. Enviar o campo passa a dar `400`.

Decisão do utilizador: «tira o `userId` da admissão».

## Context

O ADR-051 criou `hr.employees.link_account`, deliberadamente fora do perfil HR,
com o argumento de que criar o vínculo conta↔colaborador é conceder,
indirectamente, o que o Cargo confere — incluindo autoridade de aprovação.

E deixou registada, no próprio ADR, a assimetria que isso não fechava:

> **A admissão continua a aceitar `userId` com `hr.employees.write`.** (…)
> significa que o perfil HR pode criar um vínculo **na admissão** — o que esta
> permissão governa é reatribuí-lo depois.

Era uma porta trancada ao lado de uma janela aberta. Quem tivesse
`hr.employees.write` não podia reatribuir um vínculo, mas podia criar um: bastava
admitir um colaborador novo já com a conta pretendida.

O ADR-053 tornou a assimetria mais cara. Ao acrescentar histórico do vínculo,
descobriu-se — pela verificação end-to-end, não pelos testes — que o histórico
tinha sido implementado num dos caminhos e esquecido no outro. **Dois caminhos
para o mesmo facto é o defeito, e não a sua consequência.**

## Decision

`POST /hr/employees` regista uma pessoa. Dizer que conta age por ela é outro
acto, com outra permissão e outra rota.

O caso de uso `HireEmployee` perdeu o parâmetro `userId`, e com ele o desfecho
`UserAlreadyLinked` — que se tornou inalcançável, porque o conflito de conta só
pode acontecer onde as contas se ligam.

### O campo é recusado, não ignorado

Esta é a parte que exigiu cuidado. Apagar `UserId` do DTO teria feito o
`System.Text.Json` descartá-lo **em silêncio** — não há
`UnmappedMemberHandling` configurado. Quem continuasse a enviá-lo receberia
`201` e ficaria convencido de que tinha ligado a conta.

Um cliente convencido de que ligou uma conta que não ligou é pior do que um
cliente que falha: desde o ADR-050 é este vínculo que determina quem pode
decidir aprovações, e a falha manifestar-se-ia mais tarde, como um `403`
inexplicável a alguém que devia poder aprovar.

Por isso `UserId` **continua declarado no DTO**, e só para poder ser recusado
com `400` e uma mensagem que aponta a rota certa. É uma peça de transição, não
parte do desenho, e pode desaparecer quando não houver cliente a enviá-lo.

## Consequences

### As suites passaram a ligar em passo próprio

`New-RivoColaboradorComConta` era de dois passos (registar conta, admitir com
`userId`) e passou a três: registar, admitir, ligar.

E a ligação usa **sempre** os cabeçalhos de Admin, mesmo quando a admissão usa
os de RH. Isso é a decisão a funcionar, não um incómodo: uma suite que se
autentique como RH admite o colaborador e não o consegue ligar.

### Dois casos de `verify-hr` mudaram de assunto

Os casos 18 e 19 verificavam «contratar com conta já ligada é recusado com
409» — comportamento que deixou de existir, porque contratar já não liga.
Passaram a verificar o que interessa agora:

- **18** — a admissão recusa `userId` com `400` **e não cria o colaborador**
- **19** — RH admite mas recebe `403` ao tentar ligar; o vínculo só se faz com
  a permissão dedicada

O caso 19 é o argumento deste ADR verificado de ponta a ponta contra a stack.

### Os testes de `HireEmployee` mudaram de propósito

Nasceram no ADR-053 a garantir «admitir com conta abre um episódio». Passaram a
garantir **«admitir não cria vínculo nenhum»**. A conversão é o próprio
argumento: o defeito do ADR-053 só era possível porque o vínculo podia nascer
por dois caminhos, e agora nasce por um.

### O que fica

`LinkCustomerAccount` em `commercial` continua a sobrepor vínculos em silêncio e
sem histórico — a mesma família de lacuna, noutro módulo, com consequência
menor. Continua como decisão em aberto.
