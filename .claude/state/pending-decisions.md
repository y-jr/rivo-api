# Decisões Pendentes

_Última actualização: 2026-08-25._

Decisões por tomar antes de o trabalho relacionado poder avançar com
confiança. Uma vez decididas, registar como ADR em
[decisions/](../decisions/) e remover daqui.

## Já decididas (não reabrir sem ADR)

Estas estavam em aberto e foram fechadas — listadas para evitar
re-litigação:

| Questão | Resolução |
|---|---|
| Modular Monolith vs. Microservices | ADR-001 |
| Base de dados e ownership | ADR-002 (desenho de schemas) + ADR-029 (motor) — SQL Server, contra a instância que a organização já opera |
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
| Tooling de testes de arquitectura | ADR-024 — reflexão e leitura de `.csproj`, sem biblioteca; 21 testes |
| Frameworks de teste de integração | ADR-026 — Testcontainers com SQL Server real, um container por assembly |
| Alojamento, CD e migrações em produção | ADR-031 (VPS por SSH e `docker compose`) + ADR-030 (migração no arranque, por interruptor) |
| Autenticação federada | ADR-032 — ID token da Google validado contra o JWKS. **Não cria contas**, e não traz MFA |
| CORS para clientes de browser | ADR-033 — lista por configuração, **sem credenciais** (o token viaja em `Authorization`, não em cookie) |
| Desenho do `approval` | ADR-034 — Política, Passo, Pedido, Atribuição, Decisão. `AnyApprover` por omissão. Sem endpoint de submissão |
| Conflito de concorrência → HTTP | ADR-035 — `409` traduzido no composition root, **sem repetição automática**. Fecha o K15 |
| Emitir sem certificação | ADR-036 — forma do documento fiscal sem conformidade legal. Reordena as Fases 3, 4 e 5 |
| Fronteira Activos Fixos (`finance`) vs. Activos (`inventory`) | ADR-039 — coexistem, com relação explícita e idealmente 1:1. Fecha o K1 |
| Orçamento de Projecto vs. Orçamento por centro de custo (`finance`) | ADR-040 — entidades distintas, relacionadas, sem duplicação |

## Stack tecnológica

- [ ] **⚠ Observabilidade em produção.** Com o Azure abandonado (ADR-031), o
      diagnóstico é `docker compose logs` numa máquina: sem métricas, sem
      alertas, sem retenção. É uma regressão assumida, não um esquecimento.
- [ ] **⚠ Utilizador de base de dados restrito aos schemas do Rivo.** A
      instância é partilhada com outros sistemas (ADR-029) e o isolamento é só
      por schema. Arrasta consigo o K9 — o papel separado para retenção da
      auditoria, que continua por criar.
- [ ] **Cópia de segurança do volume de documentos da VPS.** A base de dados
      tem backup; `rivo-documents-data` não (ADR-031).
- [ ] **Frameworks de teste de integração** com infraestrutura real
      (candidato: Testcontainers). O domínio está resolvido pelo ADR-022;
      Application, Infrastructure e API continuam sem cobertura própria.
- [ ] **Testes de arquitectura para regras de persistência** (ADR-010). O
      ADR-024 fechou as regras de referência; FKs entre schemas não são
      verificáveis por reflexão e exigiriam inspecção da base de dados, ou
      seja, teste de integração.
- [ ] **Mecanismo geral de despacho de eventos** entre módulos. O worker de
      `notifications` resolve o caso dele; os eventos de domínio previstos em
      [modules/](../modules/) não têm mecanismo.
- [x] ~~**Gestão central de versões de pacotes em `src/`.**~~ Resolvido por
      `Directory.Packages.props` na raiz, que é onde as versões vivem hoje.
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
      do **Cargo**.

      **Actualizado em 2026-08-25:** `approval` existe desde 2026-08-23 e o
      `501` fechou — a razão original deste pendente caducou. O que resta é o
      ovo e a galinha: criar o primeiro Cargo com autoridade exige decisão de
      `approval`, e não há ninguém com autoridade para a tomar. Resolve-se
      estendendo o `BootstrapUserSeeder`, à maneira do ADR-016: o bootstrap é o
      passo anterior às regras de autorização existirem.
- [x] ~~Assemblies de contratos por módulo~~ — **ADR-017**. `Rivo.X.Contracts`
      sem dependências; criado quando o módulo tem consumidor. Já aplicado a
      `audit`, `documents`, `hr` e `notifications`. **Por exercitar:** a
      dependência mútua `hr ↔ approval`, que é o caso que motivou o ADR, só
      será posta à prova quando `approval` existir.
