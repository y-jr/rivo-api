# ADR-022: Stack de Testes e Estrutura da Camada de Domínio

## Status

Aceite (2026-08-15)

## Context

[standards/testing.md](../standards/testing.md) fixa o que testar e por
camada, e diz explicitamente que **o domínio é a prioridade**. Fechava com
"Frameworks de teste por camada — em aberto".

Cinco módulos depois, a única verificação existente eram seis suites
PowerShell caixa-preta por HTTP (66 casos). Exercitam o sistema montado, o que
tem valor próprio, mas **não testam invariantes**: uma regra apagada dentro do
domínio passa despercebida se a resposta HTTP não mudar.

Era o risco 2 de [project-state.md](../state/project-state.md), e o único
nível sem cobertura nenhuma.

## Requirements

- **Facto** — Invariantes testadas em isolamento: sem infraestrutura, sem
  framework, sem base de dados. Se uma regra precisa de base de dados para ser
  testada, vazou do domínio — e isso é defeito de arquitectura, não de teste.
- **Facto** — Um módulo nunca alcança as internals de outro (ADR-017).
- **Facto** — Código em inglês; comentários em português.
- **Facto** — As invariantes de `approval` (segregação, alçadas,
  anti-fraccionamento) têm de viver e ser testadas no domínio (ADR-008).
- **Inferência** — Serão catorze módulos. A estrutura tem de escalar sem que
  um módulo novo obrigue a mexer nos existentes.

## Constraints

- .NET 10. **No SDK 10, o VSTest deixou de ser suportado pelo
  Microsoft.Testing.Platform** — o que condiciona a escolha de runner mais do
  que é evidente à partida. Ver Alternatives.

## Alternatives

### Framework

1. **xUnit v2.9.3** (escolhida).
2. xUnit v3 (4.0.0).
3. NUnit ou MSTest.

O **xUnit v3 foi a primeira escolha** e foi abandonado por atrito real, não
por preferência: assenta no Microsoft.Testing.Platform, e no SDK 10 o
`dotnet test` recusa-se a correr essa combinação sem opt-in explícito. Três
formas documentadas de o activar — `dotnet.config` com `[dotnet.test:runner]`,
a variante `[dotnet.test.runner]`, e a propriedade MSBuild
`TestingPlatformDotnetTestSupport` — falharam todas com o mesmo erro nesta
versão do SDK.

**A stack de testes não pode ser o que luta com o toolchain no primeiro dia.**
O xUnit v2.9.3 é o que `dotnet new xunit` produz neste SDK e corre sem
configuração nenhuma.

A opção 3 é equivalente em capacidade. xUnit é o que o template do SDK produz,
o que torna a escolha a de menor surpresa para quem chegar.

### Biblioteca de asserções

1. **Nenhuma — `Assert` do xUnit** (escolhida).
2. FluentAssertions.
3. Shouldly.

FluentAssertions mudou para licença comercial a partir da v8, o que a torna
uma decisão com custo por programador — desproporcionado para melhorar a
sintaxe de asserções.

Shouldly é Apache-2.0 e seria defensável. Rejeitada por ser dependência que
não resolve problema nenhum: o `Assert` do xUnit chega para tudo o que estes
testes fazem, e [CLAUDE.md](../CLAUDE.md) manda não introduzir abstracções sem
justificação.

## Decision

**xUnit v2.9.3, sem biblioteca de asserções.**

### Um projecto de teste por domínio de módulo

```
tests/Modules/<Módulo>/Rivo.<Módulo>.Domain.Tests/
```

Espelha `src/`. **Não há um projecto único `Rivo.Domain.Tests`** que
referencie os catorze domínios — seria o sítio onde todos os módulos se
encontram, exactamente o que a arquitectura proíbe. Um projecto de teste
referencia **um** domínio e mais nada.

### `tests/Directory.Build.props` carrega o que é comum

Versões dos pacotes, `TargetFramework`, `Nullable`, `IsPackable=false` e
`TreatWarningsAsErrors=true`. Um projecto de teste novo é só um `.csproj` com
a referência ao módulo que testa.

