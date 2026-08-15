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

`hr` (`ReferenciaColaborador` para alocação), `finance` (centro de custo,
custo real), `commercial` (facturação de projecto), `fleet` (alocação de
viatura), `documents`, `approval`, `audit`, `notifications`.

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

## Perguntas em aberto

- Relação com `procurement`: requisições geradas por projecto são
  dependência directa ou baseada em eventos?
- O Orçamento de Projecto relaciona-se com o Orçamento por centro de custo
  de `finance`, ou é independente?

## Estado

Não iniciado.
