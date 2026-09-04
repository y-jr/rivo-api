# ADR-049: Parecer fiscal levanta a trave de produção de `payroll`

## Status

Aceite (2026-09-04). Decisão do utilizador — não é decisão de arquitectura
no sentido usual, mas fecha um critério de saída que o roteiro de execução
fixa explicitamente como "decisão explícita e registada".

## Context

`state/roadmap-execucao.md` — Fase 6 (`payroll`) — fixa desde 2026-08-16:

> **Trave de produção:** a parcela fixa do escalão 150.001–200.000 Kz
> precisa de confirmação. Questão de direito fiscal, não de código.
>
> **Critério de saída:** recibos correctos em staging; ida a produção
> condicionada ao parecer, por decisão explícita e registada.

O motor de cálculo de IRT/INSS nasceu completo e testado a 2026-08-30/31,
com quatro valores de entrada que o utilizador confirmou directamente, mas
sempre com a mesma reserva explícita registada em cada entrada de
`pending-decisions.md`: a fonte era o próprio utilizador, **não** o Anexo I
da Lei n.º 14/25 nem parecer de fiscalista — que continuavam por obter. A
trave de produção nunca bloqueou o desenvolvimento ou o teste, só a ida a
produção.

O utilizador informou, nesta sessão, que obteve o parecer fiscal
profissional. Perguntado directamente se o parecer confirma os mesmos
valores já implementados ou traz correcções, confirmou: **os mesmos
valores, sem alteração nenhuma.**

## Decision

### Os quatro valores ficam confirmados por fonte profissional

| Valor | Confirmado pelo utilizador em | Fonte agora |
|---|---|---|
| Parcela fixa do escalão 150.001–200.000 Kz = 12.500 Kz | 2026-08-30 | Parecer fiscal profissional |
| Parcela fixa do escalão 1.500.001–2.000.000 Kz = 292.250 Kz | 2026-08-30 | Parecer fiscal profissional |
| INSS sem tecto contributivo (8%/3% sobre o bruto inteiro) | 2026-08-30 | Parecer fiscal profissional |
| Isenções de subsídio (Alimentação/Transporte, 30.000 Kz/mês cada) | 2026-08-31 | Parecer fiscal profissional |

Nenhum dos quatro mudou de valor. **Nenhuma alteração de código resulta
deste ADR** — o mecanismo (`IncomeTaxSchedule`, `SubsidyExemptionSchedule`,
`AddPayrollItem`) já estava correcto; o que muda é só a proveniência do
dado, de "confirmado pelo utilizador" para "confirmado pelo utilizador e
validado por parecer profissional".

### Critério de saída da Fase 6 cumprido

"Ida a produção condicionada ao parecer, por decisão explícita e
registada" — o parecer existe, a decisão é explícita (esta conversa) e
fica registada (este ADR + `pending-decisions.md` + `roadmap-execucao.md`
§Fase 6). `payroll` deixa de estar travado para produção.

## Consequences

### O que fica mais fácil

- A Fase 6 fecha o único critério de saída que lhe faltava. Do roteiro de
  execução original (Fases 0–8), fica só a Fase 1 por fechar por completo
  (K16 — sem TLS, depende de domínio, fora do controlo deste repositório).
- Qualquer decisão futura sobre os mesmos quatro valores já não carrega a
  reserva "fonte não-profissional" — pode citar-se o parecer directamente.

### O que fica em aberto, e é assumido

- **O parecer em si não está anexado ao repositório.** Este ADR regista
  que existe e o que confirma, pela palavra do utilizador — não é
  substituto do documento, que deve ficar arquivado fora do código (é
  registo fiscal/jurídico, não artefacto de engenharia).
- **Regras completas do Grupo B do IRT, prazos de entrega, regras de
  expatriados** continuam por confirmar — este ADR cobre só os quatro
  valores listados acima, nenhum outro.
- **Não é aprovação para emissão fiscal certificada.** O ADR-036 já
  despriorizou a certificação junto da AGT como objectivo de produto; este
  ADR não a reabre.

## Related

`state/roadmap-execucao.md` §Fase 6 (o critério de saída que este ADR
fecha), `state/pending-decisions.md` (histórico completo dos quatro
valores, incluindo as reservas agora levantadas), ADR-011 (taxas fiscais
como dados versionados, não código — é a razão de nenhuma alteração de
código ter sido precisa aqui).
