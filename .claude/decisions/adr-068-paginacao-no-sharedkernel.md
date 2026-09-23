# ADR-068: Paginação via SharedKernel, aditiva e opt-in

## Status

Aceite

## Context

Item #10 do levantamento de pendências backend: as listagens da API
ignoram `page`/`pageSize`, e algumas (lançamentos contabilísticos,
movimentos de stock) crescem sem limite natural — devolvê-las por inteiro
não escala.

Corrigir isto significa tocar `Store`, `Application` e `Api` de
praticamente todos os módulos (55 casos de uso `List*` confirmados). É a
primeira vez que um conceito genuinamente sem dono de negócio — "a página
pedida por um cliente" — precisa de existir em mais do que um módulo ao
mesmo tempo, e `domain/shared-concepts.md` está vazio: nada tinha sido
justificado para o SharedKernel até agora.

## Requirements

- **Facto**: `docs/rivo-arquitetura-global-v1.md` não define paginação —
  é lacuna, não decisão anterior a rever.
- **Facto**: existem consumidores actuais (frontend) de praticamente todas
  as listagens, que hoje recebem um array simples.
- **Inferência**: mudar a forma da resposta para todos ao mesmo tempo
  exigiria coordenar essa mudança no frontend antes de este PR poder ir
  para produção — risco desnecessário para um item de "roadmap", não de
  blocker.

## Constraints

- Sem sessão de Docker disponível para correr a app e confirmar o
  `/openapi/v1.json` ou o comportamento ao vivo — a prova fica pelos
  testes de aplicação/domínio e, onde existir, pelos `verify-*.ps1`
  (que não correm nesta sessão, mas ficam correctos para a próxima).
- 55 casos de uso, mais os `Store` e `Endpoints` correspondentes — âmbito
  grande de mais para uma só pessoa rever num só PR; sai em vários,
  agrupados por módulo.

## Alternatives

1. **Duplicar `PageRequest` em cada módulo.** `shared-concepts.md` prefere
   isto por omissão ("na dúvida, deixar duplicado"). Rejeitado aqui porque
   os três critérios de justificação estão todos satisfeitos (primitiva
   estrutural, ≥2 módulos — na prática, quase todos —, sem dono natural), e
   duplicar uma tupla de paginação por 15 módulos é o oposto de barato: é
   15 sítios para divergir em nome de campo, limite máximo ou regra de
   validação.
2. **Envelope de resposta sempre presente** (`{ items, page, total }` em
   vez de array simples), mesmo sem `page`/`pageSize`. Rejeitado: muda o
   contrato de todas as listagens de uma vez, para todos os consumidores,
   mesmo os que nunca vão precisar de paginação (catálogos pequenos como
   contas ou fornecedores).
3. **Cortar em memória** depois de ler tudo da base, sem tocar a query da
   `Store`. Mais barato de implementar, mas não resolve a razão original
   do item #10 — as tabelas que crescem sem limite continuam a ser lidas
   por inteiro antes do corte. Rejeitado por decisão explícita do
   utilizador (paginação real em todas, Skip/Take na query).

## Trade-offs

A opção aditiva (resposta continua array simples sem `page`/`pageSize`,
passa a paginar só quando pedido) não resolve o caso de um cliente que
**precisa** de paginação mas não sabe pedir — mas nenhum cliente actual
está nessa posição (todos leem hoje o array inteiro), por isso não há
comportamento existente a proteger para além do que já existe.

## Decision

1. `Rivo.SharedKernel` (projecto novo, sem dependências) ganha
   `PageRequest` (`Page`, `PageSize`) e `Pagination.TryParse(page,
   pageSize, out pedido, out erro)`.
2. **Aditivo por desenho**: sem `page` nem `pageSize` no pedido, a
   listagem devolve tudo, exactamente como hoje — zero mudança de
   contrato para quem já consome. Com os dois, a query da `Store` aplica
   `Skip`/`Take` (nunca corte em memória), e o endpoint acrescenta os
   cabeçalhos `X-Page`, `X-Page-Size` e `X-Total-Count` — o corpo
   continua a ser o mesmo array de sempre, nunca um envelope.
3. Cada listagem exige `OrderBy` determinístico antes do `Skip`/`Take` —
   onde a query já o tem (a maioria), mantém-se; onde não tem, acrescenta-se
   como parte da mesma mudança (paginar sem ordem determinística é um bug
   à espera de acontecer, não uma simplificação).
4. `pageSize` tem tecto de 200 (`Pagination.MaxPageSize`) — sem tecto, um
   cliente mal-intencionado ou com um bug pede `pageSize=1000000` e
   recria exactamente o problema que a paginação existe para resolver.

## Consequences

Fica mais fácil acrescentar paginação a uma listagem nova — é `Pagination
.TryParse` mais dois parâmetros na assinatura da `Store`, não uma decisão
de desenho outra vez. Fica mais difícil justificar uma **segunda**
excepção ao SharedKernel mínimo sem passar pelos mesmos três critérios:
esta entrada não abre precedente automático para outras.

## Risks

Um `OrderBy` acrescentado a uma query que não o tinha pode mudar a ordem
percebida por quem já consome essa listagem sem pedir paginação (ex.: uma
UI que assumia "a ordem que a base devolve"). Mitigação: preferir a
ordem que a UI já parece assumir (normalmente, mais recente primeiro ou
por código), documentada no commit de cada listagem alterada.

## Revisit When

Se uma segunda primitiva sem dono precisar do SharedKernel, revalidar que
os mesmos três critérios se aplicam antes de a acrescentar — não assumir
que, por já lá estar `PageRequest`, o SharedKernel deixou de precisar de
justificação por adição.

## Related

`domain/shared-concepts.md` (critérios de justificação), item #10 do
levantamento de pendências backend, ADR-002 (identificador substituto,
outro candidato a primitiva estrutural ainda não justificado).
