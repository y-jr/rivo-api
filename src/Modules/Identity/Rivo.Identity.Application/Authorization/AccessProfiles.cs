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

namespace Rivo.Identity.Application.Authorization;

/// <summary>
/// Os sete Perfis de Acesso previstos no documento de produto.
///
/// Perfil de Acesso responde a "o que este utilizador pode ver/fazer no
/// sistema". <strong>Não confundir com Cargo</strong>, que é posição
/// organizacional ("Director Financeiro", "Chefe de Departamento") e pertence
/// ao módulo `hr` (ADR-005).
///
/// O protótipo tinha apenas quatro papéis em código contra sete no documento
/// de produto. Herdamos os sete, que são a definição do negócio.
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
            [Admin] = [
                .. Permissions.All,
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
                .. FleetPermissions.All],

            // `Manager` decide sobre pedidos de aprovação — incluindo pedidos de
            // pagamento — e regista facturas de compra e pede que sejam pagas.
            //
            // **Sem gerir políticas** (quem configura as alçadas decidiria
            // indirectamente o que pode aprovar sozinho) e **sem executar
            // pagamentos**: quem aprova não paga, e BR-3 começa aqui, no
            // catálogo, antes de o domínio a impor.
            [Manager] = [
                ApprovalPermissions.RequestsRead,
                ApprovalPermissions.RequestsDecide,
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
                .. FinancePermissions.ForBilling],

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
        };
}

