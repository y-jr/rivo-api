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
atribuição verifica que o Colaborador existe antes de gravar; 2026-08-31: o
Registo de Viagem também, quando o motorista é indicado — opcional, ao
contrário da Atribuição), `documents` (Seguros e documentação legal —
**ligado**, 2026-08-31, mesmo desenho ADR-009 de `hr`), `finance` (centro de
custo, postagem de custos), `inventory` (peças e consumíveis, se geridos como
inventário geral), `audit`, `notifications`. As direcções por ligar
pertencem à postagem de Despesa de Frota em `finance` — facto operacional
por agora, sem publicar (ver "Regras de negócio").

⚠ **`notifications` não está ligado, apesar de o Plano de Manutenção ter
"alertas".** `INotifier.QueueAsync` entrega a um `RecipientUserId` de
`identity`; não há forma de resolver "todos os `AssetManager`" para um
destinatário concreto — essa capacidade não existe em `identity` ainda, e
inventá-la aqui seria adivinhar uma peça de outro módulo. O alerta
implementado é uma consulta (`GET /fleet/maintenance-plans/due`), não uma
notificação empurrada. Ver "Estado".

## Consumido por

`finance` (custos de manutenção e combustível), `projects` (Alocação de
Recursos — **ligado**, 2026-08-31, via `IVehicleDirectory`).

## Contratos publicados

- **`IVehicleDirectory`** — leitura de Viatura por identificador, sem posse
  do registo. Publicado a 2026-08-31, primeiro contrato de leitura de
  `fleet`; mesmo desenho de `IEmployeeDirectory` em `hr` (ADR-010). Único
  consumidor até agora: `projects`.
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
- Plano de Manutenção pertence ao agregado Viatura. Ao contrário de
  Manutenção e Atribuição, **vários planos activos ao mesmo tempo são
  normais** — não há exclusão mútua entre eles.
- Concluir um ciclo do Plano reagenda a próxima data a partir de **quando foi
  concluído**, não da data que estava marcada — não empilha ciclos em atraso
  se a conclusão vier tarde.
- Uma viatura inactiva não aceita novo Plano nem conclusão de ciclo — mas
  **cancelar um Plano continua permitido**, mesmo com a viatura inactiva: é
  o que se espera ao desactivar.
- Cancelar um Plano nunca o elimina (BR-14) — fica como facto histórico, e
  deixa de contar como devido.
- **2026-08-31 — Registo de Viagem e Despesa de Frota pertencem ao agregado
  Viatura**, mesma disciplina de Manutenção/Atribuição/Plano — uma viatura
  inactiva não aceita nenhum dos dois. Ao contrário de Manutenção e
  Atribuição, **não têm abrir/fechar**: registam-se já como facto concluído
  (mesma disciplina de `StockMovement` em `inventory`), e nunca se alteram
  nem se eliminam depois (BR-9, BR-14).
- **2026-09-04 — Manutenção ganhou `Cost` (ADR-048), opcional.** Preenchido
  só ao fechar o registo (`CloseMaintenance`), nunca à abertura — é quando
  se sabe o valor final. Nulo é "não registado", não zero; a soma por
  período (`IFleetActivityOverview.GetPeriodMaintenanceCostAsync`) ignora o
  que não tem custo em vez de o contar como grátis. **Despesa de Frota
  manteve-se com exactamente as três categorias já documentadas** — o custo
  de manutenção não passou a ser uma quarta categoria; vive no registo de
  Manutenção, que já existia para capturar o que aconteceu.
- O motorista de uma Viagem é **opcional** (ao contrário do de uma
  Atribuição) — uma viatura pode ser usada sem atribuição formal. Quando
  indicado, verifica-se contra `hr` como qualquer outra referência de
  Colaborador (ADR-010, BR-18).
- Despesa de Frota cobre exactamente três categorias — combustível, portagem,
  estacionamento — as que `docs/rivo-suite-descricao-modulos.md` nomeia,
  nenhuma outra. Sem campo de moeda: é sempre AOA, mesma simplificação de
  `NetSalary` em `payroll`.
- **Seguros e documentação legal não são filhos do agregado Viatura** — vivem
  em `VehicleDocument`, uma ligação autónoma (mesmo desenho de
  `EmployeeDocument` em `hr`, ADR-009): não há invariante de Viatura que
  dependa de quantos documentos existem, por isso não precisam do limite de
  consistência do agregado. Sem guarda de estado — uma viatura inactiva
  continua a aceitar documento novo (ex.: encerramento administrativo).

## Perguntas em aberto

- Peças e consumíveis: stock próprio de `fleet` ou itens de `inventory`?
  `docs` deixa em aberto.

## Estado

**Manutenção, Atribuição e Plano de Manutenção, com regra de negócio real —
2026-08-30.** `Vehicle` (matrícula única, modelo, estado
Active/InMaintenance/Inactive) nasceu esqueleto a 2026-08-29; ganhou
Manutenção (registo histórico, uma aberta de cada vez), Atribuição
(motorista, verificado contra `hr`, uma aberta de cada vez) e Plano de
Manutenção (calendário preventivo — vários por viatura, cada um com
intervalo e próxima data devida) como parte do mesmo agregado.

**O "alerta" é uma consulta, não uma notificação empurrada** —
`GET /fleet/maintenance-plans/due?withinDays=N` lista viaturas com plano
activo devido até N dias a partir de hoje, incluindo o já atrasado. Ver a
nota ⚠ em "Depende de" sobre porquê não usar `notifications`.

42 testes de domínio (`Rivo.Fleet.Domain.Tests`); `scripts/verify-fleet.ps1`
— **38/38 confirmados contra a stack local a 2026-08-30**, sem nenhuma
falha, primeira corrida.

**2026-08-31 — `IVehicleDirectory` publicado**, para `projects` verificar a
Viatura na Alocação de Recursos sem lhe possuir o registo (ADR-010) — ver
`modules/projects.md`. Não altera nada do que já existia em `fleet`; só
expõe leitura.

**2026-08-31 — Registo de Viagem, Despesa de Frota e Seguros.** `VehicleTrip`
e `FleetExpense` nasceram como filhos do agregado Viatura (sem abrir/fechar,
registam-se já concluídos); `VehicleDocument` nasceu como ligação autónoma a
`documents`, mesmo desenho de `EmployeeDocument` em `hr`. Fecha a Fase 7 de
`fleet` por completo — nenhuma pergunta de negócio ficou em aberto: as
decisões de forma vieram do precedente já estabelecido no módulo.

63 testes de domínio (`Rivo.Fleet.Domain.Tests` — cresceu de 42 para 58 no
agregado Viatura com Viagem e Despesa, mais 5 de `VehicleDocument`);
`scripts/verify-fleet.ps1` — **50/50 confirmados contra a stack local a
2026-08-31**, sem nenhuma falha, primeira corrida.

Permissões atribuídas a `AssetManager`, que deixou de estar vazio a
2026-08-29.
