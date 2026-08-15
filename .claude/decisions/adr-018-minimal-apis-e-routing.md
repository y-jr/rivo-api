# ADR-018: Framework Web/API — Minimal APIs e Convenções de Routing

## Status

Aceite (2026-08-15).

**Registo retroactivo.** A decisão foi tomada e implementada em 2026-08-10, ao
montar `identity`, e replicada nos quatro módulos seguintes. Este ADR não
reabre nada — documenta o que já está a correr, porque estava listado como
"em aberto" quando não estava, e isso convidava a re-litigá-lo.

## Context

[technology-decisions.md](../architecture/technology-decisions.md) listava
"Framework web/API (.NET) e convenções de routing" como decisão em aberto, com
a instrução de não a assumir ao implementar.

Ao montar `identity` a decisão tornou-se bloqueante — não há módulo sem
superfície HTTP — e foi tomada sem se registar. Cinco módulos depois, a
convenção está estabelecida e uniforme.

O que este ADR fixa não é sobretudo "Minimal APIs vs. Controllers". É a
**convenção de como um módulo expõe a sua superfície**, que é o que tem
consequência arquitectural: é ela que impede o host de se tornar o sítio onde
todos os módulos se encontram.

## Requirements

- **Facto** — API REST (documento de produto).
- **Facto** — Monólito modular com fronteiras internas fortes (ADR-001).
- **Facto** — Autorização por permissão declarada, com policies por módulo
  (ADR-014).
- **Facto** — Entidades de domínio nunca atravessam a fronteira HTTP
  ([standards/api.md](../standards/api.md)).
- **Inferência** — Serão catorze módulos. A convenção tem de escalar sem que
  acrescentar um módulo obrigue a mexer nos outros.

## Constraints

- A camada API é fina: mapeamento de pedido/resposta e códigos de estado, sem
  lógica de negócio ([standards/testing.md](../standards/testing.md)).
- O `Domain` não pode conhecer framework
  ([dependency-rules.md](../architecture/dependency-rules.md)).

## Alternatives

1. **Minimal APIs, um grupo de rotas por módulo** (escolhida).
2. MVC Controllers com `[ApiController]`.
3. Biblioteca de terceiros (FastEndpoints, Carter).

A opção 2 é a mais familiar e traz filtros, model binding rico e convenções
maduras. Rejeitada por dois motivos: o descobrimento automático de controllers
por assembly torna a superfície de um módulo **implícita** — nada obriga a que
as rotas de `hr` estejam sob `/hr` — e a camada API do Rivo é fina o
suficiente para não justificar o peso do pipeline MVC.

A opção 3 resolveria o mesmo com mais estrutura, ao custo de uma dependência
externa numa camada que é deliberadamente fina. Não se justifica.

## Trade-offs

Minimal APIs dão registo explícito: para uma rota existir, alguém escreveu a
linha. Em troca, perde-se o pipeline de filtros do MVC e a validação por
atributos — que teriam de ser reconstruídos se viessem a fazer falta.

## Decision

**ASP.NET Core Minimal APIs. Sem controllers.**

Convenções vinculativas:

### Cada módulo expõe a sua própria superfície

```csharp
public static class HrModuleEndpoints
{
    public static IEndpointRouteBuilder MapHrModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/hr");
        ...
    }
}
```

- Uma classe estática `X.Api/XModuleEndpoints.cs` por módulo.
- **Namespace de rotas = prefixo do módulo**, via `MapGroup`: `/identity`,
  `/audit`, `/hr`, `/documents`, `/notifications`.
- **O host não agrega endpoints.** `Program.cs` chama `MapXModule()` e mais
  nada. Acrescentar um módulo é uma linha; nunca é editar rotas de outro.

### Autorização declara-se no endpoint, nunca no handler

```csharp
group.MapGet("/employees", ListEmployeesAsync)
    .RequireAuthorization(HrPermissions.EmployeesRead);
```

