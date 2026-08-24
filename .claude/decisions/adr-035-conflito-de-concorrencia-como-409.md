# ADR-035: Conflito de Concorrência Traduzido no Composition Root

## Status

Aceite (2026-08-24).

**Completa o [ADR-025](adr-025-concorrencia-optimista.md)**, que fez metade do
trabalho. Não substitui nada. Fecha o **K15**.

## Context

O ADR-025 pôs `Version` nos agregados e marcou-a como token de concorrência.
A partir daí, uma escrita sobre uma versão desactualizada deixou de sobrepor em
silêncio e passou a lançar `DbUpdateConcurrencyException`.

Ficou por fazer a outra metade: **nenhum handler a tratava.** A excepção subia
até ao topo do pipeline e o cliente recebia `500 Internal Server Error`.

Semanticamente errado, e não por detalhe: `500` diz "o servidor avariou, não
sabemos porquê, não tente outra vez". A verdade é o oposto — o servidor
funcionou perfeitamente, recusou uma escrita por a base ter mudado, e o
chamador **pode e deve** reler e repetir. Um cliente que trate `500` como
irrecuperável desiste de uma operação que era recuperável.

`standards/error-handling.md` já dizia o que fazer: *"Conflitos de concorrência
optimista (BR-17) são violação de regra de domínio, não falha técnica. Devem
produzir uma resposta que o chamador possa tratar."* Faltava o código.

O `roadmap-execucao.md` atribui explicitamente este item à fase de `approval`,
com a razão certa: é lá que a colisão deixa de ser anomalia e passa a caso de
uso normal — duas pessoas a decidir o mesmo pedido é o comportamento esperado
de uma caixa de entrada de aprovações, não um acidente.

## Requirements

- **Facto** — BR-17 exige concorrência optimista nas decisões
  (`modules/approval.md`).
- **Facto** — Nenhuma camada Application referencia o EF Core. Verificado nos
  sete `.csproj`: só `Domain`, `Contracts` e contratos de outros módulos.
- **Facto** — `standards/error-handling.md` classifica a colisão como violação
  de regra de domínio e proíbe revelar detalhe interno ao cliente.
- **Facto** — Não existe projecto `SharedKernel` na solução. `src/` tem
  `Modules` e `Rivo.Api`, e mais nada.
- **Inferência** — A mensagem de `DbUpdateConcurrencyException` nomeia tabela e
  tipo de entidade; devolvê-la ao cliente expõe o esquema.

## Constraints

- As regras de dependência (`architecture/dependency-rules.md`) impedem
  Application de conhecer infraestrutura. Não é preferência estilística — é o
  que os testes de arquitectura impõem.
- O CLAUDE.md manda manter o SharedKernel mínimo, e o projecto foi até ao ponto
  de não ter nenhum. Criar um para isto é decisão de peso, não um detalhe.

## Alternatives

### A. Cada Application apanha e traduz

Rejeitada por impossibilidade, não por gosto: Application não referencia o EF
Core. Só funcionaria acrescentando essa referência aos sete projectos — que é
exactamente o acoplamento que as regras de dependência existem para impedir.

### B. Cada Infrastructure apanha e relança uma excepção própria

Funciona, e respeita as camadas. Custa: um tipo de excepção partilhado, e
portanto um `SharedKernel` novo — ou a repetição do mesmo tipo em seis módulos,
que o handler do host teria depois de conhecer um a um.

E tem um modo de falha desagradável: **um módulo novo que se esqueça de apanhar
volta a devolver `500`**, e ninguém dá por isso até acontecer em produção.

### C. Traduzir no composition root, com `IExceptionHandler`

O host já referencia todas as `Infrastructure`, logo já conhece o EF Core — não
é acoplamento novo, é acoplamento que já lá estava e que é a própria função do
composition root. Registado uma vez, aplica-se aos seis módulos, e um módulo
novo herda-o sem fazer nada.

### D. Repetir automaticamente a operação no servidor

Rejeitada, e é a mais tentadora. Repetir sozinho uma decisão de aprovação
aplicá-la-ia sobre um estado que o autor da decisão não chegou a ver — que é
**exactamente o que BR-17 existe para impedir**. Automatizar a repetição aqui
desfaz a regra em nome da conveniência.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| B | Fronteira formalmente limpa; o tipo de excepção é contrato do módulo | SharedKernel novo, ou seis tipos; e o esquecimento silencioso |
| C | Um sítio, seis módulos, zero esquecimentos possíveis | O host conhece um tipo do EF Core |

Escolhe-se C. O que se perde é nominal — o host já compila contra o EF Core há
muito. O que B perdia era real: a garantia de que a regra se aplica a módulos
que ainda não existem.

## Decision

**`DbUpdateConcurrencyException` é traduzida em `409 Conflict` por um
`IExceptionHandler` registado no composition root**
(`src/Rivo.Api/Errors/ConcurrencyConflictHandler.cs`).

Quatro pontos fixados:

1. **Só a concorrência.** `DbUpdateException` genérica continua a dar `500`.
   Alargar esconderia violações de chave e de restrição, que são defeitos e
   devem falhar ruidosamente.
2. **Sem detalhe interno na resposta.** O tipo da entidade vai para o log, a
   nível `Warning`; o corpo é `ProblemDetails` com "o registo foi alterado
   entretanto, recarregue e repita".
3. **Sem repetição automática.** Quem chama relê e decide de novo (alternativa
   D).
4. **Primeiro middleware do pipeline**, para envolver também a autenticação —
   que escreve a sessão em base de dados e é, ela própria, uma escrita capaz de
   colidir.

## Consequences

**Mais fácil:** um cliente distingue "tente outra vez" de "isto avariou". A
caixa de entrada de aprovações pode reagir ao `409` recarregando o pedido e
mostrando a decisão que chegou primeiro — que é a interacção correcta, e era
impossível de construir contra um `500`.

**Mais difícil:** o host ganhou um `using Microsoft.EntityFrameworkCore`. É o
primeiro conhecimento de persistência em `Rivo.Api` fora do arranque, e vale a
pena vigiar que não cresça.

**Novo:** `tests/Rivo.Api.Tests`, o primeiro projecto de teste da camada API do
host. Existe porque este comportamento não é testável em nenhum dos que havia —
os de domínio não conhecem HTTP, e os de arquitectura verificam forma e não
comportamento.

## Risks

- **Um `409` mascarar um defeito real.** Se um agregado colidir consigo próprio
  por erro de mapeamento, o cliente vê `409` e presume contenção. Detecta-se
  pelo log: `Warning` com o tipo da entidade, e uma sequência sobre o mesmo
  agregado sem utilizadores concorrentes é o sinal.
- **A regra é invisível no código do módulo.** Quem lê `ManageApprovals` não vê
  onde a colisão é tratada. Mitigado pelo comentário no ponto de registo e por
  este ADR; se um dia houver mais do que uma tradução deste género, passa a
  valer a pena um ficheiro só de mapeamentos.
