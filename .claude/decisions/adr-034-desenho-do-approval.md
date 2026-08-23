# ADR-034: Desenho do Motor de Aprovação

## Status

Aceite (2026-08-23).

**Concretiza o [ADR-007](adr-007-approval-supporting-domain.md) e o
[ADR-008](adr-008-segregacao-funcoes.md)**, e aplica a resolução R1 do
[ADR-015](adr-015-atribuicao-cargo.md). Não substitui nenhum.

## Context

`modules/approval.md` fecha com *"Modelo de dados definitivo — `docs` remete
para fase de desenho detalhado"*. Esta é essa fase.

`approval` é o maior desbloqueio do projecto: fecha o `501` de `hr` e destrava
`procurement`, `finance`, `payroll`, `commercial` e `projects`. É também o
domínio com mais invariantes de negócio — oito regras (BR-2, BR-3, BR-4, BR-6,
BR-7, BR-8, BR-17, BR-20) cuja sede de imposição o ADR-008 fixa **no domínio,
em código**.

## Requirements

- **Facto** — ADR-008: todas as invariantes de segregação vivem no domínio
  `approval`, testadas ao nível do domínio. Uma regra que só exista em SQL é
  defeito de arquitectura.
- **Facto** — ADR-007: sem BPMN, sem designer visual, sem grafos arbitrários.
- **Facto** — BR-6/BR-19: aprovadores e contexto **congelados na submissão**.
  É a única excepção autorizada à proibição de copiar dados entre módulos.
- **Facto** — BR-2: quem submete nunca decide sobre o próprio pedido.
- **Facto** — BR-4: sem acumulação de papéis conflituantes **no mesmo
  processo**, verificado ao nível do Pedido.
- **Facto** — BR-17: concorrência optimista nas decisões.
- **Facto** — `Rivo.Hr.Contracts` já publica `IEmployeeDirectory` com
  `FindByPositionAsync(positionId, asOf)` — exactamente a resolução de
  aprovadores por Cargo à data que BR-6 exige.
- **Facto** — BR-7 e BR-8 dependem de `finance`, que não existe.

## Constraints

- `hr → approval` e `approval → hr` formam ciclo, e em .NET uma referência
  mútua de projectos não compila.
- `finance` não existe. BR-7 (anti-fraccionamento) e BR-8 (verificação
  orçamental) não são implementáveis.

## Decision

### 1. O ciclo `hr ↔ approval` resolve-se por assemblies de contratos

É a aplicação literal da resolução R1 do ADR-015, no momento em que ela
previa:

```
hr        → Rivo.Approval.Contracts     (submete atribuições de cargo, férias)
approval  → Rivo.Hr.Contracts           (resolve aprovadores por Cargo)
```

Não é ciclo porque os assemblies de contratos não dependem de nada. O teste
`Modules_HaveNoDependencyCycles` do ADR-024 continua a valer, e passa a ter
sobre o que incidir.

### 2. Política e Pedido são coisas diferentes, e é isso que faz BR-6 funcionar

| Conceito | Vive enquanto | Muda? |
|---|---|---|
| **Política** + **Passos** | Configuração corrente da organização | Sim, a qualquer momento |
| **Pedido** + **Atribuições** | Um processo concreto | **Nunca, depois de submetido** |

Na submissão, a política aplicável é resolvida, os seus passos são **copiados**
para atribuições com pessoas concretas, e a ligação à política passa a ser
histórica — guarda-se o identificador para rasto, **não como chave estrangeira
viva**.

**É isto que impede o que `modules/approval.md` proíbe:** recalcular
silenciosamente um processo em curso porque alguém mudou de cargo. Uma
alteração organizacional a meio de um processo de pagamento mudaria quem
aprova depois de já haver decisões tomadas — e ninguém saberia porquê.

### 3. Correspondência de política: tipo, departamento e faixa de valor

Sem motor de regras e sem grafos (ADR-007). Uma política declara:

- **tipo de processo** (obrigatório);
- **departamento** (opcional — nulo aplica-se a todos);
- **faixa de valor** (mínimo inclusivo, máximo exclusivo, ambos opcionais).

Escolhe-se a **mais específica** que corresponda: departamento definido bate
departamento nulo; faixa mais estreita bate faixa mais larga. Empate é erro de
configuração e recusa-se a submissão, em vez de escolher uma ao acaso — duas
políticas igualmente aplicáveis significam que ninguém sabe qual é a alçada.

### 4. Modo do passo: quantos ocupantes do Cargo têm de decidir

Os passos correm **sempre por ordem**. O modo é sobre pessoas *dentro* do
passo — e um passo aponta para um **Cargo**, que pode ter mais do que um
ocupante.

- **`AnyApprover`** (omissão): basta um dos ocupantes.
- **`AllApprovers`**: todos têm de decidir.