- [ ] **Permissões de `AssetManager` e `ProjectManager`.** Dos sete perfis,
      **cinco já têm permissões** (2026-08-25): `Admin`, `HR`, `Manager`,
      `Finance` e `Sales`. Os dois que faltam esperam por `inventory`/`fleet` e
      `projects`, que não têm código — inventar-lhes permissões seria adivinhar.
- [ ] Gestão de segredos em produção (chave de assinatura JWT, credencial da
      base de dados).
- [x] ~~**Topologia de produção e cabeçalhos reencaminhados.**~~ **K8 fechado
      em 2026-08-16.** A confiança não vem de uma lista de proxies: vem de o
      container não publicar porto nenhum no host, portanto o único caminho até
      ele é o reverse proxy (ADR-031).
- [x] ~~Aplicação de migrações em produção.~~ **ADR-030** — migração no
      arranque por interruptor explícito (`MIGRATE_ON_STARTUP`), porque o
      deployment em VPS não tem pipeline com acesso à base de dados. O
      interruptor é a aprovação.
- [ ] **Provider de e-mail transaccional.** Bloqueia a entrega real: o canal
      registado é `LoggingNotificationChannel`, que escreve em log e não envia
      nada (K13). A porta `INotificationChannel` já existe — falta o adaptador.
- [x] ~~Gateway de pagamento (mercado angolano).~~ **Resolvido a 2026-09-03:
      não há gateway.** Os meios de pagamento electrónico em Angola (Multicaixa
      Express, referência) têm tectos pensados para retalho, não para B2B.
      Confirmado pelo utilizador — o fluxo real é transferência bancária
      directa, com o cliente a submeter o comprovativo e `finance` a confirmar
      manualmente. Ver ADR-044.
- [ ] Fonte da taxa de câmbio — candidato: BNA.
- [ ] Provider de modelos de IA.
- [ ] **Serviço de object storage para `documents`.** A implementação actual de
      `IDocumentStorage` escreve num volume do sistema de ficheiros. É também
      do que depende a resolução do K11 (sem cifra em repouso).
- [ ] **Hipótese a validar:** existe API oficial da AGT? Até confirmação,
      tratar como geração de ficheiro para submissão via portal.
- [ ] Mecanismo de reconciliação bancária (OFX / CSV / MT940 / API) —
      depende de cada instituição.

      **A metade que não dependia disto está feita** (2026-08-25): o Rivo tem
      extracto de conta próprio, com movimento por alteração de saldo e o
      apontador de volta ao documento que o causou. Falta o lado de fora —
      importar o extracto do banco e emparelhar. **É essa metade que precisa da
      decisão**, e a pergunta concreta para o banco é: em que formato
      disponibiliza o extracto?

## Domínio e negócio

- [ ] **Portal do Cliente — as duas capacidades que faltam
      (`docs/rivo-suite-descricao-modulos.md` §12).** Registado a
      2026-09-03, depois de o resumo financeiro, as facturas e o extracto
      de conta corrente terem fechado. A primeira das três, pagamentos
      online, fechou no mesmo dia (ADR-044). Nenhuma das duas que restam é
      corte de âmbito — são infra-estrutura que ainda não existe, e cada
      uma exige uma decisão de negócio antes de haver código:

      1. ~~Mensagens directas com a equipa comercial.~~ **Resolvido a
         2026-09-04 (ADR-045):** assíncronas; `Customer.AssignedToEmployeeId`
         (vendedor responsável, só decide quem é notificado — não é controlo
         de acesso); módulo novo `messaging`, uma conversa aberta por
         cliente. Ver ADR-045.
      2. **Tickets de suporte.** Categorias, SLA, quem resolve (perfil
         `Sales` existente, ou um novo) — mesma pergunta de fundo de
         `approval`: se algum dia tiver alçada, seguiria o motor já feito.

      Não inventar a que resta sem resposta a isto — ver ADR-043, ADR-044,
      ADR-045 e `state/roadmap-execucao.md` Fase 8.

