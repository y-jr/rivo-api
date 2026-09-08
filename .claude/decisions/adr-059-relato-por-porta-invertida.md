# ADR-059: O relato fiscal lê os módulos por porta invertida

## Status

Aceite (2026-09-08). Primeira aplicação: a tabela de clientes do SAF-T AO.

## Context

`docs/rivo-fiscal-saft-ao-v1.md` §2 mapeia cada secção do SAF-T ao módulo que
a possui — `Customer` a `commercial`, `Supplier` a `procurement`, `Product` a
`inventory`, o razão a `finance` — e diz, sem ambiguidade:

> `fiscal` **lê** todos estes módulos para gerar a exportação.

`modules/fiscal.md` distingue as duas direcções e chama-lhes **determinação**
(transaccionais → `fiscal`, «eles perguntam, `fiscal` responde») e **relato**
(`fiscal` → transaccionais). `docs/rivo-arquitetura-global-v1.md` §1.5 confirma
a segunda.

O problema não é a direcção — é materializá-la como referência de projecto.

`commercial` vai depender de `fiscal` para determinar o imposto da venda
(ADR-011). Se `fiscal` também referenciar `commercial`, forma-se
`fiscal ↔ commercial`, e `Modules_HaveNoDependencyCycles` recusa-o —
deliberadamente, porque esse teste verifica ciclos **mesmo quando passam por
contratos**. O ADR-017 resolve a compilação de `A → B.Contracts` com
`B → A.Contracts`; não resolve o desenho, e o teste existe para dizer isso.

A colisão não é hipotética. É a mesma que o ADR-015 §R1 previu para
`hr ↔ approval` e a que o ADR-034 fechou duas vezes.

## Decision

`fiscal` declara o que precisa de ler numa porta sua — `ISaftMasterData`, em
`Rivo.Fiscal.Application/Abstractions` — e o **composition root** liga-a aos
contratos dos módulos que possuem os dados.

```
Rivo.Fiscal.Application        declara ISaftMasterData
Rivo.Api/Composition           SaftMasterData : ISaftMasterData
                               → ICustomerDirectory  (commercial)
                               → ... procurement, inventory, finance
```

`fiscal` continua a não referenciar módulo nenhum além de `audit`.

### Porquê esta forma e não uma camada de composição

Foi a primeira hipótese: um `SaftExport` ao lado de `EmployeePortal` e
`Dashboard`, que juntasse os dados e chamasse `fiscal`. Evita o ciclo tão bem
como esta.

Perde noutro sítio. A forma do ficheiro SAF-T é conhecimento fiscal: a ordem
dos elementos, o que é obrigatório, que `AccountID` admite `"Desconhecido"`.
Numa camada de composição, esse conhecimento sai de `fiscal` — e `fiscal` deixa
de ser o módulo que sabe o que a AGT exige, que é a única coisa que ele é.

A porta invertida mantém as duas propriedades: `fiscal` sabe o formato, e não
sabe onde vivem os dados.

### O que a porta não pede

`SaftCustomer` não traz `AccountID` nem `SelfBillingIndicator`, embora o XSD
exija ambos. Nenhum é facto de `commercial`:

- `AccountID` é a conta corrente no plano de contas — de `finance`, e não
  existe (ADR-037: não se inventa o PGC angolano). Vai a `"Desconhecido"`, que
  é o valor que a documentação do próprio elemento manda usar nesse caso.
- `SelfBillingIndicator` diz se há autofacturação. O Rivo não a faz. Vai a
  `"0"`.

Ambos são preenchidos por `fiscal`, e ambos são verdade. Pedi-los a quem não os
sabe seria convidar a inventá-los.

## Consequences

### O identificador do cliente deixa de ser o `Guid`

`CustomerID` admite 30 caracteres; um `Guid` canónico tem 36 e a forma "N" tem
32. A conversão usada é Base64 sem preenchimento — 22 caracteres, **sem
perda**.

⚠ **Truncar seria o erro fácil e o pior possível.** O XSD tem
`CustomerIDConstraint` sobre este campo porque é por ele que as facturas
referenciam o cliente: dois identificadores iguais não dão um erro, dão um
ficheiro que atribui as facturas de um cliente a outro. Há um caso de teste
sobre 5000 identificadores exactamente para fixar isto.

### A exportação enumera clientes inactivos

`ICustomerDirectory.ListAllAsync` inclui-os, e não é opção. Um cliente
desactivado hoje pode ter sido facturado em Março, e o XSD exige que todo o
documento referencie master data presente no ficheiro. Deixá-los de fora
produziria referências penduradas — e a desactivação existe precisamente
porque BR-14 proíbe eliminar quem tem documentos emitidos.

### A porta cresce; o sítio onde se liga não

Fornecedores, produtos, plano de contas e tabela de taxas entram como métodos
novos em `ISaftMasterData` e como dependências novas de `SaftMasterData`. É de
propósito que seja uma classe só: é aí que se vê, de uma vez, tudo o que a
exportação lê.

### Não há teste de registo

Se `ISaftMasterData` não estiver registado, `GET /fiscal/saft` falha em tempo
de pedido e não de arranque — como acontece com `IPaymentApproval` e as outras
portas invertidas, que também não têm um. A verificação que existe é ponta a
ponta, contra a API a correr. Fechar esta janela para todas as portas de uma
vez é trabalho por fazer, e não pertence a este ADR.
