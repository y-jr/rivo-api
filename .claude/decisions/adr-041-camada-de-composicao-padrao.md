# ADR-041: Camadas de Composição — Padrão de Implementação (Fase 8)

## Status

Aceite (2026-08-31). Primeira aplicação: `Rivo.Settings` (Configurações &
Administração).

## Context

`docs/rivo-arquitetura-global-v1.md` §1.4, `domain/domain-map.md` e
`architecture/architecture.md` já resolviam, em prosa, que Dashboard
Executivo, Portal do Colaborador, Portal do Cliente, Configurações &
Administração e Analytics & IA — cinco dos catorze "módulos" do documento de
produto — não são bounded contexts: são camadas de leitura/composição sobre
os contextos reais, sem entidades nem base de dados próprias.

O que faltava era a forma concreta. Todo o resto da arquitectura — ADR-017
(contratos por módulo), ADR-018 (Minimal APIs, um grupo de rotas por
módulo), `dependency-rules.md`, os testes de `Rivo.Architecture.Tests` — foi
escrito a assumir que um "módulo" tem Domain, Application, Infrastructure e
Api. A Fase 8 (`roadmap-execucao.md`) é a primeira vez que o código encontra
essa distinção: sem forma escrita, cada uma das cinco áreas reabriria o
debate.

## Decision

**Uma camada de composição é `Application` + `Api`, nada mais.** Sem
Domain (não há agregado nem invariante própria), sem Infrastructure (não há
base de dados, connection string nem migração), sem `Contracts` (nada a
compõe a si própria hoje — ver Alternatives).

**Vive em `src/Composition/<Nome>/`, não em `src/Modules/`.** A distinção
que `domain-map.md` já fazia em prosa fica visível na árvore de ficheiros, e
não só documentada.

**Depende de outros módulos exclusivamente pelos seus assemblies de
contratos** — a mesma regra de `dependency-rules.md`, sem excepção por ser
composição. Uma camada de composição é só mais um consumidor; não muda a
regra, é a primeira vez que um consumidor não é ele próprio um bounded
context.

**Regista-se no host exactamente como um módulo** — um par
`AddXModule`/`MapXModule`, acrescentado às mesmas listas de `Program.cs`.
Sem `Infrastructure`, `AddXModule` vive em `Api` (que já referencia
`Microsoft.AspNetCore.App`, de onde vem `IServiceCollection`) em vez de
`Application`, que ficaria sem essa dependência disponível sem a inventar.

**Os assemblies seguem a convenção `Rivo.<Nome>.<Camada>`.** É o que os
testes de arquitectura usam para descobrir módulos (`RivoAssemblies.cs`,
por nome de ficheiro `.dll`, não por caminho) — a localização em
`src/Composition/` em vez de `src/Modules/` não lhes muda nada.
`dependency-rules.md` e `ProjectReferenceTests.DependenciasDeclaradas`
ganham a mesma entrada que qualquer módulo novo ganharia.

**`ProjectReferenceTests.ProjectDiscovery_FindsEveryModuleProject` deixa de
assumir Domain para todos.** A asserção "todo o módulo declarado tem, no
mínimo, Domain" deixa de ser universal — uma camada de composição não tem.
Corrigido com uma lista explícita (`CamadasDeComposicao`), mesmo padrão do
`IsentosPorDesenho` de `ConcurrencyTokenTests` (K14/ADR-019): a excepção é
nomeada, não um `if` silencioso.

**Primeira aplicação: `Rivo.Settings`** (Configurações & Administração).
Compõe `identity` (perfis de acesso e as suas permissões) e `approval`
(políticas de aprovação, por processo) num único
`GET /settings/overview`. Admin-only — não por permissão nova, mas porque
as duas permissões subjacentes (`identity.roles.read`,
`approval.policies.read`) já só pertencem a `Admin` no catálogo existente
(`AccessProfiles.Catalogue`). `identity` publica `Rivo.Identity.Contracts`
pela primeira vez (ADR-017: "criado quando o módulo tem consumidor") — o
catálogo de permissões que vivia em `Rivo.Identity.Application.Authorization`
muda-se para lá como `IdentityPermissions`, mesmo lugar de
`CommercialPermissions`, `HrPermissions` e todos os outros. `approval` ganha
um segundo contrato de leitura, `IApprovalPolicyCatalogue` — separado de
`IApprovalGateway` porque serve um propósito distinto (composição
administrativa, não submissão/estado de processo) e devolve um resumo, não
`PolicyView` com passos e aprovadores.

