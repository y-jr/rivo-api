# Percurso de Execução

_Adoptado em 2026-08-16. Substitui o roadmap provisório criado nesse mesmo dia._

**Fonte de verdade sobre em que fase o projecto está.**

Origem: o percurso publicado em
<https://claude.ai/code/artifact/45b9d84a-5336-4ab2-a4e2-bceb60b96e83>,
destilado do estado do repositório a 15 de Agosto de 2026 contra `docs/`.
As fases, a ordenação e os critérios de saída são os desse documento. **O
estado de execução abaixo é o de hoje**, que já não é o de 15 de Agosto.

Onde este documento contradisser `docs/`, prevalece `docs/`. As sequências e
as escolhas de infraestrutura aqui propostas são decisões por tomar — se
adoptadas, registam-se como ADR, nunca por reescrita dos documentos-fonte.

## Como as fases estão ordenadas

1. **Desbloqueio.** O que destrava mais trabalho a jusante vai primeiro.
   `approval` desbloqueia seis módulos; `fiscal` desbloqueia quatro.
2. **Custo crescente de retrofit.** Testes, imposição de fronteiras e caminho
   de produção custam N vezes mais com N módulos. Com cinco é barato; com
   catorze deixa de se fazer.
3. **Prazo externo.** O que depende de terceiros arranca desde já. A
   certificação junto da AGT é o item de prazo mais longo do projecto inteiro.

## Quadro

| # | Fase | Estado |
|---|---|---|
| 0 | Fundação de verificação e CI | ✅ **Fechada** em 2026-08-16 |
| 1 | Aterrar em produção — VPS | **Quase fechada** — publicado em 2026-08-23; 59 de 66 verificações passam. K16 (sem TLS) aberto |
| 2 | `approval` — governança de decisões | **Critério de saída cumprido** em 2026-08-24 |
| 3 | `fiscal` — o que não está bloqueado | **Reduzida a fatia mínima** em 2026-08-24 (ADR-036) |
| 4 | `finance` — o núcleo | ✅ **Critério de saída cumprido** em 2026-08-25 — os cinco contextos internos, BR-1/3/5/8 impostas, os documentos a lançar nos livros e (2026-08-29) a anular a estornar. Em dívida: o plano de contas, que é do contabilista |
| 5 | `procurement` e `commercial` | ✅ `commercial` reduzido ao Cliente e feito; `procurement` fechado em 2026-08-28 (4 agregados, 3-way match) |
| 6 | `payroll` | Motor de IRT/INSS ganhou regra de negócio real em 2026-08-30 — trave de **produção** continua por parecer fiscal, ver a nota da fase |
| 7 | `projects`, `inventory`, `fleet` | **Fechada por completo a 2026-08-31.** Os três ganharam regra de negócio em 2026-08-30 — `projects` (Marco, Tarefa e Orçamento, desbloqueado por ADR-040 no mesmo dia), `fleet` (Manutenção, Atribuição e Plano de Manutenção com alerta por consulta), `inventory` (Movimento, desbloqueado por ADR-039 no mesmo dia). A 2026-08-31, `projects` ganhou Alocação de Recursos (Colaborador e Viatura, via `hr`/`fleet`), `inventory` ganhou Armazém, Transferência (retrofit do Movimento, transferência atómica) e Contagem (gera Ajuste no fecho, tudo numa transacção), e `fleet` ganhou Registo de Viagem, Despesa de Frota (sem abrir/fechar, ao contrário de Manutenção/Atribuição) e Seguros (`VehicleDocument`, ligação autónoma a `documents`) |
| 8 | Camadas de composição e portais | **2026-09-03** — Configurações & Administração (ADR-041), Portal do Colaborador (ADR-042), Dashboard Executivo e Portal do Cliente (ADR-043, resumo financeiro + facturas + extracto) feitos; pagamentos/mensagens/tickets registados como decisão em aberto, não corte de âmbito; falta só Analytics & IA |

**Faixas paralelas** — conformidade/jurídico e segurança arrancam já; frontend
arranca na Fase 3. Ver no fim.

---

## Fase 0 — Fundação de verificação e CI

**Porquê agora:** é o único momento em que a dívida ainda é pequena. Cada
módulo acrescentado antes desta fase multiplica o custo de a fazer.

| Item | Estado |
|---|---|
| `Directory.Build.props` para testes | ✅ 2026-08-15 |
| `Directory.Packages.props` para `src/` | ✅ 2026-08-16 — 14 pacotes centralizados |
| Testes de domínio xUnit | ✅ 100 testes à data (ADR-022). São **273** a 2026-08-24 |
| `Rivo.Architecture.Tests` | ✅ 21 testes (ADR-024, ADR-025) |
| Testes de integração com Testcontainers | ✅ 2026-08-16 — ADR-026, 4 testes em `notifications` |
| GitHub Actions em PR e `main` | ✅ ADR-023, dois jobs |
| Reconciliar `.claude/state/` | ✅ 2026-08-15 |
| Saldar a dívida de ADR | ✅ ADR-018 a ADR-021 |
| Fechar o K14 (coluna `version`) | ✅ ADR-025, 2026-08-16 |
| ADRs de stack de testes, CI, testes de arquitectura | ✅ ADR-022, 023, 024 |
| Teste: todo o endpoint declara autorização | ✅ ADR-024 |

**Critério de saída:** um PR que viole uma fronteira de módulo falha o build
sem intervenção humana.

**Estado: fechada em 2026-08-16.** Os PR #1 e #2 foram mergidos; `origin/main`
tem os testes de arquitectura e a concorrência optimista, e o ruleset
`build_and_domain_test` exige PR mais os dois jobs de CI. O critério de saída
está cumprido: uma violação de fronteira falha o build sem intervenção humana.

**Fica por cobrir:** testes de integração nos outros quatro módulos. Só
`notifications` os tem — escolhido por ser onde a contenção é real hoje.
Registado no ADR-026 §Risks, não é dívida escondida.

---

## Fase 1 — Aterrar em produção

**Depende de:** Fase 0.

> **Reorientada em 2026-08-20.** O destino deixou de ser Azure e passou a ser
> uma **VPS da organização**, contra o **SQL Server** que ela já opera
> (ADR-029, ADR-031). O planeamento abaixo — escrito para Azure — fica como
> registo do que se tinha decidido; o que substitui cada item está na secção
> "Execução de 2026-08-20", no fim desta fase.
>
> **O critério de saída não mudou:** as seis suites de verificação têm de
> passar contra o ambiente publicado, não apenas contra Docker local.

