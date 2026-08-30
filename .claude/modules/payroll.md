# payroll — Payroll & Compensação

**Classificação:** core domain (bounded context dentro de Recursos
Humanos), com schema próprio.

## Responsabilidade

Cálculo e processamento da folha salarial: vencimentos, descontos,
contribuições e recibos.

Separado de `hr` porque tem densidade e regras próprias — `hr` possui a
relação de trabalho, `payroll` possui o cálculo.

## Conceitos

| Conceito | Notas |
|---|---|
| Folha de Pagamento (Run) | por período, com estado |
| Item de Folha | por colaborador; vencimentos e descontos |
| Recibo (Payslip) | gerado via `documents` |

## Possui

Folha de Pagamento, Item de Folha, Recibo, e os inputs de cálculo
(horas, subsídios, deduções) na medida em que sejam de cálculo e não de
registo de assiduidade.

## Depende de

`hr` (`ReferenciaColaborador`, contrato de trabalho, assiduidade),
`finance` (execução do pagamento a colaboradores, postagem contabilística),
`approval` (aprovação da folha), `documents` (recibos), `fiscal`
(IRT, INSS), `audit`, `notifications`.

## Consumido por

`finance` (custo salarial e execução), `fiscal` (base de IRT/INSS).

## Contratos publicados

- Folha aprovada e pronta para execução.
- Custo salarial por período e por centro de custo, para postagem em
  `finance`.

## Não pode

- **Ter tabela própria de passos de aprovação.** A aprovação da folha é um
  Pedido de Aprovação submetido a `approval`. Isto corrige directamente o
  padrão do protótipo, onde `payroll_approval_steps` duplicava o motor
  genérico.
- Ter log de auditoria próprio. Usa `audit` (o protótipo tinha
  `payroll_audit_logs` quase idêntico a `audit_logs`).
- Executar o pagamento — isso é `finance`/Tesouraria.
- Calcular as regras fiscais — consulta `fiscal` para IRT e INSS.

## Ordem de cálculo do IRT — invariante

Confirmado no artigo 7.º do Código do IRT (Lei n.º 18/14, de 22 de Outubro):

```
Salário Bruto
   − INSS do trabalhador (3%)
   − Componentes não sujeitas / isentas
   = Matéria Colectável do IRT
   → identificar escalão
   → Parcela Fixa + Taxa × (MC − Excesso do escalão)
   = IRT
```

**Deduz-se apenas a parcela do trabalhador (3%).** A contribuição patronal
(8%) é custo da empresa e **nunca** é subtraída ao rendimento do trabalhador
para efeitos de IRT.

Errar isto afecta todos os recibos de vencimento. É invariante de domínio,
com teste dedicado.

A tabela de escalões é **dado com vigência temporal** (ADR-011), obtido de
`fiscal` **à data do facto gerador** — nunca constante em código, nunca "a
tabela actual". Uma correcção emitida em 2027 sobre um facto de 2026 aplica
a tabela de 2026.

As descontinuidades da parcela fixa nas fronteiras de escalão são
**comportamento esperado da tabela**, não defeito. Fixar em teste para que
ninguém as "corrija".

## Regras de negócio

- A folha só é executável após decisão "Aprovado" registada em `approval`
  (BR-1, BR-5).
- Concorrência optimista na folha e nos seus itens (BR-17).
- Retenção legal dos recibos — prazo conhecido por `payroll`, storage em
  `documents` (BR-15).

## Perguntas em aberto

- Âmbito exacto: o cálculo salarial completo é in-scope, ou parte é
  externa? `docs` regista isto como por confirmar.
- Os valores de IRT e INSS que `fiscal` usa — mecanismo implementado desde
  2026-08-30, mas a fonte é o utilizador, não fonte fiscal profissional; ver
  `state/pending-decisions.md`.

## Estado

**`PayrollRun` e `PayrollItem`, com cálculo de IRT/INSS desde 2026-08-30.**
CRUD ligado a `approval` (submete-se pelo total bruto, aprova/recusa
aplicado deste lado). `AddPayrollItem` pergunta a `fiscal` — nunca calcula
por si — na ordem do artigo 7.º do CIRT: determina o INSS do trabalhador
(`TaxKind.EmployeeSocialSecurity`, código `INSS`) à data do fim do período
(`PayrollRun.PeriodEndDate`), deduz-o do bruto para obter a matéria
colectável, pede o IRT sobre essa matéria (`IIncomeTaxDetermination`), e só
então `PayrollItem.ApplyCalculation` grava os três campos —
`NetSalary = GrossSalary − WithholdingTax − SocialSecurityContribution`,
calculado, nunca recebido como parâmetro, para que a invariante seja
verdadeira por construção.

**Recusa, não omissão**: sem taxa de INSS ou tabela de IRT em vigor à data,
o item não nasce (400, mesmo padrão de `IssueSalesInvoice` perante
`NoRateInForce`) — nunca fica com um campo nulo a fingir "ainda não
calculado". Regras de negócio impostas: BR-17 (concorrência optimista);
BR-1/BR-5 (aprovação via `approval`) já existiam. BR-15 (retenção de
recibos) continua por implementar — não há `documents` ligado ainda.

Testes: 16 de domínio (`Rivo.Payroll.Domain.Tests`, novo a 2026-08-30) e
verificação end-to-end (`scripts/verify-payroll.ps1`, 17 casos, incluindo o
exemplo documentado bruto 250.000 → líquido 203.600 reproduzido como
regressão). Permissões atribuídas a `HR`.

**A fonte dos valores continua a ser o utilizador, não o Anexo I da Lei
n.º 14/25 nem parecer de fiscalista** — ver `state/pending-decisions.md`
para a reserva completa; o que mudou a 2026-08-30 foi o mecanismo, não a
proveniência do dado. Subsídios (alimentação, transporte, férias, Natal)
continuam sem tratamento definido, e `PayrollItem` não distingue componentes
do salário bruto — o cálculo aplica-se ao bruto inteiro enquanto isso não
for decidido.
