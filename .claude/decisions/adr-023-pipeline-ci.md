# ADR-023: Pipeline de Integração Contínua

## Status

Aceite (2026-08-16)

## Context

O risco número 1 declarado do projecto é **erosão de fronteiras** — foi o que
produziu cinco implementações paralelas de aprovação e duas tabelas de
auditoria no protótipo. A mitigação registada em
[project-state.md](../state/project-state.md) são "testes de arquitectura
automatizados".

O ADR-022 fechou a camada de domínio: 100 testes, verificados por mutação.
Mas ficou um problema que o próprio ADR-022 assinala nos Riscos: **os testes
existem e nada os corre**. Um teste que depende de alguém se lembrar de
escrever `dotnet test` não protege nada.

As seis suites caixa-preta (66 casos) têm o mesmo problema, agravado por
exigirem uma stack Docker que ninguém sobe por hábito.

## Requirements

- **Facto** — 100 testes de domínio, rápidos e sem infraestrutura (ADR-022).
- **Facto** — 66 casos end-to-end que exigem Docker e credenciais de
  bootstrap.
- **Facto** — Segredos nunca versionados; o `.env` está em `.gitignore`.
- **Facto** — A API aplica migrações e seed no arranque, só em `Development`
  (ADR-020) — logo a stack levanta-se sozinha, mas demora.
- **Inferência** — Serão catorze módulos. O tempo de execução vai crescer, e o
  que bloqueia um PR tem de continuar rápido.

## Constraints

- O repositório **não estava sob controlo de versões** quando este ADR foi
  escrito. O pipeline pressupõe git e um remoto no GitHub.
- `verify-all.ps1` invocava `powershell`, que só existe no Windows.

## Alternatives

1. **GitHub Actions** (escolhida).
2. Azure Pipelines.
3. Sem CI, disciplina por revisão.

A opção 3 é o estado actual e é o que este ADR corrige. Foi exactamente a
disciplina por revisão que falhou no protótipo.

A opção 2 é defensável, sobretudo por o alojamento previsto ser Azure. Foi
rejeitada por acrescentar uma segunda plataforma e uma segunda conta ao ciclo
de trabalho — o código vive no GitHub, e manter o CI ao lado do código reduz
atrito. **Não fecha a escolha de CD:** publicar em Azure a partir de Actions é
caminho corrente, e a decisão de deployment continua em aberto.

## Decision

**GitHub Actions**, em `.github/workflows/ci.yml`, com **dois jobs
deliberadamente separados**.

### Job 1 — `build-and-test`

`restore → build (Release) → test`. Sem Docker, sem base de dados.

**É este que tem de bloquear um PR.** Rápido e determinístico: se falhar, o
código está errado, não o ambiente.

`--no-build` no passo de teste garante que se testa exactamente o que foi
compilado, e não uma recompilação silenciosa com outras opções.

### Job 2 — `verify-stack`

Sobe a stack com `docker compose`, espera por `/health`, corre as seis suites,
despeja logs em caso de falha e destrói tudo.

Separado do primeiro por ser **mais lento e mais frágil** — depende de
containers, de rede e de tempos de arranque. Misturar os dois faria uma falha
de infraestrutura parecer uma falha de código.

Corre depois (`needs: build-and-test`): não vale a pena levantar containers
para código que nem compila.

### Credenciais geradas por execução

As suites lêem cinco chaves do `.env`, que nunca é versionado. O workflow
gera-as com `openssl rand` a cada execução, cumprindo a política de password
do Identity.

**Não se usam segredos do repositório para isto.** O ambiente é efémero e
destruído no fim; uma credencial fixa guardada em GitHub Secrets seria um
segredo real a proteger sem nada de valor a proteger.

### Cache

Chave = hash dos `.csproj` e dos `Directory.Build.props`. Sem ficheiros de
lock, é o melhor sinal disponível — e é onde as versões de pacote vivem.

## Consequência sobre a portabilidade das suites

`verify-all.ps1` invocava `powershell`, que **não existe em Linux**. Corrigido
para escolher `pwsh` quando disponível e recorrer a `powershell` quando não —
o CI usa o primeiro, uma máquina Windows sem PowerShell 7 usa o segundo.

Ficou registado no script que `verify-authorization` usa `Join-String`, que só
existe a partir do PowerShell 6.2: sob o 5.1 essa linha falha, mas apenas no
caminho de erro. **O 5.1 é fallback, não é suportado em pé de igualdade.**

## Consequences

Facilita:

- Os testes passam a correr sem depender de memória humana.
- Uma regressão aparece no PR, não semanas depois.
- Dá o lugar onde os testes de arquitectura vão entrar quando existirem — que
  é o que fecha o risco 1.

Dificulta / exige:

- Exige repositório git com remoto no GitHub, que não existia.
- O job 2 consome minutos de runner e vai ficar mais lento com cada módulo.
- Uma falha intermitente no job 2 desgasta a confiança no pipeline se não for
  investigada.

## Risks

- **O job 2 não foi validado em execução real** à data deste ADR — foi
  construído a partir do comportamento conhecido das suites, e os tempos de
  arranque em runner podem exigir ajuste do limite de 5 minutos de espera por
  `/health`.
- **Falha intermitente a ser normalizada.** Se acontecer, a resposta é
  diagnosticar, não aumentar o timeout nem marcar o job como não bloqueante.
- **Falsa sensação de cobertura:** o pipeline corre o que existe. Application,
  Infrastructure, arquitectura e a autorização declarada nos endpoints
  (ADR-018 §Risks) continuam sem testes — o CI não os inventa.

## Revisit When

- Existirem testes de arquitectura — entram no job 1, porque são rápidos e
  devem bloquear.
- For decidido o CD para Azure — provavelmente workflow à parte, com gate de
  aprovação, e o passo de migrações que o ADR-020 deixou em aberto.
- O tempo do job 1 deixar de ser aceitável para bloquear um PR.

## Related

- [ADR-020](adr-020-migracoes-por-modulo.md) — deixa o caminho de migração em
  produção em aberto; o CD terá de o fechar
- [ADR-021](adr-021-ambiente-local-docker.md) — a stack que o job 2 levanta
- [ADR-022](adr-022-stack-de-testes.md) — os testes que o job 1 corre
- [standards/testing.md](../standards/testing.md)
