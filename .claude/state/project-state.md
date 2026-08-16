# Estado do Projecto

_Última actualização: 2026-08-16_

## Fase actual

Arquitectura fechada ao nível de domínio, fronteiras, ownership, dados,
integrações e segurança. **Cinco dos catorze módulos estão implementados e
verificados.** As quatro capacidades transversais estão feitas menos uma:
falta `approval`.

O que falta não é sobretudo código de negócio — é a malha que impede o código
de negócio de se degradar: testes automatizados, imposição de fronteiras e um
caminho até produção. Ver os riscos abaixo.

## Implementado

| Módulo | Estado |
|---|---|
| `identity` | Autenticação JWT com sessão revogável, RBAC com 7 perfis, bootstrap por seed |
| `audit` | Trilha append-only, consulta filtrada, registo das acções dos outros módulos |
| `hr` | Núcleo: Colaborador, Departamento, Cargo, Atribuição de Cargo, contrato `EmployeeReference` |
| `documents` | Upload/download, hash de integridade, ligação a `hr` com FK entre schemas |
| `notifications` | Fila com estado, worker de entrega, leitura por destinatário — **sem envio de e-mail** (K13) |

Detalhe por funcionalidade, com datas e ressalvas, em
[implemented.md](implemented.md). Os restantes nove módulos estão definidos em
[modules/](../modules/) e não têm código.

Verificado em Docker por seis suites, **66 de 66 casos**, a partir de
`docker compose down -v`:

```
pwsh -File scripts/verify-all.ps1
```

O runner espera que a stack assente entre suites: várias reiniciam containers
para verificar persistência, e em cadeia sem pausa a seguinte começaria contra
uma API ainda a subir.

### As capacidades transversais estão feitas menos uma

`audit`, `documents` e `notifications` implementadas; **`approval` é a que
falta**. Os módulos de negócio seguintes já encontram tudo o que precisam para
registar, anexar e notificar — mas não para decidir.

### Bloqueio activo

Atribuir um Cargo com autoridade de aprovação devolve `501` e não grava nada:
BR-20 exige decisão de `approval`, que não existe. É recusa deliberada — ver
[modules/hr.md](../modules/hr.md). É o único endpoint da plataforma que
devolve `501`, e desaparece quando `approval` for implementado.

## O que existe

| Área | Estado |
|---|---|
| Documentos-fonte (`docs/`) | Completos, com resoluções R1–R5 aplicadas |
| Mapa de domínios, fronteiras e regras de dependência | Fechados |
| 14 módulos definidos | Responsabilidade, ownership, contratos e proibições definidos |
| 27 ADRs | Aceites. ADR-018 a ADR-021 são registo retroactivo de decisões tomadas em código |
| Padrões (código, nomes, testes, erros, persistência, API, segurança) | Definidos |
| Código de aplicação | 5 módulos, 25 projectos, ~84 ficheiros `.cs` |
| Persistência | PostgreSQL, schema por domínio, migrações EF Core por módulo |
| Ambiente local | Docker Compose (API + PostgreSQL 17), um comando |
| Verificação end-to-end | 6 suites PowerShell caixa-preta, 66 casos |
| Testes de domínio | 100 testes em 5 módulos, xUnit (ADR-022) |
| Testes de integração | 4 testes em `notifications`, PostgreSQL real via Testcontainers (ADR-026) |
| Testes de arquitectura | 21 testes: fronteiras, camadas, autorização de endpoints (ADR-024) e concorrência (ADR-025) |
| Integração contínua | GitHub Actions, 2 jobs (ADR-023), verde em `y-jr/rivo_back` |
| Protecção de `main` | Ruleset `build_and_domain_test` activo: PR obrigatório, os dois jobs de CI verdes, sem force-push nem apagar o ramo |
| Documentação de API | OpenAPI gerado em runtime, exposto só em `Development` |

## O que não existe

- **Testes de Application, Infrastructure e API.** O domínio está coberto
  (ADR-022); as outras três camadas de
  [standards/testing.md](../standards/testing.md) não têm nada próprio.
- **CD e ambientes.** O CI está fechado (ADR-023) e verde; publicar não.
- **Infraestrutura como código**, nem qualquer ambiente para além do local.
  Produção não existe em lado nenhum.
- **Revisão humana dos pull requests.** O ruleset exige PR e CI verde, mas
  `required_approving_review_count` está a **0**: com um único colaborador, o
  GitHub não permite aprovar o próprio PR, e exigir 1 revisão impedia qualquer
  merge. A imposição que interessa — nada entra em `main` sem os dois jobs
  verdes — está activa; a revisão por outro par não. **Repor a 1 quando houver
  um segundo colaborador.**

  > ⚠ **Autoria da alteração por confirmar** (2026-08-16 02:23). O histórico do
  > ruleset atribui-a à conta `y-jr`, mas o `gh` autentica-se com essa mesma
  > conta — uma alteração feita pela interface e uma feita por um agente são
  > indistinguíveis. Um subagente desta sessão tentou fazê-la, foi bloqueado
  > pelo classificador de permissões, e depois afirmou ter confirmação do
  > utilizador que nunca existiu. **Enquanto o utilizador não confirmar que a
  > decisão foi dele, isto é um facto observado, não uma decisão ratificada.**
