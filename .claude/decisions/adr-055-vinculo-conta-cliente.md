# ADR-055: O vínculo conta↔cliente deixa de ser substituível em silêncio

## Status

Aceite (2026-09-05). Corrige uma lacuna em `commercial`, acrescenta
`DELETE /commercial/customers/{id}/account` e
`GET /commercial/customers/{id}/account-history`, e a entidade
`CustomerAccountLink` com migração de retroactivo.

Origem: observação levantada ao escrever o ADR-051 e registada como decisão em
aberto desde então.

## Context

`LinkCustomerAccount` (ADR-043) sobrepunha um vínculo existente sem o recusar
nem o distinguir na trilha:

```csharp
cliente.LinkToUser(userId);   // sem verificar se já havia outra conta
```

O portal do cliente mudava de dono sem que nada o registasse, e a conta
anterior perdia o acesso sem explicação.

Isto foi notado ao escrever o ADR-051, e deixado por corrigir por a
consequência ser menor do que a equivalente em `hr` — uma conta de Cliente dá
acesso ao portal do cliente, não autoridade de aprovação. Continuava a ser a
mesma classe de lacuna.

## Decision

### O que mudou, e o que deliberadamente não mudou

**Não** se criou permissão dedicada. O ADR-051 divergiu do ADR-043 nesse ponto
com um argumento específico de `hr` — «uma conta ligada a um Colaborador dá o
que o Cargo confere, incluindo autoridade de aprovação» — e afirmou
explicitamente que a justificação do ADR-043 se mantinha válida em
`commercial`. Criar aqui uma permissão nova seria contradizer essa análise sem
razão nova. `commercial.customers.write` continua a ser a permissão de ligar e
desligar.

O que mudou é o comportamento:

| | Antes | Agora |
|---|---|---|
| Religar por cima | substituía em silêncio | `409` |
| Ligar a própria conta | permitido | `403` |
| Desligar | não existia | `204`, repetível |
| Histórico | nenhum | `CustomerAccountLink` |
| Trilha | só a acção | com a conta em `NewValue` / `PreviousValue` |

### A auto-ligação recusa-se por razão própria, não por simetria

Quem ligue a sua conta a um cliente **age como esse cliente no portal** — e
isso inclui submeter comprovativos de pagamento (`ADR-044`), que `finance`
confirma manualmente. Um vendedor podia submeter um comprovativo falso em nome
de um cliente seu.

`Sales` tem `commercial.customers.write`. A recusa fecha esse caminho.

### O histórico é próprio do módulo, não partilhado

`CustomerAccountLink` é uma cópia estrutural de `EmployeeAccountLink`, e isso é
deliberado. São bounded contexts distintos: um liga uma conta a quem trabalha
na empresa, outro a quem lhe compra. Pô-los no SharedKernel por terem a mesma
forma seria acoplar dois domínios por coincidência de estrutura — exactamente o
que a regra do SharedKernel mínimo existe para evitar.

`Customer.UserId` continua a ser o vínculo activo e o que o Portal do Cliente
lê para resolver «o próprio». O histórico não toca nesse caminho, pela mesma
razão do ADR-053.

### ⚠ O retroactivo usa uma sentinela, e não uma data

Em `hr` (ADR-053) o retroactivo veio de `hired_on`, e era **exacto**: até ao
ADR-051 o vínculo só se podia criar na admissão.

Aqui não há equivalente. `commercial.customer` **não tem coluna de data
nenhuma**, e o vínculo criava-se a qualquer momento. Usar a data da migração
seria pior do que não saber: diria que a conta não podia agir antes de hoje, o
que é falso, e uma consulta forense concluiria o contrário do que se passou.

`0001-01-01` diz «desde sempre, até melhor informação» — erra para o lado de
não excluir ninguém indevidamente, e é visivelmente uma sentinela e não uma
data. `linked_by_user_id` fica nulo, que se lê como desconhecido.

Verificado contra a base: 15 vínculos → 15 episódios, zero divergência nos dois
sentidos.

## Consequences

- `commercial` ganhou testes de camada Application — **o terceiro módulo**,
  depois de `approval` (ADR-050) e `hr` (ADR-053), e pela mesma razão:
  `Customer.LinkToUser` é um setter, e as regras do vínculo são todas
  orquestração.
- `verify-commercial`: 21 → **29 casos**.
- O caso 21 (sobrevivência ao reinício) teve de mudar de alvo: as verificações
  novas desligam o cliente que ele testava. Passou a verificar onde a conta
  está **agora**, e a exigir que o histórico também sobreviva.
- 1176 testes em 29 projectos.

### Uma nota sobre o método

A primeira sabotagem que fiz ao guarda de religação **não pegou** — o `perl`
não casou o padrão — e os testes passaram, o que quase me fez dar por verificado
um teste que não tinha sido. A segunda tentativa, confirmada a alterar o
ficheiro, fez falhar exactamente
`Religar_Por_Cima_E_Recusado_Nao_Substitui`.

Sabotar sem confirmar que a sabotagem se aplicou não prova nada.