- [ ] **O plano de contas (PGC angolano), e as regras de postagem que o usam.**
      ⚠ **É o que hoje impede a contabilidade de servir para alguma coisa.**

      Não é decisão de engenharia: o Rivo fixa a estrutura que o XSD do SAF-T
      fixa — formato do código, as seis categorias, a conta agregadora — e
      **recusa-se a inventar o conteúdo** (ADR-037). Não há fonte primária do
      PGC neste repositório, e um plano plausível seria pior do que nenhum:
      ninguém o reveria, e o erro apareceria no primeiro ficheiro entregue à
      AGT.

      **Precisa do contabilista.** São duas coisas:

      1. As contas, carregadas de cima para baixo por
         `POST /finance/ledger/accounts`.
      2. As regras de postagem — que conta debita e que conta credita em cada
         um dos cinco acontecimentos (factura de venda, nota de crédito,
         recibo, factura de compra, execução de pagamento). O sistema verifica
         que a regra equilibra, mas não sabe que contas usar.

      Enquanto não existirem, os documentos emitem-se e **não lançam** — o que
      é o comportamento correcto, e não uma avaria.

- [ ] **`ChartOfAccountsVersion`/`AccountingRule` chegaram ao código (commit
      `50000fa`, fora do fluxo desta sessão) sem nenhum caminho que os ligue a
      `LedgerAccount`.**

      Achado a 2026-09-02 ao diagnosticar um deploy de produção partido por
      falta de migração (`FinanceDbContext` com `PendingModelChangesWarning`).
      `POST /finance/ledger/accounts` (`OpenLedgerAccount`) nunca recebe nem
      atribui uma versão do plano; `LedgerAccount.AssignToVersion` é `internal`
      e não é chamado de lado nenhum; `BootstrapChartOfAccounts.Load()` é
      código morto (não semeado por `SeedFinanceModuleAsync` nem por outro
      arranque). Todas as contas — já abertas ou por abrir — ficam com
      `ChartOfAccountsVersionId = Guid.Empty`.

      A migração `ContabilidadeDeGestao` (gerada nesta sessão, para desbloquear
      o deploy) semeia uma versão-placeholder com id `Guid.Empty` só para a FK
      `NOT NULL` não rejeitar essas contas — **não é a atribuição real de
      versão**, é o mínimo para o schema não rebentar com o código como está.

      Falta decidir (negócio, não engenharia): quando o PGC real acima existir,
      como é que uma conta passa a apontar para a versão certa — no momento em
      que se abre, ou por migração em lote quando uma versão nova entra em
      vigor? Isto é anterior a essa decisão: hoje não há *nenhum* caminho,
      nem para o placeholder nem para uma versão real.

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
- [x] ~~⚠ Parcela fixa do escalão 150.001–200.000 (12.500 Kz)~~ — **confirmado
      pelo utilizador a 2026-08-30: 12.500 Kz, o salto é real** (produz um
      rendimento de 150.001 a pagar 12.500 Kz — salto de 12.500 Kz por 1 Kz —
      tal como no escalão equivalente da tabela histórica, isenção 70.000).

      Reafirmado antes pelo mesmo utilizador, no mesmo dia, como
      deliberadamente por decidir e não inventável por decisão de
      arquitectura — a fonte da confirmação é o próprio utilizador, **não**
      o Anexo I da Lei n.º 14/25 nem parecer de fiscalista, que continuam por
      obter. Registar esta distinção sempre que o valor for citado.
- [x] ~~Divergência na parcela fixa do escalão 1.500.001–2.000.000: 292.000
      vs. 292.250~~ — **confirmado pelo utilizador a 2026-08-30: 292.250 Kz**
      (valor da Angolex; a contribuição do cliente, 292.000, fica descartada
      como provável erro de OCR). Mesma reserva de fonte da entrada acima.
- [ ] **Obter DS.120 v1.4 oficial da AGT** (Especificação Técnica de
      Facturação Electrónica, Agosto 2025). As cópias localizadas estão em
      Scribd — não é fonte fiável para especificação normativa.
- [ ] **Código oficial para a isenção do art. 14.º/1 f)** do CIVA (doações
      ao Estado) — não existe na DS.120 v1.4. Até haver, bloquear emissão;
      **não inventar `M87`**.
- [ ] Outras taxas reduzidas de IVA além dos 5% de equipamento industrial.
- [x] ~~Tratamento de subsídios em IRT (alimentação, transporte, férias,
      Natal)~~ — **confirmado pelo utilizador a 2026-08-31**: Subsídio de
      Alimentação e Subsídio de Transporte isentos até **30.000 Kz/mês
      cada**; o excesso soma-se à matéria colectável (não perde a isenção
      da parte dentro do limiar). Subsídio de Férias e Subsídio de Natal
      **sem isenção nenhuma** — tributados normalmente, somam-se ao
      salário do mês em que são pagos. **A fonte é o utilizador, não
      fonte fiscal profissional** — mesma reserva das entradas
      equivalentes de IRT/INSS acima. Implementado no mesmo dia:
      `SubsidyExemptionSchedule` em `fiscal` (ADR-011), `PayrollItem` com
      os quatro componentes.
