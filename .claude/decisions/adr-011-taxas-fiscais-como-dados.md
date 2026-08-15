# ADR-011: Taxas e Escalões Fiscais são Dados com Vigência Temporal, Nunca Código

## Status

Aceite

## Context

O levantamento das regras fiscais angolanas
(`docs/rivo-fiscal-regras-angola-v1.md`) revelou um padrão que é mais
importante do que os números concretos:

- O limite de isenção do IRT passou de 100.000 para 150.000 Kz de um ano
  para o outro, pela Lei do OGE.
- O número de escalões do IRT foi reduzido na mesma lei.
- O INSS tem alteração pendente e noticiada de 8%/3% para 10%/5%.
- A Lei n.º 14/25 introduziu uma taxa reduzida de IVA de 5% que não existia.
- Os limiares dos regimes de IVA determinam mudança obrigatória de regime,
  com prazo legal.

**Estas regras mudam anualmente, por lei orçamental.** Qualquer desenho que
as trate como constantes obriga a alterar código, testar e fazer deploy
todos os anos — e, pior, torna impossível recalcular ou auditar um período
anterior com as regras que estavam em vigor à data.

O XSD do SAF-T AO já antecipa isto: `TaxTableEntry` inclui
`TaxExpirationDate`.

## Requirements

- **Facto** — SAF-T AO exige exportação de períodos passados com a
  informação fiscal desses períodos.
- **Facto** — Auditoria com retenção de 10 anos (BR-11); um recibo de
  vencimento de 2026 tem de continuar explicável em 2034.
- **Facto** — `TaxTableEntry` do SAF-T tem `TaxExpirationDate`.
- **Facto** — Lei n.º 14/25 alterou escalões de IRT e isenção com efeito a
  01/01/2026.
- **Inferência** — Correcções retroactivas de folha salarial exigem aplicar
  as taxas vigentes à data do facto, não as actuais.

## Constraints

- `fiscal` é a fonte autoritativa das regras fiscais; nenhum outro módulo
  pode implementar regras de imposto por sua conta.
- As regras concretas ainda não estão verificadas — ver
  `docs/rivo-fiscal-regras-angola-v1.md`.

## Alternatives

1. **Dados de referência versionados com vigência temporal, em `fiscal`.**
2. Constantes em código, alteradas por deploy anual.
3. Ficheiro de configuração por ambiente.
4. Motor de regras genérico configurável em runtime.

A opção 2 impede recalcular períodos passados e transforma uma alteração
legislativa previsível num evento de engenharia.

A opção 3 partilha o mesmo defeito: a configuração é o estado *actual*, sem
histórico. Não permite responder a "que taxa estava em vigor em Março".

A opção 4 é sobre-engenharia — as regras fiscais angolanas não são
arbitrárias nem definidas pelo utilizador; são tabelas com vigência. Um
motor de regras genérico é complexidade sem requisito, e contraria a mesma
disciplina de âmbito de ADR-007.

## Trade-offs

A opção 1 exige modelar vigência e resolver "que versão se aplica a esta
data" em todos os cálculos. Em troca, alterações legislativas passam a ser
entrada de dados, e o recálculo histórico torna-se possível por construção.

## Decision

Taxas, escalões, limiares e códigos de isenção são **dados de referência
versionados, propriedade de `fiscal`**, com **vigência temporal explícita**.

Regras vinculativas:

1. **Nenhuma taxa, escalão ou limiar fiscal em código.** Nem constantes, nem
   `enum`, nem ficheiro de configuração sem histórico.
2. Toda a entidade de regra fiscal tem **`vigente_desde` e `vigente_ate`**
   (aberto no fim para a versão corrente).
3. Toda a determinação fiscal é feita **à data do facto gerador**, não à data
   do cálculo. Uma correcção emitida em 2027 sobre um facto de 2026 aplica as
   regras de 2026.
4. O **instrumento legal** (ex.: "Lei n.º 14/25") é registado com cada
   versão de regra, para rastreabilidade e auditoria.
5. Introduzir uma nova versão de regra é **operação de dados auditada**
   (BR-13), não um deploy.
6. Alterações a regras fiscais são de acesso restrito e auditadas com a
   mesma disciplina que transacções de negócio.

Aplica-se a: escalões e taxas de IRT, taxas e isenções de IVA, limiares de
regime de IVA, taxas de INSS, taxas de retenção na fonte, e mapeamento
situação legal → `TaxExemptionCode`.

## Consequences

Facilita:

- Alteração legislativa anual passa a ser entrada de dados verificada, não
  alteração de código.
- Recálculo e reemissão de períodos passados com as regras correctas.
- Exportação SAF-T de exercícios anteriores fica correcta por construção.
- Auditoria pode responder a "porquê este valor" com o instrumento legal
  aplicado.

Dificulta / exige:

- Todo o cálculo tem de resolver a versão aplicável à data — não pode ler
  "a taxa actual".
- Modelo mais complexo do que constantes.
- Processo de entrada e verificação das novas regras antes de cada exercício.

## Risks

- **Regra sem vigência definida**, aplicada a datas erradas. Mitigação:
  vigência obrigatória no modelo, não opcional.
- **Cálculo que lê a versão corrente em vez da versão à data do facto.**
  É o erro mais provável. Mitigação: cobertura de teste explícita para
  recálculo retroactivo — ver
  [standards/testing.md](../standards/testing.md).
- **Dados fiscais errados introduzidos sem verificação profissional.**
  Mitigação: alterações auditadas e de acesso restrito; o levantamento
  provisório está explicitamente marcado como não implementável.

## Revisit When

- Surgir requisito de regras fiscais definidas pelo utilizador em runtime
  (reabriria a alternativa 4).
- O Rivo passar a operar noutra jurisdição, exigindo regras por jurisdição
  além de por data.

## Related

- `docs/rivo-fiscal-regras-angola-v1.md`
- `docs/rivo-fiscal-saft-ao-v1.md` (`TaxTableEntry.TaxExpirationDate`)
- [modules/fiscal.md](../modules/fiscal.md),
  [modules/payroll.md](../modules/payroll.md)
- [ADR-007](adr-007-approval-supporting-domain.md) — mesma disciplina de
  âmbito: não construir motor genérico sem requisito