**Se o pedido chega ao handler, já está autorizado.** Os handlers não
verificam permissões. Isto é o oposto directo do anti-padrão A8 do protótipo,
onde a política de escrita era "qualquer membro autenticado" e a verificação
real vivia no frontend.

Excepção admitida: quando o que limita o acesso **não é uma permissão mas uma
invariante de propriedade** — ler as próprias notificações — o endpoint exige
apenas autenticação, e a invariante é verificada no domínio. Ver
[modules/notifications.md](../modules/notifications.md).

### O resultado do caso de uso não é uma excepção

A Application devolve um `enum` ou `record` de resultado; a API mapeia-o para
código de estado. Violação de regra de negócio **não se sinaliza com
excepção** — é resposta legítima ao chamador.

### Disciplina de códigos de estado

| Situação | Código | Porquê |
|---|---|---|
| Regra conhecida, capacidade do sistema em falta | `501` | Não é erro do chamador (4xx) nem falha inesperada (500). É o caso do `hr`/BR-20 |
| Recurso alheio, cuja existência não deve ser revelada | `404`, não `403` | Distinguir revelaria que existe |
| Credenciais inválidas | `401` sem detalhe | Não revelar se o endereço existe |

### DTOs na fronteira, sempre

Entidades de domínio nunca são expostas nem aceites. Os DTOs vivem no projecto
`Api` do módulo (ou em `Contracts`, quando outro módulo os consome).

### O transporte só existe na camada API

O actor, o IP de origem e o identificador de correlação são conhecidos aqui e
em mais lado nenhum. É a API que constrói o `AuditContext` e o passa ao caso
de uso — abaixo dela não existe `HttpContext`.

## Consequences

Facilita:

- A superfície de um módulo lê-se num ficheiro, e o seu prefixo de rota é
  garantido por construção.
- Acrescentar um módulo não toca em nenhum outro.
- Autorização auditável por leitura: as permissões exigidas estão todas
  visíveis no mesmo sítio que as rotas.

Dificulta / exige:

- Sem filtros de MVC, preocupações transversais (validação, tratamento
  uniforme de erro) exigem `AddEndpointFilter` explícito ou middleware — não
  vêm de graça.
- Sem validação por atributos. Hoje a validação vive no domínio, que é onde
  [standards/testing.md](../standards/testing.md) a quer; se vier a ser
  precisa validação de forma na fronteira, é decisão nova.
- `[FromForm]` e afins têm de ser explícitos: os Minimal APIs ligam tipos
  simples à query string por omissão, e um campo de formulário seria
  silenciosamente ignorado.

## Risks

- **Ficheiros de endpoints a engordar** até serem controllers com outro nome.
  Detecta-se em revisão: um handler com mais do que mapeamento e despacho está
  a acumular lógica que pertence à Application.
- **Lógica de negócio a instalar-se nos handlers**, por serem convenientes.
  Mitigação: [prompts/04-review.md](../prompts/04-review.md) e, quando
  existirem, os testes de arquitectura.
- **Autorização esquecida num endpoint novo.** Hoje nada o detecta
  automaticamente — um `MapPost` sem `RequireAuthorization` fica público em
  silêncio. **É a lacuna mais perigosa desta decisão** e justifica um teste de
  arquitectura próprio: todo o endpoint tem de declarar autorização ou ser
  explicitamente marcado como anónimo.

## Revisit When

- Um módulo precisar de negociação de conteúdo, versionamento de API por
  cabeçalho, ou OData — coisas que o MVC dá e isto não.
- O número de endpoints por módulo tornar o ficheiro único ingerível. A
  correcção é repartir por área dentro do mesmo módulo, não voltar a
  controllers.
- Ser necessário um pipeline de validação uniforme na fronteira.

## Related

- [ADR-001](adr-001-architecture-style.md),
  [ADR-014](adr-014-rbac-permissoes.md),
  [ADR-017](adr-017-contratos-por-modulo.md)
- [standards/api.md](../standards/api.md),
  [standards/error-handling.md](../standards/error-handling.md)
- [architecture/module-boundaries.md](../architecture/module-boundaries.md)
