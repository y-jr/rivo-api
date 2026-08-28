# Estado do Projecto

_Última actualização: 2026-08-27_

## Fase actual

**Dez dos catorze módulos têm código, e há um ambiente publicado.**

As quatro capacidades transversais estão feitas — `audit`, `documents`,
`notifications` e `approval`. A partir daí, o objectivo do produto mudou: o
ADR-036 dispensou a emissão legalmente válida e fixou **emitir** como meta, o
que reordenou as Fases 3, 4 e 5 do
[roadmap-execucao.md](roadmap-execucao.md).

Hoje o **ciclo de venda fecha** — emitir, corrigir por nota de crédito, receber
por recibo, e o saldo diz o que falta — e o **ciclo de compra também**: registar
a factura do fornecedor, pedir o pagamento, aprová-lo e executá-lo contra uma
conta bancária, com extracto que reconcilia.

É no pagamento que **BR-1, BR-3, BR-5 e BR-8** se encontram: verifica-se o
orçamento antes de deixar decidir, sem decisão aprovada não se paga, a decisão é
revalidada no momento, o saldo é verificado, e quem aprovou não pode executar.

`finance` tem os **cinco contextos internos** desde 2026-08-25, e os documentos
**lançam nos livros na mesma transacção** em que são emitidos.

⚠ Mas **a contabilidade está de pé e vazia**: o plano de contas carrega-se — o
Rivo fixa a estrutura do SAF-T e recusa-se a inventar o PGC angolano (ADR-037) —
e a tradução documento → contas é configuração. **Sem plano carregado e sem
regras definidas, nada lança**, e isso é deliberado.

⚠ **As facturas não são documentos fiscais válidos em Angola** — têm a forma,
falta a certificação da AGT, e trazem menção disso congelada na emissão.

## Módulos

| Módulo | Estado |
|---|---|
| `identity` | Completo. JWT com sessão revogável, RBAC com 7 perfis, entrar com Google, bootstrap por seed, **gestão de conta** — mudar e repor password, activar/desactivar contas, ver e terminar sessões, retirar perfis |
| `audit` | Completo. Trilha append-only imposta pela base de dados, consulta filtrada |
| `documents` | Completo. Upload/download, listagem do arquivo, hash de integridade, ligação a `hr` por FK entre schemas |
| `notifications` | Completo menos a entrega real. Fila com estado, worker, leitura e marcação (uma a uma ou todas) — **sem envio de e-mail** (K13) |
| `hr` | Completo. Colaborador, Departamento, Cargo, Contrato, Assiduidade, Férias, Benefícios, Recrutamento, Onboarding/Offboarding |
| `approval` | Completo para o âmbito fixado. Políticas (criar e desactivar), pedidos, decisões, BR-2/4/6/17, worker de reconciliação. ⚠ **K18**: cancelar um pedido exige só permissão de leitura |
| `fiscal` | ⚠ **Fatia mínima** (ADR-036). Taxa com vigência e determinação. Não é o motor fiscal |
| `commercial` | ⚠ **Reduzido ao Cliente** (ADR-036). Sem funil comercial |
| `finance` | **Os cinco contextos existem, e os documentos lançam.** Venda (factura, nota de crédito, recibo, saldo), Contas a Pagar, Tesouraria com extracto append-only, Contabilidade & Fecho com postagem automática, Planeamento. **BR-1, BR-3, BR-5 e BR-8 impostas.** ⚠ Contabilidade vazia até alguém carregar o plano; a anulação não estorna; activos fixos bloqueados por K1 |
| `procurement` | **Os quatro agregados.** Fornecedor com IBAN verificado (ISO 13616) e publicado a `finance`; requisição com linhas e decisão de `approval`; Ordem de Compra, que só nasce de requisição aprovada e não deixa encomendar acima do aprovado; Recepção parcial, acumulada por linha e nunca acima do encomendado. ⚠ **3-way match por fazer** — dois dos três lados existem, falta a factura de compra, que é de `finance` |
| `payroll`, `projects`, `inventory`, `fleet` | Sem código. Definidos em [modules/](../modules/) |

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

