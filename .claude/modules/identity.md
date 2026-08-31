# identity — Identidade & Acesso

**Classificação:** generic domain. Fundacional — quase tudo depende dele,
ele não depende de quase nada.

## Responsabilidade

Autenticação e autorização. Responde a duas perguntas: *"é mesmo esta
pessoa?"* e *"o que é que este utilizador pode ver e fazer no sistema?"*

Split deliberado (ADR-004):

- **Autenticação = infraestrutura.** Não tem regras de negócio. Delegável a
  um provider.
- **Autorização/RBAC = domínio partilhado.** "Que papéis pode esta pessoa
  acumular no mesmo processo" é regra de negócio explícita.

## Conceitos

| Conceito | Notas |
|---|---|
| Utilizador | email, hash de password, MFA activo, estado |
| Perfil de Acesso | catálogo: Admin, Manager, Finance, HR, Sales, Asset Manager, Project Manager |
| Atribuição de Perfil | um utilizador pode ter mais do que um perfil |
| Sessão | criada em, expira em, IP; suporta expiração por inactividade |

## Possui

Utilizador, Perfil de Acesso, Atribuição de Perfil, Sessão.

## Não possui

**Cargo organizacional** ("Director Financeiro", "Chefe de Departamento",
"DAF", "CEO", "CFO"). Cargo pertence a `hr` (ADR-005).

Esta separação é deliberada e não negociável: evita que a autorização se
torne um segundo ponto de dados organizacionais.

| Dimensão | Responde a | Dono |
|---|---|---|
| **Perfil de Acesso** | "O que este utilizador pode ver/fazer?" | `identity` |
| **Cargo** | "Que posição organizacional ocupa?" | `hr` |

## Depende de

Nada. É fundacional.

## Consumido por

Todos os módulos.

## Contratos publicados

- Identidade do utilizador autenticado (actor corrente).
- Verificação de permissão por perfil.
- **`IAccessProfileCatalogue`** (`Rivo.Identity.Contracts`, desde
  2026-08-31) — os sete Perfis de Acesso e as permissões de cada um.
  Primeiro consumidor: `Rivo.Settings` (Configurações & Administração,
  ADR-041). O catálogo de permissões (`IdentityPermissions`) mudou-se para
  este assembly no mesmo dia — mesmo lugar de `HrPermissions`,
  `CommercialPermissions` e todos os outros; até aqui vivia em
  `Rivo.Identity.Application.Authorization` só porque `identity` nunca tinha
  tido consumidor externo.

## Não pode

- Modelar Cargo, Departamento ou qualquer estrutura organizacional.
- Guardar dados de emprego — isso é `hr`.
- Resolver aprovadores — isso é `approval`, usando Cargo de `hr`.
- Ser o local onde vive a segregação de funções — essa invariante é de
  `approval` (ADR-008).

## Regras de negócio

- Autenticação individual por utilizador. Sem contas partilhadas.
- **MFA obrigatório** para qualquer perfil com poder de aprovação ou de
  execução financeira.
- Password com política robusta e hash forte (bcrypt/argon2), nunca
  reversível.
- Sessões com expiração por inactividade (referência de partida: 15 min
  para perfis decisórios).
- Sessão única reforçada, configurável, para perfis decisórios sensíveis.
- Um utilizador **pode** acumular vários perfis. O que não pode é intervir
  mais do que uma vez, em papéis conflituantes, no mesmo processo (BR-4) —
  verificação que pertence a `approval`.
- Alterações a perfis e permissões são auditadas (BR-13).

## Perguntas em aberto

- Provider de autenticação e mecanismo concreto de MFA.
- Expiração de sessão: uniforme ou por perfil?
- Os 7 perfis do documento de produto são suficientes, ou é preciso
  granularidade por operação além da visibilidade por módulo?

## Estado

**Implementado.** Autenticação por JWT bearer com sessão persistida e
revogável (ADR-012, ADR-013), catálogo dos sete Perfis de Acesso semeados com
permissões como role claims (ADR-014), e bootstrap idempotente do Admin e do
decisor iniciais por configuração (ADR-016).

**Dois caminhos de autenticação:** password e Google (ADR-032). O segundo
recebe um ID token do frontend, valida-o contra as chaves públicas da Google e
desagua na **mesma** sessão persistida — o Google diz quem a pessoa é, o Rivo
continua a ser dono da sessão, do IP na trilha e da revogação.

O caminho federado **não cria contas**: uma identidade Google válida sem conta
Rivo correspondente é recusada, porque a existência de uma conta é acto
deliberado de quem administra (ADR-016). A primeira entrada liga a identidade
à conta com o mesmo e-mail, e só quando a Google confirma ser dono desse
endereço.

Verificado em `scripts/verify-bootstrap.ps1` (9 casos) e
`scripts/verify-authorization.ps1` (8 casos).

### `Contracts` desde 2026-08-31

Até 2026-08-31, `identity` era o único módulo implementado sem assembly de
contratos — o ADR-017 manda criá-lo **quando houver consumidor**, e não
havia: os outros módulos lêem o actor do token, não de `identity`.
`Rivo.Settings` (ADR-041, Configurações & Administração) é o primeiro —
`Rivo.Identity.Contracts` nasceu para isso, com `IAccessProfileCatalogue` e
o catálogo de permissões (`IdentityPermissions`, mudado de
`Rivo.Identity.Application.Authorization` para aqui).

O sentido inverso já acontecia antes, e continua: `identity` consome
`Audit.Contracts`,
`Hr.Contracts` e `Documents.Contracts` para compor o catálogo de permissões —
cada módulo declara **que permissões existem**, `identity` decide **quem as
tem**.

### Fora do implementado

| Omitido | Porquê |
|---|---|
| **Permissões de cinco dos sete perfis** | `Admin` e `HR` estão povoados; `Manager`, `Finance`, `Sales`, `AssetManager` e `ProjectManager` estão vazios porque dependem de módulos de negócio que não existem. Inventá-las seria adivinhar |
| **Refresh token** | Expirada a sessão, o utilizador volta a autenticar-se. Revisitar se a duração se revelar incómoda |

### ⚠ Requisitos por satisfazer

- **Expiração por inactividade não existe** — só há expiração absoluta. A
  regra de negócio acima fixa 15 minutos como referência para perfis
  decisórios; **esse requisito não está cumprido**. Implementá-lo exige
  escrita por pedido ou estratégia de janela.
- **MFA não existe.** A regra de negócio torna-o obrigatório para perfis com
  poder de aprovação ou execução financeira. O mecanismo concreto está em
  aberto. **O login com Google não o resolve, e é fácil pensar que sim:** a
  2FA da conta Google é da Google, e o Rivo não a consegue exigir nem sequer
  verificar a partir do ID token. O requisito continua por satisfazer, agora
  com aparência de resolvido (ADR-032).
- **K8** — o IP registado na sessão é o do proxy, não o do cliente, o que
  esvazia BR-9. Ver [state/known-issues.md](../state/known-issues.md).
