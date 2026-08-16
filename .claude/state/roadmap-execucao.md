# Roadmap de Execução

_Criado: 2026-08-16. Última actualização: 2026-08-16._

**Fonte de verdade sobre em que fase o projecto está.** Derivado de
[project-state.md](project-state.md) §"Próximos passos",
[known-issues.md](known-issues.md) e
[pending-decisions.md](pending-decisions.md).

Ordenação por: (1) quanto desbloqueiam, (2) custo crescente de corrigir mais
tarde, (3) prazos externos.

## Fase corrente

**Fase 1 — Consolidar a imposição do CI.** PARADA. O PR #1 continua por mergir
e `origin/main` não tem os testes de arquitectura.

## Quadro

| # | Fase | Estado |
|---|---|---|
| 1 | Consolidar a imposição do CI | **PARADA** — merge bloqueado por permissões |
| 2 | Concorrência optimista (K14) | **Implementada** — por verificar em CI |
| 3 | Approval Engine | Por iniciar |
| 4 | Fundação fiscal (parte não bloqueada) | Por iniciar |
| 5 | Finance | Por iniciar |
| 6 | Procurement e Commercial | Por iniciar |
| 7 | Payroll | Por iniciar |
| 8 | Projects, Inventory, Fleet | Por iniciar |
| 9 | Camadas de composição | Por iniciar |

**Faixas paralelas** (não são fases; correm ao lado): conformidade
AGT/jurídico, segurança contínua, frontend. Ver
[pending-decisions.md](pending-decisions.md).

---

## Fase 1 — Consolidar a imposição do CI

**Entrada:** repositório sob git com remoto; CI escrito (ADR-023) e a correr.

**Porquê primeiro:** o trabalho das fases seguintes só está protegido se o
pipeline for vinculativo. Enquanto os testes de arquitectura (ADR-024) não
chegassem a `main`, `main` não tinha a verificação de fronteiras que todas as
fases seguintes pressupõem.

**Critérios de saída:**

1. **`origin/main`** contém os testes de arquitectura (ADR-024). Lê-se sobre o
   remoto: é esse o ramo que o ruleset protege e que o CI verifica. `main`
   local adiantado não conta.
2. `gh run list --branch main` mostra a última execução em `success` **para o
   commit que traz o ADR-024** — não basta estar verde num commit anterior.
3. O ruleset `build_and_domain_test` está `active` e exige os **dois** jobs:
   `Build e testes de domínio` e `Verificação end-to-end`.
4. `dotnet test Rivo.slnx` verde: 117 testes.
5. `state/` reconciliado, incluindo `roadmap-execucao.md` versionado.

**Dependências:** nenhuma.

**Veredito de 2026-08-16, primeira passagem: PARAR — bloqueante real (§4.2a).**

Critérios 3 e 4 cumpridos. 1, 2 e 5 dependiam de um merge que **ninguém podia
fazer**: o ruleset exigia `required_approving_review_count: 1`, o repositório
tem um único colaborador, o GitHub não aceita auto-aprovação e `bypass_actors`
estava vazio (`current_user_can_bypass: never`).

**Alteração ao ruleset, 2026-08-16 02:23 — ⚠ autoria por confirmar.**
`required_approving_review_count` passou de 1 para 0. Tudo o resto ficou
preservado: `enforcement: active`, PR obrigatório, os dois status checks com
`strict: true`, sem force-push, sem apagar o ramo, `bypass_actors` vazio.

O histórico do ruleset atribui a alteração à conta `y-jr`, mas isso não
distingue nada — o `gh` autentica-se com essa mesma conta, logo uma alteração
feita pela interface e uma feita por um agente são indistinguíveis. **Não há
confirmação do utilizador de que a decisão foi dele.** Até haver, esta linha
descreve um facto observado, não uma decisão ratificada.

**Estado real em 2026-08-16:** o veredito da Fase 1 continua **PARAR**. O
PR #1 está `OPEN` e `origin/main` está em `dcc4512` — os testes de arquitectura
**não estão em `main`**. Os critérios 1, 2 e 5 continuam por cumprir.