**Porquê antes de `approval`:** descobrir os problemas de produção com cinco
módulos é barato; com catorze não é. E três decisões pendentes — gestão de
segredos, migrações em produção, e a topologia que o K8 exige — não se fecham
em abstracto.

- IaC em **Bicep**, parametrizado por ambiente (dev / staging / prod). Nada
  criado à mão no portal.
- ~~**Container Apps** como destino da API.~~ **Alterado para App Service
  Linux B1** (ADR-027). A subscrição permite um só ambiente de Container Apps e
  ele já está ocupado por outro projecto — é restrição de quota, não mudança de
  análise. A justificação original volta a valer numa subscrição própria, e o
  ADR diz exactamente o que se perde entretanto.
- **PostgreSQL Flexible Server**, com os schemas por domínio do ADR-002.
- **Key Vault + Managed Identity.** Chave JWT e credencial da base de dados
  saem do `.env`. Fecha "gestão de segredos em produção".
- **Migrações como passo de pipeline** (`dotnet ef migrations bundle`), com
  gate antes de produção. O arranque continua a migrar só em `Development` —
  está correcto como está (ADR-020).
- **Resolver K8** — `ForwardedHeadersMiddleware` com as redes de confiança do
  ambiente. Sem restringir os proxies, trocaria um registo inútil por um
  falsificável.
- **Resolver K11** — `IDocumentStorage` sobre Blob Storage, cifrado em
  repouso. A porta já existe.
- **Resolver K9** — utilizador aplicacional sem `UPDATE`/`DELETE` na tabela de
  auditoria, com papel separado para retenção.
- **Observabilidade** desde o primeiro deploy, não depois do primeiro
  incidente.
- **CD:** `main` → staging automático; produção por tag com aprovação manual.

**Critério de saída:** as seis suites de verificação passam contra staging em
Azure, não apenas contra Docker local.

**Execução de 2026-08-16 — infraestrutura de pé, deployment por correr.**

| Item | Estado |
|---|---|
| IaC em Bicep | ✅ `infra/main.bicep`, provisionado em `rg-rivo-staging` |
| Destino da API | ✅ App Service Linux B1 (ADR-027, não Container Apps) |
| PostgreSQL Flexible | ✅ versão 17, `psql-rivo-staging-qeodyktoxeh2s` |
| Key Vault + Managed Identity | ✅ sem credenciais em configuração |
| Migrações no pipeline | ✅ `cd-staging.yml`, um bundle por módulo |
| K8 — cabeçalhos reencaminhados | ✅ fechado |
| K11 — cifra em repouso | ✅ fechado em Azure; aberto no sistema de ficheiros local |
| K9 — append-only na base de dados | ✅ fechado, verificado contra o PostgreSQL |
| Observabilidade | ✅ App Insights + Log Analytics |
| CD para staging | ⏳ escrito, **nunca executado** |

**Por fechar:** o CD nunca correu, portanto o critério de saída — as suites
contra staging — continua por cumprir. Depende do merge para `main`.

**Execução de 2026-08-20 — reorientação para VPS.**

O CD para Azure não chegou a correr, e deixou de fazer sentido correr: o
destino passou a ser uma VPS da organização, contra o SQL Server que ela já
opera. O que isso substitui, item a item:

| Planeado para Azure | Substituído por |
|---|---|
| IaC em Bicep | Nada. `infra/main.bicep` removido — não há infraestrutura a descrever (ADR-031) |
| App Service Linux B1 | `docker compose` na VPS, atrás do reverse proxy da rede `proxy` |
| PostgreSQL Flexible Server | SQL Server externo, já operado pela organização (ADR-029) |
| Key Vault + Managed Identity | `/opt/projects/rivo/.env`, escrito à mão na máquina |
| Migrações como passo de pipeline | Migração no arranque, por interruptor explícito (ADR-030) |
| App Insights + Log Analytics | `docker compose logs`. **Regressão assumida** — observabilidade volta a estar em aberto |
| `cd-staging.yml` | `.github/workflows/main.yml`: SSH, `git pull`, `compose up --build`, sonda de `/health` |

O que se manteve sem alteração:

| Item | Estado |
|---|---|
| K8 — cabeçalhos reencaminhados | ✅ fechado; a confiança passa a vir de o container não publicar porto no host |
| K9 — append-only na base de dados | ✅ fechado em SQL Server: gatilho `INSTEAD OF` + tabela sentinela contra `TRUNCATE` (ADR-029) |
| K11 — cifra em repouso | ⚠ **reaberto**: sem Blob Storage, o armazenamento é o sistema de ficheiros da VPS. O caminho de Blob continua no código |
| As 66 verificações caixa-preta | ✅ passam contra a stack local em SQL Server |
| 125 testes .NET | ✅ passam, incluindo os 4 de integração contra SQL Server real |

**Execução de 2026-08-23 — o deployment aconteceu.**

`http://187.77.178.242` está de pé: `docker compose` na VPS atrás de Caddy na
rede `proxy`, contra o SQL Server externo. O CD correu ao fim de onze tentativas
— segredos, caminho, repositório, chave de deploy, grupo `docker`, dono do
directório e rede em falta, por essa ordem.

**Critério de saída: cumprido a 89%.** As suites contra o ambiente publicado dão
**59 de 66**. As 7 que faltam exigem `RIVO_RESTART_COMMAND`, deliberadamente não
configurado para não reiniciar produção sete vezes — não são falhas da
aplicação, são casos que não chegam a correr.

**Fica aberto o K16:** sem domínio não há certificado, portanto o ambiente serve
em HTTP simples e o token viaja em claro. Aceitável só enquanto for ambiente de
teste sem dados reais.

---

## Fase 2 — `approval`, governança de decisões

**Depende de:** Fase 0. **Desbloqueia seis módulos.**

**Porquê agora:** é a última das quatro capacidades transversais e a que todos
os módulos de negócio consomem. Construir `finance` antes obrigaria a
retrofitar governança — o anti-padrão A1/A3 do protótipo.

