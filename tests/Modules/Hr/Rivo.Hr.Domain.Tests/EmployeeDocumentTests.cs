using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Ligação entre Colaborador e documento. Vive em `hr` e não em `documents`
/// (ADR-009): a classificação e a retenção só fazem sentido no contexto que
/// sabe que um contrato de trabalho se guarda X anos.
/// </summary>
public class EmployeeDocumentTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid Document = Guid.CreateVersion7();
    private static readonly DateTimeOffset AttachedAt = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Attach_KeepsBothSidesOfTheLink()
    {
        var link = EmployeeDocument.Attach(Employee, Document, "contrato", AttachedAt);

        Assert.Equal(Employee, link.EmployeeId);
        Assert.Equal(Document, link.DocumentId);
        Assert.Equal(AttachedAt, link.AttachedAt);
    }

    [Fact]
    public void Attach_TrimsCategory()
    {
        var link = EmployeeDocument.Attach(Employee, Document, "  contrato  ", AttachedAt);

        Assert.Equal("contrato", link.Category);
    }

    /// <summary>
    /// Uma ligação sem os dois lados não é ligação nenhuma — é exactamente a
    /// integridade que a chave polimórfica do desenho inicial não dava
    /// (anti-padrão A5).
    /// </summary>
    [Fact]
    public void Attach_WithoutEmployee_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => EmployeeDocument.Attach(Guid.Empty, Document, "contrato", AttachedAt));
    }

    [Fact]
    public void Attach_WithoutDocument_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => EmployeeDocument.Attach(Employee, Guid.Empty, "contrato", AttachedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Attach_WithoutCategory_Throws(string category)
    {
        Assert.Throws<ArgumentException>(
            () => EmployeeDocument.Attach(Employee, Document, category, AttachedAt));
    }
}
