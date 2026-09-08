using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Emite uma factura preenchendo os três argumentos que a cadeia de
/// integridade trouxe (K7) e que nenhum destes testes é sobre.
///
/// <para>
/// <strong>Existe para os manter legíveis, e não para os facilitar.</strong>
/// <c>SalesInvoice.Issue</c> exige <c>systemEntryDate</c>,
/// <c>issuedByUserId</c> e o elo anterior — de propósito, porque o código de
/// produção não os pode esquecer. Um teste sobre moeda inválida não fica mais
/// claro por os repetir; um teste sobre a cadeia passa-os explicitamente.
/// </para>
/// </summary>
internal static class FacturaDeTeste
{
    internal static readonly DateTimeOffset Registo =
        new(2026, 9, 8, 10, 30, 0, TimeSpan.Zero);

    internal static readonly Guid Emissor = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    internal static SalesInvoice Emitir(
        DocumentNumber number,
        DateOnly issuedOn,
        DateOnly taxPointDate,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        IReadOnlyList<NewInvoiceLine> lines,
        string? fiscalNotice = null,
        DateTimeOffset? systemEntryDate = null,
        Guid? issuedByUserId = null,
        string? previousHash = null) =>
        SalesInvoice.Issue(
            number,
            issuedOn,
            taxPointDate,
            customerId,
            customer,
            currency,
            lines,
            systemEntryDate ?? Registo,
            issuedByUserId ?? Emissor,
            previousHash,
            fiscalNotice);
}
