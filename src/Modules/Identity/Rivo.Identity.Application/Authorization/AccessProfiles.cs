using Rivo.Approval.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Finance.Contracts;
using Rivo.Fiscal.Contracts;
using Rivo.Hr.Contracts;

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
                .. CommercialPermissions.All],

            // `Manager` deixa de estar vazio: decidir sobre pedidos de
            // aprovação é a primeira competência que um perfil de chefia tem no
            // sistema. **Sem gerir políticas** — quem configura as alçadas
            // decidiria indirectamente o que pode aprovar sozinho, que é a
            // mesma escalada que ADR-015 fecha em `hr`.
            [Manager] = [ApprovalPermissions.RequestsRead, ApprovalPermissions.RequestsDecide],

            // `Finance` acrescenta a supervisão da facturação: consulta tudo e
            // é quem anula. **Sem emitir** — quem anula não emite, e é a mesma
            // segregação de BR-3 aplicada ao documento em vez de ao pagamento.
            [Finance] = [
                ApprovalPermissions.RequestsRead,
                ApprovalPermissions.RequestsDecide,
                FinancePermissions.InvoicesRead,
                FinancePermissions.InvoicesCancel],

            // Note-se a ausência de `hr.positions.write`: RH atribui Cargos,
            // mas não decide quais existem nem quais conferem autoridade de
            // aprovação. Essa separação é o que fecha a escalada de
            // privilégios de ADR-015.
            [HumanResources] = [.. HrPermissions.ForHumanResources, .. DocumentPermissions.All],

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

            [AssetManager] = [],
            [ProjectManager] = [],
        };
}