A documentação da API (`/swagger`, `/openapi/v1.json`) é publicada por
interruptor próprio, `EXPOSE_OPENAPI` (ADR-038). Foi aberta a 2026-08-26 para
o frontend poder ler o contrato do ambiente — e, na primeira tentativa, foi
aberta pondo `ASPNETCORE_ENVIRONMENT=Development` no compose, o que **reabriu
o K8 em silêncio** e pôs a página de excepções de desenvolvimento à frente do
pipeline. Corrigido a 2026-08-27. O risco que fica é o **K17**: sem TLS, a
superfície inteira é legível por quem estiver a ouvir.

## Números

| Área | Estado |
|---|---|
| Código | 10 módulos, 50 projectos em `src/`, 237 ficheiros `.cs` |
| Superfície HTTP | 150 endpoints em 10 grupos de rota, mais `/health` |
| ADRs | 38, aceites |
| Testes | **694** em 15 projectos — 532 de domínio, 128 de Application, 21 de arquitectura, 9 da API do host, 4 de integração. **690 passam**; os 4 de integração exigem Docker, e o motor caiu a 2026-08-27 depois de a suite ter passado inteira |
| Verificação end-to-end | **12 suites** PowerShell, **261 casos**. ⚠ Última corrida completa: **246/246 a 2026-08-27**. Os 15 casos novos e as alterações a cinco suites estão por correr — o Docker caiu |
| Persistência | SQL Server externo, um schema por domínio, migrações EF Core por módulo |
| CI | GitHub Actions, 2 jobs (ADR-023), em `y-jr/rivo-api` |
| Protecção de `main` | Ruleset `build_and_domain_test`: PR obrigatório, os dois jobs verdes |

## O que não existe

- **Cobertura de Application em sete dos nove módulos.** `finance` (100) e
  `identity` (8) têm-na; os outros não. 429 testes de domínio contra 108 de
  Application e 4 de Infrastructure.
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
  trabalho de outra sessão. O contrato HTTP que esse trabalho consome está
  escrito em [API-FRONTEND.md](../../API-FRONTEND.md), na raiz do repositório
  — 119 rotas com permissão, corpo e código de sucesso, verificadas contra o
  código a 2026-08-27.
- **`SharedKernel`.** O [CLAUDE.md](../CLAUDE.md) refere-o e manda mantê-lo
  mínimo; nunca chegou a ser criado. O ADR-035 considerou criá-lo e decidiu
  contra — ver a alternativa B desse ADR.
- **Utilizador aplicacional restrito na base de dados.** A aplicação liga-se
  como `sa`.
- Regras fiscais angolanas de cálculo — IRT, INSS, códigos de isenção. O
  **modelo de dados** está fixado pelo XSD do SAF-T; as **regras** não, e
  `CLAUDE.md` proíbe implementá-las a partir do levantamento provisório.

## Riscos principais

1. **Cobertura desigual entre camadas.** Deixou de crescer em `finance`, que
   era onde mais custava — `ExecutePayment`, `RegisterReceipt`,
   `IssueCreditNote` e `CreatePaymentRequest` têm agora teste unitário, e a
   ordem das verificações de BR-5 está fixada por um teste que falha se
   alguém a inverter. **Os outros sete módulos continuam sem.** O CI apanha
   regressões de domínio e violações de fronteira; um caso de uso errado que
   compile continua a passar em `hr`, `approval` e nos restantes.
2. **Nada revê o código além do próprio autor.** Com um colaborador, a revisão
   aprovadora teve de ficar a 0.
3. **Três módulos parecem mais completos do que são.** `fiscal`, `commercial`
   e `finance` respondem a HTTP e têm testes, o que é fácil de confundir com
   estarem feitos. Uma factura do Rivo tem número, série e ar de factura, e não
   é documento fiscal. Mitigação: ⚠ em cada `modules/*.md`, no ADR-036 e aqui.
4. **K16 — sem TLS.** Credenciais e token em claro no ambiente publicado. Com
   a documentação da API agora aberta (K17), a superfície inteira viaja no
   mesmo canal.
5. **K18 — cancelar um pedido de aprovação exige só permissão de leitura.**
   Quem acompanha processos pode matá-los. Não é escalada de privilégio; é
   segregação de funções por definir para um acto que já existe.
6. **`hr.Colaborador` como ponto de acoplamento** — mitigado por ADR-010 e
   respeitado no código, mas exige vigilância à medida que os consumidores
   aparecem.

