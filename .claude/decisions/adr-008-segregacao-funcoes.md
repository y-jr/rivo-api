# ADR-008: Segregação de Funções — Invariante no Domínio, RLS como Defesa em Profundidade

## Status

Aceite (resolução R2)

> **Nota (2026-08-10):** SQL Server e MySQL foram avaliados e PostgreSQL foi
> mantido (ADR-002). Isto é relevante para esta decisão: **MySQL não tem
> RLS**, o que eliminaria a regra 2 abaixo e deixaria o domínio como única
> sede da invariante. A escolha de PostgreSQL preserva a defesa em
> profundidade que esta decisão pressupõe.

## Context

`docs/rivo-dados-integracoes-seguranca-v1.md` v1 continha uma contradição
interna:

- §3.2: segregação de funções é "regra de código no domínio `approval`, não
  configuração alterável por administrador".
- §3.6: "a segregação de funções deve ser tecnicamente imposta ao nível dos
  dados (RLS por atribuição de aprovação)".

Não ficava definido onde vive a invariante. Sem essa definição, o risco real
é a regra acabar duplicada e divergente — mantida em dois sítios que se
desalinham silenciosamente.

## Requirements

- **Facto** — SGAP: "tentativas não autorizadas devem ser bloqueadas
  tecnicamente"; quem submete não valida, quem valida não aprova, quem
  aprova não paga.
- **Facto** — No protótipo, a política de escrita em tabelas de aprovação
  era "qualquer membro autenticado", com a verificação real só no frontend.
- **Facto** — Sem multi-tenancy (ADR-003), a autorização é a primeira linha
  de defesa; não há fronteira de tenant a compensar falhas.

## Constraints

Invariantes de domínio têm de ser testáveis ao nível do domínio, sem base de
dados ([standards/testing.md](../standards/testing.md)).

## Alternatives

1. **Domínio como fonte de verdade; RLS como segunda linha.**
2. RLS como sede da regra.
3. Apenas domínio, sem RLS.

A opção 2 falha em vários eixos: regras como alçadas por cargo ou
anti-fraccionamento exigem contexto que uma política SQL exprime mal; não é
testável ao nível do domínio; e torna a regra invisível no código.

A opção 3 é aceitável mas desperdiça uma protecção barata — sobretudo dado
que já não existe fronteira de tenant (ADR-003).

## Trade-offs

Manter as duas camadas exige mantê-las alinhadas. O risco de divergência é
real, e é por isso que a regra 3 abaixo é vinculativa.

## Decision

Hierarquia explícita, sem ambiguidade:

1. **O domínio `approval` é a fonte de verdade** de toda a regra de
   segregação de funções. É lá que a invariante é expressa, testada e
   imposta.
2. **RLS é segunda linha de defesa**, não a regra. Existe para que um erro
   na camada de aplicação não permita escrever uma decisão que não pertence
   ao autor.
3. **Nenhuma regra de negócio pode existir apenas em RLS.** Toda a política
   RLS tem de reflectir uma invariante já expressa e testada no domínio.
   **Uma regra que só existe em SQL é um defeito de arquitectura.**

A segregação é regra de **código**, não configuração alterável por
administrador. Mínimo garantido: quem submete um pedido nunca decide sobre
ele.

O requisito de "bloqueio técnico" fica satisfeito pela imposição no
servidor. RLS acrescenta profundidade — não substitui.

## Consequences

Facilita:

- Invariantes testáveis em testes de domínio rápidos, sem base de dados.
- Regra visível e revisível no código, não escondida numa migração.
- Protecção adicional ao nível dos dados sem ambiguidade sobre quem manda.

Dificulta / exige:

- Duas camadas a manter alinhadas.
- Testes de integração que verifiquem que a política RLS faz o que diz — em
  **acréscimo** ao teste de domínio, nunca em substituição.

## Risks

- **Divergência entre domínio e RLS.** É o risco central. Mitigação: a
  regra 3 torna a RLS derivada por definição; qualquer política RLS sem
  invariante de domínio correspondente é tratada como defeito em revisão.
- **Falsa sensação de segurança** — assumir que RLS cobre o que o domínio
  não cobre. Mitigação: esta ADR e
  [standards/persistence.md](../standards/persistence.md).

## Revisit When

- Surgir requisito de acesso directo à base de dados por ferramentas
  externas que contornem a aplicação — nesse caso RLS deixa de ser segunda
  linha e passa a ser fronteira primária, o que muda o cálculo.

## Related

- `docs/rivo-arquitetura-global-v1.md` §10 (R2),
  `…seguranca-v1.md` §1.1, §3.2, §3.6
- [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-007](adr-007-approval-supporting-domain.md)
- [modules/approval.md](../modules/approval.md),
  [standards/security.md](../standards/security.md)
