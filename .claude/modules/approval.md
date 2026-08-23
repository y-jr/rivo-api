# approval — Governança de Decisões

**Classificação:** supporting domain (reclassificado de core — ADR-007),
implementado como capacidade transversal.

## Responsabilidade

Executar processos de aprovação definidos pelo Rivo. Recebe pedidos dos
módulos de negócio, determina os aprovadores segundo o workflow do sistema
e as regras configuradas, regista decisões e expõe o resultado ao módulo de
origem.

**Não possui a transacção de negócio que aprova.**

## Workflow vs. regras

Distinção fundamental:

- **O workflow é definido pelo Rivo.** Não é configurável pelo
  administrador.
- **As regras são configuráveis:** alçadas, cargo, departamento, faixas de
  valor, níveis exigidos, condições por processo.

Regras de domínio obrigatórias não podem ser desactivadas nem enfraquecidas
por configuração administrativa.

**Disciplina de âmbito (ADR-007):** sem BPMN, sem designer visual de
workflows, sem grafos arbitrários. Funcionalidade genérica de workflow
engine é complexidade sem requisito.

## Conceitos

| Conceito | Notas |
|---|---|
| Política de Aprovação | tipo de processo, cargo/departamento/faixa de valor; referencia Cargo de `hr` por id, nunca duplica o catálogo |
| Passo da Política | ordem, modo (sequencial/paralelo), candidato a aprovador, SLA |
| Pedido de Aprovação | tipo, origem (tabela + id), requisitante, valor, departamento — **todos congelados na submissão**; política aplicada por snapshot, não por FK viva |
| Atribuição | pessoa concreta, resolvida na submissão e **não recalculada** |
| Decisão | acção, autor, data, notas — **imutável** |
| Delegação | delegante, delegado, período — auditada |

## Estados

Pendente, Em curso, Em espera, Esclarecimento pedido, Aprovado, Rejeitado,
Cancelado. Suporta substituição de aprovador onde as regras do processo o
permitam.

## Possui

Política, Passo, Pedido, Atribuição, Decisão, Delegação.

Nunca possui a entidade de negócio de origem.

## Depende de

`identity` (actor, autorização), `hr` (Cargo → pessoa actual), `finance`
(disponível orçamental), `audit`, `notifications`.

As duas leituras — `hr` e `finance` — são **o ponto onde nasce o God
Module**. São contratos estreitos, explícitos e versionados. `approval`
nunca lê tabelas desses módulos.

## Consumido por

`procurement`, `finance`, `payroll`, `hr`, `commercial`, `projects`.

**Caso especial — atribuição de Cargo (ADR-015).** `hr` submete a `approval`
as atribuições de Cargos que confiram autoridade de aprovação. É o único
processo em que o resultado altera *quem pode aprovar no futuro*, e existe
precisamente para fechar essa escalada.

⚠ Cria dependência mútua `hr ↔ approval` — ver ADR-015 §R1 para a resolução
(assemblies de contratos) quando os módulos forem implementados.

## Contratos publicados

- Submeter Pedido de Aprovação (tipo, valor, departamento, requisitante,
  referência à origem).
- Consultar estado de um pedido.
- Notificar decisão ao módulo de origem.

## Eventos

- `PedidoAprovacaoSubmetido`
- `DecisaoRegistada`
- `ProcessoAprovacaoConcluido`

## Não pode

- Interpretar o significado de negócio do que aprova.
- Modificar dados de negócio do módulo de origem.
- Possuir a transacção aprovada.
- Permitir que administradores substituam regras de workflow obrigatórias.
- Recalcular silenciosamente um processo em curso porque dados
  organizacionais mudaram.
- Ler directamente tabelas de `hr` ou `finance`.

## Regras de negócio

- Quem submete nunca decide sobre o próprio pedido (BR-2).
- Segregação de funções: quem valida não aprova, quem aprova não paga
  (BR-3).
- Sem acumulação de papéis conflituantes **no mesmo processo** — verificado
  ao nível do Pedido, não do sistema global (BR-4).
- Aprovadores e contexto congelados na submissão (BR-6).
- Anti-fraccionamento: agregação por fornecedor + rubrica em janela de 30
  dias (BR-7).
- Verificação orçamental antes da decisão (BR-8).
- Concorrência optimista nas decisões (BR-17).
- Decisões são imutáveis.

**Sede da imposição (ADR-008):** todas estas invariantes vivem no domínio
`approval`, em código, testadas ao nível do domínio. RLS é segunda linha de
defesa e tem de reflectir uma invariante já expressa no domínio. Uma regra
que exista apenas em RLS é um defeito de arquitectura.

