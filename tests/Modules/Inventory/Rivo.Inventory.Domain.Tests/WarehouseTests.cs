using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Domain.Tests;

public class WarehouseTests
{
    [Fact]
    public void Register_StartsActive()
    {
        var warehouse = Warehouse.Register("PRINCIPAL", "Armazém Principal");

        Assert.Equal(WarehouseStatus.Active, warehouse.Status);
        Assert.NotEqual(Guid.Empty, warehouse.Id);
    }

    [Fact]
    public void Register_NormalizesCode()
    {
        var warehouse = Warehouse.Register("  principal  ", "Armazém Principal");

        Assert.Equal("PRINCIPAL", warehouse.Code);
    }

    [Fact]
    public void Register_TrimsName()
    {
        var warehouse = Warehouse.Register("PRINCIPAL", "  Armazém Principal  ");

        Assert.Equal("Armazém Principal", warehouse.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Register_WithoutCode_Throws(string code)
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Register(code, "Armazém Principal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Register_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Warehouse.Register("PRINCIPAL", name));
    }

    [Fact]
    public void Deactivate_SetsStatusToInactive()
    {
        var warehouse = Warehouse.Register("PRINCIPAL", "Armazém Principal");

        warehouse.Deactivate();

        Assert.Equal(WarehouseStatus.Inactive, warehouse.Status);
    }

    [Fact]
    public void Reactivate_SetsStatusBackToActive()
    {
        var warehouse = Warehouse.Register("PRINCIPAL", "Armazém Principal");
        warehouse.Deactivate();

        warehouse.Reactivate();

        Assert.Equal(WarehouseStatus.Active, warehouse.Status);
    }
}