- [x] ~~Tecto contributivo no INSS~~ — **confirmado pelo utilizador a
      2026-08-30: sem tecto.** Os 3%/8% incidem sobre o salário bruto
      inteiro, sem limite superior. Fonte é o utilizador, não texto legal
      primário — mesma reserva das duas entradas acima. Prazo de entrega e
      regras de expatriados continuam em aberto, sem resposta.
- [x] ~~Motor de cálculo de IRT/INSS em `payroll`~~ — **implementado a
      2026-08-30**, com as quatro entradas acima como valores de entrada.
      `IncomeTaxSchedule` (novo agregado em `fiscal`) modela a tabela de
      escalões; `AddPayrollItem` pergunta a `fiscal` na ordem do artigo 7.º
      do CIRT. **A reserva de fonte não muda** — o mecanismo está pronto e
      testado, mas os valores continuam por confirmar profissionalmente
      antes de produção.
- [ ] Regras completas do Grupo B do IRT.
- [ ] Prazos e formatos das declarações periódicas à AGT.
- [ ] Processo de certificação de software junto da AGT
      (`SoftwareValidationNumber` é campo obrigatório do SAF-T).
- [x] ~~Fronteira Activos Fixos (`finance`) vs. Activos (`inventory`)~~ —
      **resolvido por ADR-039** (2026-08-30): coexistem — `inventory` dono do
      activo físico/operacional, `finance` do activo contabilístico, relação
      explícita e idealmente 1:1 quando é o mesmo bem. Nem todo item de
      `inventory` é Activo Fixo. Fecha o K1.
- [x] ~~Método de valorização de stock (FIFO / custo médio ponderado /
      outro)~~ — **confirmado pelo utilizador a 2026-08-31: custo médio
      ponderado.** Mesma reserva das demais decisões de negócio directas ao
      utilizador nesta lista — não há fonte fiscal a verificar aqui, é
      escolha de gestão, não facto legal. Implementado no mesmo dia:
      `InventoryItem.AverageCost`, recalculado só na Recepção; Saída, Ajuste
      e Transferência congelam o custo corrente sem o alterar. Ver
      `modules/inventory.md`.
- [ ] Âmbito exacto de `payroll` — o cálculo salarial completo é in-scope?
- [ ] Peças e consumíveis de frota: stock próprio de `fleet` ou itens de
      `inventory`?
- [ ] Política de preços e descontos: `commercial` ou configuração
      administrativa? Limiares que disparam aprovação.
- [ ] Regras de férias (acumulação, saldo, carry-over).
- [x] ~~Orçamento de Projecto vs. Orçamento por centro de custo~~ —
      **resolvido por ADR-040** (2026-08-30): entidades distintas,
      relacionadas. `projects` é dono do Orçamento de Projecto; `finance`
      continua dono do orçamento por centro de custo (ADR-037). O mecanismo
      concreto de validação cruzada fica por desenhar quando houver código.
- [ ] Postagem em `finance`: tempo real ou em lote?
- [ ] **Recibo só se anexa a um item de folha Aprovada** (`payroll`,
      2026-08-30) — implementado como inferência do domínio (um recibo é
      prova do que foi autorizado; os valores de um item podem mudar em
      Draft/PendingApproval), **não confirmado com o utilizador nem
      registado em `docs/`**. Revisível se aparecer caso de uso real que
      precise de anexar antes (ex.: rascunho de recibo para conferência).
      Ver `modules/payroll.md`.
- [x] ~~Prazo de retenção legal do recibo (BR-15, `payroll`)~~ —
      **confirmado pelo utilizador a 2026-08-31: 10 anos.** Mesma reserva de
      fonte das demais entradas fiscais — não é texto legal primário.
      **Só documentado, sem código novo**: BR-14 já bloqueia eliminação
      física em todo o sistema (nenhum módulo publica rota `DELETE`), por
      isso o prazo já está estruturalmente satisfeito; um campo explícito
      de "retido até" ficaria sem consumidor e seria especulativo. Ver
      `modules/payroll.md`.
