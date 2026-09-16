using System.Linq;
using Rivo.Approval.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Finance.Contracts;
using Rivo.Fiscal.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Procurement.Contracts;
using Rivo.Payroll.Contracts;
using Rivo.Projects.Contracts;
using Rivo.Inventory.Contracts;
using Rivo.Fleet.Contracts;
using Rivo.Identity.Contracts;
using Rivo.Dashboard.Contracts;
using Rivo.Analytics.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.Identity.Application.Authorization;

/// <summary>
/// Os sete Perfis de Acesso previstos no documento de produto, mais
/// `Cliente` (ADR-043), `Colaborador` (ADR-061) e `SuperAdmin` (ADR-058).
///
/// Perfil de Acesso responde a "o que este utilizador pode ver/fazer no
/// sistema". <strong>Não confundir com Cargo</strong>, que é posição
/// organizacional ("Director Financeiro", "Chefe de Departamento") e pertence
/// ao módulo `hr` (ADR-005).
///
/// O protótipo tinha apenas quatro papéis em código contra sete no documento
/// de produto. Herdamos os sete, que são a definição do negócio.
///
/// <para>
/// <strong>`Cliente` é o oitavo, e não está no documento de produto.</strong>
/// `docs/rivo-suite-descricao-modulos.md` §Perfis de Acesso fixa "7 perfis
/// predefinidos" — mas também descreve o Portal do Cliente (módulo 12), para
/// uma audiência externa que nenhum dos sete cobre. Decisão explícita do
/// utilizador (ADR-043, 2026-09-03): conta própria em `identity`, perfil
/// novo. Estende o documento-fonte; não o reescreve.
/// </para>
///
/// <para>
/// <strong>`SuperAdmin` é o nono, e não é um Perfil de negócio.</strong> É
/// uma conta de operação/arranque (ADR-058): resolve o bloqueio circular de
/// BR-20 — nenhum Cargo com autoridade de aprovação pode ser atribuído sem
/// que já exista alguém a ocupar um, e num ambiente novo ninguém ocupa
/// nenhum. Só semeada pelo bootstrap (nunca atribuível em runtime, ver
/// <see cref="AssignableProfiles"/>) e nunca dada a ninguém da empresa.
/// </para>
/// </summary>
public static class AccessProfiles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Finance = "Finance";
    public const string HumanResources = "HR";
    public const string Sales = "Sales";
    public const string AssetManager = "AssetManager";
    public const string ProjectManager = "ProjectManager";

    /// <summary>
    /// Audiência externa do Portal do Cliente (ADR-043). Vazio até o Portal
    /// existir — mesmo estado em que `AssetManager`/`ProjectManager`
    /// esperaram pelos seus módulos.
    /// </summary>
    public const string Customer = "Cliente";

    /// <summary>
    /// Quem trabalha na empresa e não administra nada (ADR-061).
    ///
    /// <para>
    /// <strong>É o perfil de toda a gente.</strong> Um programador, um
    /// estagiário, um técnico — pessoas que precisam de ver a sua assiduidade,
    /// os seus recibos e os seus documentos, e mais nada. Até 2026-09-16 não
    /// existia: convidar um funcionário obrigava a dar-lhe um perfil de função
    /// que não lhe pertence, ou <see cref="Customer"/>, que é a audiência
    /// <em>externa</em> do Portal do Cliente.
    /// </para>
    ///
    /// <para>
    /// <strong>E é deliberadamente vazio.</strong> O Portal do Colaborador não
    /// autoriza por permissão — autoriza por <em>vínculo</em>: quem vê é quem
    /// está ligado àquele colaborador, e a ninguém mais (ADR-042). Dar-lhe
    /// permissões de módulo seria dar acesso a dados de terceiros para
    /// resolver um problema que o vínculo já resolve.
    /// </para>
    /// </summary>
    public const string Employee = "Colaborador";

    /// <summary>
    /// Conta de bootstrap/operação, nunca de negócio (ADR-058). Ver a nota de
    /// classe.
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Tudo o que `Admin` recebe — ver a entrada de <see cref="Admin"/> em <see cref="Catalogue"/>.</summary>
    private static readonly IReadOnlyList<string> AdminPermissions =
    [
        .. IdentityPermissions.All,
        .. AuditPermissions.All,
        .. HrPermissions.All,
        .. DocumentPermissions.All,
        .. ApprovalPermissions.All,
        .. FiscalPermissions.All,
        .. FinancePermissions.All,
        .. CommercialPermissions.All,
        .. ProcurementPermissions.All,
        .. PayrollPermissions.All,
        .. ProjectsPermissions.All,
        .. InventoryPermissions.All,
        .. FleetPermissions.All,
        .. DashboardPermissions.All,
        .. MessagingPermissions.All,
        .. AnalyticsPermissions.All,
    ];

    /// <summary>
    /// Perfil e permissões que lhe são atribuídas no seed.
    ///
    /// Cada módulo declara <em>que permissões existem</em>; `identity` decide
    /// <em>que perfis as recebem</em>, porque é dono do Perfil de Acesso
    /// (ADR-005).
    ///
    /// Os perfis ainda vazios esperam pelos módulos de negócio que os
    /// justificam. Inventar-lhes permissões agora seria adivinhar.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Catalogue =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Admin] = AdminPermissions,

            // `Manager` decide sobre pedidos de aprovação — incluindo pedidos de
            // pagamento — e regista facturas de compra e pede que sejam pagas.
            //
            // **Sem gerir políticas** (quem configura as alçadas decidiria
            // indirectamente o que pode aprovar sozinho) e **sem executar
            // pagamentos**: quem aprova não paga, e BR-3 começa aqui, no
            // catálogo, antes de o domínio a impor.
            //
            // **Vê o Dashboard Executivo e o Analytics & IA** — é o próprio
            // perfil que `docs/rivo-suite-descricao-modulos.md` nomeia para
            // isso ("Manager | Dashboard, Frota, Projectos, Analytics,
            // Aprovações"). Permissões à parte (`dashboard.overview.read`,
            // Fase 8/ADR-041; `analytics.overview.read`, ADR-047): `Manager`
            // não tem `finance.invoices.read` (só `Finance` tem), e exigi-lo
            // excluiria a audiência que o documento de produto nomeia — ver
            // `Rivo.Dashboard.Contracts`/`Rivo.Analytics.Contracts`.
            [Manager] = [
                ApprovalPermissions.RequestsRead,
                ApprovalPermissions.RequestsDecide,
                DashboardPermissions.OverviewRead,
                AnalyticsPermissions.OverviewRead,
                .. FinancePermissions.ForPayables,

                // Elabora o orçamento do seu centro de custo. **Não o aprova**
                // — quem sobe o tecto não pode ser quem precisa que ele suba.
                .. FinancePermissions.ForBudgetOwners,

                // Requisita compras para o seu departamento. **Sem qualificar
                // fornecedores** — quem pede a compra não escolhe para que
                // conta se paga, e é a mesma separação que tira a `Manager` a
                // execução do pagamento.
                .. ProcurementPermissions.ForRequesters],

            // `Finance` é a tesouraria e a supervisão: regista o dinheiro que
            // entra, credita, anula e estorna. **Sem emitir facturas** — quem
            // desfaz não faz, e é a segregação de BR-3 aplicada ao documento em
            // vez de ao pagamento.
            [Finance] = [
                ApprovalPermissions.RequestsRead,
                ApprovalPermissions.RequestsDecide,
                .. FinancePermissions.ForTreasury,
                FinancePermissions.InvoicesCancel,

                // Contabilidade é o outro lado da tesouraria: quem regista o
                // dinheiro também o lança nos livros. **Sem `LedgerClose`** —
                // fechar e reabrir períodos é de `Admin`, pela mesma razão que
                // abrir séries de documento é.
                .. FinancePermissions.ForAccounting,

                // Aprova orçamentos, e não os escreve. É a outra metade da
                // segregação que dá sentido a BR-8.
                .. FinancePermissions.ForBudgetApprovers,

                // Vê fornecedores — precisa deles para registar a factura de
                // compra. **Não os qualifica**, e a ausência é deliberada:
                // quem fixa o IBAN decide para onde o dinheiro sai, e quem
                // executa o pagamento não pode ser a mesma pessoa. É BR-3
                // aplicada um passo antes do pagamento.
                ProcurementPermissions.SuppliersRead,

                // E vê as ordens de compra, que é o que a factura do fornecedor
                // vai ter de casar. **Não as emite:** encomendar e pagar são as
                // duas pontas do mesmo processo.
                ProcurementPermissions.OrdersRead,

                // E as recepções, que são o lado do meio do 3-way match: sem
                // elas, casar a factura do fornecedor seria comparar o que se
                // pediu com o que se cobrou, e nunca com o que chegou.
                ProcurementPermissions.ReceiptsRead],

            // Note-se a ausência de `hr.positions.write`: RH atribui Cargos,
            // mas não decide quais existem nem quais conferem autoridade de
            // aprovação. Essa separação é o que fecha a escalada de
            // privilégios de ADR-015.
            //
            // `payroll` (esqueleto, `modules/payroll.md`) fica com RH: é onde
            // a folha nasce hoje, por não haver ainda um perfil próprio de
            // compensação.
            [HumanResources] = [
                .. HrPermissions.ForHumanResources,
                .. DocumentPermissions.All,
                .. PayrollPermissions.All],

            // `Sales` deixa de estar vazio: clientes e emissão de facturas
            // (ADR-036).
            //
            // Duas ausências deliberadas. **Sem `fiscal`** — quem vende não fixa
            // a taxa que a sua própria venda vai liquidar. E **sem anular nem
            // abrir séries** — desfazer não é a mesma autorização que fazer, e
            // uma série paralela é a forma óbvia de emitir fora da sequência
            // auditável.
            [Sales] = [
                CommercialPermissions.CustomersRead,
                CommercialPermissions.CustomersWrite,
                .. FinancePermissions.ForBilling,
                // Mensagens à equipa comercial (ADR-045) — Sales é a
                // audiência que o próprio nome do módulo nomeia.
                .. MessagingPermissions.All],

            // `AssetManager` deixa de estar vazio, e a razão não é adivinhação:
            // **a recepção de mercadoria é a porta de entrada do stock**, e
            // `modules/procurement.md` diz que `procurement` publica o facto da
            // recepção para `inventory` o consumir. Quem gere activos e
            // existências é quem conta o que chega.
            //
            // **Recebe e não encomenda**, e é a segregação que dá valor ao
            // 3-way match: se quem encomenda fosse quem regista a chegada, uma
            // entrega a menos podia ser dada como completa sem mais ninguém ver.
            //
            // `inventory` e `fleet` (esqueletos, sem regra de negócio ainda)
            // ficam com `AssetManager` pela mesma razão de fundo: são as
            // duas outras existências que a organização gere — stock e
            // viaturas.
            [AssetManager] = [
                ProcurementPermissions.SuppliersRead,
                ProcurementPermissions.OrdersRead,
                ProcurementPermissions.ReceiptsRead,
                ProcurementPermissions.ReceiptsWrite,
                .. InventoryPermissions.All,
                .. FleetPermissions.All],

            // `ProjectManager` deixa de estar vazio: `projects` (esqueleto,
            // `modules/projects.md`) é literalmente o módulo que este perfil
            // nomeia.
            [ProjectManager] = [.. ProjectsPermissions.All],

            // Deixa de estar vazio com o comprovativo de pagamento
            // (ADR-044): o cliente carrega o ficheiro directamente em
            // `POST /documents`, mesma permissão que qualquer módulo já usa
            // para anexar ficheiros a um registo seu — não é pensada para
            // clientes especificamente.
            [Customer] = [DocumentPermissions.Write],

            // Vazio, e é o ponto — ver a nota em `Employee`. O Portal do
            // Colaborador autoriza por vínculo e não por permissão, por isso
            // este perfil não precisa de nenhuma para fazer o que existe para
            // fazer. Qualquer permissão aqui daria acesso a dados de terceiros.
            //
            // Não confundir com os perfis que estiveram vazios à espera do seu
            // módulo: este está vazio por desenho, e assim deve ficar.
            [Employee] = [],

            // Tudo o que `Admin` tem, mais a única permissão que nem `Admin`
            // tem: saltar a submissão a `approval` ao atribuir um Cargo com
            // autoridade (BR-20). É o que dá a esta conta o poder de resolver
            // o arranque circular sem ninguém ter de mexer na base de dados
            // (ADR-058).
            [SuperAdmin] = [.. AdminPermissions, HrPermissions.PositionsAssignDirect],
        };

    /// <summary>
    /// Os perfis que se podem atribuir através de <c>POST
    /// /identity/users/{id}/roles</c> — todo o catálogo menos
    /// <see cref="SuperAdmin"/>.
    ///
    /// <para>
    /// <strong>Existe para <see cref="SuperAdmin"/> nunca aparecer</strong> na
    /// lista de perfis do ecrã de administração nem ser atribuível por lá,
    /// mesmo por quem já é `Admin` — do contrário um `Admin` da empresa
    /// conseguiria conceder-se a si próprio, ou a outra conta, a permissão que
    /// contorna BR-20. A única via para esta conta existir é o bootstrap
    /// (ADR-058).
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> AssignableProfiles =
        [.. Catalogue.Keys.Where(profile => profile != SuperAdmin)];
}

