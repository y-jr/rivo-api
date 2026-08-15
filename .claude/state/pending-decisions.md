# Decisões Pendentes

_Última actualização: 2026-08-15._

Decisões por tomar antes de o trabalho relacionado poder avançar com
confiança. Uma vez decididas, registar como ADR em
[decisions/](../decisions/) e remover daqui.

## Já decididas (não reabrir sem ADR)

Estas estavam em aberto e foram fechadas — listadas para evitar
re-litigação:

| Questão | Resolução |
|---|---|
| Modular Monolith vs. Microservices | ADR-001 |
| Base de dados e ownership | ADR-002 — PostgreSQL. SQL Server e MySQL avaliados e rejeitados |
| Multi-tenancy | ADR-003 — não há na v1 |
| Autenticação vs. autorização | ADR-004 |
| Cargo vs. Perfil; Departamento vs. Centro de Custo | ADR-005 |
| Orçamento vs. Previsão de Custos Departamentais | ADR-006 |
| Approval: core ou supporting | ADR-007 — supporting |
| Onde vive a segregação de funções | ADR-008 — domínio; RLS é secundária |
| Documentos e contratos | ADR-009 |
| Referência a Colaborador e FK entre schemas | ADR-010 |
| Jurisdição fiscal | Angola (AGT, SAF-T AO, IVA/IRT/INSS, PGC, Lei 22/11) |
| Payroll dentro de `hr`? | Bounded context próprio, schema `payroll` |
| Assemblies de contratos por módulo | ADR-017 — `Rivo.X.Contracts` sem dependências, criado quando o módulo tem consumidor |
| Framework web/API e convenções de routing | ADR-018 — Minimal APIs, um grupo de rotas por módulo; o host não agrega endpoints |
| ORM e convenções de mapeamento | ADR-019 — EF Core, um `DbContext` por módulo, `snake_case`, UUIDv7 |
| Tooling de migrações | ADR-020 — migrações EF Core por módulo, com tabela de histórico própria em cada schema |
| Ambiente local / containerização | ADR-021 — Docker Compose, imagem multi-fase, utilizador não-root |
| Framework de teste e estrutura do domínio | ADR-022 — xUnit v2.9.3, sem biblioteca de asserções, um projecto por domínio de módulo |
| Pipeline de CI | ADR-023 — GitHub Actions, dois jobs: build+testes de domínio (bloqueia PR) e verificação end-to-end |

## Stack tecnológica

- [ ] **⚠ CD e ambientes.** O CI está fechado (ADR-023); **o deployment não**.
      Continua por decidir onde e como se publica, e com ele o passo de
      migrações em produção que o ADR-020 deixou deliberadamente em aberto.
- [ ] **Frameworks de teste de integração** com infraestrutura real
      (candidato: Testcontainers). O domínio está resolvido pelo ADR-022;
      Application, Infrastructure e API continuam sem cobertura própria.
- [ ] **Tooling de testes de arquitectura** (imposição de fronteiras) —
      mitigação do risco 1 em [project-state.md](project-state.md). O ADR-017
      dá a estrutura; falta quem a imponha automaticamente. O ADR-018 §Risks
      acrescenta um caso concreto: verificar que **todo o endpoint declara
      autorização**.
- [ ] **Mecanismo geral de despacho de eventos** entre módulos. O worker de
      `notifications` resolve o caso dele; os eventos de domínio previstos em
      [modules/](../modules/) não têm mecanismo.
- [ ] **Gestão central de versões de pacotes em `src/`.** Cada um dos 25
      `.csproj` fixa as suas — diverge sozinho à medida que os módulos crescem.
      `tests/` já está resolvido por `Directory.Build.props` (ADR-022); falta
      fazer o equivalente em `src/`, provavelmente com
      `Directory.Packages.props`.
- [ ] **`SharedKernel`:** criar ou assumir que não é preciso. O
      [CLAUDE.md](../CLAUDE.md) manda mantê-lo mínimo, mas ele nunca existiu e
      até hoje não fez falta. Decidir explicitamente em vez de deixar a
      contradição de pé.

## Fornecedores e integrações

- [x] ~~Provider de autenticação~~ — **ASP.NET Core Identity** (ADR-012).
      Fecha implicitamente o ORM: **EF Core**.
- [x] ~~Esquema de credencial~~ — **JWT bearer** com sessão persistida
      (ADR-013).