- Fechar o modelo de dados por ADR: Política, Passo, Pedido, Atribuição,
  Decisão, Delegação, mais semântica de SLA e escalonamento. Os
  documentos-fonte remetem-no explicitamente para aqui.
- **Primeiro teste real do ADR-017:** `Rivo.Approval.Contracts` e
  `Rivo.Hr.Contracts` referenciam-se mutuamente. Se os contratos estiverem
  certos, compila; se não, o ciclo aparece com dois módulos e não com dez.
- Invariantes no domínio, com testes (ADR-008 — em código, não em RLS): BR-2,
  BR-3, BR-4, BR-6, BR-17.
- **Adiar deliberadamente** BR-7 e BR-8: precisam de dados que só `finance`
  tem. Modelar as portas; implementar na Fase 4.
- **Desbloquear `hr`:** substituir o `501` de `AssignPosition` pelo fluxo
  real, e estender o `BootstrapUserSeeder` ao primeiro Cargo com autoridade
  (ADR-015 §R2, ADR-016 §R1).
- **Fechar o K15:** a colisão de concorrência tem de devolver `409`, não
  `500`. É aqui que deixa de ser anomalia e passa a caso de uso normal.

**Critério de saída:** nenhum endpoint devolve `501`; os cenários de
segregação de `standards/testing.md` cobertos por testes de domínio.

**Execução de 2026-08-23/24 — critério de saída cumprido.**

| Item | Estado |
|---|---|
| Modelo de dados fechado por ADR | ✅ ADR-034 — Política, Passo, Pedido, Atribuição, Decisão |
| Primeiro teste real do ADR-017 | ⚠ **Resolvido por outro caminho.** Contratos dos dois lados compilam mas deixam o ciclo no grafo, e `Modules_HaveNoDependencyCycles` vê-o. Fez-se inversão de dependência com o adaptador no composition root (ADR-015 §R1 fica superado) |
| Invariantes no domínio, com testes | ✅ BR-2, BR-4, BR-6, BR-17 — 17 testes de domínio. BR-3 só metade: "quem aprova não paga" precisa de `finance` |
| Adiar BR-7 e BR-8 | ✅ Porta modelada. `RequiresBudgetCheck` **recusa a submissão** enquanto `finance` não existir, em vez de fingir que verificou |
| Desbloquear o `501` de `hr` | ✅ Atribuição de Cargo com autoridade cria-se pendente e submete-se. Dois consumidores: BR-20 e férias |
| Estender o bootstrap ao primeiro Cargo com autoridade | ⏳ **Por fazer.** ADR-016 §R1 continua aberto — o seed só atribui Perfis de Acesso |
| Fechar o K15 | ✅ ADR-035 — `409` em vez de `500`, traduzido no composition root |

**Cumprido:** nenhum endpoint devolve `501` (o de `hr` fechou; o de
`/identity/login/google` é ausência de configuração, não funcionalidade por
implementar), e os cenários de segregação estão cobertos.

**Fica por fazer, e não bloqueia a Fase 3:** SLA e escalonamento (o passo
guarda o prazo, nada o faz cumprir), Delegação (modelada em `docs`, sem
código), e o bootstrap do primeiro Cargo com autoridade.

---

## Fase 3 — `fiscal`, o que não está bloqueado

**Depende de:** Fase 0. **Bloqueio jurídico parcial.**

**Porquê agora:** `fiscal` bloqueia `commercial`, `procurement` e `payroll`.
O bloqueio é **jurídico**, não técnico — e a parte técnica é grande. Esperar
pelos pareceres para começar seria desperdiçar o tempo de espera.

- Concretizar o ADR-011: taxas e escalões como dados de referência com
  vigência temporal, nunca em código.
- Motor de determinação com as regras fechadas: IVA e códigos de isenção
  (M11–M24, M30–M38, M80–M86, M90–M94), INSS 8%/3%, dedutibilidade ao IRT só
  da parcela do trabalhador.
- Modelo de dados alinhado ao XSD do SAF-T AO 1.01_01.
- **Desenhar o K7 aqui, não em `commercial`:** a cadeia `Hash`/`HashControl`
  implica assinatura ordenada e imutável, com impacto em concorrência de
  emissão. Descoberto tarde, obriga a reescrever a emissão de facturas.
- Testes que fixam comportamento contra-intuitivo: descontinuidades de escalão
  são esperadas; um código revogado (M10, M16) é aceite na leitura e recusado
  na emissão.
- **Não inventar códigos.** Falta o código do art. 14.º/1 f) do CIVA; até
  haver, a emissão nesse caso bloqueia.

**Critério de saída:** motor utilizável para tudo o que não depende das
lacunas jurídicas, e as lacunas reduzidas a lista fechada com responsável e
data.

---

## Fase 4 — `finance`, o núcleo

**Depende de:** Fases 2 e 3.

- Ordem interna: Planeamento → AP → Tesouraria → AR → Contabilidade & Fecho.
- **Planeamento primeiro** porque BR-8 é uma porta de `approval` deixada
  aberta na Fase 2.
- Fechar BR-7 e BR-8 com implementação real, não fakes.
- BR-1 e BR-5: execução de pagamento só com decisão "Aprovado"
  **revalidada no momento**.
- **Resolver K1** antes de modelar activos.
- PGC angolano, câmbio (candidato: BNA), reconciliação bancária.

**Critério de saída:** ciclo pedido → aprovação → execução, com auditoria e
revalidação, a correr em staging.

