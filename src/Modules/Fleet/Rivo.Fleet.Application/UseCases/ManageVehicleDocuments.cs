using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>
/// Liga um documento já carregado a uma viatura — seguros e documentação
/// legal (`modules/fleet.md` §Possui).
///
/// <para>
/// Separação deliberada, mesma de `hr`: o <em>upload</em> exige
/// <c>documents.write</c>; <em>anexar</em> exige <c>fleet.vehicles.write</c>,
/// porque está a alterar-se o registo da viatura.
/// </para>
/// </summary>
public sealed class AttachDocumentToVehicle(
    IVehicleStore store,
    IDocumentCatalogue documents,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<AttachVehicleDocumentResult> ExecuteAsync(
        Guid vehicleId,
        Guid documentId,
        string category,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return AttachVehicleDocumentResult.VehicleNotFound();
        }

        // Verificado pelo contrato publicado, não por consulta às tabelas de
        // `documents`. A chave estrangeira também o garantiria, mas falharia
        // com erro de base de dados em vez de resposta útil.
        var descriptor = await documents.FindAsync(documentId, cancellationToken);

        if (descriptor is null)
        {
            return AttachVehicleDocumentResult.DocumentNotFound();
        }

        VehicleDocument link;

        try
        {
            link = VehicleDocument.Attach(vehicleId, documentId, category, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return AttachVehicleDocumentResult.Rejected(error.Message);
        }

        await store.AddVehicleDocumentAsync(link, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.DocumentAttached,
                FleetAuditEntityTypes.Vehicle,
                vehicleId.ToString(),
                context,
                NewValue: $$"""{"documentId":"{{documentId}}","category":"{{link.Category}}"}"""),
            cancellationToken);

        return AttachVehicleDocumentResult.Attached(link.Id);
    }
}

public sealed record AttachVehicleDocumentResult(AttachVehicleDocumentOutcome Outcome, Guid? LinkId, string? Error)
{
    public static AttachVehicleDocumentResult Attached(Guid id) =>
        new(AttachVehicleDocumentOutcome.Attached, id, null);

    public static AttachVehicleDocumentResult VehicleNotFound() =>
        new(AttachVehicleDocumentOutcome.VehicleNotFound, null, "Viatura não encontrada.");

    public static AttachVehicleDocumentResult DocumentNotFound() =>
        new(AttachVehicleDocumentOutcome.DocumentNotFound, null, "Documento não encontrado.");

    public static AttachVehicleDocumentResult Rejected(string error) =>
        new(AttachVehicleDocumentOutcome.Rejected, null, error);
}

public enum AttachVehicleDocumentOutcome
{
    Attached,
    VehicleNotFound,
    DocumentNotFound,

    /// <summary>Pedido malformado — sem categoria. 400.</summary>
    Rejected,
}

/// <summary>
/// Lista os documentos de uma viatura, com os metadados de `documents`.
///
/// A junção é feita em memória a partir de duas fontes, e não por SQL entre
/// schemas: `fleet` sabe que documentos estão ligados, `documents` sabe como
/// se chamam e quanto ocupam. Mesmo desenho de `ListEmployeeDocuments` em `hr`.
/// </summary>
public sealed class ListVehicleDocuments(IVehicleStore store, IDocumentCatalogue documents)
{
    public async Task<IReadOnlyList<VehicleDocumentView>?> ExecuteAsync(
        Guid vehicleId, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return null;
        }

        var links = await store.ListVehicleDocumentsAsync(vehicleId, cancellationToken);

        if (links.Count == 0)
        {
            return [];
        }

        var descriptors = await documents.FindManyAsync(
            [.. links.Select(link => link.DocumentId)], cancellationToken);

        var byId = descriptors.ToDictionary(descriptor => descriptor.DocumentId);

        return
        [
            .. links
                .Where(link => byId.ContainsKey(link.DocumentId))
                .Select(link => new VehicleDocumentView(
                    link.DocumentId,
                    link.Category,
                    byId[link.DocumentId].FileName,
                    byId[link.DocumentId].ContentType,
                    byId[link.DocumentId].SizeInBytes,
                    link.AttachedAt))
        ];
    }
}

public sealed record VehicleDocumentView(
    Guid DocumentId,
    string Category,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset AttachedAt);
