# Regras de Negócio Transversais

Só entram aqui regras que **atravessam** fronteiras de módulo ou que
governam a interacção entre módulos. Regras internas de um módulo ficam no
ficheiro desse módulo em [modules/](../modules/).

Origem: `docs/rivo-arquitetura-global-v1.md` e
`docs/rivo-dados-integracoes-seguranca-v1.md`, que por sua vez absorvem os
requisitos do SGAP.

## Governança de decisões

| # | Regra | Dono da imposição | Vinculados |
|---|---|---|---|
| BR-1 | Nenhum pagamento pode ser executado sem decisão de aprovação registada | `approval` decide; `finance`/Tesouraria verifica antes de executar | finance, approval |
| BR-2 | Quem submete um pedido nunca pode decidir sobre ele | `approval` (código, não configuração) | todos os módulos que submetem |
| BR-3 | Segregação de funções por processo: quem valida não aprova, quem aprova não paga | `approval` + `finance` | approval, finance, procurement, payroll |
| BR-4 | Uma pessoa pode ter vários perfis e cargos no sistema; o que não pode é intervir mais do que uma vez, em papéis conflituantes, **no mesmo processo** | `approval`, ao nível do Pedido de Aprovação | identity, hr, approval |
| BR-5 | Na execução do pagamento, o estado da aprovação é **revalidado** e a disponibilidade de tesouraria verificada (SGAP RN-020) | `finance`/Tesouraria | finance, approval |
| BR-6 | Aprovadores e contexto do pedido são **congelados na submissão**. Alterações organizacionais posteriores não recalculam um processo em curso | `approval` | hr, approval |
| BR-7 | Anti-fraccionamento: agregação por fornecedor + rubrica numa janela de 30 dias, para impedir fraccionamento de despesa abaixo de alçada | `approval`, lendo dados de `finance` | approval, finance, procurement |
| BR-8 | Verificação orçamental antes da decisão (SGAP RN-017) | `approval` lê `finance`; `finance` possui o orçamento | approval, finance |
| BR-20 | A atribuição de um Cargo que confira autoridade de aprovação só produz efeito após decisão "Aprovado". Uma atribuição pendente não confere autoridade nenhuma | `hr` retém o efeito; `approval` decide | hr, approval |
| BR-21 | A marca `confere_autoridade_aprovacao` de um Cargo só é alterável pelo perfil `Admin`, e a alteração é auditada. Baixá-la desactiva o controlo de BR-20 | `hr` | hr, identity, audit |

**Imposição:** todas estas regras são impostas no servidor/domínio, nunca só
na interface. RLS é defesa em profundidade, nunca a sede da regra
(ADR-008).

**Estado a 2026-08-25:** BR-1, BR-2, BR-3, BR-4, BR-5, BR-6, BR-8, BR-17 e
BR-20 estão implementadas e verificadas. **BR-7 continua por desenhar** (K5).

BR-8 fechou com Planeamento (ADR-037): `approval` pergunta a `finance` se o
valor cabe, antes de escolher aprovadores. ⚠ Duas ressalvas assumidas — o
consumo conta **compromissos e não realizações**, e a verificação é à **data de
hoje** e não à do pedido.

⚠ **A postagem automática reabre a primeira.** Desde 2026-08-25 os documentos
lançam nos livros, e no dia em que o consumo passar a contar lançamentos há que
escolher uma das fontes: somar as duas contaria a dobrar.

## Auditoria

| # | Regra | Vinculados |
|---|---|---|
| BR-9 | Toda a acção significativa é auditada: actor, acção, entidade, data, IP, correlation ID | todos |
| BR-10 | O log de auditoria é **append-only** e não é alterável por funcionalidade aplicacional | audit |
| BR-11 | Retenção mínima de 10 anos para o log de auditoria | audit |
| BR-12 | Tentativas de acção **não autorizada** são registadas explicitamente, não apenas bloqueadas em silêncio | todos |
| BR-13 | Alterações de configuração (perfis, regras de aprovação, parâmetros) são auditadas com a mesma disciplina que transacções de negócio | identity, approval, todos |

## Retenção e eliminação

| # | Regra | Vinculados |
|---|---|---|
| BR-14 | Sem eliminação física em entidades sujeitas a auditoria ou retenção legal (decisões, pagamentos, documentos fiscais) — apenas anulação lógica, auditada | todos |
| BR-15 | A retenção legal de um documento é conhecida pelo **contexto de origem**, não por `documents` (ADR-009) | documents + contextos consumidores |
| BR-16 | Minimização de dados pessoais nos termos da Lei n.º 22/11 (Protecção de Dados Pessoais, Angola), excepto onde a retenção legal obriga ao contrário | hr, payroll, commercial |

## Concorrência

| # | Regra | Vinculados |
|---|---|---|
| BR-17 | Controlo de concorrência optimista em qualquer entidade decidida por mais de uma pessoa — decisões de aprovação e execução de pagamento em particular | approval, finance |

## Referências entre contextos

| # | Regra | Vinculados |
|---|---|---|
| BR-18 | Atributos de Colaborador lêem-se pelo contrato `ReferenciaColaborador`; nunca por leitura directa a `hr` nem por cópia para tabelas próprias (ADR-010) | todos |
| BR-19 | Excepção deliberada a BR-18: o snapshot de submissão em `approval` (BR-6) | approval |

## Regras ainda por levantar

Regras específicas do domínio fiscal angolano (IVA, IRT, INSS, requisitos de
numeração e conteúdo de factura, prazos de declaração à AGT) ainda não estão
detalhadas em `docs/`. São **decisão em aberto**, não omissão — ver
[state/pending-decisions.md](../state/pending-decisions.md).