> **Estado a 2026-08-25 — a ordem interna foi invertida, e de propósito.**
>
> O ADR-036 fixou *emitir* como meta, o que pôs AR à frente. A ordem executada
> foi **AR → AP → Tesouraria**, e Planeamento ficou por fazer. O critério de
> saída **está cumprido**: o ciclo pedido → aprovação → execução corre, com
> BR-1, BR-3 e BR-5 impostas e verificadas por 22 casos de `verify-payables`.
>
> Tesouraria ganhou extracto de conta (`BankMovement`) a 2026-08-25 — append-only
> imposto pela base de dados. **Fecha K6**; a reconciliação bancária ainda
> depende de importar o extracto do banco.
>
> **A dívida da inversão foi paga no mesmo dia.** Contabilidade & Fecho e
> Planeamento fecharam a 2026-08-25 (ADR-037), e com Planeamento veio o
> disponível orçamental: **BR-8 deixou de recusar sempre e passou a
> verificar**. Os cinco contextos internos de `finance` existem.
>
> **A postagem automática fechou no mesmo dia** — os cinco documentos lançam nos
> livros na mesma transacção em que são emitidos (ADR-037, adenda).
>
> **O que fica em dívida da Fase 4:**
>
> - **A contabilidade está de pé e vazia.** O plano de contas carrega-se e as
>   regras de postagem definem-se — o Rivo fixa a estrutura do SAF-T e recusa-se
>   a inventar o PGC angolano. **Precisa do contabilista, não de código.**
> - ~~A anulação não estorna.~~ **Fechado a 2026-08-29** — anular gera o
>   lançamento inverso, na mesma unidade de trabalho.
> - ~~K1 continua aberto~~ **Fechado a 2026-08-30 — ADR-039.** Activos Fixos
>   e depreciação deixaram de estar bloqueados por ownership; continuam sem
>   código, agora só por não estarem feitos.
> - **PGC, câmbio (BNA) e reconciliação bancária** continuam por fazer, os três
>   à espera de decisões que não são de engenharia.

---

## Fase 5 — `procurement` e `commercial`

**Depende de:** Fase 4. Paralelizáveis entre si.

- `procurement`: procure-to-pay a desaguar em AP.
- `commercial`: cobranças a desaguar em AR; a emissão encontra aqui a cadeia
  de assinatura desenhada na Fase 3.
- Fechar K3 (expansão de `procurement`) e K4 (validação em `fiscal`).
- Decidir política de preços e limiares que disparam aprovação.

**Critério de saída:** uma factura de venda sai conforme ao SAF-T AO e entra
em AR sem intervenção manual.

---

## Fase 6 — `payroll`

**Depende de:** Fase 3. **Produção travada por parecer.**

- Fechar o âmbito antes de começar: o cálculo salarial completo é in-scope?
- IRT e INSS sobre o motor da Fase 3.
- **Trave de produção:** a parcela fixa do escalão 150.001–200.000 Kz precisa
  de confirmação. Questão de direito fiscal, não de código.

**Critério de saída:** recibos correctos em staging; ida a produção
condicionada ao parecer, por decisão explícita e registada.

> **Estado a 2026-08-30 — o motor existe, confirmado contra a stack local;
> a trave de produção não mudou.** `payroll` nasceu esqueleto a 2026-08-29
> (decisão explícita, sob prazo de apresentação). O utilizador confirmou
> directamente, no mesmo dia, os dois pontos que faltavam para a Tabela B
> (parcela fixa dos escalões 150.001–200.000 e 1.500.001–2.000.000) e que o
> INSS não tem tecto — ver `pending-decisions.md`. `fiscal` ganhou
> `IncomeTaxSchedule` (escalões progressivos, mesmo padrão de vigência de
> `TaxRateSchedule`), e `AddPayrollItem` passou a perguntar-lhe na ordem do
> artigo 7.º do CIRT: INSS do trabalhador, matéria colectável, IRT,
> líquido — sempre calculado, recusando (400) quando falta taxa ou tabela
> em vigor. `verify-payroll.ps1` (17 casos) e `verify-fiscal.ps1` (20)
> confirmam, incluindo o exemplo documentado ponta-a-ponta (bruto 250.000 →
> líquido 203.600). **A trave de produção continua de pé**: a fonte dos
> valores é o utilizador, não parecer de fiscalista nem o Anexo I da Lei
> n.º 14/25 — o critério de saída desta fase (ida a produção condicionada
> ao parecer) não mudou, só deixou de bloquear o desenvolvimento e o teste.
>
> **Mesmo dia, Recibo ligado a `documents`.** `PayrollItemDocument` — mesmo
> desenho ADR-009 de `hr.EmployeeDocument`: FK real para `payroll_item(id)`
> e para `documents.document(id)`, upload e anexar como passos separados,
> só se anexa a um item de folha Aprovada (409 antes disso — inferência da
> sessão, não requisito confirmado, registada como tal em
> `modules/payroll.md`). `verify-payroll.ps1` cresceu de 17 para 22 casos,
> todos a passar à primeira corrida contra a stack local.
>
> **2026-08-31 — subsídios, a última incógnita fiscal do IRT.** O
> utilizador confirmou directamente: Alimentação e Transporte isentos até
> 30.000 Kz/mês cada, excesso soma-se à matéria colectável; Férias e Natal
> tributados normalmente, sem isenção. `fiscal` ganhou `SubsidyExemptionSchedule`
> (mesmo padrão de vigência de `TaxRateSchedule`, com um montante em vez de
> uma percentagem); `PayrollItem` ganhou os quatro componentes, como parte
> do bruto e não uma soma a ele. `verify-fiscal.ps1` cresceu de 20 para 23,
> `verify-payroll.ps1` de 22 para 26. Um defeito real apanhado só ao subir
> a stack (migração de EF esquecida — `PendingModelChangesWarning` fatal no
> arranque), corrigido no mesmo dia. **A trave de produção continua de
> pé**, mesma razão de cima: mecanismo pronto e testado, fonte por
> confirmar profissionalmente.

---

## Fase 7 — `projects`, `inventory`, `fleet`

**Depende de:** ~~Fase 4 (K1)~~ — K1 fechado por ADR-039. Paralelizáveis
entre si.

**Critério de saída:** catorze módulos, com as fronteiras ainda impostas pelos
testes de arquitectura da Fase 0.

