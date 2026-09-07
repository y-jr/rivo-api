# Arranque em produção — a ordem em que as coisas se ligam

Guia de primeira utilização de uma instância vazia. Não é documentação de
funcionalidades: é a **ordem**, e as razões pelas quais ela não é arbitrária.

Escrito a 2026-09-07, a partir da primeira tentativa real de usar o sistema em
`https://syyt.tech`.

## O nó do arranque: o Admin não se pode ligar a si próprio

Entrar como Admin e tentar agir dá:

> Esta conta não está associada a nenhum colaborador.

Não é defeito. Desde o **ADR-057**, nenhum acto aceita que lhe digam quem o
pratica — o autor resolve-se da conta autenticada, que tem de estar ligada a um
Colaborador. E desde o **ADR-054**, admitir alguém **não** cria esse vínculo:
são dois actos.

O problema é o passo seguinte. Ligar exige `hr.employees.link_account`, que só
o perfil `Admin` tem — e `LinkEmployeeAccount` recusa a auto-ligação:

```csharp
if (context.ActorId is { } actor && actor == userId)
    return LinkEmployeeAccountResult.SelfLinkRefused();
```

É o mesmo princípio do BR-2 — ninguém se autoriza a si mesmo — e a consequência
é que **uma instância precisa de dois Admins para arrancar**. O primeiro não
consegue habilitar-se sozinho.

⚠ **Isto não está registado em ADR nenhum.** É consequência não intencional do
cruzamento entre ADR-051, ADR-054 e ADR-057, e nenhum dos três a considerou.
Ver "Decisão em aberto" no fim.

## Fase 0 — desatar o nó (uma vez, e nunca mais)

| # | Quem | O quê |
|---|---|---|
| 1 | Admin ①  | **Admitir dois Colaboradores** — um para si, um para o Admin ②. Só precisa do nome (`POST /hr/employees`) |
| 2 | *(browser anónimo)* | **Registar a segunda conta** em `/registar`. Nasce sem perfil e sem poder fazer nada |
| 3 | Admin ① | **Dar-lhe o perfil `Admin`** em Utilizadores |
| 4 | Admin ② | **Entrar.** As permissões são resolvidas no login e viajam no token (ADR-014) — sem entrar depois de receber o perfil, não as tem |
| 5 | Admin ② | **Ligar a conta do Admin ① ao Colaborador dele** |
| 6 | Admin ① | **Ligar a conta do Admin ② ao Colaborador dele** |

A partir daqui os dois agem. A ordem 5→6 importa: só depois do passo 5 é que o
Admin ① deixa de estar bloqueado.

## Fase 1 — as pessoas e a estrutura

| Onde | O quê | Porque antes do resto |
|---|---|---|
| RH › Departamentos | Os departamentos reais | Um Colaborador pode não ter, mas o orçamento por centro de custo e a política de aprovação por departamento precisam |
| RH › Cargos | Os cargos, com `grantsApprovalAuthority` | ⚠ **Só o Admin cria cargos** (ADR-015). Esta marca decide quem pode vir a aprovar pagamentos — quem a controla controla a alçada |
| RH › Colaboradores | As pessoas | Admitir não liga conta (ADR-054) |
| Utilizadores | Uma conta por pessoa, com perfil | Registo em `/registar`, perfil atribuído aqui |
| RH › Colaboradores | **Ligar cada conta ao seu Colaborador** | Sem isto a pessoa entra e não faz nada. É o passo que se esquece |

**Atribuir um Cargo com autoridade pode devolver `202` e não `201`** — isso
quer dizer que foi submetido a aprovação e **não atribuiu nada** (BR-20).

## Fase 2 — a governança, antes de haver o que aprovar

Sem política de aprovação, submeter uma requisição ou uma folha devolve `409`:
não é defeito, é configuração em falta.

Em **Definições › Regras de aprovação**, criar uma política por tipo de
processo que vá usar:

| Processo | Quando morde |
|---|---|
| `procurement.purchase_requisition` | Ao submeter uma requisição |
| `payroll.payroll_run` | Ao submeter a folha |
| `hr.leave_request` | Ao pedir férias |
| `finance.payment_request` | Ao pedir um pagamento |
| `hr.position_assignment` | Ao atribuir um cargo com autoridade |

