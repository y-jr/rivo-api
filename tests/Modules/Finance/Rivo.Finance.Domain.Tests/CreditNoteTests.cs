using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// A nota de crédito existe porque a factura é imutável: corrigir um documento
/// emitido é emitir outro que o referencia, nunca reescrevê-lo.
/// </summary>
public class CreditNoteTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);

    private static DocumentNumber NumeroNc() =>
        DocumentSeries.Open(DocumentType.NC, "S001").Allocate();

    private static SalesInvoice Factura() =>
        SalesInvoice.Issue(
            DocumentSeries.Open(DocumentType.FT, "S001").Allocate(),
            Hoje.AddDays(-10), Hoje.AddDays(-10),
            Guid.CreateVersion7(),
            new InvoicedParty("Kianda Lda", "5417000000", "Rua Rainha Ginga 12", "Luanda", "AO"),
            "AOA",
            [new NewInvoiceLine("Consultoria", 2, 50_000m, "NOR", 14m)]);

    private static CreditNote Nota(SalesInvoice? factura = null, params NewInvoiceLine[] linhas) =>
        CreditNote.Issue(
            NumeroNc(), factura ?? Factura(), Hoje, "Servico nao prestado",
            linhas.Length > 0 ? linhas : [new NewInvoiceLine("Consultoria", 1, 50_000m, "NOR", 14m)]);

    [Fact]
    public void NotaEmitida_NumeraSeEmSerieNc()
    {
        Assert.Equal("NC S001/1", Nota().Number.Formatted);
    }

    [Fact]
    public void NotaEmSerieDeFactura_ERecusada()
    {
        var numeroFt = DocumentSeries.Open(DocumentType.FT, "S001").Allocate();

        Assert.Throws<ArgumentException>(() =>
            CreditNote.Issue(numeroFt, Factura(), Hoje, "Motivo",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m)]));
    }

    [Fact]
    public void NotaGuardaAReferenciaTextualDaFactura()
    {
        var factura = Factura();
        var nota = Nota(factura);

        Assert.Equal(factura.Id, nota.SalesInvoiceId);
        Assert.Equal(factura.Number.Formatted, nota.CorrectedInvoiceNumber);
    }

    /// <summary>
    /// O imposto que se devolve é o que foi liquidado, não o de hoje
    /// (ADR-011 §3). A taxa pode ter mudado entretanto.
    /// </summary>
    [Fact]
    public void FactoGeradorEODaFacturaCorrigida()
    {
        var factura = Factura();
        var nota = Nota(factura);

        Assert.Equal(factura.TaxPointDate, nota.TaxPointDate);
        Assert.NotEqual(nota.IssuedOn, nota.TaxPointDate);
    }

    [Fact]
    public void ClienteEOMesmoDaFacturaCorrigida()
    {
        var factura = Factura();
        var nota = Nota(factura);

        Assert.Equal(factura.Customer.TaxId, nota.Customer.TaxId);
        Assert.Equal(factura.CustomerId, nota.CustomerId);
        Assert.Equal(factura.Currency, nota.Currency);
    }

    [Fact]
    public void TotaisSaoASomaDasLinhas()
    {
        var nota = Nota(null, new NewInvoiceLine("Consultoria", 1, 50_000m, "NOR", 14m));

        Assert.Equal(50_000m, nota.NetTotal);
        Assert.Equal(7_000m, nota.TaxTotal);
        Assert.Equal(57_000m, nota.GrossTotal);
    }

    // ---- recusas ----

    /// <summary>
    /// Uma factura anulada já não tem o que corrigir. Creditá-la produziria duas
    /// correcções do mesmo facto.
    /// </summary>
    [Fact]
    public void CreditarFacturaAnulada_ERecusado()
    {
        var factura = Factura();
        factura.Cancel("Engano", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => Nota(factura));
    }

    [Fact]
    public void NotaSemMotivo_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            CreditNote.Issue(NumeroNc(), Factura(), Hoje, "   ",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m)]));
    }

    [Fact]
    public void NotaSemLinhas_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            CreditNote.Issue(NumeroNc(), Factura(), Hoje, "Motivo", []));
    }

    [Fact]
    public void NotaDeValorZero_ERecusada()
    {
        // Nao corrige nada, e ficaria no historico a fingir que corrigiu.
        Assert.Throws<ArgumentException>(() =>
            Nota(null, new NewInvoiceLine("Oferta", 1, 0m, "NOR", 0m)));
    }

    [Fact]
    public void NotaAnteriorAFactura_ERecusada()
    {
        var factura = Factura();

        Assert.Throws<ArgumentException>(() =>
            CreditNote.Issue(NumeroNc(), factura, factura.IssuedOn.AddDays(-1), "Motivo",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m)]));
    }

    // ---- anulação ----

    [Fact]
    public void AnularMantemLinhasETotais()
    {
        var nota = Nota();
        nota.Cancel("Emitida por engano", DateTimeOffset.UtcNow);

        Assert.Equal(InvoiceStatus.Cancelled, nota.Status);
        Assert.Single(nota.Lines);
        Assert.Equal(57_000m, nota.GrossTotal);
    }

    [Fact]
    public void AnularDuasVezes_ERecusado()
    {
        var nota = Nota();
        nota.Cancel("Primeira", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => nota.Cancel("Segunda", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var nota = Nota();
        nota.Cancel("Engano", DateTimeOffset.UtcNow);

        Assert.Equal(0, nota.Version);
    }
}
