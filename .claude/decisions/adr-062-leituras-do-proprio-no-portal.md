# ADR-062: As quatro leituras do próprio, e o parâmetro que não existe

## Status

Aceite (2026-09-16). Decisão do utilizador: as quatro rotas **agora, antes do
lançamento** — perguntado quando ficou claro que o ADR-061 dava o perfil mas o
portal continuava vazio.

## Context

O ADR-061 criou o perfil `Colaborador`. Um programador ou um estagiário passou a
poder ser convidado e ligado ao seu colaborador — e a entrar num portal que lhe
mostrava o nome, o departamento e o cargo.

Assiduidade, férias, documentos e recibos respondiam **404**: o ecrã
`/minha-area` tinha cinco secções e o backend tinha uma rota. As quatro coisas
que um funcionário quer ver do que é seu eram as quatro que não existiam.

`hr` e `payroll` já as têm, e há semanas — com as permissões de quem **gere**:
`hr.attendance.read` lê a assiduidade de toda a empresa, `payroll.runs.read` lê
a folha salarial de todos. Dar isso a um colaborador para ele ver o seu resolve o
problema errado.

## Requirements

- **Facto** — o portal autoriza por vínculo, e o 403 vem da sua falta (ADR-042).
- **Facto** — `Colaborador` não tem permissão nenhuma, de propósito (ADR-061).
- **Facto** — as leituras existentes em `hr` e `payroll` aceitam filtro por
  colaborador; o que falta não é a consulta, é a **resolução de «o próprio»**.
- **Facto (verificado)** — `PayrollRun` tem estado, e itens de folhas em
  rascunho são números por confirmar.
- **Decisão do utilizador** — as quatro, antes do lançamento; começando pelos
  recibos e pela assiduidade, que foram os que nomeou.

## Alternatives

1. **Dar as permissões de `hr` e `payroll` ao perfil `Colaborador`, e filtrar na
   API pelo utilizador autenticado.** Rejeitada, e é a alternativa mais
   tentadora porque é a mais rápida: o perfil passaria a poder ler os dados de
   todos, e a única coisa a impedi-lo seria um `where` numa rota. Um segundo
   ponto de entrada esquecido — uma exportação, um relatório, um endpoint novo
   que reutilize o mesmo caso de uso — e o filtro deixa de estar lá. A
   permissão, essa, fica.
2. **Rotas em `hr` e `payroll` com um parâmetro `employeeId` que a API valida
   contra o token.** Rejeitada: é a mesma defesa, guardada por uma comparação em
   vez de pela estrutura. Basta um caminho que não compare.
3. **Adiar para depois do lançamento.** Rejeitada pelo utilizador. Um portal do
   colaborador que não mostra o recibo do colaborador não é uma versão reduzida
   da funcionalidade: é a funcionalidade a faltar.
4. **Quatro leituras na camada de composição, sem `employeeId` em parte
   nenhuma** (escolhida).

## Decision

### 1. Contratos de «próprio» nos módulos, e não permissões novas

`hr` publica `IEmployeeSelfService` (assiduidade, férias, documentos) e
`payroll` publica `IPayrollSelfService` (recibos). Ambos recebem um
`employeeId` **já resolvido** e reutilizam os casos de uso administrativos que
já existiam — não há consultas paralelas, que ficariam para trás no dia em que
as primeiras mudassem.

Nenhuma permissão nova, em nenhum dos dois módulos. A autorização acontece antes,
e é o vínculo.

### 2. `GetMyRecords`, e a propriedade que a torna segura

A camada de composição resolve a conta autenticada no colaborador ligado
(`FindByUserIdAsync`) e só então lê. Uma classe e não quatro, porque as quatro
partilham exactamente essa resolução.

**Nenhum método aceita um `employeeId`.** É estrutural em vez de vigiada: não há
parâmetro por onde pedir a assiduidade de outra pessoa, nem sequer por engano.
Quem quiser ver dados de terceiros usa os ecrãs de `hr` e `payroll`, que pedem
permissão para isso.

Sem vínculo, devolve `NotLinked` **antes de ler** — e o módulo não é chamado, por
isso não há resposta que filtrar depois.

### 3. Quatro rotas, todas `RequireAuthorization()` e mais nada

```
GET /portal/me/attendance?from=&to=
GET /portal/me/leave
GET /portal/me/documents
GET /portal/me/payslips
```

`NotLinked` traduz-se em **403 e não 404**, como `/portal/me` já fazia: a conta
existe e está autenticada, só não tem «o próprio» que o portal existe para
mostrar.

### 4. A assiduidade tem janela, e tem omissão

`from` e `to` omitidos são o **mês corrente**. Sem omissão, quem abrisse o portal
sem escolher datas puxava o histórico inteiro pela rede.

### 5. Só folhas **aprovadas** dão recibo, e o filtro vive na consulta

`ListApprovedItemsForEmployeeAsync` filtra por `PayrollRunStatus.Approved` na
própria consulta, e não em quem a chama. Um item de rascunho é um número por
confirmar, e mostrá-lo ao próprio prometia-lhe um vencimento que ainda pode
mudar. Uma regra que vive na consulta não pode ser esquecida por um chamador
novo.

### 6. Documentos são metadados, nunca conteúdo

As duas leituras que devolvem ficheiros (`/me/documents` e o `documentId` do
recibo) dão nome, categoria, tamanho e data. **Descarregar continua a ser de
`documents`**, com a sua própria permissão — este ADR não abre uma segunda porta
para o conteúdo.

