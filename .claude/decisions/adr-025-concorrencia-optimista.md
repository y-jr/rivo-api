# ADR-025: Concorrência Optimista — Coluna `version`

## Status

Aceite (2026-08-16)

## Context

O ADR-002 fixou "concorrência optimista: coluna `version`" e
`standards/naming.md` repetiu-o. **Nenhuma entidade a tinha.**

O desvio não foi descoberto por um defeito em produção — foi descoberto ao
escrever o ADR-019, que documentava as convenções de persistência e teve de o
registar como divergência conhecida (K14). Cinco módulos tinham sido
construídos sobre uma regra que ninguém estava a cumprir.

Até aqui não mordia: nenhum dos agregados implementados tinha contenção real
de escrita. **Deixa de ser verdade em duas frentes:**

- Já hoje, em `notifications`: o worker de entrega e o destinatário tocam na
  mesma linha ao mesmo tempo — um a marcar entrega, o outro a marcar como
  lida.
- Em `approval`, BR-17 exige explicitamente concorrência optimista nas
  decisões, e o cenário é o clássico: duas pessoas a decidir o mesmo pedido em
  simultâneo.

Implementada depois de `approval`, propagar-se-ia a todos os módulos
consumidores. Por isso é fase própria, antes.

## Requirements

- **Facto** — ADR-002 exige coluna `version`; `naming.md` fixa o nome.
- **Facto** — BR-17 exige concorrência optimista nas decisões de `approval`.
- **Facto** — Um `DbContext` por módulo (ADR-019); não há contexto partilhado
  onde pôr comportamento comum.
- **Facto** — O `Domain` não conhece framework.
- **Inferência** — Serão catorze módulos. O mecanismo tem de sobreviver a
  agregados escritos por quem não leu este ADR.

## Alternatives

1. **Coluna `version` inteira, incrementada pela infraestrutura** (escolhida).
2. `xmin` do PostgreSQL como token, via `UseXminAsConcurrencyToken()`.
3. Incremento explícito no domínio, em cada método que altera estado.
4. Bloqueio pessimista (`SELECT ... FOR UPDATE`).

A opção 2 é tecnicamente superior em duas coisas: não precisa de coluna nem de
migração de dados, e é impossível de esquecer porque o PostgreSQL a mantém
sozinho. **Foi rejeitada por o ADR-002 e o `naming.md` fixarem explicitamente
uma coluna `version`** — mudar isso seria re-litigar uma decisão aceite sem
causa forte. Fica registada aqui como a alternativa a considerar se alguma vez
houver motivo para reabrir: uma coluna visível no schema também tem valor, que
é o leitor perceber que a tabela tem controlo de concorrência sem ir ler
código.

A opção 3 é a que falha em silêncio: obriga cada método de negócio a lembrar-se
de incrementar um contador, e basta um esquecimento para a protecção
desaparecer nesse caminho sem nada avisar.

A opção 4 serializa o acesso e transfere o problema para contenção de
bloqueios. Não se justifica ao volume previsto.

## Decision

**Coluna `version` inteira, token de concorrência do EF Core, incrementada
pela infraestrutura.**

### No domínio

```csharp
public int Version { get; private set; }
```

Sem `set` público. O domínio **nunca** lhe toca.

### Na configuração do `DbContext`

```csharp
notification.Property(n => n.Version).IsConcurrencyToken();
```

O `UPDATE` passa a filtrar por `version`. Se outra transacção tiver gravado
entretanto, afecta zero linhas e o EF Core lança
`DbUpdateConcurrencyException` — em vez de sobrepor em silêncio.

Verificado directamente contra o PostgreSQL: dois `UPDATE` com a mesma versão
de partida devolvem `UPDATE 1` e `UPDATE 0`.

### O incremento vive no `SaveChangesAsync`

Cada `DbContext` faz override e sobe o `Version` de tudo o que está
`Modified`. Subir o `CurrentValue` basta: o EF Core usa o `OriginalValue` na
cláusula `WHERE`, que é o que detecta a colisão.

**Duplicado por módulo, deliberadamente.** É o mesmo compromisso que o ADR-019
já aceitou para o bloco de registo do `DbContext`: repetição em troca de
independência entre módulos. Um `DbContext` base partilhado criaria o sítio
onde todos os módulos se encontram — e o `SharedKernel` é para conceitos de
domínio, não para infraestrutura (CLAUDE.md).

### Isenções são explícitas, nunca omissões

Por omissão, **todo** o agregado precisa de contador. Três estão isentos, com
razão escrita em `ConcurrencyTokenTests.IsentosPorDesenho`:

| Agregado | Razão |
|---|---|
| `AuditEvent` | Append-only por BR-10; nunca alterado depois de escrito |
| `Position` | Sem métodos que alterem estado. **Passa a precisar** quando a marca de autoridade for editável (BR-21) |
| `EmployeeDocument` | Linha de ligação: cria-se e elimina-se, não se altera |

### Imposto por teste de arquitectura

`ConcurrencyTokenTests` falha se um agregado novo nascer sem `Version` e sem
isenção justificada, se um isento ganhar `Version` (isenção obsoleta), ou se
uma isenção deixar de corresponder a um agregado real.

Verificado por mutação: um agregado novo sem `Version` faz o teste falhar
nomeando-o.

## Consequences

Facilita:

- Duas escritas concorrentes sobre o mesmo agregado deixam de se sobrepor em
  silêncio.
- BR-17 deixa de ser um requisito sem mecanismo quando `approval` chegar.
- Um agregado novo sem contador falha o build, não a produção.

Dificulta / exige:

- Uma migração por módulo afectado, e uma coluna a mais em seis tabelas.
- Quem consome os casos de uso passa a ter de lidar com
  `DbUpdateConcurrencyException`. **Hoje ninguém a trata** — ver Riscos.
- O override de `SaveChangesAsync` repete-se por módulo.

## Risks

- **A excepção não é tratada em lado nenhum.** Uma colisão real hoje sobe até
  ao handler e devolve `500`. É melhor do que perder a escrita em silêncio,
  mas não é a resposta certa: devia ser `409 Conflict`. **Registado como K15**;
  a fase de `approval` tem de o resolver, porque é lá que a colisão passa a ser
  um caso de uso normal e não uma anomalia.
- **`SaveChanges` síncrono não incrementa.** O override cobre
  `SaveChangesAsync`, que é o único usado. Se alguém chamar a versão síncrona,
  o contador não sobe e a protecção desaparece nesse caminho.
- **Contador inteiro transborda** ao fim de ~2 mil milhões de escritas no mesmo
  registo. Não é preocupação real a esta escala.

## Revisit When

- `approval` for implementado — obriga a fechar o K15 e a decidir a semântica
  de retry.
- A marca de autoridade de `Position` passar a ser editável (BR-21) — sai da
  lista de isentos.
- Surgir motivo forte para preferir `xmin`, que dispensaria coluna e
  incremento. Reabre também o ADR-002 e o `naming.md`.

## Related

- [ADR-002](adr-002-database.md) — cumpre a exigência que estava por cumprir
- [ADR-019](adr-019-persistencia-ef-core.md) — onde o desvio ficou registado
- [ADR-024](adr-024-testes-de-arquitectura.md) — o teste que o impõe
- [state/known-issues.md](../state/known-issues.md) — K14 (fechado), K15 (novo)