- [x] ~~Sessão como entidade~~ — implementada em `Domain`, com IP, user agent,
      expiração e revogação (ADR-013).
- [x] ~~Idioma do código~~ — código em inglês; comentários e comunicações
      externas em português.
- [ ] **⚠ Expiração por inactividade.** Só existe expiração absoluta. O
      requisito de 15 min para perfis decisórios **não está satisfeito**.
      Implementá-lo exige escrita por pedido ou estratégia de janela.
- [ ] **Refresh token.** Sem ele, expirada a sessão o utilizador volta a
      autenticar-se. Revisitar se a duração se revelar incómoda.
- [ ] Mecanismo concreto de MFA.
- [x] ~~Catálogo de Perfis de Acesso~~ — os 7 perfis semeados; permissões como
      role claims (ADR-014).
- [x] ~~Quem atribui Cargos~~ — **ADR-015**: catálogo por `Admin`; atribuição
      por `HR`; atribuições de Cargos com autoridade de aprovação passam por
      `approval`.
- [x] ~~Bootstrap do primeiro Admin~~ — **ADR-016**: seed controlado e
      idempotente, credenciais de configuração. O mecanismo de bootstrap não
      participa das regras normais de autorização.
- [ ] **⚠ Bootstrap do primeiro Cargo com autoridade** (ADR-015 §R2, ADR-016
      §R1). O seed atribui apenas Perfis de Acesso; a autoridade de decisão vem
      do **Cargo**. `hr` já existe e já tem a tabela — **o bloqueio mudou de
      módulo**: criar um Cargo com autoridade exige decisão de `approval`, que
      não existe, e é isso que faz o `501`. Resolve-se estendendo o
      `BootstrapUserSeeder` quando `approval` for implementado.
- [x] ~~Assemblies de contratos por módulo~~ — **ADR-017**. `Rivo.X.Contracts`
      sem dependências; criado quando o módulo tem consumidor. Já aplicado a
      `audit`, `documents`, `hr` e `notifications`. **Por exercitar:** a
      dependência mútua `hr ↔ approval`, que é o caso que motivou o ADR, só
      será posta à prova quando `approval` existir.
- [ ] Permissões dos restantes cinco perfis. Estão semeados os sete, mas só
      `Admin` e `HR` têm permissões — os outros esperam pelos módulos de
      negócio que os justificam.
- [ ] Gestão de segredos em produção (chave de assinatura JWT, credencial da
      base de dados).
- [ ] **Topologia de produção e cabeçalhos reencaminhados.** Necessária para
      corrigir K8 (o IP registado na sessão é o do proxy). Configurar
      `X-Forwarded-For` sem definir os proxies de confiança permitiria a
      qualquer cliente forjar o próprio IP — pior do que o defeito actual.
- [ ] Aplicação de migrações em produção. No arranque só acontece em
      `Development`; produção precisa de passo próprio no pipeline.
- [ ] **Provider de e-mail transaccional.** Bloqueia a entrega real: o canal
      registado é `LoggingNotificationChannel`, que escreve em log e não envia
      nada (K13). A porta `INotificationChannel` já existe — falta o adaptador.
- [ ] Gateway de pagamento (mercado angolano).
- [ ] Fonte da taxa de câmbio — candidato: BNA.
- [ ] Provider de modelos de IA.
- [ ] **Serviço de object storage para `documents`.** A implementação actual de
      `IDocumentStorage` escreve num volume do sistema de ficheiros. É também
      do que depende a resolução do K11 (sem cifra em repouso).
- [ ] **Hipótese a validar:** existe API oficial da AGT? Até confirmação,
      tratar como geração de ficheiro para submissão via portal.
- [ ] Mecanismo de reconciliação bancária (OFX / CSV / MT940 / API) —
      depende de cada instituição.

## Domínio e negócio

- [x] ~~Numeração e conteúdo obrigatório de factura, estrutura de dados
      fiscal~~ — **resolvido** pelo XSD do SAF-T AO v1.01_01
      (`github.com/assoft-portugal/SAF-T-AO`). Ver
      [modules/fiscal.md](../modules/fiscal.md) §"Fonte normativa".
- [x] ~~Modelo para taxas e escalões~~ — **resolvido** por ADR-011: dados de
      referência versionados com vigência temporal, nunca código.