## Perguntas em aberto

- Semântica exacta de SLA e escalonamento.
- Modelo de dados definitivo — `docs` remete para fase de desenho
  detalhado.
- Regras adicionais de segregação além do mínimo já fixado.

## Estado

**Desenho fixado e domínio iniciado** (2026-08-23) — ADR-034, que é a "fase de
desenho detalhado" para que `docs` remetia.

Existem `Rivo.Approval.Contracts` e `Rivo.Approval.Domain`, com 17 testes de
domínio.

### O que já está imposto

| Regra | Forma concreta |
|---|---|
| **BR-2** | O autor da decisão não pode ser o requisitante — vale mesmo que esteja atribuído ao passo |
| **BR-4** | Uma pessoa decide no máximo uma vez por pedido; com dois cargos, satisfaria sozinha um workflow de dois passos |
| **BR-6** | Aprovadores congelados na submissão. A política fica como rasto, **não como chave estrangeira viva** |
| **BR-17** | `Version` em `ApprovalRequest` |
| Decisões imutáveis | `Decision` sem setters. Corrigir é decidir outra vez, não reescrever |

BR-2 e BR-4 lançam `SegregationOfDutiesException`, distinta de um erro de
estado qualquer: uma tentativa de as violar é evento de segurança e vai para a
trilha como tal, não como um 409 anónimo.

### Alcançável desde 2026-08-23

As cinco camadas existem, com schema `approval` e migração aplicada. Rotas:

| Método | Rota | Permissão |
|---|---|---|
| GET | `/approval/policies` | `approval.policies.read` |
| POST | `/approval/policies` | `approval.policies.write` |
| GET | `/approval/requests?processType=&pendingFor=` | `approval.requests.read` |
| GET | `/approval/requests/{id}` | `approval.requests.read` |
| POST | `/approval/requests/{id}/decisions` | `approval.requests.decide` |
| POST | `/approval/requests/{id}/cancellation` | `approval.requests.read` |

**Não há endpoint de submissão, e é deliberado.** Submeter é acto do módulo de
origem, por `IApprovalGateway` — expor uma rota HTTP deixaria criar processos
sem transacção de negócio por trás, e `approval` não possui nem interpreta
essas transacções.

Uma violação de BR-2 ou BR-4 devolve **403**, e não 409: não é o estado do
pedido que impede, é *esta pessoa*. A tentativa recusada vai para a trilha com
acção própria — uma sequência delas contra o mesmo pedido é o padrão que
interessa detectar.

`Manager` e `Finance` deixam de ser perfis vazios: recebem
`approval.requests.read` e `.decide`, **sem** gestão de políticas — quem
configura as alçadas decidiria indirectamente o que pode aprovar sozinho.

### O `501` de `hr` está fechado (2026-08-23)

Uma atribuição de Cargo com autoridade deixa de ser recusada: cria-se
**pendente** e submete-se. Pendente não confere Cargo nenhum, e é isso que
mantém fechado o caminho de escalada.

**A ligação `hr → approval` é feita por inversão, no composition root.** `hr`
declara `IPositionApprovalSubmission` nas suas palavras e não sabe que
`approval` existe; o adaptador vive em `Rivo.Api`. A alternativa que o ADR-015
§R1 previa — assemblies de contratos dos dois lados — resolve a compilação mas
deixa o ciclo no grafo de módulos, e o teste `Modules_HaveNoDependencyCycles`
continuaria a vê-lo. Com razão: dois módulos que se lêem mutuamente estão
acoplados, compilem ou não.

`approval` nunca empurra a decisão — `hr` pergunta, por
`POST /hr/position-assignments/{id}/approval-outcome`, que é idempotente.

### Por fazer

- **Automatizar a aplicação da decisão.** Hoje alguém tem de chamar o
  endpoint. Um worker de reconciliação fecha isto quando o mecanismo de
  eventos for decidido.
- **BR-7 e BR-8** — anti-fraccionamento e verificação orçamental exigem
  `finance`. Uma política pode declarar `RequiresBudgetCheck`, e enquanto
  `finance` não existir isso **recusa a submissão** em vez de fingir que
  verificou (ADR-034).
- **Metade de BR-3** — "quem aprova não paga" precisa de `finance`.
- **SLA e escalonamento.** O passo guarda o prazo; nada o faz cumprir.
- **Delegação** — modelada em `docs`, ainda sem código.
