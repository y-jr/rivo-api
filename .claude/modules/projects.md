# projects — Gestão de Projectos

**Classificação:** supporting domain.

## Responsabilidade

Definir e gerir projectos, acompanhar execução, gerir marcos e tarefas, e
associar recursos e custos a projectos.

Possui o **contexto de projecto** e as suas regras. **Não possui os
recursos** — pessoas, viaturas e centros de custo pertencem aos seus donos.

## Conceitos

| Conceito | Notas |
|---|---|
| Projecto | ciclo de vida, estado |
| Marco / Tarefa | atribuição, prazos, timeline |
| Orçamento de Projecto | distinto do Orçamento de `finance` — este é a dotação do projecto |
| Alocação de Recursos | pessoas, viaturas, custos |

## Possui

Projecto, Marco, Tarefa, Orçamento de Projecto, Alocação de Recursos,
atribuição de custo ao nível do projecto.

## Depende de

`hr` (`ReferenciaColaborador` — **ligado**, 2026-08-30: a atribuição de
Tarefa verifica que o Colaborador existe antes de gravar; Alocação de
Recursos usa o mesmo contrato desde 2026-08-31), `fleet`
(`IVehicleDirectory` — **ligado**, 2026-08-31: Alocação de Recursos verifica
a Viatura antes de gravar, mesmo padrão de `hr`), `finance` (centro de
custo, custo real — **Orçamento de Projecto não liga a `finance` ainda**,
ver ADR-040: a relação existe por decisão, o mecanismo fica por desenhar),
`commercial` (facturação de projecto), `documents`, `approval`, `audit`,
`notifications`. **Custos ao nível do projecto continuam por ligar** —
postar em `finance` depende de "tempo real ou em lote?", decisão em aberto
(`state/pending-decisions.md`); ver "Não pode" abaixo.

## Consumido por

`finance` (custo de projecto, orçamentado vs. real).

## Contratos publicados

- Custo e progresso por projecto.
- Alocação activa de um recurso.

## Não pode

- Possuir o registo de Colaborador ou de Viatura — referencia-os.
- Copiar nome, departamento ou cargo de Colaborador (BR-18), nem matrícula
  ou modelo de Viatura.
- Possuir a factura gerada a partir do projecto — isso é `finance`/AR, com
  base fornecida por `commercial`.
- **Atribuir um custo directo ao projecto sem postar em `finance`** — o
  mecanismo de postagem (tempo real ou em lote) é decisão em aberto, e
  construir sem ela seria especulativo. Por isso "custos" (§Conceitos) fica
  fora da Alocação de Recursos implementada a 2026-08-31 — só Colaborador e
  Viatura.

## Regras de negócio

- Alocações referenciam recursos por id; atributos lêem-se por contrato.
- Custos de projecto são postados em `finance`; `projects` não escreve no
  razão.
- **Alocação de Recursos, ao nível do projecto, é distinta da atribuição de
  Tarefa** (operacional, "quem faz isto até quando"). Uma pessoa pode estar
  alocada ao projecto sem ter Tarefas; uma Tarefa pode ser atribuída a
  alguém que não está alocado. Os dois não têm relação estrutural.
- Uma Alocação referencia o recurso (Colaborador ou Viatura) só por
  identificador (ADR-010); a Application verifica que existe no módulo
  dono (`hr` ou `fleet`) antes de gravar.
- O mesmo recurso (mesmo par tipo+identificador) não se aloca duas vezes em
  aberto ao mesmo projecto — termina a alocação actual antes de alocar de
  novo. Mesma leitura de `Vehicle.Assign` em `fleet` (uma viatura, um
  motorista de cada vez).
- Uma Alocação não pode começar antes do início do Projecto, nem terminar
  antes de começar. Nem alocar nem terminar é possível depois de o Projecto
  fechar (mesma leitura de Marco, Tarefa e Orçamento).
