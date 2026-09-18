using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Envia um documento fiscal ao cliente, por correio electrónico.
///
/// <para>
/// Assenta em <see cref="IssueFiscalDocumentFile"/> e não compõe nada: o ficheiro
/// que vai anexado é <strong>o mesmo</strong> que o cliente descarregaria pelo
/// portal. Se fossem composições separadas, dois papéis do mesmo documento
/// podiam divergir — e é exactamente isso que um documento fiscal não pode
/// permitir.
/// </para>
///
/// <para>
/// <strong>O destinatário nunca vem do pedido.</strong> Lê-se do Cliente pelo
/// contrato de `commercial`. Aceitar um endereço no corpo deixava qualquer pessoa
/// com permissão de facturação mandar a factura de um cliente para onde quisesse
/// — a mesma classe de falha que o ADR-057 corrigiu no executor do pagamento.
/// </para>
/// </summary>
public sealed class DeliverFiscalDocument(
    IssueFiscalDocumentFile ficheiros,
    ISalesInvoiceStore documents,
    ICustomerDirectory customers,
    ITaxEntityDirectory taxEntity,
    IFiscalDocumentDelivery delivery,
    IAuditTrail audit)
{
    public async Task<FiscalDocumentDeliveryOutcomeResult> ExecuteAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await ResolverClienteAsync(kind, documentId, cancellationToken);

        if (cliente is null)
        {
            return new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.DocumentNotFound, null, null);
        }

        // Consumidor final não tem cliente registado, logo não tem endereço. Não
        // é erro de configuração nem falha: é um documento que se entrega em mão.
        if (cliente.CustomerId is null)
        {
            return new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.NoRecipient,
                null,
                "O documento foi emitido a consumidor final e não tem cliente a quem enviar.");
        }

        var referencia = await customers.FindAsync(cliente.CustomerId.Value, cancellationToken);

        if (referencia is null)
        {
            return new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.NoRecipient,
                null,
                "O cliente do documento não foi encontrado.");
        }

        if (string.IsNullOrWhiteSpace(referencia.Email))
        {
            return new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.NoRecipient,
                null,
                $"O cliente {referencia.Name} não tem endereço de correio registado.");
        }

        var ficheiro = await ficheiros.ExecuteAsync(kind, documentId, context, cancellationToken);

        switch (ficheiro.Outcome)
        {
            case FiscalDocumentFileOutcome.DocumentNotFound:
                return new FiscalDocumentDeliveryOutcomeResult(
                    FiscalDocumentDeliveryOutcome.DocumentNotFound, null, null);

            case FiscalDocumentFileOutcome.IssuerNotDeclared:
                return new FiscalDocumentDeliveryOutcomeResult(
                    FiscalDocumentDeliveryOutcome.IssuerNotDeclared, null, null);
        }

        var emitente = await taxEntity.FindAsync(cancellationToken);
        var nomeDoEmitente = emitente?.CompanyName ?? "a empresa";

        var assunto = $"{cliente.Title} {cliente.Number}";

        var resultado = await delivery.SendAsync(
            new FiscalDocumentDelivery(
                referencia.Email!,
                referencia.Name,
                assunto,
                Corpo(cliente.Title, cliente.Number, referencia.Name, nomeDoEmitente),
                ficheiro.FileName!,
                ficheiro.Content!),
            cancellationToken);

        // Registra-se o envio tentado, com o destino, tenha corrido bem ou mal.
        // Um envio falhado é informação: diz que alguém tentou e que o cliente
        // não recebeu.
        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.FiscalDocumentDelivered,
                IssueFiscalDocumentFile.EntityTypeDe(kind),
                documentId.ToString(),
                context,
                NewValue: $$"""{"to":"{{referencia.Email}}","sent":{{(resultado.Sent ? "true" : "false")}}}"""),
            cancellationToken);

        return resultado.Sent
            ? new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.Sent, referencia.Email, null)
            : new FiscalDocumentDeliveryOutcomeResult(
                FiscalDocumentDeliveryOutcome.DeliveryFailed, referencia.Email, resultado.Error);
    }

    /// <summary>
    /// O texto do corpo. Simples de propósito: o documento é o anexo, e o corpo
    /// só tem de dizer o que vem e de quem.
    /// </summary>
    private static string Corpo(string titulo, string numero, string cliente, string emitente) =>
        $"""
        Estimado(a) {cliente},

        Segue em anexo a {titulo.ToLowerInvariant()} {numero}.

        Com os melhores cumprimentos,
        {emitente}
        """;

    private async Task<DocumentoParaEnviar?> ResolverClienteAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case FiscalDocumentKind.SalesInvoice:
            {
                var factura = await documents.FindAsync(documentId, cancellationToken);
                return factura is null
                    ? null
                    : new DocumentoParaEnviar("Factura", factura.Number.ToString(), factura.CustomerId);
            }

            case FiscalDocumentKind.CreditNote:
            {
                var nota = await documents.FindCreditNoteAsync(documentId, cancellationToken);
                return nota is null
                    ? null
                    : new DocumentoParaEnviar("Nota de crédito", nota.Number.ToString(), nota.CustomerId);
            }

            case FiscalDocumentKind.Receipt:
            {
                var recibo = await documents.FindReceiptAsync(documentId, cancellationToken);
                return recibo is null
                    ? null
                    : new DocumentoParaEnviar("Recibo", recibo.Number.ToString(), recibo.CustomerId);
            }

            default:
                return null;
        }
    }

    private sealed record DocumentoParaEnviar(string Title, string Number, Guid? CustomerId);
}

public sealed record FiscalDocumentDeliveryOutcomeResult(
    FiscalDocumentDeliveryOutcome Outcome,
    string? SentTo,
    string? Error);

public enum FiscalDocumentDeliveryOutcome
{
    Sent,
    DocumentNotFound,

    /// <summary>
    /// Não há a quem enviar — consumidor final, cliente apagado, ou cliente sem
    /// endereço registado. A mensagem diz qual dos três.
    /// </summary>
    NoRecipient,

    IssuerNotDeclared,

    /// <summary>O servidor de correio recusou. A mensagem dele sobe ao chamador.</summary>
    DeliveryFailed,
}
