namespace Rivo.Hr.Domain;

/// <summary>
/// Ligação entre um Colaborador e um documento.
///
/// <para>
/// <strong>A ligação vive aqui, em `hr`, e não em `documents`</strong>
/// (ADR-009). O desenho inicial usava chave polimórfica
/// (<c>entidade_tipo</c> + <c>entidade_id</c>) em `documents`, o que perdia
/// integridade referencial num domínio com retenção legal.
/// </para>
///
/// <para>
/// Com esta tabela há chaves estrangeiras reais nos dois sentidos, e a
/// <strong>classificação e o prazo de retenção ficam em `hr`</strong> — o
/// único contexto que sabe que um contrato de trabalho se guarda X anos e uma
/// declaração Y. `documents` não pode saber isso genericamente.
/// </para>
/// </summary>
public sealed class EmployeeDocument
{
    private EmployeeDocument() => Category = string.Empty;

    private EmployeeDocument(
        Guid id,
        Guid employeeId,
        Guid documentId,
        string category,
        DateTimeOffset attachedAt)
    {
        Id = id;
        EmployeeId = employeeId;
        DocumentId = documentId;
        Category = category;
        AttachedAt = attachedAt;
    }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>
    /// Referência a `documents.document(id)`. FK entre schemas para a chave
    /// primária do contexto dono, que é o único caso permitido (ADR-010).
    /// </summary>
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Classificação no contexto de RH: "contrato", "declaracao", "cv".
    /// Distinta da categoria genérica que `documents` guarda — esta tem
    /// significado de negócio, aquela não.
    /// </summary>
    public string Category { get; private set; }

    public DateTimeOffset AttachedAt { get; private set; }

    public static EmployeeDocument Attach(
        Guid employeeId,
        Guid documentId,
        string category,
        DateTimeOffset attachedAt)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de pertencer a um colaborador.", nameof(employeeId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de referir um documento.", nameof(documentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return new EmployeeDocument(Guid.CreateVersion7(), employeeId, documentId, category.Trim(), attachedAt);
    }
}
