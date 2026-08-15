# ADR-010: Referência entre Contextos — Contrato `ReferenciaColaborador` e FK entre Schemas

## Status

Aceite (resoluções R4 e R5)

## Context

`docs/rivo-arquitetura-global-v1.md` identifica que **`Colaborador` é o
shared kernel de facto com maior fan-out de todo o sistema** — referenciado
por `approval` (requisitante, aprovador), `procurement` (requisitante),
`finance` (responsável de centro de custo), `projects` (dono), `fleet`
(motorista) e `commercial` (dono comercial).

O documento usa esse fan-out como argumento **contra** microservices — cedo
atrás de uma fronteira de rede, geraria chamadas constantes a partir de
quase todos os contextos.

Mas não propunha como impedir que `Colaborador` se torne uma god-entity
**dentro** do monólito. Sem contrato, o resultado é o mesmo acoplamento, só
sem a latência.

Adicionalmente, o desenho de dados menciona referências "por FK simples"
entre schemas, sem definir o que é permitido.

## Requirements

- **Facto** — `hr.Colaborador` tem o maior fan-out do schema.
- **Facto** — Aprovadores e contexto são congelados na submissão em
  `approval` (BR-6).
- **Inferência** — Qualquer alteração ao modelo de Colaborador tem raio de
  impacto no sistema inteiro se o acesso não for contratual.

## Constraints

- Monólito modular com schema por domínio (ADR-001, ADR-002).
- O desenho deve permitir extracção futura sem remodelação.

## Alternatives

1. **Contrato de leitura estreito publicado por `hr`, com FK apenas à
   chave primária.**
2. Colaborador no SharedKernel.
3. Leitura directa às tabelas de `hr` por quem precisar.
4. Cada contexto copia os atributos de que precisa.

A opção 2 dissolveria a fronteira mais importante do sistema. A opção 3 é o
acoplamento a evitar. A opção 4 produz dados obsoletos silenciosamente — o
nome ou o departamento mudam em `hr` e as cópias não.

## Trade-offs

O contrato custa uma indirecção e obriga a versioná-lo quando muda. Em
troca, o modelo interno de `hr` fica livre para evoluir sem raio de impacto
global.

## Decision

### Contrato `ReferenciaColaborador` (R4)

`hr` publica um read model estreito. Nenhum outro contexto lê tabelas de
`hr`.

| Campo | Nota |
|---|---|
| `colaborador_id` | identificador estável |
| `nome_exibicao` | — |
| `estado` | activo / inactivo |
| `departamento_id` | — |
| `cargo_actual` | id + nome, resolvido à data pedida (usa Atribuição de Cargo, que é histórica) |
| `utilizador_id` | opcional — nem todo colaborador tem login |

Regras vinculativas:

- Os consumidores guardam **apenas `colaborador_id`**. Nunca copiam nome,
  departamento ou cargo para as suas tabelas.
- **Excepção única e deliberada:** o snapshot de submissão em `approval`
  (Pedido e Atribuição congelam requisitante, departamento e aprovador
  resolvido). Aí a cópia é intencional — o processo não pode mudar porque a
  organização mudou a meio (BR-6).
- Precisar de mais campos significa que ou o caso de uso pertence a `hr`, ou
  o contrato precisa de extensão explícita e versionada. Nunca leitura
  directa.

### FK entre schemas (R5)

**Permitido:** FK entre schemas exclusivamente para a **chave primária** do
contexto dono (`fleet.viatura.motorista_id → hr.colaborador(id)`), com o
único propósito de garantir integridade referencial.

**Proibido:**

- FK para colunas que não sejam a chave primária do dono.
- `JOIN` a outras tabelas do contexto dono. Ler um atributo faz-se pelo
  contrato, não por SQL.
- FK no sentido inverso ao da dependência declarada em
  [architecture/dependency-rules.md](../architecture/dependency-rules.md).

Numa eventual extracção futura, estas FKs degradam-se para identificadores
simples — alteração localizada, não remodelação.

## Consequences

Facilita:

- O modelo interno de `hr` evolui sem raio de impacto global.
- Integridade referencial mantida sem acoplamento de leitura.
- Extracção futura de um contexto torna-se mecânica.

Dificulta / exige:

- Uma indirecção em vez de um `JOIN` directo — custo de performance
  desprezável em processo, mas exige disciplina.
- O contrato tem de ser versionado quando muda.
- Consultas de relatório que cruzem contextos precisam de composição
  explícita, não de SQL livre.

## Risks

- **Erosão:** alguém escreve um `JOIN` a `hr` "só desta vez". Mitigação:
  permissões ao nível do schema; testes de arquitectura quando a stack
  estiver fechada.
- **Cópia de atributos** por conveniência, produzindo dados obsoletos.
  Mitigação: BR-18 e revisão contra
  [architecture/module-boundaries.md](../architecture/module-boundaries.md).
- O contrato crescer até virar a tabela inteira. Mitigação: cada extensão é
  explícita e justificada.

## Revisit When

- Um caso de uso legítimo exigir consistentemente mais do que o contrato
  oferece — sinal de que o caso de uso pertence a `hr`.
- A performance da composição se revelar problemática em relatórios de
  grande volume.

## Related

- `docs/rivo-arquitetura-global-v1.md` §1.5, §3, §10 (R4, R5)
- [ADR-001](adr-001-architecture-style.md), [ADR-002](adr-002-database.md)
- [modules/hr.md](../modules/hr.md),
  [architecture/dependency-rules.md](../architecture/dependency-rules.md)