Sem isto, a versão do xUnit ficaria repetida em catorze ficheiros e divergiria
sozinha — o mesmo problema que a gestão central de versões resolve para
`src/`, e que **continua por fazer lá**.

### Nomes

`MethodUnderTest_Scenario_ExpectedOutcome`, em inglês por serem
identificadores de código. O comentário que explica *porque é que a regra
existe* vai em português, como no resto do projecto.

### O que um teste de domínio pode tocar

Só o `Domain` do seu módulo. Sem `DbContext`, sem `HttpContext`, sem
`IServiceCollection`, sem ficheiros. Um teste de domínio que precise de
qualquer um deles está a assinalar que a regra vazou da camada.

### Asserir comportamento, não implementação

Excepção deliberada: a imutabilidade de `AuditEvent` é verificada por
**reflexão** (ausência de setters públicos e de métodos de instância). Parece
testar implementação, mas é a única forma de testar a invariante real — um
teste que criasse um evento e verificasse valores passaria na mesma se alguém
acrescentasse um setter. É a garantia estrutural de BR-10 no caminho
aplicacional; a garantia ao nível da base de dados continua em falta (K9).

## Consequences

Facilita:

- **100 testes de domínio a correr em menos de 2 segundos**, sem Docker e sem
  base de dados. Podem correr a cada gravação de ficheiro.
- As fronteiras de compilação do ADR-017 passam a ser exercitadas também pelos
  testes.
- Regras contra-intuitivas ficam fixadas — as fronteiras de vigência, a
  idempotência das revogações, o recuo exponencial — para ninguém as
  "corrigir".

Dificulta / exige:

- Um projecto de teste por módulo: catorze projectos no fim.
- `TreatWarningsAsErrors` obriga a limpar avisos que noutros projectos
  passariam.

## Verificação de que a suite tem dentes

`standards/testing.md` avisa: *"uma suite que passa com a regra de negócio
apagada não está a testar a regra."*

Verificado por mutação em 2026-08-15: removida a verificação de estado de
`PositionAssignment.IsEffectiveAt`, o teste
`IsEffectiveAt_PendingAssignmentInsideItsPeriod_GrantsNothing` falhou, e mais
nenhum. A alteração foi revertida. **É esta a invariante que fecha a escalada
de privilégios do ADR-015** — se passasse com a regra apagada, submeter o
pedido bastaria para ganhar autoridade de aprovação.

Repetir este exercício sempre que se acrescente um teste a uma invariante
crítica.

## Risks

- **Cobertura a estagnar nos módulos existentes** enquanto os novos nascem sem
  testes. Mitigação: o passo 8 do fluxo em [CLAUDE.md](../CLAUDE.md) já exige
  testes como parte de terminar uma funcionalidade; o CI é o que o torna
  verificável, e ainda não existe.
- **Testes a acompanhar a implementação em vez da regra** — asserir o que o
  código faz, não o que devia fazer. Detecta-se por mutação, não por leitura.
- **Falsa sensação de segurança:** 100 testes de domínio não cobrem
  orquestração, persistência nem autorização. As camadas Application e
  Infrastructure continuam sem cobertura, e a autorização declarada nos
  endpoints (ADR-018) não é verificada por nada.

## Revisit When

- O opt-in do Microsoft.Testing.Platform estabilizar no SDK — reabre a
  migração para xUnit v3, que é a linha em desenvolvimento activo.
- For preciso teste de integração com infraestrutura real: é decisão à parte
  (Testcontainers é o candidato), e **não substitui** o teste de domínio.
- For preciso teste de arquitectura para impor as fronteiras: também decisão à
  parte, e continua em aberto.

## Related

- [ADR-008](adr-008-segregacao-funcoes.md) — exige invariantes testadas no
  domínio
- [ADR-015](adr-015-atribuicao-cargo.md), [ADR-017](adr-017-contratos-por-modulo.md),
  [ADR-018](adr-018-minimal-apis-e-routing.md)
- [standards/testing.md](../standards/testing.md)
