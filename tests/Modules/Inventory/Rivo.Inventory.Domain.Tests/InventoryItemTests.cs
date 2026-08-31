using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Domain.Tests;

public class InventoryItemTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 30);
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ArmazemA = Guid.CreateVersion7();
    private static readonly Guid ArmazemB = Guid.CreateVersion7();

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

        var movimento = item.RegisterReceipt(ArmazemA, 10m, "Compra inicial", Hoje, Agora);

        Assert.Equal(10m, item.QuantityOnHand);
        Assert.Equal(10m, item.QuantityOnHandAt(ArmazemA));
        Assert.Equal(StockMovementType.Receipt, movimento.Type);
        Assert.Equal(ArmazemA, movimento.WarehouseId);
        Assert.Null(movimento.RelatedWarehouseId);
        Assert.Equal(10m, movimento.Quantity);
        Assert.Same(movimento, Assert.Single(item.Movements));
    }

    [Fact]
    public void RegisterReceipt_AccumulatesAcrossMultipleReceipts()
    {
        var item = Registado();

        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);
        item.RegisterReceipt(ArmazemA, 5m, null, Hoje, Agora);

        Assert.Equal(15m, item.QuantityOnHand);
        Assert.Equal(2, item.Movements.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterReceipt_NonPositiveQuantity_Throws(decimal quantity)
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterReceipt(ArmazemA, quantity, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterReceipt_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterReceipt_WithoutWarehouse_Throws()
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterReceipt(Guid.Empty, 10m, null, Hoje, Agora));
    }

    // --- Saída ---------------------------------------------------------

    [Fact]
    public void RegisterIssue_DecreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        var movimento = item.RegisterIssue(ArmazemA, 4m, "Consumo interno", Hoje, Agora);

        Assert.Equal(6m, item.QuantityOnHand);
        Assert.Equal(6m, item.QuantityOnHandAt(ArmazemA));
        Assert.Equal(StockMovementType.Issue, movimento.Type);
        Assert.Equal(-4m, movimento.Quantity);
    }

    [Fact]
    public void RegisterIssue_ExceedingQuantityOnHand_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 5m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(() => item.RegisterIssue(ArmazemA, 6m, null, Hoje, Agora));

        // A quantidade não muda quando a saída é recusada.
        Assert.Equal(5m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterIssue_ExactlyAllOnHand_IsAllowed()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 5m, null, Hoje, Agora);

        item.RegisterIssue(ArmazemA, 5m, null, Hoje, Agora);

        Assert.Equal(0m, item.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterIssue_NonPositiveQuantity_Throws(decimal quantity)
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        Assert.Throws<ArgumentException>(() => item.RegisterIssue(ArmazemA, quantity, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterIssue_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterIssue(ArmazemA, 1m, null, Hoje, Agora));
    }

    [Fact]
    public void RegisterIssue_EnoughStockGloballyButNotInThatWarehouse_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        // Há 10 no total (armazém A), mas nada no armazém B — não se pode
        // "emprestar" de outro armazém numa saída.
        Assert.Throws<InvalidOperationException>(() => item.RegisterIssue(ArmazemB, 1m, null, Hoje, Agora));
        Assert.Equal(10m, item.QuantityOnHand);
    }

    // --- Ajuste --------------------------------------------------------

    [Fact]
    public void RegisterAdjustment_Positive_IncreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        var movimento = item.RegisterAdjustment(ArmazemA, 3m, "Contagem física encontrou mais 3", Hoje, Agora);

        Assert.Equal(13m, item.QuantityOnHand);
        Assert.Equal(StockMovementType.Adjustment, movimento.Type);
        Assert.Equal(3m, movimento.Quantity);
    }

    [Fact]
    public void RegisterAdjustment_Negative_DecreasesQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        item.RegisterAdjustment(ArmazemA, -4m, "Contagem física encontrou menos 4", Hoje, Agora);

        Assert.Equal(6m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterAdjustment_Zero_Throws()
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterAdjustment(ArmazemA, 0m, "Sem variação", Hoje, Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RegisterAdjustment_WithoutReason_Throws(string reason)
    {
        var item = Registado();

        Assert.Throws<ArgumentException>(() => item.RegisterAdjustment(ArmazemA, 5m, reason, Hoje, Agora));
    }

    [Fact]
    public void RegisterAdjustment_BelowZero_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 5m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(() => item.RegisterAdjustment(ArmazemA, -6m, "Contagem", Hoje, Agora));

        Assert.Equal(5m, item.QuantityOnHand);
    }

    [Fact]
    public void RegisterAdjustment_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.RegisterAdjustment(ArmazemA, 5m, "Contagem", Hoje, Agora));
    }

    [Fact]
    public void RegisterAdjustment_BelowZeroInThatWarehouseEvenWithStockElsewhere_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);
        item.RegisterReceipt(ArmazemB, 2m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(
            () => item.RegisterAdjustment(ArmazemB, -3m, "Contagem", Hoje, Agora));
    }

    // --- Transferência ---------------------------------------------------

    [Fact]
    public void Transfer_MovesQuantityBetweenWarehouses()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        var (saida, entrada) = item.Transfer(ArmazemA, ArmazemB, 4m, "Reorganização", Hoje, Agora);

        Assert.Equal(6m, item.QuantityOnHandAt(ArmazemA));
        Assert.Equal(4m, item.QuantityOnHandAt(ArmazemB));
        Assert.Equal(StockMovementType.TransferOut, saida.Type);
        Assert.Equal(-4m, saida.Quantity);
        Assert.Equal(ArmazemA, saida.WarehouseId);
        Assert.Equal(ArmazemB, saida.RelatedWarehouseId);
        Assert.Equal(StockMovementType.TransferIn, entrada.Type);
        Assert.Equal(4m, entrada.Quantity);
        Assert.Equal(ArmazemB, entrada.WarehouseId);
        Assert.Equal(ArmazemA, entrada.RelatedWarehouseId);
    }

    [Fact]
    public void Transfer_DoesNotChangeGlobalQuantityOnHand()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        item.Transfer(ArmazemA, ArmazemB, 4m, null, Hoje, Agora);

        Assert.Equal(10m, item.QuantityOnHand);
    }

    [Fact]
    public void Transfer_IsAtomic_ProducesExactlyTwoMovements()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        item.Transfer(ArmazemA, ArmazemB, 4m, null, Hoje, Agora);

        Assert.Equal(2, item.Movements.Count(m => m.Type is StockMovementType.TransferOut or StockMovementType.TransferIn));
    }

    [Fact]
    public void Transfer_ExceedingSourceQuantity_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 5m, null, Hoje, Agora);

        Assert.Throws<InvalidOperationException>(() => item.Transfer(ArmazemA, ArmazemB, 6m, null, Hoje, Agora));

        Assert.Equal(5m, item.QuantityOnHand);
        Assert.DoesNotContain(item.Movements, m => m.Type is StockMovementType.TransferOut);
    }

    [Fact]
    public void Transfer_SameWarehouseOnBothSides_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        Assert.Throws<ArgumentException>(() => item.Transfer(ArmazemA, ArmazemA, 4m, null, Hoje, Agora));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Transfer_NonPositiveQuantity_Throws(decimal quantity)
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        Assert.Throws<ArgumentException>(() => item.Transfer(ArmazemA, ArmazemB, quantity, null, Hoje, Agora));
    }

    [Fact]
    public void Transfer_OnInactiveItem_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);
        item.Deactivate();

        Assert.Throws<InvalidOperationException>(() => item.Transfer(ArmazemA, ArmazemB, 4m, null, Hoje, Agora));
    }

    [Fact]
    public void Transfer_WithoutSourceWarehouse_Throws()
    {
        var item = Registado();
        item.RegisterReceipt(ArmazemA, 10m, null, Hoje, Agora);

        Assert.Throws<ArgumentException>(() => item.Transfer(Guid.Empty, ArmazemB, 4m, null, Hoje, Agora));
    }

    // --- Invariante geral ------------------------------------------------

    [Fact]
    public void QuantityOnHand_IsAlwaysTheSumOfMovements()
    {
        var item = Registado();

        item.RegisterReceipt(ArmazemA, 20m, null, Hoje, Agora);
        item.RegisterIssue(ArmazemA, 5m, null, Hoje, Agora);
        item.RegisterAdjustment(ArmazemA, -2m, "Contagem", Hoje, Agora);
        item.RegisterReceipt(ArmazemA, 3m, null, Hoje, Agora);

        Assert.Equal(item.Movements.Sum(m => m.Quantity), item.QuantityOnHand);
        Assert.Equal(16m, item.QuantityOnHand);
    }

    [Fact]
    public void QuantityOnHandAt_IsTheSumOfMovementsOfThatWarehouseOnly()
    {
        var item = Registado();

        item.RegisterReceipt(ArmazemA, 20m, null, Hoje, Agora);
        item.RegisterReceipt(ArmazemB, 5m, null, Hoje, Agora);
        item.RegisterIssue(ArmazemA, 3m, null, Hoje, Agora);

        Assert.Equal(17m, item.QuantityOnHandAt(ArmazemA));
        Assert.Equal(5m, item.QuantityOnHandAt(ArmazemB));
        Assert.Equal(22m, item.QuantityOnHand);
    }
}