- [x] ~~Ordem e bloqueios do resto da Fase 8~~ — **decidido pelo
      utilizador a 2026-08-31**, em resposta directa à escolha de por onde
      continuar depois de Configurações & Administração (ADR-041) e Portal
      do Colaborador (ADR-042):
      1. ~~**Contratos de leitura Finance/Commercial**~~ (receita, despesa,
         AR/AP, top clientes) — **feito a 2026-08-31**:
         `IReceivablesOverview`/`IPayablesOverview`, ambos em `finance`
         (a única lacuna real; `commercial.ICustomerDirectory` já
         resolvia o nome do cliente). Ver `modules/finance.md`.
      2. ~~**Dashboard Executivo**~~ — **feito a 2026-08-31, mesmo dia**:
         `Rivo.Dashboard` (`GET /dashboard/overview`), âmbito confirmado
         directamente pelo utilizador ("os cinco chegam, um só endpoint").
         Ver `state/implemented.md` §dashboard.
      3. **Decisão de identidade externa** — identidade de cliente, ciclo
         de vida da conta, autenticação, isolamento, recuperação, MFA,
         relação Customer ↔ conta externa. Bloqueia (4); não improvisar
         solução temporária. **Próximo item da ordem, ainda por decidir.**
      4. **Portal do Cliente**, só depois de (3).
      5. **Analytics & IA** — adiado até os módulos produtores terem
         contratos e semântica estáveis; IA acrescenta governação e acesso
         a dados que ainda não têm onde assentar.

      **Princípio registado para não se repetir a pergunta:** não
      interromper uma decisão arquitectural já fechada (como esta) para
      resolver uma decisão de negócio que não bloqueia o trabalho actual —
      por isso o plano de contas do PGC (acima) não volta a estar em cima
      da mesa só por a Fase 8 estar em curso, a menos que alguma
      funcionalidade da Fase 8 passe a depender directamente dele.

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

- [x] ~~Modelo de dados definitivo — `docs` remete para fase de desenho
      detalhado.~~ **Fechado por ADR-034** (2026-08-23), que é essa fase.
- [ ] **Semântica de SLA e de escalonamento.** O passo guarda o prazo; nada o
      faz cumprir. Por decidir: o que acontece quando o prazo passa —
      notificar, escalar para o nível acima, aprovar por omissão (quase de
      certeza não), ou nada.
- [ ] **Delegação.** Modelada em `docs`, sem código. Delegante, delegado,
      período, e o efeito sobre BR-2 e BR-4 — se o delegado herda os
      impedimentos do delegante, o que é a resposta provável mas não está
      decidido.
- [ ] Regras de segregação além do mínimo já fixado ("quem submete não
      decide"). **Metade de BR-3** — "quem aprova não paga" — depende de
      `finance` ter execução de pagamento, que não existe.

## Emissão sem certificação (ADR-036)

**As três primeiras foram respondidas a 2026-08-25.** Detalhe na adenda ao
[ADR-036](../decisions/adr-036-emitir-sem-certificacao.md).

- [x] ~~**Marcar visivelmente uma factura que não é documento fiscal?**~~
      **Sim.** `SalesInvoice.FiscalNotice`, vinda de `Finance:FiscalNotice` e
      **congelada na emissão** — no dia da certificação, as facturas emitidas
      antes continuam a não ser válidas e mantêm a menção. Vazio em
      configuração significa sistema certificado.

- [x] ~~**Que série de numeração usar, e quem a abre?**~~ **Uma contínua por
      tipo de documento**, `S001`, sem reinício anual, criada pelo seed no
      arranque. Reiniciar por ano obrigaria a decidir para qual série emitir
      perto da viragem, que é uma hipótese de erro que a numeração contínua não
      tem.

- [x] ~~**Existe "consumidor final"?**~~ **Sim.** `CustomerId` passa a anulável
      e `InvoicedParty.FinalConsumer(...)` constrói o retrato de quem não se
      identificou.

      ⚠ **Fica um pendente dentro deste:** o identificador que vai no lugar do
      NIF vem de `Finance:FinalConsumerTaxId`, com omissão `CONSUMIDORFINAL` —
      deliberadamente não plausível como NIF. **A convenção angolana continua
      por levantar em fonte primária, e tem de substituir esta antes de
      qualquer certificação.**

- [ ] **Unicidade do NIF:** índice único em `commercial.customer` mais
      verificação na camada Application. Por fazer — não é invariante que o
      agregado possa garantir, porque não vê o conjunto.