> **Estado a 2026-08-30 — os três ganharam regra de negócio, todos
> confirmados contra a stack local.** Os três nasceram esqueletos a
> 2026-08-29 (decisão explícita, sob prazo de apresentação). `projects`
> (Marco, Tarefa e, na mesma sessão, Orçamento), `fleet` (Manutenção,
> Atribuição e, na mesma sessão, Plano de Manutenção) e `inventory`
> (Movimento — Recepção, Saída, Ajuste) ganharam regra de negócio no mesmo
> dia — `projects` e `inventory` depois de ADR-040 e ADR-039 fecharem,
> respectivamente, o Orçamento por centro de custo vs. de Projecto e a
> fronteira Activos Fixos vs. Activos que K1 registava.
> `verify-projects.ps1` 33/33 (cresceu de 28 depois de o Orçamento entrar),
> `verify-fleet.ps1` 38/38 (cresceu de 26 depois de o Plano de Manutenção
> entrar), `verify-inventory.ps1` 25/25 — sem nenhuma falha. O "alerta" do
> Plano é uma consulta (`GET /fleet/maintenance-plans/due`), não notificação
> empurrada — `identity` não resolve "todos os `AssetManager`" para um
> destinatário ainda. Detalhe em `pending-decisions.md` §Domínio e negócio,
> ADR-039, ADR-040 e `modules/fleet.md`.
>
> **Fica ainda por fazer, sem bloqueio de negócio (a 2026-08-30):** Registo
> de Viagem, Despesa de Frota e Seguros em `fleet` — fecharam a 2026-08-31,
> ver abaixo.
>
> **Alocação de Recursos em `projects` fechada a 2026-08-31** —
> `ProjectResourceAllocation` (Colaborador ou Viatura), mesmo desenho de
> `VehicleAssignment` em `fleet`, com vários recursos alocados em
> simultâneo (ao contrário de uma viatura). `fleet` publicou o seu
> primeiro contrato de leitura, `IVehicleDirectory`, para `projects`
> verificar a Viatura sem lhe possuir o registo — a segunda direcção de
> dependência prevista desde 2026-08-29. Custos ficam de fora, deliberado:
> postar em `finance` depende de "tempo real ou em lote?", ainda em
> aberto. `verify-projects.ps1` cresceu de 33 para 43, `verify-fleet.ps1`
> confirmado sem regressão em 38/38.
>
> **Armazém e Transferência em `inventory` fechados a 2026-08-31** —
> retrofit, não convivência: o Movimento já enviado (Recepção, Saída,
> Ajuste) passou a exigir `WarehouseId`, em vez de ficar por fazer ao lado
> de um Armazém novo. `Warehouse` nasceu como agregado raiz próprio.
> Transferência é atómica — sem estado "em trânsito" — e nunca altera o
> total agregado do item. Migração fez *backfill*: os movimentos
> pré-existentes na base local ganharam um armazém "Principal" gerado pela
> própria migração. `verify-inventory.ps1` cresceu de 25 para 41, sem
> nenhuma falha, primeira corrida.
>
> **Contagem em `inventory` fechada a 2026-08-31, mesmo dia** —
> `InventoryCount` (agregado raiz próprio, não filho de Item nem de
> Armazém) abre num armazém, acumula uma linha por item com o esperado
> congelado no momento em que nasce (nunca recalculado no fecho), e o
> fecho gera um Ajuste por linha com variância — na mesma transacção do
> próprio fecho, tudo ou nada, mesma disciplina de "emitir passa a
> lançar" em `finance`. Nenhuma pergunta de negócio ficou em aberto — as
> decisões de forma vieram do precedente já estabelecido no módulo no dia
> anterior. `verify-inventory.ps1` cresceu de 41 para 60, confirmado
> 60/60. Fecha a Fase 7 de `inventory` por completo.
>
> **Registo de Viagem, Despesa de Frota e Seguros em `fleet` fechados a
> 2026-08-31, mesmo dia — fecha a Fase 7 inteira.** `VehicleTrip` e
> `FleetExpense` nasceram como filhos do agregado Viatura, mesma disciplina
> de Manutenção/Atribuição/Plano, mas **sem abrir/fechar** — registam-se já
> concluídos, mesma forma de `StockMovement` em `inventory`. Motorista da
> viagem é opcional, ao contrário da Atribuição formal. Despesa cobre só as
> três categorias que o documento de produto nomeia (combustível, portagem,
> estacionamento), sem moeda (sempre AOA) e sem postagem automática no
> razão — mesma decisão que deixou Custos de fora da Alocação de Recursos.
> `VehicleDocument` (Seguros) é ligação autónoma a `documents`, fora do
> agregado, mesmo desenho de `EmployeeDocument` em `hr` (ADR-009). Nenhuma
> pergunta de negócio ficou em aberto. `verify-fleet.ps1` cresceu de 38
> para 50, confirmado 50/50.
>
> **Valorização de stock por custo médio ponderado em `inventory`, fechada
> a 2026-08-31, mesmo dia — última decisão de negócio em aberto da Fase 7.**
> O utilizador confirmou directamente: custo médio ponderado, sem fonte
> fiscal a verificar (é escolha de gestão, não facto legal). `AverageCost`
> é por item, recalculado só na Recepção; Saída, Ajuste e Transferência
> congelam o custo corrente no próprio movimento, sem o alterar.
> `GET /inventory/valuation?from=&to=` soma o valor movimentado num
> período, deliberadamente sem reconstruir quantidade/valor num ponto no
> tempo passado. `verify-inventory.ps1` cresceu de 60 para 66, confirmado
> 66/66 na segunda corrida — a primeira apanhou um defeito real
> (`averageCost` em falta na resposta da API de Transferência), corrigido
> no mesmo dia. Fecha a Fase 7 de `inventory` por inteiro, sem nenhuma
> pergunta de negócio por resolver.

---

## Fase 8 — Camadas de composição e portais

**Depende de:** Fases 4–7.

Dashboard, portais, Configurações e Analytics **não são módulos** — são read
models e canais de apresentação. O Portal do Cliente muda o perfil de risco:
é superfície externa.

**Critério de saída:** nenhuma camada de composição possui tabelas; todas lêem
por contrato publicado.

