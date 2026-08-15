# fleet — Gestão de Frota

**Classificação:** supporting domain. Auto-contido, baixo acoplamento.

## Responsabilidade

Viaturas da empresa, atribuições, manutenção, combustível e utilização.

Gere o ciclo operacional das viaturas. **Não possui** os registos de
colaborador nem os organizacionais que referencia.

## Conceitos

| Conceito | Notas |
|---|---|
| Viatura | dados técnicos, documentação legal, seguros |
| Registo de Manutenção / Plano | preventiva e correctiva, com alertas |
| Atribuição de Viatura | viatura ↔ colaborador/motorista; viatura ↔ projecto |
| Registo de Viagem | quilometragem |
| Despesa de Frota | combustível, portagens, estacionamento |

## Possui

Viatura, Manutenção, Plano de Manutenção, Atribuição, Registo de Viagem,
Despesa de Frota, Seguros e documentação legal (ficheiros via `documents`).

## Depende de

`hr` (`ReferenciaColaborador` do motorista), `finance` (centro de custo,
postagem de custos), `inventory` (peças e consumíveis, se geridos como
inventário geral), `documents`, `audit`, `notifications`.

## Consumido por

`finance` (custos de manutenção e combustível), `projects` (alocação de
viatura).

## Contratos publicados

- Disponibilidade e atribuição de viatura.
- Custo de frota por período e centro de custo.

## Não pode

- Possuir informação de colaborador. Referencia por `colaborador_id` e lê
  atributos pelo contrato de `hr` (BR-18).
- Postar directamente no razão — publica custos para `finance`.

## Regras de negócio

- FK para `hr.colaborador(id)` permitida apenas para a chave primária
  (ADR-010); sem `JOIN` a outras tabelas de `hr`.

## Perguntas em aberto

- Peças e consumíveis: stock próprio de `fleet` ou itens de `inventory`?
  `docs` deixa em aberto.

## Estado

Não iniciado.