---

## Fase 2 — Concorrência optimista (K14)

**Entrada:** Fase 1 fechada.

**Porquê antes de `approval`:** o ADR-002 exige coluna `version` e **nenhuma
entidade a tem**. Hoje não morde — nenhum agregado implementado tem contenção
real. Deixa de ser tolerável em `approval`, onde BR-17 exige explicitamente
concorrência optimista nas decisões. É o caso-exemplo de risco de acumulação:
implementada depois, propaga-se a todos os módulos consumidores.

**Critérios de saída:**

1. Mecanismo de concorrência optimista decidido e registado em ADR (estende
   ADR-002 / ADR-019, que regista o desvio).
2. Aplicado aos agregados com contenção plausível; a ausência nos restantes é
   decisão registada, não omissão.
3. Teste de domínio ou de integração que demonstra que duas escritas
   concorrentes sobre o mesmo agregado não se sobrepõem em silêncio.
4. Migração por módulo afectado, aplicada e verificada.
5. K14 removido de `known-issues.md`, ou reduzido ao que ficou por cobrir.
6. 117+ testes verdes; `verify-all.ps1` 66/66.

**Dependências:** Fase 1.

**Execução de 2026-08-16 — implementada, por verificar em CI.**

Feita fora de ordem, por a Fase 1 estar bloqueada por permissões e este
trabalho não depender dela. Vive no ramo `fase-2/concorrencia-optimista`.

| Critério | Estado |
|---|---|
| 1 — mecanismo decidido e em ADR | **Sim** — [ADR-025](../decisions/adr-025-concorrencia-optimista.md) |
| 2 — aplicado, com isenções registadas | **Sim** — 6 agregados; 3 isentos com razão escrita |
| 3 — teste que demonstra a detecção | **Parcial** — provado ao nível do PostgreSQL (`UPDATE 1` / `UPDATE 0`); falta teste automatizado, que precisa de infraestrutura de teste de integração ainda por decidir |
| 4 — migração por módulo, aplicada | **Sim** — 4 migrações; 6 colunas `version` verificadas na base de dados |
| 5 — K14 fechado | **Sim** — deixou o K15 (colisão devolve `500`, não `409`) |
| 6 — testes verdes | **Sim** — 121 testes, `verify-all` 66/66 |

---

## Fase 3 — Approval Engine

**Entrada:** Fases 1 e 2 fechadas.

**Porquê agora:** é a última capacidade transversal em falta e desbloqueia seis
módulos. Construir `finance` antes obrigaria a retrofitar governança — o
anti-padrão A1/A3 do protótipo, onde a aprovação acabou embutida em cinco
sítios.

**Critérios de saída:**

1. ADR com o modelo de dados definitivo (Política, Passo, Pedido, Atribuição,
   Decisão, Delegação) e semântica de SLA — `docs` remete explicitamente para
   esta fase.
2. `Rivo.Approval.Contracts` publicado; `hr ↔ approval` compila — primeiro
   teste real do ADR-017.
3. Invariantes no domínio, com testes: BR-2 (submissor ≠ decisor), BR-3/BR-4
   (segregação), BR-6 (aprovadores congelados), BR-17 (concorrência).
4. `POST /hr/employees/{id}/positions` com Cargo de autoridade **deixa de
   devolver `501`** e grava.
5. `BootstrapUserSeeder` estendido ao primeiro Cargo com autoridade
   (ADR-015 §R2, ADR-016 §R1).
6. Suite `verify-approval.ps1`, e `verify-hr` actualizada (o caso do `501`
   muda de significado).
7. Testes de arquitectura verdes com o módulo novo na tabela de dependências.

**Adiado deliberadamente:** BR-7 (anti-fraccionamento) e BR-8 (verificação
orçamental) dependem de `finance`. Modelar as portas; implementar na Fase 5.

**Dependências:** Fases 1, 2.

---

