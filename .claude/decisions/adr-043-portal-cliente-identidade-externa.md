# ADR-043: Portal do Cliente — Identidade Externa

## Status

Aceite (2026-09-03). Decisão do utilizador, em resposta directa às duas
perguntas que bloqueavam o item seguinte da ordem fixada para a Fase 8.

## Context

`roadmap-execucao.md` fixou a ordem do resto da Fase 8: contratos de leitura
Finance/Commercial → Dashboard Executivo → **decisão de identidade
externa** → Portal do Cliente → Analytics & IA. Os três primeiros estão
feitos. O Portal do Cliente (`docs/rivo-suite-descricao-modulos.md` §12) é o
único, de toda a Fase 8, que muda o perfil de risco: é a primeira superfície
pensada para alguém que não é colaborador da organização.

Duas perguntas, por esta ordem, bloqueavam o resto:

1. Como é que um Cliente se autentica?
2. Dado que se autentica, como é que o sistema sabe **a qual**
   `commercial.Customer` essa conta corresponde?

Sem resposta às duas, "Portal do Cliente" não tinha por onde começar — ao
contrário das outras camadas de composição desta fase, que compunham
contratos já publicados sobre uma identidade já resolvida (ADR-042).

`docs/rivo-suite-descricao-modulos.md` §Perfis de Acesso fixa **"7 perfis de
acesso predefinidos"**, com uma tabela fechada — nenhum deles é externo.
Este ADR estende esse conjunto para oito; não reescreve o documento-fonte,
que continua a descrever os sete internos correctamente. A extensão é
registada aqui, como `CLAUDE.md` manda.

## Decision

### 1. Conta própria em `identity`, com um Perfil de Acesso novo — `Cliente`

O Cliente regista-se por `POST /identity/register`, como qualquer conta
hoje — mesmo mecanismo, mesmo JWT, mesmas sessões revogáveis. Ganha o
oitavo Perfil de Acesso, `Cliente`, em vez de um dos sete internos.

**Alternativas rejeitadas:**

- **Credenciais próprias, fora de `identity`.** Duplicaria autenticação —
  duas formas de emitir token, duas de repor password — para isolar
  clientes de colaboradores por um desenho que o vínculo (§2, abaixo) já
  isola sem duplicar nada.
- **Sem password, por link temporário.** Depende de um canal de entrega
  fiável (K13, envio de e-mail, continua em aberto) e desenharia sessões
  efémeras, um segundo modelo de sessão a par do que já existe. Fica como
  opção se o envio de e-mail se fechar e a fricção da password se revelar
  o problema real.

### 2. A ligação a `commercial.Customer` é sempre manual, nunca por auto-declaração

O Cliente regista-se sem perfil nenhum. **Sales/Admin liga a conta a um
`Customer` já existente**, por um endpoint novo
(`POST /commercial/customers/{id}/account`) — só depois disso o perfil
`Cliente` é atribuído.

Mesmo desenho do ADR-042 (`Employee.UserId`), com os papéis invertidos: lá
o colaborador já existia e a conta chegava depois pela contratação; aqui o
cliente já existe (registado por Sales quando a relação comercial começou)
e a conta chega depois, por auto-registo.

**Alternativa rejeitada:** auto-ligação pelo NIF indicado no registo. O NIF
é informação pública — quem o sabe não prova que representa a empresa.
Aceitar a auto-declaração deixaria qualquer pessoa reclamar a conta de
outra empresa, só por saber o seu NIF.

### O que isto resolve, e o que continua a depender de `2`

Com a ligação por identificador (`Customer.UserId`, único quando
preenchido — mesmo desenho de `Employee.UserId`), as perguntas que
ficariam por resolver resolvem-se pela mesma via:

- **Isolamento entre clientes.** "Ver só os meus dados" não é uma
  permissão nem uma vista com filtro — é a mesma regra de contexto do
  ADR-042: o Portal do Cliente resolve "o próprio Cliente" a partir de
  `CurrentUser`, nunca aceita um `customerId` no pedido. Sem `Customer`
  ligado, 403 — nunca adivinha, mesma disciplina.
- **Recuperação de conta.** Reutiliza `/identity/me/password` e o resto do
  fluxo que já existe — não é capacidade nova.
- **Autorização dentro do portal** (que endpoints o perfil `Cliente` vê) é
  decisão do Portal em si, não desta identidade — fica para quando o
  Portal for construído.

### O que fica deliberadamente por resolver aqui

- **MFA.** Mesmo estado de todos os perfis — gap já registado, não
  específico do Cliente.
- **Se uma conta pode ser Cliente e Employee ao mesmo tempo.** Não há
  nada no desenho que o impeça estruturalmente (são dois campos
  `UserId?` independentes, em módulos diferentes), e nada nesta decisão
  o exige nem o proíbe. Não inventado por não ter caso de uso concreto
  hoje.

## Consequences

### O que fica mais fácil

- O Portal do Cliente em si (dashboard financeiro, facturas, extracto —
  `docs/rivo-suite-descricao-modulos.md` §12) passa a ter uma identidade
  resolvida sobre a qual construir, exactamente como o Portal do
  Colaborador teve depois do ADR-042.
- `Rivo.Commercial.Contracts` ganha o mesmo padrão de segundo sentido de
  leitura que `Rivo.Hr.Contracts` ganhou (`FindByUserIdAsync`), se algum
  dia outro consumidor precisar de "a quem pertence esta conta" do lado
  comercial.

### O que fica em aberto, e é assumido

- **Só a ligação está feita, não o Portal.** `LinkCustomerAccount` e
  `Customer.UserId` existem; o próprio Portal (`GET /portal-cliente/me`
  ou equivalente, e tudo o que `docs/` §12 descreve) é o próximo item da
  Fase 8, sem código ainda.
- **O perfil `Cliente` nasce vazio no catálogo** (`AccessProfiles.cs`) —
  mesmo estado em que `AssetManager`/`ProjectManager` estiveram antes de
  `inventory`/`fleet`/`projects` ganharem código. Ganha permissões quando
  o Portal existir.
- **Nenhum canal de convite/notificação avisa o Cliente de que a conta foi
  ligada.** `notifications` já existe e cobre o padrão (atribuição de
  perfil enfileira notificação); estender-lhe o mesmo para a ligação de
  conta é trabalho separado, não bloqueante.

## Related

ADR-042 (mesmo padrão, para `Employee`), ADR-041 (padrão de camada de
composição), ADR-010 (referência por identificador entre contextos),
ADR-004 (Identity ≠ Employee/Customer), ADR-014 (RBAC por permissões),
`modules/commercial.md`, `domain/domain-map.md` §Read models,
`state/roadmap-execucao.md` Fase 8,
`docs/rivo-suite-descricao-modulos.md` §12 e §Perfis de Acesso.
