# ADR-057: Nenhum acto declara quem o pratica

## Status

Aceite (2026-09-05). **Alteração de contrato em sete rotas.** Os campos que
nomeavam o autor de um acto deixaram de ser aceites; declará-los passa a dar
`400`.

Decisão do utilizador, perante a escolha entre corrigir só o pagamento ou todos
os campos da mesma família: «todos os campos de uma vez».

## Context

O ADR-050 fixou a regra para as decisões de aprovação: **quem decide resolve-se
da conta autenticada, dentro do caso de uso**, e nunca de um identificador que o
cliente escreva no corpo. O argumento era que um campo declarado é uma
declaração sob palavra do cliente, e o servidor não tem como a verificar.

O que o ADR-050 não fez foi procurar a mesma forma noutros sítios. Ficou por
verificar se havia mais actos a aceitar o autor por palavra.

Havia sete.

### A prova, e não a suspeita

O primeiro sítio examinado foi a execução de pagamento — o acto com mais
consequência de todos, e o único protegido por três regras de negócio (BR-1
sem aprovação não se paga, BR-3 quem aprova não paga, BR-5 a aprovação
revalida-se na execução).

Contra a stack a correr, autenticado como `Admin` — uma conta **sem colaborador
associado** — executei um pagamento de **100 000 AOA** declarando
`executedByEmployeeId` de um colaborador sem nenhuma relação com a conta. O
servidor aceitou: `200`, dinheiro movido, e a trilha de auditoria a atribuir a
execução a essa pessoa.

Isto é pior do que uma falha de autorização. As três regras **funcionaram** —
BR-3 comparou o aprovador com o identificador declarado e não encontrou
conflito, porque o identificador declarado não era o de quem estava a agir. A
segregação de funções não foi contornada: foi alimentada com dados falsos e
respondeu correctamente àquilo que lhe foi dito.

**Facto:** a trilha de auditoria, que existe para reconstruir quem fez o quê,
registava a pessoa que o executante escolhesse nomear.

## Decision

Nenhum caso de uso aceita, no corpo do pedido, o identificador de quem pratica
o acto. O autor resolve-se **sempre** da conta autenticada, através de
`IEmployeeDirectory.FindByUserIdAsync`, **dentro do caso de uso** — nunca no
endpoint.

A colocação importa e é o ponto do ADR-050 que aqui se repete: se a resolução
ficasse no endpoint, o caso de uso continuaria a ter um caminho que aceita um
autor arbitrário, e bastaria um segundo endpoint, um teste ou um comando para
o usar. Dentro do caso de uso, esse caminho não existe.

O endpoint faz duas coisas, e só duas: recusa o campo obsoleto com `400`, e
extrai `context.ActorId`, devolvendo `403` se a sessão não o tiver.

### Os sete sítios

| Módulo | Acto | Campo removido |
|---|---|---|
| `finance` | executar pagamento | `executedByEmployeeId` |
| `finance` | pedir pagamento | `requestedByEmployeeId` |
| `finance` | fechar período contabilístico | `closedByEmployeeId` |
| `finance` | aprovar orçamento | `approvedByEmployeeId` |
| `procurement` | abrir requisição | `requestedByEmployeeId` |
| `procurement` | registar recepção | `receivedByEmployeeId` |
| `payroll` | abrir folha | `openedByEmployeeId` |

Dois destes já liam o colaborador pelo contrato de `hr` — só o liam **pelo
identificador declarado** (`FindAsync`) em vez de pela conta
(`FindByUserIdAsync`). A verificação existia e verificava a coisa errada:
confirmava que a pessoa nomeada existe, nunca que é quem está a agir.

### Conta sem colaborador não age

Uma conta autenticada mas sem vínculo recebe `403` com uma mensagem que o diz:

> Esta conta não está associada a nenhum colaborador, e só um colaborador paga.

É a consequência directa e desejada do ADR-051 e do ADR-054: o vínculo
conta↔colaborador é um acto próprio, com permissão própria, e é ele que
autoriza a agir em nome de uma pessoa.

### O campo é recusado, não ignorado

Mesmo raciocínio do ADR-054, e pela mesma razão técnica: sem
`UnmappedMemberHandling` configurado, o `System.Text.Json` descartaria o campo
**em silêncio**. Um cliente que continuasse a enviar `executedByEmployeeId`
receberia `200` e ficaria convencido de que tinha atribuído o pagamento a
alguém.

Os campos continuam declarados nos DTOs como `Guid?`, e só para poderem ser
recusados com `400` e uma mensagem que explica de onde vem agora o autor. São
peças de transição.

Em `LedgerEndpoints`, onde o corpo do pedido passou a não carregar nada de
essencial, tornou-se **opcional** (`ClosePeriodRequest? request`): fechar um
período deixou de precisar de corpo, mas quem ainda o envie com o campo antigo
recebe o `400`.

## Consequences

### Duas dependências novas de módulo

`finance` e `payroll` passaram a referenciar `Rivo.Hr.Contracts`.

Não é acoplamento novo de domínio: é a mesma leitura estreita que
`procurement` e `approval` já faziam, e o ADR-017 garante que não há ciclo
possível — os `Contracts` não dependem de nada.

A tabela declarada em `ProjectReferenceTests` foi actualizada, e o comentário de
`finance` **reescrito**. O anterior documentava a suposição que abriu o buraco:
que quem executa um pagamento chega por identificador simples e não precisa do
contrato de `hr`. Deixar o comentário de pé teria deixado o argumento errado
por escrito, ao lado da correcção.

### As suites deixaram de poder fingir

Cinco suites de verificação usavam contas de perfil — autenticadas, com
permissões, **sem colaborador associado** — e declaravam o autor no corpo. Cada
uma passou a criar contas ligadas com `New-RivoColaboradorComConta`.

Isto é a decisão a funcionar. Uma suite que não consiga arranjar um colaborador
para agir está a descrever exactamente o que a aplicação agora recusa.

Dois casos mudaram de assunto, e ambos ficaram mais úteis:

- **`verify-payables` caso 11** verificava BR-3 com um identificador declarado.
  Passou a executar o pagamento **com a conta do próprio aprovador** — a
  segregação de funções verificada contra quem está mesmo a agir, e não contra
  um nome escrito no corpo. É a diferença entre testar a regra e testar o que
  se disse à regra.
- **`verify-procurement` caso 10** verificava que um requisitante inexistente
  dava `404`. Passou a verificar a condição equivalente e mais forte: uma conta
  sem colaborador associado não abre requisição nenhuma — e declarar o campo
  obsoleto dá `400`.

`verify-approval` perdeu uma conta inteira: a conta de perfil RH que abria a
folha declarando o autor não tinha mais nada que só ela pudesse fazer.

### O que continua a comparar identificadores

Os campos permanecem nas **respostas** — `requestedByEmployeeId`,
`executedByEmployeeId`, `decidedByEmployeeId`. São o resultado da resolução, não
a sua entrada, e é deles que vivem as asserções de `verify-hr` e
`verify-payables` sobre quem ficou registado. O frontend lê-os; não os envia.

### Custo aceite

`FakeEmployeeDirectory`, nos testes de `finance` e `procurement`, mapeia
`userId → colaborador com o mesmo Id`. É identidade deliberada: mantém válidas
as asserções anteriores sobre BR-3, que comparam identificadores entre si.

O preço é que os testes de unidade **não distinguem** conta de colaborador — a
distinção que este ADR introduz não é exercitada por eles. É por isso que a
prova está nas suites end-to-end, contra a stack, com contas reais: `403` para a
conta sem vínculo, `400` para quem declara o campo.
