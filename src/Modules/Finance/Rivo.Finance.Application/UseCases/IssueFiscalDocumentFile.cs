using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Dá o ficheiro de um documento fiscal — compondo-o na primeira vez e
/// devolvendo o mesmo daí em diante.
///
/// <para>
/// Fecha o K23: até aqui, factura, nota de crédito e recibo existiam como dados
/// e não havia nada que se imprimisse ou entregasse.
/// </para>
///
/// <para>
/// <strong>Compor uma vez não é optimização, é requisito.</strong> Duas
/// impressões da mesma factura têm de sair iguais — é isso que permite a um
/// cliente e à empresa compararem o que cada um tem na mão. Ver
/// <see cref="FiscalDocumentFile"/> para a mecânica, incluindo o caso da
/// anulação.
/// </para>
/// </summary>
public sealed class IssueFiscalDocumentFile(
    ISalesInvoiceStore documents,
    IFiscalDocumentFileStore files,
    ITaxEntityDirectory taxEntity,
    IFiscalDocumentRenderer renderer,
    IFiscalDocumentArchive archive,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<FiscalDocumentFileResult> ExecuteAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var conteudo = await ProjectarAsync(kind, documentId, cancellationToken);

        if (conteudo is null)
        {
            return FiscalDocumentFileResult.NotFound();
        }

        // O emitente vem de `fiscal` pelo contrato. Sem ele não há documento
        // fiscal — e recusar é mais honesto do que imprimir um papel sem
        // cabeçalho que alguém possa confundir com uma factura.
        var emitente = await taxEntity.FindAsync(cancellationToken);

        if (emitente is null)
        {
            return FiscalDocumentFileResult.IssuerNotDeclared();
        }

        var existente = await files.FindAsync(kind, documentId, conteudo.Cancelled, cancellationToken);

        if (existente is not null)
        {
            var guardado = await archive.FetchAsync(existente.StoredDocumentId, cancellationToken);

            if (guardado is not null)
            {
                return FiscalDocumentFileResult.Ready(
                    guardado, NomeDoFicheiro(conteudo), existente.StoredDocumentId, existente.ContentHash);
            }

            // Ficheiro órfão (K12): a linha existe, o conteúdo não. Compõe-se de
            // novo em vez de responder erro — e a linha nova fica ao lado da
            // antiga, porque nada se apaga aqui.
        }

        var bytes = renderer.Render(new FiscalDocumentPrintout(emitente, conteudo));
        var nome = NomeDoFicheiro(conteudo);

        var arquivado = await archive.StoreAsync(nome, bytes, context, cancellationToken);

        var registo = FiscalDocumentFile.Record(
            kind, documentId, arquivado.DocumentId, arquivado.ContentHash,
            conteudo.Cancelled, clock.GetUtcNow());

        await files.AddAsync(registo, cancellationToken);
        await files.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.FiscalDocumentFileGenerated,
                EntityTypeDe(kind),
                documentId.ToString(),
                context,
                NewValue: $$"""{"documentId":"{{arquivado.DocumentId}}","hash":"{{arquivado.ContentHash}}","cancelled":{{(conteudo.Cancelled ? "true" : "false")}}}"""),
            cancellationToken);

        return FiscalDocumentFileResult.Ready(bytes, nome, arquivado.DocumentId, arquivado.ContentHash);
    }

    /// <summary>
    /// O nome com que o ficheiro sai. O número do documento com as barras
    /// trocadas por hífens — <c>FT 2026/1</c> não serve como nome de ficheiro em
    /// nenhum sistema operativo.
    /// </summary>
    private static string NomeDoFicheiro(FiscalDocumentContent conteudo) =>
        $"{conteudo.Number.Replace('/', '-').Replace(' ', '-')}.pdf";

    private async Task<FiscalDocumentContent?> ProjectarAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        CancellationToken cancellationToken) => kind switch
        {
            FiscalDocumentKind.SalesInvoice =>
                Projectar(await documents.FindAsync(documentId, cancellationToken)),

            FiscalDocumentKind.CreditNote =>
                Projectar(await documents.FindCreditNoteAsync(documentId, cancellationToken)),

            FiscalDocumentKind.Receipt =>
                Projectar(await documents.FindReceiptAsync(documentId, cancellationToken)),

            _ => null,
        };

    private static FiscalDocumentContent? Projectar(SalesInvoice? factura) => factura is null ? null : new(
        "Factura",
        factura.Number.ToString(),
        factura.IssuedOn,
        factura.TaxPointDate,
        factura.Currency,
        Parte(factura.Customer),
        [.. factura.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new FiscalDocumentLine(
                l.LineNumber, l.Description, l.Quantity, l.UnitPrice,
                l.TaxCode, l.TaxPercentage, l.NetAmount, l.TaxAmount))],
        factura.NetTotal,
        factura.TaxTotal,
        factura.GrossTotal,
        factura.FiscalNotice,
        factura.Status == InvoiceStatus.Cancelled,
        factura.CancellationReason);

    private static FiscalDocumentContent? Projectar(CreditNote? nota) => nota is null ? null : new(
        "Nota de crédito",
        nota.Number.ToString(),
        nota.IssuedOn,
        nota.TaxPointDate,
        nota.Currency,
        Parte(nota.Customer),
        [.. nota.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new FiscalDocumentLine(
                l.LineNumber, l.Description, l.Quantity, l.UnitPrice,
                l.TaxCode, l.TaxPercentage, l.NetAmount, l.TaxAmount))],
        nota.NetTotal,
        nota.TaxTotal,
        nota.GrossTotal,
        nota.FiscalNotice,
        nota.Status == InvoiceStatus.Cancelled,
        nota.CancellationReason,
        Reference: nota.CorrectedInvoiceNumber,
        Note: nota.Reason);

    /// <summary>
    /// O recibo não tem imposto nem preço unitário — tem liquidações.
    ///
    /// <para>
    /// Cada linha é «esta quantia foi paga contra aquela factura». Encaixa na
    /// projecção comum com quantidade 1 e sem taxa, e o compositor esconde as
    /// colunas de imposto quando não há nenhuma — ver o compositor. A alternativa
    /// era uma segunda projecção e uma segunda composição a divergir com o tempo.
    /// </para>
    /// </summary>
    private static FiscalDocumentContent? Projectar(Receipt? recibo) => recibo is null ? null : new(
        "Recibo",
        recibo.Number.ToString(),
        recibo.ReceivedOn,
        null,
        recibo.Currency,
        Parte(recibo.Customer),
        [.. recibo.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new FiscalDocumentLine(
                l.LineNumber,
                $"Liquidação da factura {l.InvoiceNumber}",
                1m, l.Amount, string.Empty, 0m, l.Amount, 0m))],
        recibo.Total,
        0m,
        recibo.Total,
        recibo.FiscalNotice,
        recibo.Status == InvoiceStatus.Cancelled,
        recibo.CancellationReason,
        Note: recibo.Notes,
        PaymentMethod: recibo.Method.ToString());

    private static FiscalDocumentParty Parte(InvoicedParty parte) => new(
        parte.Name,
        parte.TaxId,
        // O consumidor final não tem endereço no documento, por desenho de
        // `InvoicedParty` — imprimir campos vazios seria pior do que omiti-los.
        parte.IsFinalConsumer ? null : parte.AddressDetail,
        parte.IsFinalConsumer ? null : parte.City,
        parte.IsFinalConsumer ? null : parte.Country,
        parte.IsFinalConsumer);

    internal static string EntityTypeDe(FiscalDocumentKind kind) => kind switch
    {
        FiscalDocumentKind.SalesInvoice => FinanceAuditEntityTypes.SalesInvoice,
        FiscalDocumentKind.CreditNote => FinanceAuditEntityTypes.CreditNote,
        FiscalDocumentKind.Receipt => FinanceAuditEntityTypes.Receipt,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de documento sem tipo de entidade."),
    };
}

public sealed record FiscalDocumentFileResult(
    FiscalDocumentFileOutcome Outcome,
    byte[]? Content,
    string? FileName,
    Guid? StoredDocumentId,
    string? ContentHash)
{
    public static FiscalDocumentFileResult Ready(byte[] content, string fileName, Guid documentId, string hash) =>
        new(FiscalDocumentFileOutcome.Ready, content, fileName, documentId, hash);

    public static FiscalDocumentFileResult NotFound() =>
        new(FiscalDocumentFileOutcome.DocumentNotFound, null, null, null, null);

    public static FiscalDocumentFileResult IssuerNotDeclared() =>
        new(FiscalDocumentFileOutcome.IssuerNotDeclared, null, null, null, null);
}

public enum FiscalDocumentFileOutcome
{
    Ready,
    DocumentNotFound,

    /// <summary>
    /// A empresa ainda não declarou a sua identidade fiscal. É configuração em
    /// falta, não erro de quem pediu.
    /// </summary>
    IssuerNotDeclared,
}