## Consequences

- **`EmployeePortal` passa a compor dois módulos.** O allowlist de
  `ProjectReferenceTests` ficou `["EmployeePortal"] = ["Hr", "Payroll"]`, e
  continua a ser só pelos contratos publicados — `documents` não aparece, porque
  `hr` já compõe os ficheiros antes de os devolver.
- **Nasceu `Rivo.Payroll.Application.Tests`**, que não existia: o módulo tinha
  testes de domínio e nenhum de aplicação. Seis, sobre o serviço novo — incluindo
  o que prova que um recibo reemitido substitui o anterior, e o que prova que o
  documento de um item não se cola a outro.
- **Oito testes novos na composição**, todos sobre a mesma coisa: que o
  identificador que chega ao módulo é o do colaborador **ligado**, nunca o da
  conta, e que sem vínculo não se lê nada.
- **`verify-employee-portal` passou de 8 casos a 19**, montados pelas rotas
  reais: RH marca a assiduidade, cria as férias, anexa o documento, abre a folha
  e leva-a pela governança toda — e o portal mostra o que é do próprio. O caso 18
  monta um **segundo** colaborador com dados seus e verifica que nenhuma das
  quatro leituras cruza os dois.
- **A suite do portal passou a convidar com `Colaborador`** onde usava `Cliente`,
  que era o mais estreito antes do ADR-061 existir.

## O que a primeira corrida de CI encontrou

Seis casos falharam na primeira volta, e vale registar o que cada um era —
porque nenhum dos três defeitos reais era visível nos 1 273 testes.

**Um defeito de produção: a consulta dos recibos não traduzia para SQL.**
`ListApprovedItemsForEmployeeAsync` projectava `ApprovedPayrollItem` e depois
pedia ao SQL Server que ordenasse pelas propriedades desse registo. O EF não
traduz isso, e a rota respondia **500 a qualquer colaborador com uma folha
aprovada** — exactamente o caso que o portal existe para servir. Os seis testes
de `PayrollSelfService` passavam, porque o duplo da persistência não é EF.
Corrigido ordenando por `r.Year`/`r.Month` antes de projectar.

**Uma armadilha do PowerShell que inventou fugas de dados.** Dois casos
acusaram o colaborador de ver dados de colegas que não existiam:

```
"[]" | ConvertFrom-Json   ->  $null
@($null).Count            ->  1     # e não 0
```

`Invoke-RestMethod` sobre `[]` devolve `$null`, e o `@(...)` que costuma garantir
um array transforma-o num array de **um** elemento. `if ($lista.Count -ne 0) {
throw }` testava o contrário do pretendido. Nenhuma das 22 suites tinha tropeçado
nisto porque nenhuma verificava uma lista **vazia** por este caminho. Resolvido
com `Get-RivoLista` em `_ambiente.ps1`, documentado onde vive.

**E uma lacuna de cobertura descoberta por acidente: `POST /hr/leave` nunca
tinha sido exercitado por suite nenhuma.** O primeiro pedido de férias da
história do projecto foi feito por esta suite — e recusou com 409, porque pedir
férias cria e submete o processo no mesmo acto, e não havia política de aprovação
para `hr.leave_request`. Não é defeito: é configuração, e o próprio servidor o
diz. Mas significa que o fluxo de férias esteve semanas sem uma única
verificação.

## Risks

- **O parâmetro ausente é uma convenção, não uma regra verificada.** Nada impede
  alguém de acrescentar um `employeeId` a `GetMyRecords` amanhã. Mitigado pelo
  comentário na classe, por este ADR, e pelo teste que falha se a leitura deixar
  de passar pelo vínculo — mas não há teste de arquitectura que proíba o
  parâmetro.
- **A janela por omissão esconde histórico.** Quem abrir o portal no dia 1 vê um
  dia. É deliberado, mas é o tipo de decisão que parece um defeito a quem não a
  conhece; o frontend tem de mostrar a janela que está a pedir.
- **Os valores do recibo vêm do item, não de um documento fiscal.** `netSalary`
  e `withholdingTax` são o que `payroll` calculou. Se o cálculo mudar
  retroactivamente, muda o que o colaborador vê de um mês fechado — e o recibo
  em PDF, se existir, passa a divergir do ecrã.

## Revisit When

- **Se o portal passar a escrever** — pedir férias, justificar uma falta,
  submeter um comprovativo —, isto volta a abrir: a escrita pode continuar a
  autorizar-se por vínculo, mas passa a precisar das regras de negócio do módulo
  dono, e a ausência de `employeeId` deixa de bastar como desenho.
- **Se surgir um chefe de equipa** que veja a assiduidade da sua equipa, «o
  próprio» deixa de ser a unidade certa e passa a ser preciso um conceito de
  âmbito, que hoje não existe (mesma nota do ADR-061).
- **Se o histórico crescer** ao ponto de a leitura dos recibos ficar lenta: hoje
  são duas consultas por abertura, e lê-se o histórico completo de cada vez.

## Related

ADR-042 (o portal autoriza por vínculo — a decisão de que esta é a continuação),
ADR-061 (o perfil `Colaborador`, vazio de propósito), ADR-041 (camadas de
composição só pelos contratos publicados), ADR-054 (ligar a conta é acto
separado), ADR-050 (o vínculo determina quem aprova).