**Riscos fechados:** as decisões de stack sem ADR (2026-08-15, ADR-018 a 021);
o K14 (2026-08-16, ADR-025); a ausência de testes de arquitectura (2026-08-16,
ADR-024); o K15 (2026-08-24, ADR-035).

## Próximos passos

Não é uma sequência ratificada — é o que está por decidir e por fazer.

1. **Terminar a verificação da reposição de password.** Cinco dos seis
   endpoints novos de `identity` foram exercitados contra a stack; o sexto,
   `POST /users/{id}/password-reset`, rebentou com 500 por faltarem os token
   providers do ASP.NET Core Identity. A correcção — `AddDefaultTokenProviders()`
   — está aplicada e compila, **e não foi exercitada**: o motor do Docker caiu
   durante a reconstrução. É o primeiro `curl` a fazer quando ele voltar.
2. **Correr `verify-all` e fechar seis dívidas de uma vez.** Estão por
   exercitar: a correcção do `password-reset`, a desactivação de políticas de
   `approval`, a listagem de documentos, o `read-all` de notificações, o
   histórico de pedidos de aprovação, e o levantamento/fecho de conta bancária.
   Nenhuma foi verificada contra a stack — **o Docker está em baixo desde
   2026-08-27**, e nem `Start-Service` nem relançar o processo directamente
   destravaram o motor. São 261 casos.
3. **Carregar um plano de contas real e definir as regras de postagem.** É o que
   falta para a contabilidade deixar de estar vazia — e **precisa do
   contabilista, não de código**. Enquanto não houver, todo o resto da
   Contabilidade está de pé e sem uso.
4. **Fechar o 3-way match.** A cadeia `requisição → OC → recepção → factura`
   está completa do lado de `procurement`, e a vista da ordem já dá dois dos
   três lados — encomendado e recebido, linha a linha. Falta o terceiro: a
   factura de compra, que é de `finance`. **É aí que os dois módulos se
   encontram**, e é a fronteira que `docs` aponta como a melhor do protótipo
   inteiro. Traz uma direcção nova, `finance → procurement`, que é decisão
   arquitectural e merece ADR.
5. **Ligar a factura de compra ao Fornecedor.** `finance` guarda hoje nome e
   NIF em texto. **Não é retroactivo:** as facturas emitidas guardam o que
   vigorava à data.
6. **Decidir quem cancela um pedido de aprovação (K18).** Hoje basta
   `approval.requests.read`, o que faz de uma permissão de leitura um poder de
   veto. A correcção é de uma linha; **a decisão não é** — é a mesma pergunta
   de segregação que BR-2 e BR-3 já responderam para decidir e para pagar.
7. **Estorno automático.** Anular uma factura, uma nota de crédito ou um recibo
   **não gera lançamento inverso** — o original fica e corrige-se à mão. É a
   lacuna mais visível da postagem.
8. **Domínio e TLS** — fecha o K16 **e o K17** (com a documentação da API
   aberta, a superfície viaja em claro), e é pré-requisito de qualquer uso
   real.
9. **Cobertura de Application nos outros módulos** — `finance` tem 100 testes. O
   próximo que mais custa é `DecideOnRequest` em `approval`: BR-2, BR-4 e BR-6
   vivem lá e só têm cobertura caixa-preta.
10. **O NIF oficial de consumidor final** — enquanto for `CONSUMIDORFINAL`, as
   vendas a balcão saem com um marcador visível. Precisa de fonte primária.

**Fechado a 2026-08-27:** o Swagger no ambiente publicado passou a ter
interruptor próprio (ADR-038), o que refechou o **K8** — aberto sem se dar por
isso quando o ambiente foi renomeado para `Development` — e deixou registados
o **K17** e o **K18**.

**Fechado a 2026-08-25:** Contabilidade & Fecho, Planeamento, **BR-8** (uma
política com `RequiresBudgetCheck` deixou de recusar sempre e passou a
verificar) e a **postagem automática** dos cinco documentos.

Ver também [implemented.md](implemented.md),
[in-progress.md](in-progress.md), [known-issues.md](known-issues.md),
[pending-decisions.md](pending-decisions.md),
[roadmap-execucao.md](roadmap-execucao.md).
