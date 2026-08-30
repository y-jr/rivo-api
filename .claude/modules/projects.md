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

`hr` (`ReferenciaColaborador` para alocação — **ligado**, 2026-08-30: a
atribuição de Tarefa verifica que o Colaborador existe antes de gravar),
`finance` (centro de custo, custo real), `commercial` (facturação de
projecto), `fleet` (alocação de viatura), `documents`, `approval`, `audit`,
`notifications`. As direcções por ligar pertencem a Orçamento de Projecto e
Alocação de Recursos (pessoas além da atribuição de Tarefa, viaturas,
custos), que ainda não estão feitos.

## Consumido por

`finance` (custo de projecto, orçamentado vs. real).

## Contratos publicados

- Custo e progresso por projecto.
- Alocação activa de um recurso.

## Não pode

- Possuir o registo de Colaborador ou de Viatura — referencia-os.
- Copiar nome, departamento ou cargo de Colaborador (BR-18).
- Possuir a factura gerada a partir do projecto — isso é `finance`/AR, com
  base fornecida por `commercial`.

## Regras de negócio

- Alocações referenciam recursos por id; atributos lêem-se por contrato.
- Custos de projecto são postados em `finance`; `projects` não escreve no
  razão.
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

**Marco e Tarefa, com regra de negócio real — 2026-08-30.** `Project`
(nome, datas, estado) nasceu esqueleto a 2026-08-29; ganhou Marco (data alvo,
alcançar uma vez) e Tarefa (título, prazo, atribuição a Colaborador
verificada contra `hr`, concluir/cancelar sem reabrir) como parte do mesmo
agregado — nascem, alteram-se e desaparecem só com o Projecto, e nada se
acrescenta depois de fechado. 29 testes de domínio
(`Rivo.Projects.Domain.Tests`); verificação end-to-end em
`scripts/verify-projects.ps1` — **28/28 confirmados contra a stack local a
2026-08-30**, sem nenhuma falha, primeira corrida.

⚠ **Continuam por fazer:** Orçamento de Projecto — **desbloqueado por
ADR-040** (2026-08-30), ainda por implementar — e Alocação de Recursos
(pessoas além da atribuição de Tarefa, viaturas, custos), essa sem decisão
própria ainda. Permissões atribuídas a `ProjectManager`, que deixou de estar
vazio a 2026-08-29.
