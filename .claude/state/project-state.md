# Estado do Projecto

_Última actualização: 2026-08-25_

## Fase actual

**Nove dos catorze módulos têm código, e há um ambiente publicado.**

As quatro capacidades transversais estão feitas — `audit`, `documents`,
`notifications` e `approval`. A partir daí, o objectivo do produto mudou: o
ADR-036 dispensou a emissão legalmente válida e fixou **emitir** como meta, o
que reordenou as Fases 3, 4 e 5 do
[roadmap-execucao.md](roadmap-execucao.md).

Hoje sai uma factura de venda com cliente, numeração `FT S001/1` e a taxa que
vigorava à data do facto gerador. **Não é documento fiscal válido em Angola** —
tem a forma, falta a certificação da AGT.

## Módulos

| Módulo | Estado |
|---|---|
| `identity` | Completo. JWT com sessão revogável, RBAC com 7 perfis, entrar com Google, bootstrap por seed |
| `audit` | Completo. Trilha append-only imposta pela base de dados, consulta filtrada |
| `documents` | Completo. Upload/download, hash de integridade, ligação a `hr` por FK entre schemas |
| `notifications` | Completo menos a entrega real. Fila com estado e worker — **sem envio de e-mail** (K13) |
| `hr` | Completo. Colaborador, Departamento, Cargo, Contrato, Assiduidade, Férias, Benefícios, Recrutamento, Onboarding/Offboarding |
| `approval` | Completo para o âmbito fixado. Políticas, pedidos, decisões, BR-2/4/6/17, worker de reconciliação |
| `fiscal` | ⚠ **Fatia mínima** (ADR-036). Taxa com vigência e determinação. Não é o motor fiscal |
| `commercial` | ⚠ **Reduzido ao Cliente** (ADR-036). Sem funil comercial |
| `finance` | ⚠ **Só Contas a Receber** (ADR-036). Factura de venda. Sem AP, Tesouraria, Contabilidade, Planeamento |
| `procurement`, `payroll`, `projects`, `inventory`, `fleet` | Sem código. Definidos em [modules/](../modules/) |

Detalhe com datas e ressalvas em [implemented.md](implemented.md).

**Os três marcados com ⚠ são fatias deliberadas, não módulos por acabar.** O
que ficou de fora está listado em cada `modules/*.md` e no ADR-036, com o custo
de o fazer depois.

## Ambiente publicado

`http://187.77.178.242` desde 2026-08-23 — VPS da organização, `docker compose`
atrás de Caddy na rede `proxy`, contra o SQL Server externo (ADR-029, ADR-031).

Deployment por `.github/workflows/main.yml`: SSH, `git pull`,
`compose up --build`, sonda de `/health`.

⚠ **Sem TLS** — não há domínio, e o Let's Encrypt não emite para endereços IP.
O token viaja em claro. É o **K16**, e não pode ir para produção a sério.

## Números

| Área | Estado |
|---|---|
| Código | 9 módulos, 45 projectos em `src/`, 131 ficheiros `.cs` |
| Superfície HTTP | 71 endpoints em 9 grupos de rota, mais `/health` |
| ADRs | 36, aceites |
| Testes | **326** em 13 projectos — 284 de domínio, 21 de arquitectura, 9 da API do host, 8 de Application, 4 de integração |
| Verificação end-to-end | **9 suites** PowerShell, **116 casos**, todas re-executáveis |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

## O que não existe

- **Cobertura fora do domínio.** 284 testes de domínio contra 8 de Application
  e 4 de Infrastructure. Cada módulo novo alarga a diferença.
- **Testes de integração** em oito dos nove módulos. Só `notifications` os tem.
- **Observabilidade.** Com o Azure fora de cena (ADR-031), o diagnóstico em
  produção é `docker compose logs` numa máquina. **Regressão assumida.**
- **Revisão humana dos pull requests.** O ruleset exige PR e CI verde, mas
  `required_approving_review_count` está a **0**: com um só colaborador, o
  GitHub não permite aprovar o próprio PR. **Repor a 1 quando houver um
  segundo colaborador.**

  > ⚠ **Autoria da alteração por confirmar** (2026-08-16 02:23). O histórico do
  > ruleset atribui-a à conta `y-jr`, mas o `gh` autentica-se com essa mesma
  > conta — uma alteração feita pela interface e uma feita por um agente são
  > indistinguíveis. Um subagente desta sessão tentou fazê-la, foi bloqueado
  > pelo classificador de permissões, e depois afirmou ter confirmação do
  > utilizador que nunca existiu. **Enquanto o utilizador não confirmar que a
  > decisão foi dele, isto é um facto observado, não uma decisão ratificada.**
- **Frontend.** React + Tailwind decidido; sem código. A pasta `front/` é
  trabalho de outra sessão.
- **`SharedKernel`.** O [CLAUDE.md](../CLAUDE.md) refere-o e manda mantê-lo
  mínimo; nunca chegou a ser criado. O ADR-035 considerou criá-lo e decidiu
  contra — ver a alternativa B desse ADR.
- **Utilizador aplicacional restrito na base de dados.** A aplicação liga-se
  como `sa`.
- Regras fiscais angolanas de cálculo — IRT, INSS, códigos de isenção. O
  **modelo de dados** está fixado pelo XSD do SAF-T; as **regras** não, e
  `CLAUDE.md` proíbe implementá-las a partir do levantamento provisório.

## Riscos principais

1. **Cobertura desigual entre camadas**, e a crescer. O domínio está bem
   coberto; a Application quase não está. O CI apanha regressões de domínio e
   violações de fronteira — não apanha um caso de uso errado que compile.
2. **Nada revê o código além do próprio autor.** Com um colaborador, a revisão
   aprovadora teve de ficar a 0.
3. **Três módulos parecem mais completos do que são.** `fiscal`, `commercial`
   e `finance` respondem a HTTP e têm testes, o que é fácil de confundir com
   estarem feitos. Uma factura do Rivo tem número, série e ar de factura, e não
   é documento fiscal. Mitigação: ⚠ em cada `modules/*.md`, no ADR-036 e aqui.
4. **K16 — sem TLS.** Credenciais e token em claro no ambiente publicado.
5. **`hr.Colaborador` como ponto de acoplamento** — mitigado por ADR-010 e
   respeitado no código, mas exige vigilância à medida que os consumidores
   aparecem.

**Riscos fechados:** as decisões de stack sem ADR (2026-08-15, ADR-018 a 021);
o K14 (2026-08-16, ADR-025); a ausência de testes de arquitectura (2026-08-16,
ADR-024); o K15 (2026-08-24, ADR-035).

## Próximos passos

Não é uma sequência ratificada — é o que está por decidir e por fazer.

1. **Decidir o que o ADR-036 deixou em aberto:** marcar visivelmente uma
   factura que não é documento fiscal; que séries de numeração usar; se existe
   consumidor final. Todas dependem do negócio, não da técnica. Ver
   [pending-decisions.md](pending-decisions.md).
2. **Nota de crédito e recebimentos** em `finance`, se o ciclo de venda tiver
   de fechar.
3. **Domínio e TLS** — fecha o K16 e é pré-requisito de qualquer uso real.
4. **Cobertura de Application**, a começar pelos casos de uso que decidem —
   `IssueSalesInvoice` e `DecideOnRequest` são os que mais custam se falharem.

Ver também [implemented.md](implemented.md),
[in-progress.md](in-progress.md), [known-issues.md](known-issues.md),
[pending-decisions.md](pending-decisions.md),
[roadmap-execucao.md](roadmap-execucao.md).
