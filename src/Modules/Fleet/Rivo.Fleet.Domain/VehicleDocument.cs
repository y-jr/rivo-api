namespace Rivo.Fleet.Domain;

/// <summary>
/// Ligação entre uma Viatura e um documento — seguros e documentação legal
/// (`modules/fleet.md` §Possui, §Conceitos).
///
/// <para>
/// <strong>Mesmo desenho de <c>EmployeeDocument</c> em `hr`</strong>
/// (ADR-009): a ligação vive aqui, não em `documents` — chave polimórfica
/// perderia integridade referencial num contexto com prazo de retenção. A
/// classificação ("seguro", "licença", "inspecção") fica em `fleet`, que é
/// quem sabe o que cada categoria significa.
/// </para>
///
/// <para>
/// <strong>Não é filho do agregado <see cref="Vehicle"/></strong> — ao
/// contrário de Manutenção, Atribuição e Registo de Viagem, anexar um
/// documento não tem nenhuma invariante que dependa dos outros filhos da
/// viatura (não há "só um de cada vez", não há estado a verificar), por isso
/// não precisa de viver dentro do limite de consistência do agregado. Mesma
/// razão que mantém <c>EmployeeDocument</c> fora do agregado Colaborador.
/// </para>
/// </summary>
public sealed class VehicleDocument
{
    private VehicleDocument(Guid id, Guid vehicleId, Guid documentId, string category, DateTimeOffset attachedAt)
    {
        Id = id;
        VehicleId = vehicleId;
        DocumentId = documentId;
        Category = category;
        AttachedAt = attachedAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private VehicleDocument() => Category = string.Empty;

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    /// <summary>
    /// Referência a `documents.document(id)`. FK entre schemas para a chave
    /// primária do contexto dono, o único caso permitido (ADR-010).
    /// </summary>
    public Guid DocumentId { get; private set; }

    /// <summary>Classificação no contexto de frota: "seguro", "licença", "inspecção".</summary>
    public string Category { get; private set; }

    public DateTimeOffset AttachedAt { get; private set; }

    public static VehicleDocument Attach(Guid vehicleId, Guid documentId, string category, DateTimeOffset attachedAt)
    {
        if (vehicleId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de pertencer a uma viatura.", nameof(vehicleId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("A ligação tem de referir um documento.", nameof(documentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return new VehicleDocument(Guid.CreateVersion7(), vehicleId, documentId, category.Trim(), attachedAt);
    }
}