- Terminar uma Alocação já terminada é recusado — não há reabertura.
- Marco e Tarefa pertencem ao agregado Projecto — nascem, alteram-se e
  desaparecem com ele, nunca de forma independente.
- Nem Marco nem Tarefa se acrescentam ou alteram depois de o projecto fechar:
  fechado é facto histórico (mesma leitura que impede reabrir o Projecto).
- A data alvo de um Marco e o prazo de uma Tarefa não podem ser anteriores ao
  início do Projecto.
- Um Marco alcançado não volta a "por alcançar" — é facto histórico, como o
  fecho do Projecto.
- Uma Tarefa atribuída referencia o Colaborador só por identificador
  (ADR-010); a Application verifica que existe em `hr` antes de gravar, e
  nunca copia nome, departamento ou cargo (BR-18).
- Uma Tarefa concluída ou cancelada não se reabre, não se reatribui, nem se
  cancela ou conclui outra vez — são estados finais.
- Cancelar uma Tarefa nunca a elimina (BR-14): fica como facto histórico.
- Orçamento pertence ao agregado Projecto, **zero ou um por projecto** — ao
  contrário de Marco e Tarefa, não há "vários orçamentos", há um, revisto ao
  longo do tempo.
- A moeda do Orçamento fixa-se na primeira vez que se define — uma revisão
  para outra moeda é recusada, não convertida: decidir a taxa de câmbio não
  é decisão deste método.
- Nem definir nem rever o Orçamento é possível depois de o Projecto fechar
  (mesma leitura de Marco e Tarefa).

## Perguntas em aberto

- Relação com `procurement`: requisições geradas por projecto são
  dependência directa ou baseada em eventos?

**Resolvida por ADR-040** (2026-08-30): o Orçamento de Projecto e o Orçamento
por centro de custo de `finance` são entidades distintas, relacionadas — uma
despesa de projecto há-de ser validada contra o disponível de `finance` sem
duplicar a entidade. O mecanismo concreto da validação cruzada fica por
desenhar quando o Orçamento de Projecto tiver código. Detalhe em
[decisions/adr-040](../decisions/adr-040-orcamento-de-projecto-vs-orcamento-financeiro.md).

## Estado

**Marco, Tarefa, Orçamento e Alocação de Recursos, com regra de negócio
real — 2026-08-30/31.** `Project` (nome, datas, estado) nasceu esqueleto a
2026-08-29; ganhou Marco (data alvo, alcançar uma vez), Tarefa (título,
prazo, atribuição a Colaborador verificada contra `hr`, concluir/cancelar
sem reabrir), Orçamento (valor e moeda, zero ou um por projecto, moeda fixa
na primeira vez) e, no dia seguinte, Alocação de Recursos — tudo parte do
mesmo agregado: nasce, altera-se e desaparece só com o Projecto, e nada se
acrescenta nem altera depois de fechado.

**Alocação de Recursos** (`ProjectResourceAllocation`) — Colaborador ou
Viatura, com data de início e fim opcional; mesmo desenho de
`Rivo.Fleet.Domain.VehicleAssignment` (`Assign`/`End`), com a diferença de
que um projecto tem vários recursos alocados em simultâneo — ao contrário
de uma viatura, que só tem um motorista de cada vez. `fleet` ganhou o seu
primeiro contrato de leitura publicado, `IVehicleDirectory`, mesmo padrão
de `IEmployeeDirectory` em `hr`, para `projects` poder verificar a Viatura
sem lhe possuir o registo. **Custos ficam de fora, de propósito** — ver
"Não pode".

55 testes de domínio (`Rivo.Projects.Domain.Tests`, 39 + 16 de
`ProjectResourceAllocationTests`); verificação end-to-end em
`scripts/verify-projects.ps1` — **43/43 confirmados contra a stack local a
2026-08-31**, sem nenhuma falha depois de corrigido um erro na própria
suite (contagem de eventos auditados). Permissões atribuídas a
`ProjectManager`, que deixou de estar vazio a 2026-08-29.
