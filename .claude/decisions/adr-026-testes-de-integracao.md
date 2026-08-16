# ADR-026: Testes de Integração — Testcontainers

## Status

Aceite (2026-08-16)

## Context

`standards/testing.md` exige, para a camada Infrastructure, "testes de
integração contra a tecnologia real (PostgreSQL efectivo) para as peças que
implementam ports". Não havia nenhum.

O que existia eram as seis suites PowerShell (66 casos), que exercitam o
sistema montado por HTTP. Têm valor próprio, mas são caixa-preta: para
verificar que um repositório persiste correctamente, obrigam a subir a stack
inteira, autenticar, e inferir o estado da base de dados a partir de respostas
HTTP.

O caso que forçou a decisão foi o ADR-025. Ao implementar a concorrência
optimista, o mecanismo só pôde ser provado por **SQL escrito à mão** — dois
`UPDATE` com a mesma versão de partida dando `UPDATE 1` e `UPDATE 0`. Isso
mostra que o PostgreSQL faz a sua parte; **não mostra que o EF Core está
configurado para tirar partido dela**. O critério ficou registado como
cumprido só em parte, e essa lacuna é o que este ADR fecha.

## Requirements

- **Facto** — `standards/testing.md` exige integração contra PostgreSQL real.
- **Facto** — Um projecto de teste referencia **um** módulo (ADR-022).
- **Facto** — O CI corre em `ubuntu-latest`, que tem Docker.
- **Facto** — O ambiente local usa `postgres:17-alpine` (ADR-021).
- **Inferência** — Serão catorze módulos. O arranque de containers tem de ser
  amortizado, ou o tempo de teste torna-se proibitivo.

## Alternatives

1. **Testcontainers com PostgreSQL real** (escolhida).
2. Provider em memória do EF Core.
3. SQLite em memória.
4. Continuar só com as suites PowerShell.

A opção 2 é a mais rápida e a mais enganadora. **O que estes testes verificam
não existe no provider em memória:** não tem schemas, não tem chaves
estrangeiras, não impõe restrições, e — decisivamente — não detecta escrita
concorrente. Um teste de persistência contra um substituto que não persiste
como o real dá confiança falsa, que é pior do que não ter teste.

A opção 3 tem os mesmos problemas em menor grau, e acrescenta divergência de
dialecto: `numeric`, `jsonb`, índices parciais e FK entre schemas comportam-se
de forma diferente.

A opção 4 é o estado anterior. As suites continuam a valer — ver Decision.

## Decision

**Testcontainers.PostgreSql, com a mesma imagem do ambiente local.**

### Divisão de trabalho com as suites PowerShell

| | Testcontainers | Suites PowerShell |
|---|---|---|
| Âmbito | Um módulo, camada Infrastructure | Sistema montado, ponta a ponta |
| Verifica | Persistência, mapeamento, restrições, concorrência | Autorização, fluxo HTTP, arranque, persistência entre reinícios |
| Custo | Segundos | Minutos, exige a stack |

**As suites não são substituídas — mudam de papel.** Passam a ser smoke
end-to-end, que é onde valem: são a única coisa que verifica que a aplicação
arranca, migra, semeia e responde. Não é sensato reimplementar isso em
Testcontainers.

### A imagem acompanha a do ambiente local

`postgres:17-alpine`, a mesma do `docker-compose.yml` (ADR-021). Testar contra
uma versão diferente daquela em que se corre é uma classe inteira de defeitos
que só aparecem em produção.

### Um container por assembly, não por classe

O fixture é partilhado por colecção do xUnit. Arrancar um PostgreSQL por
classe multiplicaria dezenas de segundos por nada — os testes isolam-se pelos
dados que criam, não pela instância.

### `Rivo.TestSupport` — e porque não viola fronteiras

O fixture vive num projecto partilhado, `tests/Rivo.TestSupport`, que
**não referencia módulo nenhum e não pode passar a referenciar**. Se o fizesse,
tornava-se o sítio onde todos os módulos se encontram — o que o ADR-022 evitou
deliberadamente ao dar um projecto de teste a cada domínio.

O que lá vive é infraestrutura de teste: saber arrancar um container e
devolver uma connection string. Duplicar isso por catorze módulos seria
repetir código de container, não preservar fronteiras.

**Constrangimento da ferramenta:** a `[CollectionDefinition]` tem de estar no
assembly dos testes — o xUnit não a encontra noutro. Cada projecto de
integração declara a sua, em quatro linhas, e reutiliza o fixture partilhado.

### Nomenclatura

`tests/Modules/<Módulo>/Rivo.<Módulo>.Infrastructure.Tests`, a espelhar
`src/`, tal como os testes de domínio.

## Consequences

Facilita:

- A lacuna do ADR-025 fecha: a colisão de concorrência passa a ser demonstrada
  automaticamente, e não por SQL escrito à mão.
- Verificar mapeamento, restrições e FK entre schemas deixa de exigir a stack
  inteira.
- Um defeito de persistência aparece em segundos, no projecto do módulo, e não
  como resposta HTTP inesperada.

Dificulta / exige:

- **`dotnet test` passa a precisar de Docker.** Quem não o tenha a correr vê
  falhar os testes de integração — os de domínio e de arquitectura continuam a
  correr sem ele.
- O job de CI que bloqueia PRs fica mais lento (~13s para quatro testes,
  dominados pelo arranque do container).
- Um projecto de integração a mais por módulo, no limite.

## Risks

- **Cobertura irregular.** Só `notifications` tem testes de integração — foi
  escolhido por ser onde a contenção de concorrência é real hoje. Os outros
  quatro módulos continuam sem. Não é dívida escondida: está registado aqui e
  em `state/`.
- **Tempo de CI a crescer** à medida que mais módulos ganham integração. Se o
  job que bloqueia PRs deixar de ser rápido, a resposta é separá-lo, não
  desligá-lo.
- **Docker como dependência de desenvolvimento local.** Já era necessário para
  as suites PowerShell, portanto não é requisito novo — mas passa a ser
  necessário mais cedo no ciclo.

## Verificação de que os testes têm dentes

Verificado por mutação em 2026-08-16: removido o `IsConcurrencyToken()` da
configuração de `notification`, **os dois testes de colisão falharam** e os
outros dois continuaram verdes. Reposto.

É exactamente a verificação que faltava ao ADR-025.

## Revisit When

- O tempo do job que bloqueia PRs deixar de ser aceitável.
- Surgir necessidade de testar contra serviços externos além do PostgreSQL —
  o Testcontainers cobre-os, mas cada um é uma decisão de âmbito.
- Um módulo precisar de fixture com estado partilhado entre testes, o que
  obrigaria a repensar o isolamento por dados.

## Related

- [ADR-021](adr-021-ambiente-local-docker.md) — a imagem que esta decisão segue
- [ADR-022](adr-022-stack-de-testes.md) — a estrutura que esta estende
- [ADR-025](adr-025-concorrencia-optimista.md) — a lacuna que esta fecha
- [standards/testing.md](../standards/testing.md)