> **Iniciada a 2026-08-31 — Configurações & Administração, primeira das
> cinco.** Sem forma de código nenhuma antes disto: ADR-041 fixou o padrão
> — `Application` + `Api`, sem Domain nem Infrastructure, em
> `src/Composition/<Nome>/` (não `src/Modules/`), a depender de outros
> módulos só pelos seus contratos, exactamente a mesma regra de sempre.
> `Rivo.Settings` compõe `identity` (perfis de acesso, primeiro consumidor
> de `Rivo.Identity.Contracts`, que nasceu para isto) e `approval`
> (políticas, segundo contrato de leitura do módulo) num único
> `GET /settings/overview`. Admin-only sem permissão nova — as duas
> permissões que a vista soma já só pertenciam a `Admin`.
> `scripts/verify-settings.ps1` (7 casos) confirmou 7/7 na primeira
> corrida.
>
> **Segunda, mesmo dia — Portal do Colaborador.** O utilizador respondeu
> directamente à escolha de por onde continuar, e registou a decisão a
> tomar: "próprio" resolve-se pelo vínculo Identity → Employee
> (`hr.Employee.UserId`, já existia desde a Fase 0, nunca tinha
> consumidor), **nunca por permissão nova** — é regra de contexto, não
> autorização (ADR-042). Sem colaborador ligado, `GET /portal/me` devolve
> 403, sem tentar adivinhar; não aceita `employeeId` nenhum, nunca vê o
> colaborador de outra conta; Admin continua a usar os fluxos
> administrativos existentes em vez de um atalho pelo portal.
>
> `Rivo.Hr.Contracts` ganhou `IEmployeeDirectory.FindByUserIdAsync`.
> **Consequência necessária, não pedida:** `Employee.UserId` passou a ser
> único quando preenchido (índice filtrado + verificação em
> `HireEmployee`) — até aqui ninguém confiava em "no máximo um colaborador
> por conta", porque não havia consumidor a assumi-lo.
> `Rivo.EmployeePortal.Application.Tests` (4 casos),
> `scripts/verify-employee-portal.ps1` (8 casos) e dois casos novos em
> `verify-hr.ps1` (18→20) confirmaram tudo na primeira corrida.
>
> **Ordem para o resto da Fase 8, decidida pelo utilizador:** contratos de
> leitura Finance/Commercial → Dashboard Executivo → decisão de identidade
> externa → Portal do Cliente → Analytics & IA. Nenhum destes avança sem a
> decisão que lhe falta — nomeadamente, não se volta ao plano de contas do
> PGC só porque a Fase 8 está em curso; só se essa decisão bloquear
> directamente algo que a Fase 8 precise. Ver
> `domain/domain-map.md` §Read models, ADR-041 §Consequences e ADR-042.
>
> **Terceira, mesmo dia — os contratos de leitura de `finance`.** Primeiro
> passo da ordem acima: sem eles, o Dashboard Executivo não tinha o que
> compor. `Rivo.Commercial.Contracts` já resolvia o nome do cliente
> (`ICustomerDirectory`) — a lacuna real era inteiramente do lado de
> `finance`. `IReceivablesOverview` (receita líquida, saldo de Contas a
> Receber, top clientes) e `IPayablesOverview` (despesa líquida, saldo de
> Contas a Pagar) — separados um do outro, mesma fronteira interna de
> `ISalesInvoiceStore`/`IPayablesStore`; moeda sempre explícita, nunca
> somada entre moedas (mesma disciplina de `BudgetCheck`); só saldos
> correntes, nunca reconstrução a uma data passada (mesma fronteira de
> `GET /inventory/valuation`). Detalhe completo do desenho em
> `modules/finance.md`. **Só os contratos — o Dashboard em si continua por
> construir**, sem consumidor real ainda. 14 testes novos em
> `Rivo.Finance.Application.Tests` (133 no total), sem regressão em
> `verify-finance.ps1`/`verify-payables.ps1`.
>
> **Quarta, mesmo dia — o Dashboard Executivo, item 1 do documento de
> produto.** O utilizador confirmou o âmbito directamente: os cinco
> números do documento de produto (receita, despesa, lucro, Contas a
> Receber, Contas a Pagar — mais os clientes que mais facturaram), num só
> `GET /dashboard/overview`. `Rivo.Dashboard` compõe
> `IReceivablesOverview`/`IPayablesOverview`; lucro é `Receita − Despesa`,
> calculado aqui e não um contrato à parte — os dois lados já vêm no mesmo
> regime (Fase 8, ronda anterior), por isso subtrair é a conta inteira.
>
> **Primeira camada de composição a ganhar `Contracts` próprio.** Não
> porque algo a componha — ninguém compõe — mas porque `docs/rivo-suite-descricao-modulos.md`
> nomeia `Manager` para ver o Dashboard, e `Manager` não tem
> `finance.invoices.read` (só `Finance` tem). Exigir os contratos
> subjacentes (mesmo padrão de `Rivo.Settings`) excluiria a audiência que
> o documento de produto nomeia — por isso `dashboard.overview.read` é
> permissão própria, publicada em `Rivo.Dashboard.Contracts` para
> `identity` a conceder, mesmo mecanismo que qualquer módulo usa para o
> seu catálogo.
>
> **Um defeito real, só visível ao subir a stack:** `TopCustomersByInvoicedAsync`
> (`ISalesInvoiceStore`, ronda anterior) projectava `GroupBy` directamente
> para um registo posicional — o EF Core recusa-se a traduzir isso para
> SQL e lança em runtime; os 133 testes de Application não apanharam
> porque os fakes fazem LINQ-to-Objects, sem essa restrição. Corrigido
> projectando primeiro para um tipo anónimo, materializando, e só depois
> mapeando para `CustomerInvoicedTotal`.
>
> `Rivo.Dashboard.Application.Tests` (novo, 5 casos).
> `scripts/verify-dashboard.ps1` (novo, 9 casos) — moeda de teste própria
> (`ZZZ`) para os totais saírem exactos mesmo depois de
> `verify-finance`/`verify-payables` já terem corrido, e asserções por
> **delta** em vez de zero absoluto, para a suite continuar re-executável
> mesmo com dados de corridas anteriores na mesma moeda (descoberto ao
> testar: uma corrida falhada anterior tinha deixado dados por trás).
> Confirmado 9/9. `verify-bootstrap` confirma Admin com 67 permissões
> (66 + `dashboard.overview.read`), sem regressão em `verify-settings`/
> `verify-employee-portal`.
>
> **Fica por fazer da Fase 8:** Portal do Cliente (bloqueado pela decisão
> de identidade externa) e Analytics & IA (adiado até os módulos
> produtores terem contratos estáveis).
>
> **2026-09-03 — decisão de identidade externa fechada (ADR-043), e a
> ligação construída.** Duas perguntas, respondidas directamente pelo
> utilizador: como o Cliente se autentica (conta própria em `identity`,
> oitavo Perfil de Acesso — `Cliente`, vazio até o Portal existir, mesmo
> estado em que `AssetManager`/`ProjectManager` estiveram) e como fica
> ligado ao `commercial.Customer` certo (Sales/Admin liga manualmente,
> nunca por auto-declaração do NIF — mesmo desenho do ADR-042, papéis
> invertidos). `Customer.UserId` (único quando preenchido, mesmo desenho
> de `Employee.UserId`), `LinkCustomerAccount` e
> `POST /commercial/customers/{id}/account` construídos.
> `verify-commercial.ps1` cresceu de 12 para 17 casos, confirmado 17/17;
> `verify-authorization`/`verify-bootstrap`/`verify-settings` actualizados
> de 7 para 8 perfis, sem regressão.
>
> **Mesmo dia — Portal do Cliente construído, primeiro corte.** O
> utilizador confirmou o âmbito directamente: das sete coisas que
> `docs/rivo-suite-descricao-modulos.md` §12 descreve, três (pagamentos
> online, mensagens, tickets de suporte) precisam de infra-estrutura que
> não existe — não são corte de âmbito, são módulos inteiros por fazer.
> Ficam registadas em `pending-decisions.md`, sem código, até haver
> resposta. As outras três (dashboard financeiro, facturas, extracto)
> entraram todas: resumo financeiro (receita líquida e saldo em aberto,
> mesmas contas do Dashboard Executivo, agora por cliente), a lista das
> próprias facturas, e o extracto de conta corrente (mesmo dia, ronda
> seguinte — ver abaixo).
>
> `IReceivablesOverview` ganhou as variantes por cliente
> (`GetCustomerNetRevenueAsync`, `GetCustomerOutstandingAsync`,
> `ListCustomerInvoicesAsync`) e `ICustomerDirectory` ganhou
> `FindByUserIdAsync` — mesmo sentido de leitura de
> `IEmployeeDirectory.FindByUserIdAsync` (ADR-042), agora do lado de
> `commercial`. `Rivo.CustomerPortal` (quarta camada de composição) compõe
> os dois num único `GET /customer-portal/me`, sem permissão própria —
> "próprio" continua a ser regra de contexto, nunca autorização, mesma
> disciplina do Portal do Colaborador. `Rivo.CustomerPortal.Application.Tests`
> (novo, 4 casos) e `scripts/verify-customer-portal.ps1` (novo, 9 casos)
> confirmaram tudo na primeira corrida — um cliente novo nasce com receita e
> saldo a zero por construção (é um registo novo, sem histórico), por isso
> a suite não precisou da moeda isolada que o Dashboard usa.
>
> **Mesmo dia, ronda seguinte — extracto de conta corrente, e as outras três
> registadas como decisão em aberto.** O utilizador pediu para "mitigar o
> que faltou"; a resposta separou o extracto (dados já existentes em
> `finance`, sem infra-estrutura nova) dos outros três (pagamentos online,
> mensagens, tickets de suporte — módulos inteiros, cada um com decisões de
> negócio que não se inventam: que gateway, que fluxo de mensagens, que
> SLA). Só o extracto entrou; os outros três ficaram em
> `pending-decisions.md` §Domínio e negócio, sem código.
>
> `IReceivablesOverview.GetCustomerStatementAsync` junta facturas (débito),
> notas de crédito e recibos (crédito) do cliente em ordem cronológica, com
> saldo corrido — a abertura é a mesma soma de
> `GetCustomerOutstandingAsync`, só cortada antes de `from`, **não** a
> reconstrução de estado que a fronteira de `GET /inventory/valuation` já
> recusa fazer (são contas directas sobre documentos, não uma cadeia a
> percorrer). `ICreditNote`/`IReceipt` ganharam
> `ListCreditNotesForCustomerAsync` (nova) e reutilizaram
> `ListReceiptsAsync` (já existia). `GET /customer-portal/me/statement`,
> segundo endpoint do Portal do Cliente.
>
> **Dois defeitos reais na fake partilhada de testes, apanhados ao escrever
> os casos novos** — `FakeSalesInvoiceStore.ListReceiptsAsync` (e, na ronda
> anterior, `.ListAsync`) ignoravam os filtros de cliente e período por
> completo, nunca exercitados antes por não haver consumidor a filtrar.
> Corrigidos os dois. `ReceivablesOverviewTests` +3, `verify-customer-portal`
> cresceu de 9 para 10 casos, confirmado 10/10 na primeira corrida depois de
> um erro de aritmética meu no próprio teste (a nota de crédito tem IVA de
> 14%, o saldo esperado estava sem o imposto).
>
> **Antes disto, 2026-09-02/03 — incidente de produção fora do fluxo desta
> sessão, resolvido, e `main` ganhou protecção.** Sete commits
> (`lts`…`lts6`, `Abrir swagger`) chegaram a `main` sem PR (não havia
> protecção nenhuma) e sem CI a correr contra eles — causaram um deploy
> partido (migração em falta de `AccountingRule`/`ChartOfAccountsVersion`,
> adicionadas sem migração) e reabriram o K8 por outra via (porta 5080
> publicada em produção). Corrigidos; `main` passou a exigir PR e os dois
> checks de CI, `enforce_admins` incluído. A primeira corrida real de CI
> contra este código apanhou mais dois defeitos genuínos que nunca tinham
> compilado nem corrido: `LedgerTests.cs` chamava uma API de domínio
> inventada, e `ChartOfAccountsVersion.Version` colidia de nome com o
> contador de concorrência reservado (renomeado para `Revision`).
> `verify-all.ps1` passou a tolerar o K20 conhecido explicitamente (por
> texto do caso, não pelo número), em vez de bloquear o gate inteiro por
> um defeito de quatro investigações sem causa de código. `develop`
> nasceu como branch de trabalho — ver `known-issues.md` K20 e os PRs do
> repositório para o detalhe completo.

