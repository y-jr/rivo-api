using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Application.UseCases;

/// <summary>
/// Liga um documento já carregado (o Recibo, tipicamente) a um Item de
/// Folha. Mesmo desenho de `AttachDocumentToEmployee` (`hr`) — ver ali o
/// comentário completo sobre a separação upload/anexar.
///
/// <para>
/// <strong>Só depois de Aprovada.</strong> Diferente de `hr`, que não impõe
/// nenhum estado ao colaborador: aqui é <em>inferência</em>, não requisito
/// confirmado — um recibo é prova do que foi pago/autorizado, e os valores
/// de um item podem mudar enquanto a folha está em rascunho ou pendente.
/// Anexar antes disso arriscaria emitir um recibo que a decisão de
/// `approval` ainda pode invalidar. Revisível se o utilizador decidir o
/// contrário.
/// </para>
/// </summary>
public sealed class AttachDocumentToPayrollItem(
    IPayrollRunStore store,
    IDocumentCatalogue documents,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<AttachPayrollDocumentResult> ExecuteAsync(
        Guid runId,
        Guid itemId,
        Guid documentId,
        string category,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var folha = await store.FindAsync(runId, cancellationToken);

        if (folha is null)
        {
            return AttachPayrollDocumentResult.RunNotFound();
        }

        var item = folha.Items.FirstOrDefault(i => i.Id == itemId);

        if (item is null)
        {
            return AttachPayrollDocumentResult.ItemNotFound();
        }

        if (folha.Status is not PayrollRunStatus.Approved)
        {
            return AttachPayrollDocumentResult.Conflict(
                $"Só se anexa recibo a um item de uma folha Aprovada. Esta folha está em {folha.Status}.");
        }

        // Verificado pelo contrato publicado, não por consulta às tabelas de
        // `documents` — mesma razão de `AttachDocumentToEmployee`.
        var descriptor = await documents.FindAsync(documentId, cancellationToken);

        if (descriptor is null)
        {
            return AttachPayrollDocumentResult.DocumentNotFound();
        }

        PayrollItemDocument link;

        try
        {
            link = PayrollItemDocument.Attach(itemId, documentId, category, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return AttachPayrollDocumentResult.Rejected(error.Message);
        }

        await store.AddPayrollItemDocumentAsync(link, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                PayrollAuditActions.DocumentAttached,
                PayrollAuditEntityTypes.Item,
                itemId.ToString(),
                context,
                NewValue: $$"""{"documentId":"{{documentId}}","category":"{{link.Category}}"}"""),
            cancellationToken);

        return AttachPayrollDocumentResult.Attached(link.Id);
    }
}

public sealed record AttachPayrollDocumentResult(AttachPayrollDocumentOutcome Outcome, Guid? LinkId, string? Error)
{
    public static AttachPayrollDocumentResult Attached(Guid linkId) =>
        new(AttachPayrollDocumentOutcome.Attached, linkId, null);

    public static AttachPayrollDocumentResult RunNotFound() =>
        new(AttachPayrollDocumentOutcome.RunNotFound, null, "Folha não encontrada.");

    public static AttachPayrollDocumentResult ItemNotFound() =>
        new(AttachPayrollDocumentOutcome.ItemNotFound, null, "Item não encontrado.");

    public static AttachPayrollDocumentResult DocumentNotFound() =>
        new(AttachPayrollDocumentOutcome.DocumentNotFound, null, "Documento não encontrado.");

    /// <summary>Campo mal preenchido (categoria em branco) — 400.</summary>
    public static AttachPayrollDocumentResult Rejected(string error) =>
        new(AttachPayrollDocumentOutcome.Rejected, null, error);

    /// <summary>Conflito com o estado corrente da folha — 409.</summary>
    public static AttachPayrollDocumentResult Conflict(string error) =>
        new(AttachPayrollDocumentOutcome.Conflict, null, error);
}

public enum AttachPayrollDocumentOutcome
{
    Attached,
    RunNotFound,
    ItemNotFound,
    DocumentNotFound,
    Rejected,
    Conflict,
}

/// <summary>
/// Lista os documentos de um Item de Folha, com os metadados de `documents`.
/// Mesmo desenho de `ListEmployeeDocuments` (`hr`): a junção é feita em
/// memória, `payroll` sabe que documentos estão ligados, `documents` sabe
/// como se chamam e quanto ocupam.
/// </summary>
public sealed class ListPayrollItemDocuments(IPayrollRunStore store, IDocumentCatalogue documents)
{
    public async Task<IReadOnlyList<PayrollItemDocumentView>> ExecuteAsync(
        Guid itemId, CancellationToken cancellationToken)
    {
        var links = await store.ListPayrollItemDocumentsAsync(itemId, cancellationToken);

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
                .Select(link => new PayrollItemDocumentView(
                    link.DocumentId,
                    link.Category,
                    byId[link.DocumentId].FileName,
                    byId[link.DocumentId].ContentType,
                    byId[link.DocumentId].SizeInBytes,
                    link.AttachedAt))
        ];
    }
}

public sealed record PayrollItemDocumentView(
    Guid DocumentId,
    string Category,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset AttachedAt);
