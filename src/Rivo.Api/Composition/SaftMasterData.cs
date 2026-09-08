using Rivo.Commercial.Contracts;
using Rivo.Procurement.Contracts;
using Rivo.Inventory.Contracts;
using Rivo.Fiscal.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a porta de relato de `fiscal` aos contratos dos módulos que possuem os
/// dados — mesmo lugar e mesma razão de <see cref="FinancePaymentApproval"/> e
/// <see cref="ProcurementApprovalSubmission"/>.
///
/// <para>
/// É aqui, e não dentro de `fiscal`, porque `commercial` vai depender de
/// `fiscal` para determinar o imposto da venda. As duas direcções da "dupla
/// relação" que `modules/fiscal.md` descreve — determinação e relato — não
/// podem ser ambas referências de projecto, e o composition root é o único
/// sítio que vê os dois lados sem os acoplar.
/// </para>
///
/// <para>
/// Cresce com o ficheiro: fornecedores virão de <c>IProcurementDirectory</c>,
/// produtos de `inventory`, o plano de contas de `finance`. Esta classe é o
/// sítio onde essa lista vive, e é de propósito que seja um sítio só.
/// </para>
/// </summary>
public sealed class SaftMasterData(
    ICustomerDirectory customers,
    ISupplierDirectory suppliers,
    IInventoryCatalogue catalogue) : ISaftMasterData
{
    public async Task<IReadOnlyList<SaftProduct>> ListProductsAsync(
        CancellationToken cancellationToken)
    {
        var artigos = await catalogue.ListAllAsync(cancellationToken);

        // O SKU vai directo, sem a conversão que clientes e fornecedores
        // levam: já é texto escolhido por gente, com 50 caracteres no máximo,
        // e `ProductCode` admite 60.
        return [.. artigos.Select(a => new SaftProduct(a.Sku, a.Name))];
    }

    public async Task<IReadOnlyList<SaftSupplier>> ListSuppliersAsync(
        CancellationToken cancellationToken)
    {
        var fornecedores = await suppliers.ListAllAsync(cancellationToken);

        return [.. fornecedores.Select(f => new SaftSupplier(
            Identificador(f.SupplierId),
            f.TaxId,
            f.Name,
            new SaftAddress(
                f.BillingAddress.Detail,
                f.BillingAddress.City,
                f.BillingAddress.Country)))];
    }

    public async Task<IReadOnlyList<SaftCustomer>> ListCustomersAsync(
        CancellationToken cancellationToken)
    {
        var clientes = await customers.ListAllAsync(cancellationToken);

        return [.. clientes.Select(c => new SaftCustomer(
            Identificador(c.CustomerId),
            c.TaxId,
            c.Name,
            new SaftAddress(
                c.BillingAddress.Detail,
                c.BillingAddress.City,
                c.BillingAddress.Country)))];
    }

    /// <summary>
    /// O identificador do cliente no ficheiro.
    ///
    /// <para>
    /// <strong>Um `Guid` não cabe em <c>CustomerID</c>.</strong> O XSD
    /// limita-o a 30 caracteres; a forma canónica tem 36 e a forma "N" tem 32.
    /// Truncar seria arriscar colisões silenciosas entre dois clientes — e o
    /// XSD tem uma restrição de unicidade sobre este campo exactamente porque
    /// colisões aqui corrompem a referência das facturas.
    /// </para>
    ///
    /// <para>
    /// A conversão usada é Base64 sem preenchimento: 22 caracteres, sem perda.
    /// O tipo do XSD (<c>SAFAOtextTypeMandatoryMax30Car</c>) só restringe o
    /// comprimento — não há padrão a cumprir aqui, e `+` e `/` passariam. Vão
    /// na mesma para `-` e `_` porque este identificador acaba em URLs e em
    /// exportações CSV, onde uma barra é um problema de outra pessoa.
    /// </para>
    ///
    /// <para>
    /// Ser reversível é o que importa: no dia em que alguém tiver de cruzar o
    /// ficheiro entregue à AGT com a base de dados, isto volta a ser um
    /// <c>Guid</c>.
    /// </para>
    /// </summary>
    public static string Identificador(Guid id) =>
        Convert.ToBase64String(id.ToByteArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
