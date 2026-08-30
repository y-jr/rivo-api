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

`hr` (`ReferenciaColaborador` do motorista — **ligado**, 2026-08-30: a
atribuição verifica que o Colaborador existe antes de gravar), `finance`
(centro de custo, postagem de custos), `inventory` (peças e consumíveis, se
geridos como inventário geral), `documents`, `audit`, `notifications`. As
direcções por ligar pertencem ao Plano de Manutenção, Registo de Viagem,
Despesa de Frota e Seguros, que ainda não estão feitos.

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
- Manutenção e Atribuição pertencem ao agregado Viatura — nascem, alteram-se
  e desaparecem com ela, nunca de forma independente.
- Só um registo de manutenção aberto de cada vez por viatura, e só uma
  atribuição aberta de cada vez — reatribuir exige terminar a actual primeiro,
  nunca a substitui em silêncio.
- Manutenção e Atribuição não se excluem: uma viatura pode estar atribuída e
  ir para revisão sem que isso desatribua ninguém.
- Uma viatura inactiva não aceita nova manutenção nem nova atribuição.
- Uma Atribuição referencia o Colaborador só por identificador (ADR-010); a
  Application verifica que existe em `hr` antes de gravar, e nunca copia
  nome, departamento ou cargo (BR-18).

## Perguntas em aberto

- Peças e consumíveis: stock próprio de `fleet` ou itens de `inventory`?
  `docs` deixa em aberto.

## Estado

**Manutenção e Atribuição, com regra de negócio real — 2026-08-30.**
`Vehicle` (matrícula única, modelo, estado Active/InMaintenance/Inactive)
nasceu esqueleto a 2026-08-29; ganhou Manutenção (registo histórico, um
aberto de cada vez) e Atribuição (motorista, verificado contra `hr`, uma
aberta de cada vez) como parte do mesmo agregado. 25 testes de domínio
(`Rivo.Fleet.Domain.Tests`); `scripts/verify-fleet.ps1` — **26/26 confirmados
contra a stack local a 2026-08-30**, sem nenhuma falha.

⚠ **Continuam por fazer:** Plano de Manutenção (calendário preventivo com
alertas), Registo de Viagem, Despesa de Frota, Seguros. Permissões
atribuídas a `AssetManager`, que deixou de estar vazio a 2026-08-29.
