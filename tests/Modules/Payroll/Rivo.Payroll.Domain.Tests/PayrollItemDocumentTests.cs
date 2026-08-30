using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Domain.Tests;

/// <summary>
/// Ligação entre Item de Folha e documento (o Recibo, tipicamente). Vive em
/// `payroll` e não em `documents` (ADR-009) — mesmo desenho de
/// `Rivo.Hr.Domain.EmployeeDocumentTests`.
/// </summary>
public class PayrollItemDocumentTests
{
    private static readonly Guid Item = Guid.CreateVersion7();
    private static readonly Guid Document = Guid.CreateVersion7();
    private static readonly DateTimeOffset AttachedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Attach_KeepsBothSidesOfTheLink()
    {
        var link = PayrollItemDocument.Attach(Item, Document, "recibo", AttachedAt);

        Assert.Equal(Item, link.PayrollItemId);
        Assert.Equal(Document, link.DocumentId);
        Assert.Equal(AttachedAt, link.AttachedAt);
    }

    [Fact]
    public void Attach_TrimsCategory()
    {
        var link = PayrollItemDocument.Attach(Item, Document, "  recibo  ", AttachedAt);

        Assert.Equal("recibo", link.Category);
    }

    [Fact]
    public void Attach_WithoutItem_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PayrollItemDocument.Attach(Guid.Empty, Document, "recibo", AttachedAt));
    }

    [Fact]
    public void Attach_WithoutDocument_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PayrollItemDocument.Attach(Item, Guid.Empty, "recibo", AttachedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Attach_WithoutCategory_Throws(string category)
    {
        Assert.Throws<ArgumentException>(
            () => PayrollItemDocument.Attach(Item, Document, category, AttachedAt));
    }
}
