using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Domain.Tests;

public class InventoryItemTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 30);
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private static InventoryItem Registado() => InventoryItem.Register("SKU-001", "Parafuso M6", "un");

    // --- Register / Deactivate ---------------------------------------------

    [Fact]
    public void Register_StartsAtZeroWithNoMovements()
    {
        var item = Registado();

        Assert.Equal(0m, item.QuantityOnHand);
        Assert.Equal(InventoryItemStatus.Active, item.Status);
        Assert.Empty(item.Movements);
    }

    [Fact]
    public void Register_NormalizesSku()
    {
        var item = InventoryItem.Register("  sku-001  ", "Parafuso M6", "un");

        Assert.Equal("SKU-001", item.Sku);
    }

    // --- Recepção ----------------------------------------------------------

    [Fact]
    public void RegisterReceipt_IncreasesQuantityOnHand()
    {
        var item = Registado();

        var movimento = item.RegisterReceipt(10m, "Compra inicial", Hoje, Agora);

        Assert.Equal(10m, item.QuantityOnHand);
        Assert.Equal(StockMovementType.Receipt, movimento.Type);
        Assert.Equal(10m, movimento.Quantity);
        Assert.Same(movimento, Assert.Single(item.Movements));
    }

    [Fact]
    public void RegisterReceipt_AccumulatesAcrossMultipleReceipts()
    {
        var item = Registado();

        item.RegisterReceipt(10m, null, Hoje, Agora);
        item.RegisterReceipt(5m, null, Hoje, Agora);

        Assert.Equal(15m, item.QuantityOnHand);
        Assert.Equal(2, item.Movements.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterReceipt_NonPositiveQuantity_Throws(decimal quantity)
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterReceipt(quantity, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterReceipt_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterReceipt(10m, null, Hoje, Agora));
    }

    // --- Saída ---------------------------------------------------------

    [Fact]
    public void RegisterIssue_DecreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(10m, null, Hoje, Agora);

        var movimento = item.RegisterIssue(4m, "Consumo interno", Hoje, Agora);

        Assert.Equal(6m, item.QuantityOnHand);
        Assert.Equal(StockMovementType.Issue, movimento.Type);
        Assert.Equal(-4m, movimento.Quantity);
    }

    [Fact]
    public void RegisterIssue_ExceedingQuantityOnHand_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(5m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(() => item.RegisterIssue(6m, null, Hoje, Agora));

        // A quantidade não muda quando a saída é recusada.
        Assert.Equal(5m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterIssue_ExactlyAllOnHand_IsAllowed()
    {
        var item = Registado();
        item.RegisterReceipt(5m, null, Hoje, Agora);

        item.RegisterIssue(5m, null, Hoje, Agora);

        Assert.Equal(0m, item.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterIssue_NonPositiveQuantity_Throws(decimal quantity)
    {
        var item = Registado();
        item.RegisterReceipt(10m, null, Hoje, Agora);

        Assert.Throws<ArgumentException>(() => item.RegisterIssue(quantity, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterIssue_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(10m, null, Hoje, Agora);
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterIssue(1m, null, Hoje, Agora));
    }

    // --- Ajuste --------------------------------------------------------

    [Fact]
    public void RegisterAdjustment_Positive_IncreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(10m, null, Hoje, Agora);

        var movimento = item.RegisterAdjustment(3m, "Contagem física encontrou mais 3", Hoje, Agora);

        Assert.Equal(13m, item.QuantityOnHand);
        Assert.Equal(StockMovementType.Adjustment, movimento.Type);
        Assert.Equal(3m, movimento.Quantity);
    }

    [Fact]
    public void RegisterAdjustment_Negative_DecreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(10m, null, Hoje, Agora);

        item.RegisterAdjustment(-4m, "Contagem física encontrou menos 4", Hoje, Agora);

        Assert.Equal(6m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterAdjustment_Zero_Throws()
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterAdjustment(0m, "Sem variação", Hoje, Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RegisterAdjustment_WithoutReason_Throws(string reason)
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterAdjustment(5m, reason, Hoje, Agora));
    }

    [Fact]
    public void RegisterAdjustment_BelowZero_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(5m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(() => item.RegisterAdjustment(-6m, "Contagem", Hoje, Agora));

        Assert.Equal(5m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterAdjustment_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterAdjustment(5m, "Contagem", Hoje, Agora));
    }

    // --- Invariante geral ------------------------------------------------

    [Fact]
    public void QuantityOnHand_IsAlwaysTheSumOfMovements()
    {
        var item = Registado();

        item.RegisterReceipt(20m, null, Hoje, Agora);
        item.RegisterIssue(5m, null, Hoje, Agora);
        item.RegisterAdjustment(-2m, "Contagem", Hoje, Agora);
        item.RegisterReceipt(3m, null, Hoje, Agora);

        Assert.Equal(item.Movements.Sum(m => m.Quantity), item.QuantityOnHand);
        Assert.Equal(16m, item.QuantityOnHand);
    }
}
