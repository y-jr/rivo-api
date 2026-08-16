# ADR-024: Testes de Arquitectura

## Status

Aceite (2026-08-16)

## Context

O risco número 1 declarado do projecto é **erosão de fronteiras**. O protótipo
acabou com cinco implementações paralelas de aprovação e duas tabelas de
auditoria quase idênticas — e a causa não foi ignorância, foi que a regra só
existia escrita.

`architecture/dependency-rules.md` §Imposição registava a situação com
precisão: desde o ADR-017, a fronteira pública é imposta pelo compilador, mas
**o que continua por rever manualmente** são os contratos a engordar e o
respeito pelas direcções declaradas.

Com o ADR-023, o CI passou a correr testes. Faltavam os testes que verificam a
arquitectura.

## Requirements

- **Facto** — Um módulo só depende de outro pelo assembly de contratos
  (ADR-017).
- **Facto** — Contratos não dependem de nada; é isso que impede ciclos.
- **Facto** — `Domain` não depende de nada fora de si.
- **Facto** — A camada API de um módulo nunca referencia `Infrastructure`; o
  host é a excepção declarada, por ser o composition root.
- **Facto** — As direcções permitidas estão tabeladas em
  `dependency-rules.md`.
- **Facto** — ADR-018 §Risks: um `MapPost` sem `RequireAuthorization` fica
  público em silêncio, e nada o detecta.
- **Inferência** — Serão catorze módulos. A verificação tem de cobrir módulos
  novos sem que alguém se lembre de os inscrever.

## Alternatives

1. **Reflexão sobre assemblies e leitura dos `.csproj`** (escolhida).
2. NetArchTest.Rules.
3. TngTech.ArchUnitNET.

As opções 2 e 3 são maduras e estão disponíveis. Foram postas de lado porque
**as regras do Rivo são, quase todas, regras sobre referências** — foi assim
que o ADR-017 as desenhou, ao fazer da fronteira pública uma referência de
projecto. Uma biblioteca de regras sobre tipos e namespaces resolveria um
problema que este projecto não tem, e traria um vocabulário próprio para
exprimir o que `GetReferencedAssemblies()` e um `XDocument` já dizem
directamente.

Pesou também o controlo das mensagens de falha: um teste de arquitectura que
falha tem de dizer **qual** referência viola **qual** regra. Aqui, cada
violação é construída à mão na forma `origem -> destino (devia ser X)`.

## Decision

**Reflexão e leitura de `.csproj`, sem biblioteca nova.** Projecto
`tests/Rivo.Architecture.Tests`, 17 testes.

### Duas fontes de verdade, deliberadamente

| Ficheiro | Verifica | Apanha |
|---|---|---|
| `ProjectReferenceTests` | referências **declaradas** nos `.csproj` | uma referência que ainda ninguém usa |
| `ModuleBoundaryTests`, `LayerDependencyTests` | o que os assemblies **usam** | uso indirecto e pacotes transitivos |

**A separação não é redundância — foi descoberta por mutação.** Acrescentar uma
referência de `Rivo.Hr.Domain` para `Rivo.Audit.Application` não fazia falhar
teste nenhum: o compilador poda referências que nenhum tipo usa, e
`GetReferencedAssemblies()` não a reportava. A referência ficava lá, à espera
de alguém a usar.

Cada regra é expressa **uma só vez**, na forma mais forte disponível. A tabela
de dependências declaradas existe num único sítio.

### O único projecto que vê tudo

`Rivo.Architecture.Tests` referencia o host, e por transitividade alcança todos
os módulos. Não é violação de fronteira — é a função dele. Referenciar o host
em vez de listar módulos evita que um módulo novo fique por verificar sem que
ninguém repare.

### Descoberta, nunca listas

Os assemblies vêm do directório de saída; os projectos, de uma varredura de
`src/`. Uma lista escrita à mão deixaria um módulo novo por verificar — e um
teste de arquitectura que silenciosamente não cobre um módulo é pior do que
não existir, porque dá confiança sem a sustentar.

Pela mesma razão, a descoberta **falha alto** se não encontrar nada: uma
colecção vazia faria todas as asserções passar por vacuidade.

### Autorização de endpoints

`EndpointAuthorizationTests` mapeia os módulos exactamente como o host faz e lê
os metadados resultantes. **Não sobe a aplicação:** os endpoints e a sua
autorização ficam registados no momento do mapeamento, antes de haver serviços,
base de dados ou pedidos.

Endpoints públicos vivem numa lista explícita — `login`, `register`, `health`.
Abrir um endpoint ao mundo passa a ser uma alteração visível a esse ficheiro,
em vez da ausência silenciosa de uma linha noutro. Um segundo teste garante que
a lista não cria entradas mortas.

## Verificação de que os testes têm dentes

Como exige `standards/testing.md`, verificado por mutação em 2026-08-16:

| Mutação | Resultado |
|---|---|
| Remover `.RequireAuthorization` de `GET /audit/entries` | Falha, nomeando `GET /audit/entries` |
| `Rivo.Hr.Domain` a referenciar `Rivo.Audit.Contracts` | Falha só `Domain_ReferencesNothing` — correcto, porque `Hr → Audit` é direcção declarada e via contratos |
| `Rivo.Hr.Domain` a referenciar `Rivo.Audit.Application` | **Não falhava.** Foi o que motivou `ProjectReferenceTests` |

Todas as alterações foram revertidas.

## Consequences

Facilita:

- As direcções entre módulos passam a ser verificadas, não vigiadas.
- Um endpoint sem autorização falha o build em vez de ficar público.
- A tabela de `dependency-rules.md` ganha um equivalente executável: mudá-la
  obriga a mudar o teste, e mudar o teste é uma decisão visível em revisão.

Dificulta / exige:

- A tabela de dependências declaradas tem de ser actualizada a cada módulo
  novo — deliberado, é o momento em que a direcção é aceite.
- `EndpointAuthorizationTests` regista descritores de serviço só para a
  inferência de parâmetros dos Minimal APIs funcionar. É andaime, e está
  documentado como tal.

## Risks

- **Os testes cobrem estrutura, não semântica.** Que um contrato seja
  estreito, e que não cresça até ser a `Application` inteira, continua a ser
  revisão humana — o ADR-017 já o dizia e isto não o altera.
- **A lista de endpoints públicos pode ser relaxada por conveniência** para
  fazer um teste passar. É o modo de falha mais provável, e a mitigação é
  revisão: uma entrada nova nessa lista é uma decisão de segurança.
- **Módulos por implementar não estão na tabela**, e o teste falha ao
  encontrá-los. É intencional: obriga a declarar a direcção antes de escrever
  o primeiro tipo.

## Revisit When

- As regras deixarem de ser exprimíveis sobre referências e passarem a exigir
  análise de tipos ou de namespaces — aí uma biblioteca passa a valer a pena.
- O número de módulos tornar a leitura dos `.csproj` lenta ao ponto de pesar
  no job que bloqueia PRs.
- Surgir necessidade de verificar regras de persistência (ADR-010) — FKs entre
  schemas não são verificáveis por reflexão, e exigiriam inspecção da base de
  dados, o que é teste de integração.

## Related

- [ADR-017](adr-017-contratos-por-modulo.md) — a fronteira que estes testes
  verificam
- [ADR-018](adr-018-minimal-apis-e-routing.md) §Risks — o endpoint sem
  autorização
- [ADR-022](adr-022-stack-de-testes.md), [ADR-023](adr-023-pipeline-ci.md)
- [architecture/dependency-rules.md](../architecture/dependency-rules.md)
