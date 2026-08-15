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

Não iniciado.