⚠ **Uma política aponta a um Cargo, não a uma pessoa** — e o Cargo tem de ter
ocupante, senão a submissão é recusada por não haver quem decida.

⚠ **Duas políticas igualmente específicas para o mesmo processo dão
ambiguidade**, e a submissão é recusada. Uma de cada.

## Fase 3 — o dinheiro

A ordem aqui é rígida, porque cada peça depende da anterior.

1. **Fiscal › Impostos** — taxa de IVA com vigência. Sem taxa em vigor à data
   do facto gerador, emitir uma factura **recusa** em vez de gravar com campo
   nulo (ADR-011).
2. **Finanças › Séries de documento** — uma série activa por tipo (`FT`, `NC`,
   `RC`). Sem série não se emite.
3. **Tesouraria › Contas Bancárias** — pelo menos uma conta aberta. Sem ela não
   há de onde executar pagamentos.
4. **Contabilidade › Plano de Contas** — ⚠ **nasce vazio de propósito.** O Rivo
   fixa a estrutura do SAF-T e **recusa-se a inventar o PGC angolano**
   (ADR-037). Isto é trabalho do contabilista, não de configuração.
5. **Contabilidade › Períodos** — abrir o período corrente. Sem período aberto,
   nada lança.
6. **Contabilidade › Regras de postagem** — a tradução documento → contas.
   Sem elas os documentos emitem-se e **não lançam nos livros**.

⚠ **Os passos 4 a 6 podem esperar.** Facturar, receber e pagar funcionam sem
contabilidade configurada; o que não acontece é o lançamento nos livros. É
recorte deliberado, não meia-implementação.

## Fase 4 — a segregação obriga a três pessoas

Não é preferência de desenho: o servidor recusa.

| Regra | O que impede |
|---|---|
| **BR-2** | Quem submete não decide |
| **BR-3** | Quem aprova um pagamento não o executa |
| Recepção ≠ ordem | Quem recebe mercadoria não é quem a encomendou |

Para um ciclo de compra completo precisa de **três contas ligadas a três
Colaboradores diferentes**: quem requisita, quem aprova, quem paga. Com duas,
bate na parede a meio — e a mensagem é correcta mas parece arbitrária a quem
não sabe porquê.

**Para explorar sozinho**, o Admin pode fazer quase tudo *excepto* fechar um
ciclo que exija duas pessoas. Vale a pena criar as três contas de teste logo na
Fase 0.

## Fase 5 — os módulos operacionais

Independentes uns dos outros; pela ordem que preferir.

- **Compras** — Fornecedores primeiro (com NIF, que **fixa e não se altera**),
  depois requisição → aprovação → ordem → recepção
- **Inventário** — Armazéns primeiro, depois artigos, depois movimentos.
  ⚠ **A recepção em Compras não dá entrada em stock** — os módulos não se
  conhecem, e a entrada regista-se à mão
- **Frota** — Viaturas, depois manutenções, atribuições, planos
- **Projectos** — Projecto, depois marcos, tarefas, orçamento, alocações

## Decisão em aberto

**A instância precisa de dois Admins para arrancar, e isso não foi decidido —
aconteceu.** É o cruzamento de três ADRs que nunca se consideraram em conjunto:
o 051 pôs a ligação atrás de permissão própria com recusa de auto-ligação, o
054 tirou o vínculo da admissão, e o 057 tornou o vínculo obrigatório para
agir.

Três saídas possíveis, e nenhuma é obviamente certa:

1. **Aceitar e documentar** — é o que este ficheiro faz. Custo: quem instala
   pela primeira vez fica bloqueado até ler isto.
2. **O seeder de bootstrap cria também o Colaborador e o vínculo.** Resolve o
   arranque sem abrir a auto-ligação. Custo: o seeder passa a conhecer `hr`.
3. **Permitir a auto-ligação só quando não há nenhum vínculo no sistema.**
   Cirúrgico, mas cria um caminho especial que existe uma vez na vida da
   instância — e caminhos especiais são onde as falhas se escondem.

**A 2 parece a melhor**, e é decisão de ADR — não se implementa sem ela.
