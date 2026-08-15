using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Liga um documento já carregado a um colaborador.
///
/// <para>
/// Separação deliberada: o <em>upload</em> exige <c>documents.write</c>;
/// <em>anexar</em> exige <c>hr.employees.write</c>, porque está a alterar-se
/// o registo do colaborador.
/// </para>
/// </summary>
public sealed class AttachDocumentToEmployee(
    IHrStore store,
    IDocumentCatalogue documents,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<AttachDocumentResult> ExecuteAsync(
        Guid employeeId,
        Guid documentId,
        string category,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return AttachDocumentResult.EmployeeNotFound();
        }

        // Verificado pelo contrato publicado, não por consulta às tabelas de
        // `documents`. A chave estrangeira também o garantiria, mas falharia
        // com erro de base de dados em vez de resposta útil.
        var descriptor = await documents.FindAsync(documentId, cancellationToken);

        if (descriptor is null)
        {
            return AttachDocumentResult.DocumentNotFound();
        }

        var link = EmployeeDocument.Attach(employeeId, documentId, category, clock.GetUtcNow());

        await store.AddEmployeeDocumentAsync(link, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.DocumentAttached,
                HrAuditEntityTypes.Employee,
                employeeId.ToString(),
                context,
                NewValue: $$"""{"documentId":"{{documentId}}","category":"{{link.Category}}"}"""),
            cancellationToken);

        return AttachDocumentResult.Attached(link.Id);
    }
}

public sealed record AttachDocumentResult(AttachDocumentOutcome Outcome, Guid? LinkId, string? Message)
{
    public static AttachDocumentResult Attached(Guid id) => new(AttachDocumentOutcome.Attached, id, null);

    public static AttachDocumentResult EmployeeNotFound() =>
        new(AttachDocumentOutcome.EmployeeNotFound, null, "Colaborador não encontrado.");

    public static AttachDocumentResult DocumentNotFound() =>
        new(AttachDocumentOutcome.DocumentNotFound, null, "Documento não encontrado.");
}

public enum AttachDocumentOutcome
{
    Attached,
    EmployeeNotFound,
    DocumentNotFound,
}

/// <summary>
/// Lista os documentos de um colaborador, com os metadados de `documents`.
///
/// A junção é feita em memória a partir de duas fontes, e não por SQL entre
/// schemas: `hr` sabe que documentos estão ligados, `documents` sabe como se
/// chamam e quanto ocupam.
/// </summary>
public sealed class ListEmployeeDocuments(IHrStore store, IDocumentCatalogue documents)
{
    public async Task<IReadOnlyList<EmployeeDocumentView>> ExecuteAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var links = await store.ListEmployeeDocumentsAsync(employeeId, cancellationToken);

        if (links.Count == 0)
        {
            return [];
        }

        // Consulta em lote, para não fazer uma chamada por documento.
        var descriptors = await documents.FindManyAsync(
            [.. links.Select(link => link.DocumentId)], cancellationToken);

        var byId = descriptors.ToDictionary(descriptor => descriptor.DocumentId);

        return
        [
            .. links
                .Where(link => byId.ContainsKey(link.DocumentId))
                .Select(link => new EmployeeDocumentView(
                    link.DocumentId,
                    link.Category,
                    byId[link.DocumentId].FileName,
                    byId[link.DocumentId].ContentType,
                    byId[link.DocumentId].SizeInBytes,
                    link.AttachedAt))
        ];
    }
}

public sealed record EmployeeDocumentView(
    Guid DocumentId,
    string Category,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset AttachedAt);