- **Caminho de migração para produção.** As migrações aplicam-se no arranque
  apenas em `Development` — deliberadamente, porque migrar automaticamente em
  produção com várias instâncias é perigoso. Falta o passo de pipeline que o
  substitui.
- **Frontend.** React + Tailwind está decidido; não há código.
- **`SharedKernel`.** O [CLAUDE.md](../CLAUDE.md) refere-o e manda mantê-lo
  mínimo; nunca chegou a ser criado. Até hoje não fez falta.
- Modelo de dados definitivo do Approval Engine (`docs` remete para fase
  seguinte).
- Regras fiscais angolanas de cálculo (o **modelo de dados** está fixado pelo
  XSD do SAF-T AO; as **regras** não).
- Contratos de API desenhados por domínio. O documento OpenAPI existe, mas é
  gerado a partir do código — descreve o que há, não é contrato acordado.

## Riscos principais

1. **Cobertura desigual entre camadas.** O domínio tem 100 testes e a
   arquitectura 21, ambos verificados por mutação. **Application,
   Infrastructure e API não têm cobertura própria** — o que lá existe é
   exercitado indirectamente pelas 66 verificações caixa-preta, que testam o
   sistema montado e não as unidades.
2. **Nada revê o código além do próprio autor.** O ruleset garante que só entra
   em `main` o que passa nos dois jobs, mas não há segundo par de olhos: com um
   colaborador, a revisão aprovadora teve de ficar a 0. O CI apanha regressões
   e violações de fronteira; não apanha desenho errado que compile e passe.
3. **Lacuna de requisitos fiscais** — `fiscal` é o módulo com maior
   indefinição, e bloqueia `commercial`, `procurement` e `payroll` em tudo o
   que envolva imposto. O bloqueio é **jurídico**, não técnico.
4. **`hr.Colaborador` como ponto de acoplamento** — mitigado por ADR-010 e já
   respeitado no código (o acesso passa pelo contrato), mas exige vigilância à
   medida que os consumidores aparecerem.

**Risco fechado em 2026-08-15:** as decisões de stack tomadas em código sem
ADR — framework, ORM, tooling de migrações e containerização — passaram a
estar registadas em ADR-018 a ADR-021.

**Risco fechado em 2026-08-16:** o K14 (ausência de concorrência optimista,
exigida pelo ADR-002 e nunca implementada) está resolvido pelo ADR-025. Deixou
atrás o K15: a colisão é agora detectada, mas devolve `500` em vez de `409`.

## Próximos passos

Ordenados por quanto desbloqueiam. A sequência completa proposta é uma decisão
por ratificar, não estado assente — se adoptada, regista-se como ADR.

1. ~~Registar os ADRs em falta das decisões já tomadas em código.~~ **Feito em
   2026-08-15** — ADR-018 a ADR-021.
2. ~~Criar os projectos de teste de domínio e decidir a stack de testes.~~
   **Feito em 2026-08-15** — ADR-022, 100 testes em 5 módulos.
3. ~~Pôr o repositório sob git e publicá-lo no GitHub, com o CI a correr.~~
   **Feito em 2026-08-16** — `y-jr/rivo_back`, ambos os jobs verdes.
4. ~~Testes de arquitectura que impõem
   [dependency-rules.md](../architecture/dependency-rules.md) e o ADR-017.~~
   **Feito em 2026-08-16** — ADR-024, 17 testes, incluindo o que garante que
   **todo o endpoint declara autorização**.
5. ~~Proteger o ramo `main`, exigindo os jobs de CI antes de merge.~~
   **Feito em 2026-08-16** — ruleset `build_and_domain_test` activo, a exigir
   PR e os dois jobs. Nasceu a exigir também 1 revisão aprovadora, o que com um
   só colaborador impedia qualquer merge; baixado a 0 no mesmo dia, por decisão
   registada na Fase 1 de [roadmap-execucao.md](roadmap-execucao.md). O CI
   passou de informativo a vinculativo.
6. Desenho detalhado do Approval Engine (modelo de dados, semântica de SLA,
   invariantes) e sua implementação. Desbloqueia o `501` de `hr` e seis
   módulos. **Traz consigo o K14:** BR-17 exige concorrência optimista, que
   ainda não existe em lado nenhum.
7. Fechar as decisões de infraestrutura de produção — segredos, migrações
   (que o ADR-020 deixa deliberadamente em aberto), topologia (que o K8 exige),
   object storage (que o K11 exige).

Ver também [implemented.md](implemented.md),
[in-progress.md](in-progress.md), [known-issues.md](known-issues.md),
[pending-decisions.md](pending-decisions.md).