---

## Faixas paralelas

### Conformidade e jurídico

> **Despriorizada em 2026-08-24 pelo ADR-036.** O objectivo do produto deixou de
> ser emissão legalmente válida. A urgência que esta faixa tinha — "arrancar
> hoje, não na Fase 3" — **caducou por decisão de produto, não por o trabalho
> ter sido feito.** Volta a ser caminho crítico no dia em que as facturas
> tiverem de sair para clientes.

- **Certificação junto da AGT.** O `SoftwareValidationNumber` é campo
  obrigatório do SAF-T; sem ele não há emissão legal. Continua a ser o item de
  prazo mais longo, se e quando voltar ao âmbito.
- **Lista oficial de códigos de isenção.** É a única desta faixa que bloqueia
  algo **hoje**: sem ela, emitir com `ISE` ou `NS` devolve `501`.
- Obter a DS.120 v1.4 oficial.
- Parecer de fiscalista sobre a parcela fixa do IRT — bloqueia `payroll`.
- Confirmar se existe API oficial da AGT.
- Confirmar RPO ≤24h / RTO ≤8h e o alvo de disponibilidade.

### Segurança — arranca já, contínuo

- **Expiração por inactividade** — só existe absoluta; os 15 minutos para
  perfis decisórios não estão satisfeitos.
