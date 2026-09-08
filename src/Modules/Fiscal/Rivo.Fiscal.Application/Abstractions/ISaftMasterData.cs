namespace Rivo.Fiscal.Application.Abstractions;

/// <summary>
/// Os dados de referência que a exportação SAF-T tem de reportar, no
/// vocabulário de `fiscal`.
///
/// <para>
/// <strong>Porque é uma porta e não uma referência a `commercial`.</strong>
/// `docs/rivo-arquitetura-global-v1.md` §1.5 e `modules/fiscal.md` fixam que
/// `fiscal` <em>lê</em> os módulos transaccionais para relatar — essa direcção
/// mantém-se e é esta interface. O que não se pode é materializá-la como
/// referência de projecto: `commercial` vai depender de `fiscal` para
/// determinar o imposto da venda (ADR-011), e `Modules_HaveNoDependencyCycles`
/// recusa o ciclo que daí resultaria — mesmo quando passa por contratos.
/// </para>
///
/// <para>
/// A resolução é a que o ADR-034 já fixou duas vezes, em
/// <c>IPaymentApproval</c> e <c>IProcurementApprovalSubmission</c>: o módulo
/// declara o que precisa nas suas próprias palavras e o composition root liga
/// ao contrato de quem possui os dados. Aqui tem uma segunda vantagem — a
/// exportação passa a ser testável sem `commercial` sequer existir.
/// </para>
/// </summary>
public interface ISaftMasterData
{
    /// <summary>
    /// A tabela de clientes do ficheiro. Vazia é resposta legítima: o XSD
    /// declara <c>Customer</c> com <c>minOccurs="0"</c>, e uma empresa sem
    /// clientes registados exporta um <c>MasterFiles</c> sem eles.
    /// </summary>
    Task<IReadOnlyList<SaftCustomer>> ListCustomersAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Um cliente, reduzido ao que o elemento <c>Customer</c> do SAF-T pede.
///
/// <para>
/// <strong>Não traz <c>AccountID</c> nem <c>SelfBillingIndicator</c>, e é
/// deliberado.</strong> Ambos são obrigatórios no XSD e nenhum é facto de
/// `commercial`: o primeiro é a conta corrente no plano de contas, que é de
/// `finance` e ainda não existe (ADR-037 — não se inventa o PGC angolano); o
/// segundo diz se há autofacturação, que o Rivo não faz. Quem os preenche é
/// <c>ExportSaftFile</c>, com valores que o esquema admite e que são verdade —
/// pedi-los a quem não os sabe seria convidar a inventá-los.
/// </para>
/// </summary>
/// <param name="CustomerId">
/// O identificador que aparece no ficheiro e a que as facturas se referem.
/// Texto e não <c>Guid</c>: o XSD limita-o a 30 caracteres, e um `Guid` com
/// hífenes tem 36 — a conversão é decisão de quem implementa a porta, não do
/// formato.
/// </param>
/// <param name="TaxId">NIF do cliente.</param>
/// <param name="Name">Razão social, tal como sai no documento fiscal.</param>
public sealed record SaftCustomer(
    string CustomerId,
    string TaxId,
    string Name,
    SaftAddress BillingAddress);

/// <param name="Country">ISO 3166-1 alpha-2.</param>
public sealed record SaftAddress(string Detail, string City, string Country);