- [x] ~~INSS dedutível ao IRT?~~ — **resolvido**: sim, artigo 7.º do CIRT
      (Lei n.º 18/14). Só a parcela do trabalhador (3%); a patronal (8%)
      nunca.
- [x] ~~Taxas de INSS~~ — **resolvido**: 8% empregador / 3% trabalhador. As
      propostas de 10%/5% não estão em vigor.
- [x] ~~Códigos de isenção de IVA~~ — **resolvido**: M11–M24 (art. 12.º),
      M30–M38 (art. 15.º), M80–M86 (art. 14.º), M90–M94 (art. 16.º).
      M10 e M16 revogados pela Lei n.º 14/23.
- [x] ~~Qual tabela de IRT está em vigor~~ — **resolvido** adoptando a KPMG
      como fonte: isenção de **150.000 Kz** desde 01/01/2026 (Lei n.º
      14/25). A tabela de isenção 70.000 é histórica.
- [ ] **⚠ Parcela fixa do escalão 150.001–200.000 (12.500 Kz).** Se
      correcta, um rendimento de 150.001 paga 12.500 — salto de 12.500 Kz
      por 1 Kz. Ou o salto é real (a tabela histórica tem o mesmo padrão), ou
      a parcela fixa foi ajustada na Lei n.º 14/25 e a transcrição arrastou o
      valor antigo. **Questão de direito fiscal.** Exige o Anexo I da Lei
      n.º 14/25 ou parecer de fiscalista. **Bloqueia produção de `payroll`**
      — desenvolvimento e teste podem prosseguir com o valor provisório.
- [ ] Divergência na parcela fixa do escalão 1.500.001–2.000.000:
      292.000 vs. 292.250.
- [ ] **Obter DS.120 v1.4 oficial da AGT** (Especificação Técnica de
      Facturação Electrónica, Agosto 2025). As cópias localizadas estão em
      Scribd — não é fonte fiável para especificação normativa.
- [ ] **Código oficial para a isenção do art. 14.º/1 f)** do CIVA (doações
      ao Estado) — não existe na DS.120 v1.4. Até haver, bloquear emissão;
      **não inventar `M87`**.
- [ ] Outras taxas reduzidas de IVA além dos 5% de equipamento industrial.
- [ ] Tratamento de subsídios em IRT (alimentação, transporte, férias,
      Natal).
- [ ] Tecto contributivo no INSS; prazo de entrega; expatriados.
- [ ] Regras completas do Grupo B do IRT.
- [ ] Prazos e formatos das declarações periódicas à AGT.
- [ ] Processo de certificação de software junto da AGT
      (`SoftwareValidationNumber` é campo obrigatório do SAF-T).
- [ ] **Fronteira Activos Fixos (`finance`) vs. Activos (`inventory`)** —
      `docs` §1.2 assinala a sobreposição mas não a resolve. Não assumir
      dono.
- [ ] Método de valorização de stock (FIFO / custo médio ponderado / outro).
- [ ] Âmbito exacto de `payroll` — o cálculo salarial completo é in-scope?
- [ ] Peças e consumíveis de frota: stock próprio de `fleet` ou itens de
      `inventory`?
- [ ] Política de preços e descontos: `commercial` ou configuração
      administrativa? Limiares que disparam aprovação.
- [ ] Regras de férias (acumulação, saldo, carry-over).
- [ ] Orçamento de Projecto vs. Orçamento por centro de custo — relacionados
      ou independentes?
- [ ] Postagem em `finance`: tempo real ou em lote?

## Segurança

- [ ] Expiração de sessão: uniforme ou por perfil? (referência de partida:
      15 min para perfis decisórios)
- [ ] Os 7 perfis de acesso são suficientes, ou é preciso granularidade por
      operação?
- [ ] Mecanismo concreto de garantia append-only em `audit`.
- [ ] **Hipótese a confirmar:** RPO ≤24h / RTO ≤8h aplicam-se a toda a
      plataforma, ou só a Pagamentos (âmbito confirmado do SGAP)?
- [ ] **Hipótese a confirmar:** disponibilidade da plataforma. O SGAP fixa
      99,5% em horário alargado para Pagamentos; o Portal do Cliente é
      externo e pode exigir 24/7.

## Approval Engine

- [ ] Modelo de dados definitivo — `docs` remete para fase de desenho
      detalhado.
- [ ] Semântica de SLA e de escalonamento.
- [ ] Regras de segregação além do mínimo já fixado ("quem submete não
      decide").
