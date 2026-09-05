# ADR-053: Histórico do vínculo conta↔colaborador

## Status

Aceite (2026-09-05). Acrescenta a entidade `EmployeeAccountLink`, a tabela
`hr.employee_account_link` com migração de retroactivo, e
`GET /hr/employees/{employeeId}/account-history`.

**Não altera `Employee.UserId` nem o caminho que decide quem aprova.**

Origem: pergunta do utilizador — «e se o desligamento fosse substituído por uma
inactivação?»

## Context

O ADR-052 implementou o desligar como `DELETE`, pondo `Employee.UserId` a nulo.
A pergunta expôs duas coisas.

**Uma inconsistência de grão.** Esse era o **único** `DELETE` em 246 endpoints.
Todo o resto que parece apagar é `/deactivation`, `/cancellation`,
`/termination`. Não é violação do BR-14 — a letra fala de *entidades* sob
auditoria ou retenção legal, e nenhuma entidade era eliminada, era um campo
anulado num registo que fica intacto. Mas ia contra o padrão sem justificação.

**E um argumento melhor do que o do BR-14.** Desde o ADR-050, «que conta podia
agir por esta pessoa no dia D» passou a ser uma pergunta **forense** — é ela
que liga uma decisão de aprovação a um ser humano concreto. Com `UserId = null`,
a resposta só se reconstruía com `LIKE` sobre JSON no `previous_value` da
trilha. Funciona, e é frágil para uma pergunta que agora importa.

## Decision

### Não substituir. Acrescentar.

A forma óbvia — manter `UserId` e marcá-lo inactivo — foi **rejeitada**, e a
razão é o caminho crítico:

```
ManageApprovals → IEmployeeDirectory.FindByUserIdAsync
                → IHrStore.FindEmployeeByUserIdAsync
                → Employees.First(e => e.UserId == userId)
```

É a única consulta que decide quem pode aprovar. Torná-la condicional
(`&& e.UserLinkActive`) põe, no ponto mais sensível do sistema, a
possibilidade de um bug devolver um vínculo inactivo — e uma conta
desprovisionada recuperar poder de decisão. É exactamente a classe de falha do
ADR-050, no mesmo sítio.

Por isso:

| | Papel |
|---|---|
| `Employee.UserId` | O vínculo **activo**. Inalterado. Continua a ser o que decide quem aprova |
| `EmployeeAccountLink` | **História.** Um episódio por ligação, append-only |

A consulta de decisão não muda uma linha.

### O episódio

`EmployeeId`, `UserId`, `LinkedOn`, `LinkedByUserId`, `UnlinkedOn`,
`UnlinkedByUserId`. Aberto enquanto `UnlinkedOn` for nulo.

Ao contrário de `Employee.LinkToUser`, que é um setter sem regras, esta
entidade **tem invariantes** — e por isso vivem no domínio, com testes de
domínio: um episódio fechado não refecha, e não existe intervalo negativo.
`CobriaEm` responde à pergunta forense, fechada no início e aberta no fim: no
instante exacto do desligamento já não se podia agir.

`LinkedByUserId` nulo lê-se como **desconhecido**, não como «ninguém».

### Índices

Único filtrado em `employee_id WHERE unlinked_on IS NULL` — no máximo um
episódio aberto por pessoa.

**Não** há restrição equivalente por conta, de propósito. A unicidade que
protege quem decide continua a ser a de `employee.user_id`; dar a esta tabela
uma restrição que sugerisse o contrário convidaria alguém a passar a lê-la para
esse fim.

### Retroactivo

A migração abre um episódio por cada vínculo existente. `linked_on` vem de
`hired_on`, e isso é exacto e não estimado para a esmagadora maioria: até
2026-09-05 o vínculo **só** se podia criar na admissão. A excepção são os
poucos criados entre o ADR-051 e esta migração, para os quais fica adiantada.

Verificado contra a base local, não assumido: 35 vínculos → 35 episódios
abertos, zero divergência nos dois sentidos.

## Consequences

### Um defeito apanhado, e o que ele ensina

O histórico foi acrescentado a `LinkEmployeeAccount` e **esquecido em
`HireEmployee`** — e o vínculo pode nascer pelos dois caminhos. Quem fosse
admitido já com conta ficava fora do histórico, que é pior do que não haver
histórico nenhum: parece uma resposta completa.

Não foi o compilador nem os testes que o apanharam — foi a verificação
end-to-end, com uma invariante escrita como consulta SQL («nenhum vínculo
activo sem episódio aberto»). Nada obrigava os dois caminhos a concordar, e é
por isso que essa invariante passou a ser caso de verificação permanente, e
`HireEmployee` ganhou testes de Application que a exigem.

É a mesma forma da falha do ADR-050 outra vez: o defeito não estava numa regra,
estava em **um dos caminhos não a aplicar**.

### E um erro de teste, que o CI apanhou

Houve um caso de verificação a mais, «o retroactivo correu», que afirmava
existirem episódios sem autor. Passava na máquina de desenvolvimento e falhou
no primeiro ambiente limpo — porque numa base que nasce vazia não há nada para
retroagir, e todos os episódios têm autor por terem sido criados pelo código.

O caso afirmava o estado de uma máquina, não uma propriedade do sistema. Foi
removido, e não substituído: o que importa do retroactivo já está na invariante
«nenhum vínculo activo sem episódio aberto», que vale nas duas situações e é
ela que diz se a migração fez o que devia.

Fica registado porque é um modo de falha fácil de repetir — uma verificação que
depende de dados acumulados localmente parece mais forte do que é.

### O que isto não resolve

Não fecha o buraco de dois passos do ADR-052 — desligar e voltar a ligar
continua a ser possível para quem tenha a permissão. **Torna-o legível**, que
era já o argumento do ADR-052, agora com uma consulta de primeira classe em vez
de `LIKE` sobre JSON.

`LinkCustomerAccount` em `commercial` continua sem histórico e a sobrepor
vínculos em silêncio. Fica como estava: decisão em aberto.

### Números

- `verify-hr`: 34 → **39 casos**
- `Rivo.Hr.Application.Tests`: 15 → **25**; `Rivo.Hr.Domain.Tests`: 129 → **136**
- **1159 testes** em 28 projectos
- **247 endpoints**