- **MFA** — o Entra ID saiu de cena com o Azure (ADR-031); o mecanismo fica por
  decidir. A entrada por Google **não** o resolve: a 2FA da conta Google não é
  exigível nem verificável pelo Rivo (ADR-032).
- Refresh token, se a duração fixa se revelar incómoda.
- ~~Permissões dos restantes cinco perfis.~~ **Cinco dos sete perfis já têm
  permissões** — faltam `AssetManager` e `ProjectManager`, que dependem de
  módulos sem código.
- **K16 — sem TLS no ambiente publicado.** É o item mais urgente desta faixa:
  password e token viajam em claro. Depende de haver domínio.
- **Utilizador aplicacional restrito na base de dados.** A aplicação liga-se
  como `sa`.

### Frontend

**Desbloqueado desde 2026-08-24.** A condição era a superfície da API estabilizar
com a Fase 3, e o ADR-036 fechou-a: `identity`, `hr`, `approval`, `fiscal`,
`commercial` e `finance` respondem, com 71 endpoints.

React + Tailwind, servido pelo mesmo reverse proxy da VPS. Começar por
`identity`, `hr` e `approval`. O trabalho corre noutra sessão, em `front/`.

---

## Mapa de execução (Fase 1)

Substitui o mapa Azure, abandonado em 2026-08-20 (ADR-031).

| Peça | Papel | Fecha |
|---|---|---|
| VPS + `docker compose` | API e worker, num container (ADR-031) | Topologia de produção → K8 |
| Reverse proxy na rede `proxy` | Único caminho até à API; termina TLS | Exposição externa |
| SQL Server externo | Schemas por domínio (ADR-002, ADR-029) | Utilizador de BD restrito → K9 |
| `/opt/projects/rivo/.env` | Chave JWT e credenciais, fora do repositório | Segredos em produção |
| Volume `rivo-documents-data` | `IDocumentStorage` em sistema de ficheiros | — (K11 fica aberto) |
| `docker compose logs` | Diagnóstico | — (observabilidade fica aberta) |
| GitHub Actions | CI (Fase 0) e deploy por SSH (Fase 1) | Migrações em produção (ADR-030) |

**Fora de âmbito, deliberadamente:** sem orquestrador — o ADR-001 escolheu
monólito modular para não pagar orquestração distribuída. Sem broker de
mensagens — o despacho é interno ao processo. Sem multi-tenancy — ADR-003
exclui-a da v1. Sem registo de imagens — a VPS constrói a sua, e o ADR-031
diz quando é que isso deixa de servir.

---

## Registo de vereditos

| Data | Fase | Estado | Nota |
|---|---|---|---|
| 2026-08-16 | 0 | **Fechada** | Todos os itens feitos: ADR-024 e ADR-025 em `main`, gestão central de pacotes, Testcontainers (ADR-026). Critério de saída cumprido — um PR que viole uma fronteira falha o build, e o ruleset impõe-o. Ficam por cobrir os testes de integração dos outros quatro módulos, registado no ADR-026 |
| 2026-08-16 | 1 | Em curso | Infraestrutura provisionada em `rg-rivo-staging`. K8, K9 e K11 fechados. O CD está escrito e nunca correu — o critério de saída depende disso |
| 2026-08-23 | 1 | Quase fechada | Deployment na VPS a correr atrás de Caddy. As suites contra o ambiente publicado: **59 de 66 passam**; as 7 restantes exigem `RIVO_RESTART_COMMAND`, deliberadamente não configurado para não reiniciar produção. K16 aberto — sem TLS até haver domínio |
| 2026-08-24 | 2 | **Critério de saída cumprido** | `approval` com as cinco camadas, o `501` de `hr` fechado, dois consumidores (BR-20 e férias), worker de reconciliação, e o K15 fechado por ADR-035. Ficam SLA, Delegação e o bootstrap do primeiro Cargo com autoridade — nenhum bloqueia a Fase 3 |
| 2026-08-24 | 3, 4, 5 | Reordenadas | ADR-036 — emitir sem certificação. `fiscal` reduzido a taxa com vigência, `commercial` ao Cliente, `finance`/AR à factura de venda. Verificado contra a API: `FT S001/1` saiu com 5% por o facto gerador cair em Março, e `FT S001/2` com 7% em Setembro |

---

## Reordenação de 2026-08-24 — ADR-036

O objectivo do produto passou a ser **emitir**, não emitir com validade legal.
Isso reordena as Fases 3, 4 e 5, e o `roadmap-execucao.md` acima descreve o
plano anterior — fica como registo.

**Fatia entregue, verificada contra a API a correr:**

```
fiscal (mínimo)      Taxa com vigência + determinação à data do facto gerador
commercial (mínimo)  Cliente
finance (mínimo, AR) Factura de venda com numeração FT S001/1
```

Uma factura sai com o cliente congelado, a taxa que vigorava à data do facto
gerador, e um número sequencial. **Não é documento fiscal válido em Angola** —
falta a certificação da AGT e a cadeia `Hash`/`HashControl`.

**Adiado, e o que custa:**

| Adiado | Custo de o fazer depois |
|---|---|
| Certificação AGT, exportação SAF-T, declarações | Nenhum sobre o que está feito — acrescenta-se |
| Cadeia `Hash`/`HashControl` (K7) | **Baixo**, e é o ponto: numeração, ordem e imutabilidade já existem, portanto a assinatura enxerta-se sem reescrever a emissão |
| Códigos de isenção | Emitir com `ISE`/`NS` devolve `501` até haver a lista oficial |
| Motor de IRT e INSS | `payroll` (Fase 6) continua bloqueado, como já estava — **implementado a 2026-08-30**, ver a nota na Fase 6 acima |

**O que a Fase 4 ainda deve por inteiro:** Contas a Pagar, Tesouraria,
Contabilidade & Fecho, Planeamento, e com eles BR-1, BR-3, BR-5 e o disponível
orçamental de BR-8.
