# ADR-038: A Documentação da API Abre-se por Interruptor, Não por Nome de Ambiente

## Status

Aceite (2026-08-27).

Aplica ao Swagger o desenho que o ADR-030 fixou para a migração no arranque.
Não substitui nenhum ADR. Protege o K8, que estava fechado e foi reaberto sem
que nada o assinalasse.

## Context

O commit `0301ef5` — "Abrie swagger em produção" — pôs no `docker-compose.yml`:

```yaml
ASPNETCORE_ENVIRONMENT: Development
#${ASPNETCORE_ENVIRONMENT:-Production}
```

O objectivo era legítimo e continua a ser: **o frontend precisa do
`/openapi/v1.json` do ambiente publicado**, e é por lá que se confirmam os
read models que o `API-FRONTEND.md` não consegue nomear. Sem Swagger, o
contrato é lido no código por quem não tem o código.

O problema não é o objectivo, é o instrumento. `ASPNETCORE_ENVIRONMENT` não é
um interruptor de documentação — é o nome do ambiente, e o `Program.cs`
pendura nele três decisões diferentes:

| Linha | Em `Development` | Consequência no ambiente publicado |
|---|---|---|
| `if (app.Environment.IsDevelopment())` | Mapeia OpenAPI e Swagger UI | ✅ era isto que se queria |
| `if (!app.Environment.IsDevelopment())` | **Não** corre `UseForwardedHeaders` | ⚠ **K8 reabre**: `user_session.ip_address` volta a guardar o IP do proxy, e BR-9 fica outra vez vazia |
| `!app.Environment.IsProduction()` (omissão de `Bootstrap:SeedOnStartup`) | Semeia | inofensivo aqui — o compose já passa `Bootstrap__SeedOnStartup` explicitamente |

E uma quarta que não está no `Program.cs`: em `Development`, o
`WebApplication` acrescenta a **página de excepções de desenvolvimento** ao
início do pipeline, por fora do `UseExceptionHandler`. Nem o
`BadRequestHandler` nem o `ConcurrencyConflictHandler` tratam excepções
desconhecidas — devolvem `false`, a excepção sobe, e quem provocar um erro não
tratado recebe **stack trace e excerto de código-fonte**. Em HTTP simples,
porque o K16 ainda não fechou.

O aviso já estava escrito no registo do K8: *"nada no código o detecta. A
garantia é topológica"*. Foi exactamente assim que se perdeu — sem teste a
falhar, sem erro no arranque, sem linha de log.

As credenciais não correram risco: `Jwt__SigningKey`, `ConnectionStrings__Rivo`
e as restantes chegam por variável de ambiente do compose, e essas ganham ao
`appsettings.Development.json`. A chave de assinatura de desenvolvimento, que
está versionada, **não** chegou a assinar tokens do ambiente publicado.

## Decision

**Expor a documentação da API passa a ser configuração própria,
`OpenApi:Expose`, com omissão igual a `IsDevelopment()`.**

```csharp
if (app.Configuration.GetValue("OpenApi:Expose", app.Environment.IsDevelopment()))
{
    app.MapOpenApi();
    app.UseSwaggerUI(...);
}
```

No compose, `OpenApi__Expose: ${EXPOSE_OPENAPI:-false}`; no `.env`,
`EXPOSE_OPENAPI`. O `ASPNETCORE_ENVIRONMENT` volta a
`${ASPNETCORE_ENVIRONMENT:-Production}`.

É a mesma frase que o ADR-030 já tinha escrito para a migração: *"não é efeito
colateral de `ASPNETCORE_ENVIRONMENT`, é uma decisão escrita no
`docker-compose.yml` de quem opera o ambiente"*. Passa a valer para as duas.

## Consequences

- Quem opera o ambiente publicado tem o Swagger **e** os cabeçalhos
  reencaminhados, que até aqui eram alternativa um do outro.
- A omissão continua fechada. Abrir é acto explícito e visível no `.env`.
- **Fica um risco residual assumido, o K17:** com `EXPOSE_OPENAPI=true` e sem
  TLS (K16), a superfície inteira da API é legível por quem estiver a ouvir a
  rede. Aceitável enquanto o ambiente for de teste e sem dados reais — que é a
  mesma condição que o K16 já impunha.
- **O nome do ambiente continua a decidir duas coisas** — cabeçalhos
  reencaminhados e seed por omissão. Não se separaram porque nenhuma delas tem
  hoje um caso de uso que peça o contrário. Se aparecer, seguem o mesmo
  caminho.

## Alternatives

**Deixar `ASPNETCORE_ENVIRONMENT=Development` e viver com o K8 reaberto.**
Recusada: troca uma conveniência de desenvolvimento por um requisito de
auditoria (BR-9), e o custo fica invisível — é precisamente o tipo de defeito
que só se descobre quando é preciso saber quem fez o quê.

**Um ambiente `Staging`.** `IsDevelopment()` seria falso, o Swagger fecharia na
mesma, e a alternativa exigiria trocar as condições por listas de ambientes
espalhadas pelo `Program.cs`. Mais peças para o mesmo resultado.

**Expor o `/openapi/v1.json` e não a interface.** O documento é a parte que
descreve a superfície; a interface é só o visualizador. Fecha o que menos
importa.
