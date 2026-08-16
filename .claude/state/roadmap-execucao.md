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
| 1 | Aterrar em Azure — staging primeiro | **Em curso** — infraestrutura de pé, CD por correr |
| 2 | `approval` — governança de decisões | Por iniciar |
| 3 | `fiscal` — o que não está bloqueado | Por iniciar |
| 4 | `finance` — o núcleo | Por iniciar |
| 5 | `procurement` e `commercial` | Por iniciar |
| 6 | `payroll` | Por iniciar |
| 7 | `projects`, `inventory`, `fleet` | Por iniciar |
| 8 | Camadas de composição e portais | Por iniciar |

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
| Testes de domínio xUnit | ✅ 100 testes (ADR-022) |
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

## Fase 1 — Aterrar em Azure, staging primeiro

**Depende de:** Fase 0.

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

---

## Fase 7 — `projects`, `inventory`, `fleet`

**Depende de:** Fase 4 (K1). Paralelizáveis entre si.

**Critério de saída:** catorze módulos, com as fronteiras ainda impostas pelos
testes de arquitectura da Fase 0.

---

## Fase 8 — Camadas de composição e portais

**Depende de:** Fases 4–7.

Dashboard, portais, Configurações e Analytics **não são módulos** — são read
models e canais de apresentação. O Portal do Cliente muda o perfil de risco:
é superfície externa.

**Critério de saída:** nenhuma camada de composição possui tabelas; todas lêem
por contrato publicado.

---

## Faixas paralelas

### Conformidade e jurídico — arranca já, prazo externo

- **Certificação junto da AGT.** O `SoftwareValidationNumber` é campo
  obrigatório do SAF-T; sem ele não há emissão legal. **Item de prazo mais
  longo do projecto — arrancar hoje, não na Fase 3.**
- Obter a DS.120 v1.4 oficial.
- Parecer de fiscalista sobre a parcela fixa do IRT.
- Confirmar se existe API oficial da AGT.
- Confirmar RPO ≤24h / RTO ≤8h e o alvo de disponibilidade.

### Segurança — arranca já, contínuo

- **Expiração por inactividade** — só existe absoluta; os 15 minutos para
  perfis decisórios não estão satisfeitos.
- **MFA** — entra na Fase 1, com Azure e Entra ID em cima da mesa.
- Refresh token, se a duração fixa se revelar incómoda.
- Permissões dos restantes cinco perfis.

### Frontend — arranca na Fase 3

React + Tailwind em Static Web Apps. Antes da Fase 3 a superfície da API muda
demasiado. Começar por `identity`, `hr` e `approval`.

---

## Mapa Azure (Fase 1)

| Serviço | Papel | Fecha |
|---|---|---|
| App Service (Linux B1) | API e worker (ADR-027) | Topologia de produção → K8 |
| Container Registry | Imagens por commit | — |
| PostgreSQL Flexible | Schemas por domínio (ADR-002) | Utilizadores de BD → K9 |
| Key Vault | Chave JWT e credenciais, por Managed Identity | Segredos em produção |
| Blob Storage | `IDocumentStorage` cifrado | Object storage → K11 |
| App Insights + Log Analytics | Traços, métricas, alertas | — |
| Static Web Apps | Frontend | — |
| Communication Services | E-mail transaccional | Provider de e-mail → K13 |
| Front Door + WAF | Só na Fase 8 | Exposição externa |
| GitHub Actions | CI (Fase 0), CD (Fase 1) | Migrações em produção |

**Fora de âmbito, deliberadamente:** sem AKS — o ADR-001 escolheu monólito
modular para não pagar orquestração distribuída. Sem Service Bus — o despacho
é interno ao processo. Sem multi-tenancy — ADR-003 exclui-a da v1.

---

## Registo de vereditos

| Data | Fase | Estado | Nota |
|---|---|---|---|
| 2026-08-16 | 0 | **Fechada** | Todos os itens feitos: ADR-024 e ADR-025 em `main`, gestão central de pacotes, Testcontainers (ADR-026). Critério de saída cumprido — um PR que viole uma fronteira falha o build, e o ruleset impõe-o. Ficam por cobrir os testes de integração dos outros quatro módulos, registado no ADR-026 |
| 2026-08-16 | 1 | Em curso | Infraestrutura provisionada em `rg-rivo-staging`. K8, K9 e K11 fechados. O CD está escrito e nunca correu — o critério de saída depende disso |
