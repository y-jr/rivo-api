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
| Item de Folha | por colaborador; bruto, composição de subsídios, vencimentos e descontos |
| Subsídio (Alimentação, Transporte, Férias, Natal) | componente do bruto, não soma a ele; só Alimentação e Transporte têm limiar de isenção no IRT |
| Recibo (Payslip) | gerado via `documents` |

## Possui

Folha de Pagamento, Item de Folha, Recibo, e os inputs de cálculo
(horas, subsídios, deduções) na medida em que sejam de cálculo e não de
registo de assiduidade.

## Depende de

`hr` (`ReferenciaColaborador`, contrato de trabalho, assiduidade),
`finance` (execução do pagamento a colaboradores, postagem contabilística),
`approval` (aprovação da folha), `documents` (recibos — ligado a
2026-08-30), `fiscal` (IRT, INSS — ligado a 2026-08-30), `audit`,
`notifications`.

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
- **Guardar o ficheiro do recibo.** `documents` guarda o ficheiro e o hash;
  `payroll` guarda só a ligação — mesmo desenho de `hr` (ADR-009).

## Ordem de cálculo do IRT — invariante

Confirmado no artigo 7.º do Código do IRT (Lei n.º 18/14, de 22 de Outubro):

```
Salário Bruto
   − INSS do trabalhador (3%)
   − Componentes não sujeitas / isentas
      = min(Subsídio de Alimentação, 30.000 Kz)
      + min(Subsídio de Transporte, 30.000 Kz)
   = Matéria Colectável do IRT
   → identificar escalão
   → Parcela Fixa + Taxa × (MC − Excesso do escalão)
   = IRT
```

**Deduz-se apenas a parcela do trabalhador (3%).** A contribuição patronal
(8%) é custo da empresa e **nunca** é subtraída ao rendimento do trabalhador
para efeitos de IRT.

**"Componentes não sujeitas/isentas" tem conteúdo concreto desde
2026-08-31**: Subsídio de Alimentação e Subsídio de Transporte, cada um
isento até 30.000 Kz/mês (confirmado pelo utilizador, não fonte fiscal
profissional — ver "Perguntas em aberto"); o excesso soma-se à matéria
colectável, nunca perde a isenção da parte que cabe no limiar. Subsídio de
Férias e Subsídio de Natal **não têm isenção nenhuma** — tributados
normalmente, já fazem parte do bruto sem dedução.

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
- **Retenção legal dos recibos: 10 anos** — storage em `documents` (BR-15).
  **Confirmado pelo utilizador a 2026-08-31, não fonte fiscal/laboral
  primária** (ver "Perguntas em aberto"). **Só documentação, sem imposição
  em código**: BR-14 já bloqueia eliminação física em todo o sistema —
  nenhum módulo publica rota `DELETE` — por isso qualquer prazo de retenção
  já está estruturalmente satisfeito sem mecanismo novo. Um campo explícito
  ("retido até") ficaria por construir sem consumidor, e seria especulativo.
- **Recibo só se anexa a um item de uma folha Aprovada** — inferência do
  domínio, não requisito confirmado em `docs/`: um recibo é prova do que foi
  autorizado, e os valores de um item podem mudar enquanto a folha está em
  rascunho ou pendente. Anexar antes arriscaria emitir um recibo que a
  decisão de `approval` ainda pode invalidar.

## Perguntas em aberto

- Âmbito exacto: o cálculo salarial completo é in-scope, ou parte é
  externa? `docs` regista isto como por confirmar.
- Os valores de IRT e INSS que `fiscal` usa — mecanismo implementado desde
  2026-08-30, mas a fonte é o utilizador, não fonte fiscal profissional; ver
  `state/pending-decisions.md`.
- ~~Tratamento de subsídios (alimentação, transporte, férias, Natal) em
  IRT~~ — **confirmado pelo utilizador a 2026-08-31** e **implementado no
  mesmo dia**: Alimentação e Transporte isentos até 30.000 Kz/mês cada,
  excesso tributado; Férias e Natal sem isenção. Mesma reserva de fonte das
  demais entradas — não é fiscalista nem texto legal primário.
- ~~Prazo de retenção legal do recibo (BR-15)~~ — **confirmado pelo
  utilizador a 2026-08-31: 10 anos.** Mesma reserva de fonte das demais
  entradas — não é texto legal primário. Sem mecanismo de eliminação em
  lado nenhum do sistema (BR-14), o prazo fica registado para o dia em que
  houver arquivo/exportação a precisar dele.
- "Só depois de Aprovada" (regra acima) é inferência desta sessão, não
  confirmada com o utilizador nem registada em `docs/`. Revisível se houver
  caso de uso real que precise de anexar antes (ex.: rascunho de recibo para
  conferência).

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
BR-1/BR-5 (aprovação via `approval`) já existiam.

**Recibo ligado a `documents`, também desde 2026-08-30.** `PayrollItemDocument`
— entidade independente, não filha do agregado da folha, mesmo desenho de
`Rivo.Hr.Domain.EmployeeDocument` (ADR-009): FK real para `payroll_item(id)`
e, por SQL entre schemas numa migração própria (`AddCrossSchemaDocumentForeignKey`,
mesmo nome e desenho da de `hr`), para `documents.document(id)`. `documents`
guarda o ficheiro; `payroll` guarda a categoria e sabe o significado de
negócio dela. **Upload e anexar são passos separados** — upload exige
`documents.write`, anexar exige `payroll.runs.write`, porque está a
alterar-se o registo do item — e **só se anexa a um item de folha Aprovada**
(400 → 409 antes disso, "Regras de negócio" acima). Um documento só se liga
uma vez (índice único em `document_id`, mesma defesa de `hr`).

**Subsídios com tratamento fiscal, desde 2026-08-31.** `PayrollItem` ganhou
`FoodAllowance`, `TransportAllowance`, `VacationAllowance`,
`ChristmasAllowance` — componentes do bruto, não uma soma a ele
(`Sum(subsídios) ≤ GrossSalary` é invariante do agregado). `AddPayrollItem`
só pergunta o limiar de isenção a `fiscal` quando o subsídio correspondente
é declarado (> 0) — um item sem alimentação nem transporte não depende de
nenhum dos dois estar configurado. Férias e Natal ficam registados para o
recibo os mostrar, mas não entram em nenhum cálculo de isenção: tributados
normalmente, confirmado pelo utilizador.

Testes: 30 de domínio (`Rivo.Payroll.Domain.Tests` — 16 do motor de
IRT/INSS, 6 de `PayrollItemDocument`, 8 de `PayrollItemAllowanceTests`,
novo a 2026-08-31) e verificação end-to-end (`scripts/verify-payroll.ps1`,
26 casos, incluindo o exemplo documentado bruto 250.000 → líquido 203.600
reproduzido como regressão, o ciclo completo do recibo — recusa antes de
Aprovada, anexar, listar com metadados de `documents`, documento inexistente
devolve 404 — e os dois cenários de subsídio: dentro do limiar (isenção
total) e acima dele (excesso tributado, Férias/Natal sem isenção nenhuma)).
Permissões atribuídas a `HR`.

**A fonte dos valores continua a ser o utilizador, não o Anexo I da Lei
n.º 14/25 nem parecer de fiscalista** — ver `state/pending-decisions.md`
para a reserva completa; o que mudou a 2026-08-30/31 foi o mecanismo, não a
proveniência do dado.
