# Implementado

_Última actualização: 2026-08-15._

Funcionalidade concluída e a funcionar, por módulo. Actualizar como parte de
terminar uma funcionalidade (passo 8 do fluxo em [CLAUDE.md](../CLAUDE.md)).

**Cinco dos catorze módulos estão implementados:** `identity`, `audit`, `hr`,
`documents`, `notifications`. Os restantes nove estão definidos em
[modules/](../modules/) e não têm código.

> As datas vêm do carimbo das migrações EF Core, que é a evidência mais fiável
> disponível — o repositório não está sob controlo de versões.

## Formato

```
## <módulo>
- <funcionalidade> — <data> — <nota breve, ADR relacionado se aplicável>
```

## identity

- Autenticação por JWT bearer com sessão persistida — 2026-08-10 — ADR-013
- Sessão como entidade de domínio, com IP, user agent, expiração absoluta e
  revogação — 2026-08-10 — ADR-013
- ASP.NET Core Identity como fonte de contas — 2026-08-10 — ADR-012; fecha
  implicitamente o ORM em EF Core
- Catálogo dos sete Perfis de Acesso, semeados; permissões transportadas como
  role claims — 2026-08-10 — ADR-014
- Bootstrap idempotente do Admin e do decisor iniciais, por configuração —
  2026-08-10 — ADR-016
- Endpoints: `register`, `login`, `logout`, `me`, `users`, `roles`,
  `users/{id}/roles`

**Por satisfazer, deliberadamente registado:**

- Só existe expiração **absoluta**. O requisito de 15 minutos por inactividade
  para perfis decisórios **não está satisfeito**.
- Sem refresh token e sem MFA.
- Das sete entradas do catálogo, só `Admin` e `HR` têm permissões atribuídas.
  As outras cinco estão vazias porque dependem de módulos de negócio que não
  existem — inventá-las agora seria adivinhar.
- O IP registado na sessão é o do proxy, não o do cliente — ver K8 em
  [known-issues.md](known-issues.md).

## audit

- Trilha append-only com `AuditEvent` imutável — 2026-08-10 — BR-10
- Contrato `IAuditTrail`, consumido pelos restantes módulos — 2026-08-10 —
  primeiro uso de ADR-017
- Consulta filtrada por tipo e identificador de entidade, com limite —
  2026-08-10
- `GET /audit/entries` é **só leitura**. Não existe endpoint de escrita: a
  trilha é escrita pelo contrato interno, nunca por HTTP — um endpoint público
  permitiria forjar registos

**Por satisfazer:** o append-only é imposto em código, não pela base de dados
(K9); a escrita da trilha não é transaccional com a operação auditada (K10).

## hr

- Colaborador, Departamento, Cargo e Atribuição de Cargo — 2026-08-11
- Contrato `EmployeeReference` / `IEmployeeDirectory` como **único** caminho de
  acesso a Colaborador a partir de outros módulos — 2026-08-11 — ADR-010
- Ligação opcional entre Colaborador e conta de `identity` — 2026-08-11 —
  ADR-004
- Separação entre Cargo e Perfil de Acesso imposta na autorização: o catálogo
  de Cargos exige `Admin`, a atribuição exige `HR` — 2026-08-11 — ADR-005,
  ADR-015. É esta separação que fecha a escalada de privilégios
- Anexação e listagem de documentos do colaborador, com FK entre schemas —
  2026-08-11 — ADR-009, ADR-010

**Recusa deliberada:** atribuir um Cargo que confira autoridade de aprovação
devolve `501` e não grava nada. BR-20 exige decisão de `approval`, que não
existe. Não é defeito — é o sistema a recusar-se a criar autoridade sem
governança. Ver [modules/hr.md](../modules/hr.md).

## documents

- Upload e download com hash de integridade e tecto de 25 MB por ficheiro —
  2026-08-11
- Porta `IDocumentStorage` com implementação em sistema de ficheiros —
  2026-08-11
- Catálogo de metadados; a ligação ao contexto de origem e a classificação
  ficam na origem, não aqui — 2026-08-11 — ADR-009

**Por satisfazer:** conteúdo sem cifra em repouso (K11); ficheiro órfão se a
gravação de metadados falhar (K12).

## notifications

- Fila em tabela com estado; enfileirar **não** entrega — 2026-08-11 —
  contrato `INotifier`, corrige o anti-padrão A4
- Worker de entrega por sondagem, com recuo exponencial e lotes limitados —
  2026-08-11