**A omissão é "basta um", e a razão é que quem ocupa um Cargo representa esse
Cargo.** Dois directores financeiros são intermutáveis para decidir em nome da
direcção financeira; exigir os dois travaria o processo sempre que um
estivesse de férias. `AllApprovers` existe para o caso genuíno de assinatura
conjunta — duas chaves para o mesmo cofre — e é escolha explícita de quem
configura a política.

**Uma versão anterior deste ADR chamou a isto "sequencial/paralelo" e fez do
unânime a omissão.** Estava errado, e apareceu na primeira verificação
ponta-a-ponta: dois ocupantes do mesmo Cargo ficaram ambos obrigados a
aprovar, e o processo parou. Os nomes passaram a dizer o que o código faz.

### 5. Uma rejeição em qualquer ponto termina o processo

Não há "rejeitado mas continua". Quem rejeita di-lo com efeito imediato, e o
módulo de origem é informado. Isto vale tanto em sequencial como em paralelo.

### 6. As invariantes, e onde cada uma vive

| Regra | Onde | Forma concreta |
|---|---|---|
| BR-2 | `ApprovalRequest.Decide` | O autor da decisão não pode ser o requisitante |
| BR-4 | `ApprovalRequest.Decide` | Quem já decidiu num passo não decide noutro, no mesmo pedido |
| BR-6 | `ApprovalRequest.Submit` | Atribuições resolvidas uma vez e nunca recalculadas |
| BR-17 | `Version` em `ApprovalRequest` | Duas decisões simultâneas — uma perde |
| Decisões imutáveis | `Decision` sem setters | Acrescenta-se, nunca se altera |

**BR-4 na forma verificável.** A regra fala de "papéis conflituantes no mesmo
processo". Traduz-se em: uma pessoa decide **no máximo uma vez por pedido**.
Sem isto, quem ocupasse dois cargos aprovaria o seu próprio processo duas
vezes e satisfaria sozinho um workflow de dois passos — que é exactamente a
acumulação que BR-3 e BR-4 existem para impedir.

### 7. BR-7 e BR-8 recusam-se em vez de fingir

Anti-fraccionamento e verificação orçamental exigem `finance`, que não existe.

Uma política pode declarar `RequiresBudgetCheck`. Enquanto `finance` não
existir, submeter um pedido a uma política com essa marca **devolve `501` e não
grava nada** — a mesma recusa deliberada com que `hr` trata um Cargo com
autoridade de aprovação (ADR-015).

A alternativa seria aprovar pagamentos sem verificar orçamento e sem detectar
fraccionamento, com o sistema a afirmar que os verificou. É pior do que não
ter a funcionalidade.

## Alternatives

1. **Snapshot na submissão** (escolhida) vs. FK viva para a política.
2. **Paralelo unânime** (escolhida) vs. quórum configurável.
3. **Recusa explícita** para BR-7/BR-8 (escolhida) vs. implementar sem
   verificação, ou adiar o módulo até `finance` existir.

A FK viva é mais simples e está errada: violaria BR-6 por construção.

Adiar `approval` até `finance` existir foi rejeitado porque `approval`
desbloqueia seis módulos e a maioria dos seus processos — férias, atribuição de
cargo — não toca em orçamento nenhum.

## Consequences

**Mais fácil:** o `501` de `hr` deixa de ser permanente. Seis módulos ganham o
motor de governança de que dependem.

**Mais difícil / exigido:**

- Duas fontes de verdade aparentes — política corrente e política congelada no
  pedido. É deliberado, e a interface tem de o mostrar: um pedido em curso pode
  seguir regras que já não são as actuais.
- Processos com verificação orçamental ficam por servir até `finance` existir.

## Risks

- **BR-3 fica parcialmente por impor.** "Quem aprova não paga" precisa de
  `finance`: `approval` não vê a execução do pagamento. A parte que depende só
  de `approval` — quem submete não decide, ninguém decide duas vezes — fica
  imposta; o resto não.
- **Empate de políticas recusa a submissão.** Uma configuração ambígua bloqueia
  processos em vez de os deixar passar pela alçada errada. É a troca certa, e
  vai gerar chamadas de suporte.
- **A semântica de SLA continua em aberto.** O passo guarda o prazo; **nada
  escalona nada**. Escalonamento automático exige decidir o que acontece ao fim
  do prazo — avançar, notificar, ou reatribuir — e essa decisão não está tomada.

## Revisit When

- `finance` existir: BR-7, BR-8 e a segunda metade de BR-3 passam a ser
  implementáveis.
- Aparecer requisito real de quórum num passo paralelo.
- A semântica de SLA e escalonamento for decidida.

## Related

- [ADR-007](adr-007-approval-supporting-domain.md) — a disciplina de âmbito que este
  desenho respeita
- [ADR-008](adr-008-segregacao-funcoes.md) — a sede de imposição no domínio
- [ADR-015](adr-015-atribuicao-cargo.md) — a resolução R1 do ciclo, aplicada aqui
- [ADR-017](adr-017-contratos-por-modulo.md) — assemblies de contratos
- [ADR-025](adr-025-concorrencia-optimista.md) — a concorrência que BR-17 exige
