# ADR-063: Corrigir não é o mesmo que mudar de estado

## Status

Aceite (2026-09-16). Decisão do utilizador — **«a edição entra agora»** —, a
testar o frontend em produção: «Colaboradores, departamentos e cargos. Não dá
pra atribuir nada a ninguém nem editar estes elementos.»

## Context

A queixa eram duas coisas diferentes, e só uma era um defeito.

**Atribuir cargo existia** — em `RH › Cargos`, na linha de cada cargo. Escolhe-se
o cargo primeiro e a pessoa depois, o que está ao contrário de como se pensa:
quem quer dar um cargo a alguém vai ao **colaborador**. Funciona, mas está onde
ninguém o procura.

**Editar não existia.** E a contagem explica melhor do que qualquer descrição:

```
hr:   14 × GET   26 × POST   1 × DELETE   0 × PUT/PATCH
todo o sistema:                           0 × PUT/PATCH
```

Não havia um único endpoint de edição em **nenhum módulo**. Nada se corrigia:
nem colaborador, nem departamento, nem cargo, nem cliente, nem artigo. Um nome
mal escrito ficava mal escrito; um colaborador admitido no departamento errado
ficava lá para sempre.

### O que torna isto estranho

**O domínio já tinha os métodos.** `Employee.MoveToDepartment`,
`Department.Rename`, `Department.AssignManager`, `Employee.Deactivate` — escritos
na Fase 0 de `hr`, com **testes de domínio a cobri-los**, e sem um único
chamador. A capacidade estava lá; faltavam as duas camadas de cima.

## Requirements

- **Facto** — nenhum PUT/PATCH em todo o backend a 2026-09-16.
- **Facto (verificado)** — `MoveToDepartment`, `Rename` e `AssignManager` existem
  no domínio e nunca foram chamados por caso de uso nenhum.
- **Facto** — `ManagerId` e `HierarchyLevel` só ordenam e apresentam: nenhum
  decide alçadas ou autoridade.
- **Facto** — `GrantsApprovalAuthority` decide quem pode vir a aprovar, e o BR-20
  obriga a que atribuir um cargo que a confira passe por governança.
- **Facto** — o BR-14 proíbe eliminação física, e há dados congelados de
  propósito noutros módulos (o nome do cliente numa factura emitida).
- **Decisão do utilizador** — a edição entra antes do lançamento.

## Alternatives

1. **Um `PATCH` genérico por entidade, com todos os campos.** Rejeitada, e é a
   mais rápida: trataria por igual um erro de digitação e uma reorganização, e
   deixaria a trilha com uma só acção — «alterado» — que não distingue as duas.
   Pior: abriria a porta a editar a marca de autoridade num cargo, que é o único
   campo que não pode ser editável.
2. **Reaproveitar `POST` do recurso com semântica de «upsert».** Rejeitada:
   confunde criar com corrigir, e o módulo já usa POST para actos com nome
   próprio.
3. **Adiar para depois do lançamento.** Rejeitada pelo utilizador. Um sistema de
   RH onde não se muda alguém de departamento não é uma versão reduzida: é uma
   versão que não serve.
4. **Distinguir correcção de acto, com verbos diferentes** (escolhida).

## Decision

### 1. `PUT` corrige; `POST` em sub-recurso é acto

```
PUT  /hr/employees/{id}              corrige o nome
POST /hr/employees/{id}/department   transfere de departamento
PUT  /hr/departments/{id}            corrige nome e responsável
PUT  /hr/positions/{id}              corrige nome e nível
```

**Os primeiros `PUT` do projecto.** O resto do módulo usa `POST` porque quase
tudo nele é um acontecimento com nome próprio — admitir, ligar uma conta,
atribuir um cargo, cessar um contrato. Corrigir não é acontecimento: é repor o
valor certo num campo que estava errado, e o verbo que diz isso é `PUT`.

A transferência de departamento fica em `POST` com sub-recurso porque **é** um
acontecimento: uma decisão de organização, não um engano arrumado.

### 2. A trilha distingue as duas, e guarda o valor anterior

`hr.employee.corrected` e `hr.employee.transferred` são acções distintas, mais
`hr.department.corrected` e `hr.position.corrected`. Todas registam
`PreviousValue` **e** `NewValue`.

Uma correcção sem o valor antigo não é auditável: fica a saber-se que algo mudou
e não o quê — e é precisamente numa correcção que a pergunta «o que dizia antes?»
se faz.

### 3. Uma não-alteração não grava nem audita

Abrir o formulário e carregar em «guardar» sem tocar em nada não produz escrita
nem entrada na trilha. Sem isto, a trilha enche-se de registos a dizer que algo
mudou quando nada mudou — e quem a lê deixa de confiar nela.