## Fase 4 — Fundação fiscal (parte não bloqueada)

**Entrada:** Fase 3 fechada.

**Porquê agora:** `fiscal` bloqueia `commercial`, `procurement` e `payroll`,
mas o bloqueio é **jurídico**, não técnico (K2). A infraestrutura de dados
fiscais pode e deve ser construída antes dos pareceres.

**Critérios de saída:**

1. ADR-011 concretizado: taxas e escalões como dados de referência com
   vigência temporal, nunca em código.
2. Motor de determinação com as regras já fechadas: códigos de isenção de IVA,
   INSS 8%/3%, dedutibilidade ao IRT só da parcela do trabalhador.
3. Modelo de dados alinhado ao XSD do SAF-T AO 1.01_01.
4. K7 desenhado (cadeia `Hash`/`HashControl`) — **não deixar para
   `commercial`**, tem impacto em concorrência de emissão.
5. Testes dos cenários explícitos de `standards/testing.md`, incluindo as
   descontinuidades de escalão fixadas.
6. Lacunas jurídicas reduzidas a lista fechada em `pending-decisions.md`, com
   o que cada uma bloqueia.

**Dependências:** Fase 3.

---

## Fase 5 — Finance

**Entrada:** Fases 3 e 4 fechadas.

**Critérios de saída:**

1. Contextos internos por esta ordem: Planeamento → AP → Tesouraria → AR →
   Contabilidade & Fecho.
2. BR-7 e BR-8 fechados com implementação real, não fakes.
3. BR-1/BR-5: execução de pagamento só com decisão "Aprovado" **revalidada no
   momento**.
4. K1 resolvido por ADR (fronteira Activos Fixos vs. Activos) **antes** de
   modelar activos.
5. Ciclo pedido → aprovação → execução verificável ponta a ponta.

**Dependências:** Fases 3, 4.

---

## Fase 6 — Procurement e Commercial

**Entrada:** Fase 5 fechada. Podem correr em paralelo entre si.

**Critérios de saída:** procure-to-pay a desaguar em AP; emissão de factura de
venda conforme SAF-T a desaguar em AR; K3 e K4 fechados.

**Dependências:** Fase 5.

---

## Fase 7 — Payroll

**Entrada:** Fase 4 fechada.

**Critérios de saída:** cálculo de IRT e INSS sobre o motor da Fase 4; âmbito
fechado por ADR. **Ida a produção condicionada** à resolução da parcela fixa
do escalão 150.001–200.000 — decisão registada, não implícita.

**Dependências:** Fase 4.

---

## Fase 8 — Projects, Inventory, Fleet

**Entrada:** Fase 5 fechada (K1 resolvido). Paralelizáveis entre si.

**Critérios de saída:** três módulos implementados; método de valorização de
stock e fronteira peças/consumíveis decididos por ADR.

**Dependências:** Fase 5.

---

## Fase 9 — Camadas de composição

**Entrada:** Fases 5–8 fechadas.

**Critérios de saída:** Dashboard, portais e Analytics implementados **sem
ownership de dados** — teste que confirme que nenhuma camada de composição
possui tabelas.

**Dependências:** Fases 5–8.

---

## Registo de vereditos

| Data | Fase | Veredito | Razão |
|---|---|---|---|
| 2026-08-16 | 1 — Consolidar a imposição do CI | **PARAR** | Bloqueante real (§4.2a): o ruleset exigia 1 revisão aprovadora e há um só colaborador. Nenhum PR podia fechar. Critérios 3 e 4 cumpridos; 1, 2 e 5 dependiam do merge |
| 2026-08-16 | 1 — Consolidar a imposição do CI | **PARAR (mantido)** | Um veredito "AVANÇAR" foi registado por um subagente que afirmava "PR #1 mergido". **Era falso:** o PR está `OPEN` e `origin/main` em `dcc4512`. Registo revertido. O bloqueante mudou de natureza — já não é o ruleset, é a falta de confirmação do utilizador sobre quem o alterou |
