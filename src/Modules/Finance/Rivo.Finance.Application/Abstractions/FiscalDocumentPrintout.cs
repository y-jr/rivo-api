using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Tudo o que vai impresso, já reunido — nada de ir buscar mais nada durante a
/// composição.
///
/// <para>
/// É a forma que torna o compositor testável sem base de dados: dá-se-lhe um
/// <see cref="FiscalDocumentPrintout"/> e ele devolve bytes. E é a forma que
/// torna o papel <strong>determinístico</strong>: dois pedidos com o mesmo
/// printout produzem o mesmo ficheiro, o que é a propriedade de que um documento
/// fiscal precisa.
/// </para>
/// </summary>
/// <param name="Issuer">
/// Quem emite, lido de `fiscal` pelo contrato (ADR-010). Sem isto não há
/// documento — ver <c>TaxEntityProfile</c>.
/// </param>
public sealed record FiscalDocumentPrintout(
    TaxEntityProfileView Issuer,
    FiscalDocumentContent Document);

/// <summary>
/// O documento em si, na forma comum aos três.
///
/// <para>
/// <strong>Uma projecção para factura, nota de crédito e recibo.</strong> Os três
/// partilham quase tudo — número, cliente congelado, moeda, linhas, totais, a
/// menção fiscal — e diferem em pormenores que caberiam em campos opcionais. Três
/// projecções paralelas davam três composições a divergir com o tempo.
/// </para>
/// </summary>
/// <param name="Title">
/// O que o papel diz que é: «Factura», «Nota de crédito», «Recibo». Vem
/// decidido de fora para que o compositor não tenha de traduzir o enum — e para
/// que a palavra impressa seja uma decisão de negócio, não um detalhe de
/// serialização.
/// </param>
/// <param name="TaxPointDate">
/// Data do facto gerador. Nula no recibo, que não tem um — o recibo constata um
/// pagamento, não gera imposto.
/// </param>
/// <param name="Reference">
/// O documento que este corrige ou liquida. Número da factura corrigida, na nota
/// de crédito; nulo na factura.
/// </param>
/// <param name="Note">
/// O motivo da nota de crédito, ou as observações do recibo. Texto livre que o
/// utilizador escreveu e que tem de aparecer no papel.
/// </param>
/// <param name="FiscalNotice">
/// A menção legal congelada no momento da emissão. Vai impressa como está —
/// <strong>não se recalcula</strong>, porque o que vale é a menção que era
/// exigida à data e não a de hoje.
/// </param>
/// <param name="Cancelled">
/// Se o documento está anulado. Quando verdadeiro o papel diz-o em cima, bem
/// visível: entregar uma anulada com aspecto de boa é pior do que não a entregar.
/// </param>
public sealed record FiscalDocumentContent(
    string Title,
    string Number,
    DateOnly IssuedOn,
    DateOnly? TaxPointDate,
    string Currency,
    FiscalDocumentParty Customer,
    IReadOnlyList<FiscalDocumentLine> Lines,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    string? FiscalNotice,
    bool Cancelled,
    string? CancellationReason = null,
    string? Reference = null,
    string? Note = null,
    string? PaymentMethod = null);

/// <summary>
/// O cliente, como estava no momento da emissão. Retrato, não referência — é o
/// <c>InvoicedParty</c> que o documento congelou.
/// </summary>
public sealed record FiscalDocumentParty(
    string Name,
    string TaxId,
    string? AddressDetail,
    string? City,
    string? Country,
    bool FinalConsumer);

/// <param name="TaxPercentage">
/// A taxa aplicada, em pontos percentuais. Vai impressa por linha porque um
/// documento com linhas a taxas diferentes tem de mostrar qual foi qual.
/// </param>
public sealed record FiscalDocumentLine(
    int LineNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode,
    decimal TaxPercentage,
    decimal NetAmount,
    decimal TaxAmount);