### 4. `GrantsApprovalAuthority` **não é editável**, e é o centro desta decisão

Não há forma de a alterar: nem no domínio (`Position.Correct` não a recebe), nem
no contrato HTTP (`CorrectPositionRequest` não a tem), nem no ecrã.

A razão: ligá-la num cargo **já atribuído** daria autoridade de aprovação a toda
a gente que o ocupa — sem que nenhuma dessas atribuições passasse pela aprovação
que o BR-20 exige. Seria contornar a governança por um formulário de correcção.

Quem precisa de um cargo com autoridade **cria um cargo novo e atribui-o**, que é
o caminho que a governança vigia. O ecrã diz isto, em vez de mostrar um campo
desactivado sem explicação.

Um teste de domínio, um de aplicação e um caso da suite verificam a propriedade —
incluindo o caso em que o corpo do pedido traz a marca de propósito e o servidor
a ignora.

### 5. O estado do colaborador continua fora

`Deactivate`/`Reactivate` existem no domínio e continuam sem endpoint. Desactivar
alguém é consequência da **cessação do contrato**, que tem o seu próprio caminho
(`POST /hr/contracts/{id}/termination`) e as suas regras. Um botão de
«desactivar» ao lado do nome seria uma segunda porta para o mesmo efeito, sem as
regras da primeira.

### 6. O que se corrige em cada entidade

| Entidade | Corrigível | Não corrigível, e porquê |
|---|---|---|
| Colaborador | nome | data de admissão (facto), conta ligada (acto próprio, ADR-054), estado (cessação) |
| Departamento | nome, responsável | — |
| Cargo | nome, nível hierárquico | **autoridade de aprovação** (BR-20) |

## Consequences

- **O frontend ganhou o seu primeiro `PUT`** (`api.put`), com a distinção
  documentada no cliente.
- **Corrigir um colaborador pode fazer dois pedidos**, sequencialmente: o nome e
  a transferência são rotas diferentes. O ecrã envia só o que mudou, e se o
  primeiro falhar não faz o segundo.
- **A coluna «Gestor» dos departamentos passou a mostrar o nome.** Dizia
  «Atribuído» porque não havia como atribuir; agora há, e um nome é mais útil do
  que um sim.
- **`IHrStore` ganhou `FindDepartmentAsync`** — rastreado, ao contrário da
  listagem: quem procura por identificador vai alterar.
- **`verify-hr` passou de 39 casos a 50.** O 48 é o que interessa: manda a marca
  de autoridade a falso no corpo e verifica que continua ligada.
- **Nada disto resolve a queixa toda.** «Atribuir» continua no ecrã de Cargos, e
  não no do colaborador — é mudança de sítio, não de funcionalidade, e ficou
  fora deste ADR.

## Risks

- **A porta aberta para o resto do sistema.** `hr` passa a ter correcções e os
  outros treze módulos não. Vai parecer arbitrário a quem encontrar um cliente
  mal escrito em `commercial` — e a resposta certa não é copiar isto para todos,
  é decidir entidade a entidade o que é corrigível, que foi o trabalho feito aqui.
- **Corrigir um nome não reescreve o que ficou congelado.** O nome impresso numa
  factura emitida é um retrato da altura e continua igual. Está escrito no
  domínio, mas é o tipo de coisa que gera uma pergunta de suporte no dia em que
  alguém corrigir um nome e for ver uma factura antiga.
- **A marca de autoridade é protegida por ausência, não por validação.** O
  servidor ignora o campo se ele vier no corpo, em vez de recusar o pedido. Um
  cliente que o envie fica convencido de que fez algo — e não fez. Recusar com
  400 seria mais claro, e foi decidido não o fazer para que um cliente antigo que
  envie o campo inteiro (copiado da criação) não parta.

## Revisit When

- **Se `HierarchyLevel` passar a decidir alçadas** — hoje só ordena. No dia em
  que uma regra de aprovação o leia, editá-lo passa a ter as consequências que a
  marca de autoridade tem, e esta decisão tem de ser revista com ele.
- **Se surgir o pedido de corrigir dados congelados** (o nome numa factura já
  emitida): isso não é correcção, é emissão de um documento retificativo, e
  pertence a `finance`.
- **Quando o segundo módulo precisar de correcções**, vale extrair o padrão —
  `CorrectionResult` e o tradutor de resposta são genéricos e estão duplicáveis
  por copiar, o que é o momento errado para o fazer.

## Related

ADR-015 (a permissão do catálogo de cargos fica fora do perfil HR), ADR-054
(ligar a conta é acto separado), ADR-042 e ADR-062 (dados congelados: o retrato
de quem não se reescreve), BR-14 (nada se elimina fisicamente), BR-20 (atribuir
autoridade passa por governança).
