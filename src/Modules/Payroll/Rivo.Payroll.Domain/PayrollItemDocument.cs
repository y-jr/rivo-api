namespace Rivo.Payroll.Domain;

/// <summary>
/// Ligação entre um Item de Folha (o recibo de um colaborador, num período) e
/// um documento.
///
/// <para>
/// <strong>A ligação vive aqui, em `payroll`, e não em `documents`</strong>
/// (ADR-009) — mesmo desenho de <c>Rivo.Hr.Domain.EmployeeDocument</c>. A
/// classificação e o prazo de retenção legal do recibo (BR-15) ficam no
/// contexto que os conhece; `documents` só guarda o ficheiro.
/// </para>
///
/// <para>
/// <strong>Entidade independente, não filha de <see cref="PayrollItem"/>.</strong>
/// Anexar um documento não é uma decisão do agregado da folha — é um registo
/// à parte, feito depois de a folha já ter dito o que tem a dizer. Mesma
/// razão por trás de `EmployeeDocument` não viver dentro de `Employee`.
/// </para>
/// </summary>
public sealed class PayrollItemDocument
{
    private PayrollItemDocument() => Category = string.Empty;

    private PayrollItemDocument(
        Guid id,
        Guid payrollItemId,
        Guid documentId,
        string category,
        DateTimeOffset attachedAt)
    {
        Id = id;
        PayrollItemId = payrollItemId;
        DocumentId = documentId;
        Category = category;
        AttachedAt = attachedAt;
    }

    public Guid Id { get; private set; }

    public Guid PayrollItemId { get; private set; }

    /// <summary>
    /// Referência a `documents.document(id)`. FK entre schemas para a chave
    /// primária do contexto dono, o único caso permitido (ADR-010).
    /// </summary>
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Classificação no contexto de `payroll`: "recibo", "declaracao-irt".
    /// Distinta da categoria genérica que `documents` guarda — esta tem
    /// significado de negócio, aquela não.
    /// </summary>
    public string Category { get; private set; }

    public DateTimeOffset AttachedAt { get; private set; }

    public static PayrollItemDocument Attach(
        Guid payrollItemId,
        Guid documentId,
        string category,
        DateTimeOffset attachedAt)
    {
        if (payrollItemId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de pertencer a um item de folha.", nameof(payrollItemId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de referir um documento.", nameof(documentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return new PayrollItemDocument(Guid.CreateVersion7(), payrollItemId, documentId, category.Trim(), attachedAt);
    }
}