- Leitura restrita ao destinatário e marcação como lida; o destinatário vem do
  token, nunca do pedido — 2026-08-11

**Por satisfazer — importante:** o canal registado é
`LoggingNotificationChannel`, que **escreve em log e não envia e-mail**.
O percurso de entrega (fila, worker, estado, recuo) é real e testável; o envio
não existe. Ver K13 em [known-issues.md](known-issues.md).

## Plataforma

- Solução `Rivo.slnx` com 25 projectos: cinco módulos em camadas
  API / Application / Domain / Infrastructure, mais o host `Rivo.Api` —
  2026-08-10
- Assembly `Rivo.X.Contracts` em `audit`, `documents`, `hr` e `notifications` —
  2026-08-11 — ADR-017. `identity` não tem, por não ter consumidor; criá-lo
  seria construir superfície pública para ninguém
- PostgreSQL com um schema lógico por domínio e um `DbContext` por módulo —
  2026-08-10 — ADR-002
- Migrações EF Core independentes por módulo, aplicadas no arranque **apenas em
  `Development`** — 2026-08-10. Produção não tem caminho de migração
- Docker Compose com API e PostgreSQL 17; imagem multi-fase, utilizador
  não-root — 2026-08-10
- `GET /health`, que verifica também o alcance da base de dados — 2026-08-10
- OpenAPI e Swagger UI expostos **só em `Development`** — 2026-08-10
- Workflow de CI em GitHub Actions, dois jobs — 2026-08-16 — ADR-023.
  **Escrito e validado localmente, mas nunca executado:** o repositório ainda
  não está sob git nem tem remoto

## Verificação

Seis suites PowerShell caixa-preta contra a stack em Docker, **66 casos**,
confirmado por execução completa em 2026-08-16.

> **O runner reportava 71 até 2026-08-16.** `Select-String` é case-insensitive
> por omissão, e cinco das seis suites terminam com "Todos os testes
> passaram." — a palavra "passaram" casava com o padrão `PASSA` e era contada
> como um caso. Corrigido com `-CaseSensitive` e ancoragem ao formato que
> `Test-Case` emite. Os 66 são reais; os 71 eram 66 mais cinco linhas de
> resumo.

| Suite | Casos |
|---|---|
| `verify-bootstrap` | 9 |
| `verify-authorization` | 8 |
| `verify-audit` | 10 |
| `verify-hr` | 13 |
| `verify-documents` | 13 |
| `verify-notifications` | 13 |

A partir de `docker compose down -v`:

```
docker compose up -d --build
pwsh -File scripts/verify-all.ps1
```

O runner espera que a stack assente entre suites: várias reiniciam containers
para verificar persistência, e em cadeia sem pausa a seguinte começaria contra
uma API ainda a subir.

## Testes de domínio

**100 testes, cinco módulos** — 2026-08-15 — ADR-022. xUnit v2.9.3, sem
biblioteca de asserções. Um projecto por domínio de módulo, em
`tests/Modules/<Módulo>/Rivo.<Módulo>.Domain.Tests/`.

```
dotnet test
```

| Módulo | Testes | O que fixam |
|---|---|---|
| `hr` | 45 | Vigência de Atribuição de Cargo (BR-6), **atribuição pendente não confere o Cargo** (BR-20/ADR-015), marca de autoridade (BR-21), desactivar não elimina (BR-14) |
| `notifications` | 20 | Leitura e entrega como estados independentes, recuo exponencial, abandono ao 5.º insucesso, propriedade do agregado |
| `documents` | 16 | Hash obrigatório, recusa de ficheiro vazio, anulação lógica que não apaga (BR-14) |
| `audit` | 10 | **Imutabilidade verificada por reflexão** (BR-10), actores não interactivos |
| `identity` | 9 | Sessão revogada deixa de valer de imediato, revogação idempotente, marcador explícito de IP desconhecido (BR-9) |

Correm em menos de 2 segundos, sem Docker e sem base de dados.

**Verificado por mutação** em 2026-08-15: removida a verificação de estado de
`PositionAssignment.IsEffectiveAt`, falhou exactamente um teste — o que fixa a
invariante que fecha a escalada de privilégios do ADR-015 — e mais nenhum. A
alteração foi revertida.

### Fora do implementado

Application, Infrastructure e API continuam sem testes próprios. **Não existem
testes de arquitectura**, logo as fronteiras de módulo continuam a depender de
revisão humana — é o risco 1 em [project-state.md](project-state.md), ainda
por mitigar.