## Alternatives

- **Chamar os endpoints HTTP dos outros módulos (hairpin).** Rejeitado: o
  Rivo é um único deployável (ADR-001); os contratos internos (ADR-017) já
  resolvem exactamente este problema, e HTTP dentro do mesmo processo só
  acrescentaria latência, tratamento de erro e reencaminhamento de
  autenticação sem ganho nenhum.
- **Código de composição dentro de `Rivo.Api` (o host).** Rejeitado: o host
  é composition root — regista implementações concretas no contentor de DI
  — não é onde vive lógica de endpoint ou de negócio
  (`dependency-rules.md` §API, a excepção é só para DI). Tornaria também a
  camada invisível aos testes de arquitectura, que descobrem "módulos" por
  convenção de nome de assembly, e `Rivo.Api` está explicitamente excluído
  dessa descoberta.
- **Dar à camada de composição o seu próprio assembly de `Contracts`, já
  agora.** Rejeitado por agora: nada compõe uma camada de composição hoje —
  o documento de produto não aninha portais dentro de portais — e o ADR-017
  só cria um assembly de contratos "quando o módulo tem consumidor".
  Publicá-lo sem consumidor seria inventar direcção antes de haver quem a
  peça, a mesma disciplina já usada para `GET /inventory/valuation` em
  `modules/inventory.md`.

## Consequences

### O que fica mais fácil

- Dashboard Executivo, Portal do Colaborador, Portal do Cliente e
  Analytics & IA seguem o mesmo desenho sem reabrir o debate arquitectural.
- `identity` deixa de ser o único módulo sem `Contracts` — a assimetria
  (nunca teve consumidor) desaparece, e o catálogo de permissões passa a
  viver no mesmo sítio em todos os módulos.

### O que fica em aberto, e é assumido

- **Dashboard Executivo precisa de contratos de leitura que `finance` e
  `commercial` ainda não publicam** (receita, despesa, lucro, AR/AP, top
  clientes) — `Rivo.Finance.Contracts` só expõe `IBudgetAvailability`,
  `Rivo.Commercial.Contracts` só `ICustomerDirectory`. Desenhar esses
  contratos é decisão à parte, para quando esse consumidor existir.
- **Portal do Cliente muda o perfil de risco** — é superfície externa, e
  precisa de decisão de autenticação de cliente separada de `identity`
  antes de poder seguir este padrão. Fora do âmbito deste ADR.
- **Portal do Colaborador precisa de um conceito de "o próprio a ver-se a
  si próprio"** — os endpoints existentes são por permissão de perfil, não
  por identidade do recurso. Também fora do âmbito deste ADR.

## Risks

Um contrato de leitura "gordo a mais" por consumidor pode tentar engordar
até ser a `Application` inteira — mesmo risco que `dependency-rules.md`
§Imposição já regista para contratos entre módulos de negócio. Não é um
risco novo desta decisão; a vigilância é a mesma.

## Revisit When

Quando Dashboard Executivo, Portal do Colaborador ou Portal do Cliente
precisarem de um contrato de leitura mais rico do que o que `finance` e
`commercial` publicam hoje — decide-se o desenho desses contratos nesse
momento, e não se generaliza a partir deste ADR nem se especula agora.

## Related

`docs/rivo-arquitetura-global-v1.md` §1.4, `domain/domain-map.md` §Read
models, `architecture/architecture.md`, ADR-017 (contratos por módulo),
ADR-018 (Minimal APIs), `architecture/dependency-rules.md`,
`state/roadmap-execucao.md` Fase 8, `modules/inventory.md` (mesma
disciplina de "não publicar contrato sem consumidor").
