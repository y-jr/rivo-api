using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Domain.Tests;

/// <summary>
/// Ligação entre Viatura e documento. Vive em `fleet` e não em `documents`
/// (ADR-009), mesma disciplina de <c>EmployeeDocument</c> em `hr`.
/// </summary>
public class VehicleDocumentTests
{
    private static readonly Guid VehicleId = Guid.CreateVersion7();
    private static readonly Guid DocumentId = Guid.CreateVersion7();
    private static readonly DateTimeOffset AttachedAt = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Attach_KeepsBothSidesOfTheLink()
    {
        var link = VehicleDocument.Attach(VehicleId, DocumentId, "seguro", AttachedAt);

        Assert.Equal(VehicleId, link.VehicleId);
        Assert.Equal(DocumentId, link.DocumentId);
        Assert.Equal(AttachedAt, link.AttachedAt);
    }

    [Fact]
    public void Attach_TrimsCategory()
    {
        var link = VehicleDocument.Attach(VehicleId, DocumentId, "  seguro  ", AttachedAt);

        Assert.Equal("seguro", link.Category);
    }

    [Fact]
    public void Attach_WithoutVehicle_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VehicleDocument.Attach(Guid.Empty, DocumentId, "seguro", AttachedAt));
    }

    [Fact]
    public void Attach_WithoutDocument_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VehicleDocument.Attach(VehicleId, Guid.Empty, "seguro", AttachedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Attach_WithoutCategory_Throws(string category)
    {
        Assert.Throws<ArgumentException>(
            () => VehicleDocument.Attach(VehicleId, DocumentId, category, AttachedAt));
    }
}
