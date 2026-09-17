using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Domain.Tests;

public class InventoryCountTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 31);
    private static readonly Guid Armazem = Guid.CreateVersion7();
    private static readonly Guid ItemA = Guid.CreateVersion7();
    private static readonly Guid ItemB = Guid.CreateVersion7();

    private static InventoryCount Aberta() => InventoryCount.Open(Armazem, Hoje);

    private static readonly DateTimeOffset Instante = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Contagem aberta com uma linha divergente — o ponto de partida da governança.</summary>
    private static InventoryCount ComUmaLinha(decimal esperado, decimal contado)
    {
        var contagem = Aberta();
        contagem.AddLine(ItemA, contado, esperado);

        return contagem;
    }

    // --- Open ----------------------------------------------------------

    [Fact]
    public void Open_StartsOpenWithNoLines()
    {
        var count = Aberta();

        Assert.Equal(InventoryCountStatus.Open, count.Status);
        Assert.Empty(count.Lines);
        Assert.Equal(Armazem, count.WarehouseId);
    }

    [Fact]
    public void Open_WithoutWarehouse_Throws()
    {
        Assert.Throws<ArgumentException>(() => InventoryCount.Open(Guid.Empty, Hoje));
    }

    // --- AddLine ---------------------------------------------------------

    [Fact]
    public void AddLine_RecordsExpectedAndCountedQuantity()
    {
        var count = Aberta();

        var linha = count.AddLine(ItemA, countedQuantity: 18m, expectedQuantity: 20m);

        Assert.Equal(ItemA, linha.ItemId);
        Assert.Equal(20m, linha.ExpectedQuantity);
        Assert.Equal(18m, linha.CountedQuantity);
        Assert.Equal(-2m, linha.Variance);
        Assert.Same(linha, Assert.Single(count.Lines));
    }

    [Fact]
    public void AddLine_ExactMatch_HasZeroVariance()
    {
        var count = Aberta();

        var linha = count.AddLine(ItemA, countedQuantity: 20m, expectedQuantity: 20m);

        Assert.Equal(0m, linha.Variance);
    }

    [Fact]
    public void AddLine_MoreThanExpected_HasPositiveVariance()
    {
        var count = Aberta();

        var linha = count.AddLine(ItemA, countedQuantity: 25m, expectedQuantity: 20m);

        Assert.Equal(5m, linha.Variance);
    }

    [Fact]
    public void AddLine_MultipleDifferentItems_AreAllRecorded()
    {
        var count = Aberta();

        count.AddLine(ItemA, 20m, 20m);
        count.AddLine(ItemB, 5m, 8m);

        Assert.Equal(2, count.Lines.Count);
    }

    [Fact]
    public void AddLine_ZeroCountedQuantity_IsAllowed()
    {
        var count = Aberta();

        var linha = count.AddLine(ItemA, countedQuantity: 0m, expectedQuantity: 3m);

        Assert.Equal(-3m, linha.Variance);
    }

    [Fact]
    public void AddLine_NegativeCountedQuantity_Throws()
    {
        var count = Aberta();

        Assert.Throws<ArgumentException>(() => count.AddLine(ItemA, countedQuantity: -1m, expectedQuantity: 0m));
    }

    [Fact]
    public void AddLine_WithoutItem_Throws()
    {
        var count = Aberta();

        Assert.Throws<ArgumentException>(() => count.AddLine(Guid.Empty, countedQuantity: 1m, expectedQuantity: 0m));
    }

    [Fact]
    public void AddLine_SameItemTwice_Throws()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);

        Assert.Throws<InvalidOperationException>(() => count.AddLine(ItemA, 18m, 20m));

        // A primeira linha continua lá, intacta.
        Assert.Single(count.Lines);
    }

    [Fact]
    public void AddLine_OnClosedCount_Throws()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);
        count.Close();

        Assert.Throws<InvalidOperationException>(() => count.AddLine(ItemB, 5m, 5m));
    }

    [Fact]
    public void AddLine_OnCancelledCount_Throws()
    {
        var count = Aberta();
        count.Cancel("Aberta por engano");

        Assert.Throws<InvalidOperationException>(() => count.AddLine(ItemA, 5m, 5m));
    }

    // --- Close -----------------------------------------------------------

    [Fact]
    public void Close_WithLines_TransitionsToClosed()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);

        count.Close();

        Assert.Equal(InventoryCountStatus.Closed, count.Status);
    }

    [Fact]
    public void Close_WithoutAnyLine_Throws()
    {
        var count = Aberta();

        Assert.Throws<InvalidOperationException>(() => count.Close());
    }

    [Fact]
    public void Close_Twice_Throws()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);
        count.Close();

        Assert.Throws<InvalidOperationException>(() => count.Close());
    }

    [Fact]
    public void Close_CancelledCount_Throws()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);
        count.Cancel("Engano");

        Assert.Throws<InvalidOperationException>(() => count.Close());
    }

    // --- Cancel ------------------------------------------------------------

    [Fact]
    public void Cancel_TransitionsToCancelledWithReason()
    {
        var count = Aberta();

        count.Cancel("Aberta no armazém errado");

        Assert.Equal(InventoryCountStatus.Cancelled, count.Status);
        Assert.Equal("Aberta no armazém errado", count.CancellationReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Cancel_WithoutReason_Throws(string reason)
    {
        var count = Aberta();

        Assert.Throws<ArgumentException>(() => count.Cancel(reason));
    }

    [Fact]
    public void Cancel_ClosedCount_Throws()
    {
        var count = Aberta();
        count.AddLine(ItemA, 20m, 20m);
        count.Close();

        Assert.Throws<InvalidOperationException>(() => count.Cancel("Tarde demais"));
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var count = Aberta();
        count.Cancel("Primeiro motivo");

        Assert.Throws<InvalidOperationException>(() => count.Cancel("Segundo motivo"));
    }

    // ── Governança da divergência (ADR-064) ────────────────────────────────

    [Fact]
    public void MarkSubmitted_RetemAContagemSemAFechar()
    {
        var contagem = ComUmaLinha(esperado: 10, contado: 4);
        var processo = Guid.CreateVersion7();

        contagem.MarkSubmitted(processo, varianceValue: 1_500m, Instante);

        Assert.Equal(InventoryCountStatus.PendingApproval, contagem.Status);
        Assert.Equal(processo, contagem.ApprovalRequestId);
        Assert.Equal(1_500m, contagem.SubmittedVarianceValue);
        Assert.Equal(Instante, contagem.SubmittedAt);
    }

    [Fact]
    public void MarkSubmitted_SemLinhas_Recusa()
    {
        var contagem = Aberta();

        Assert.Throws<InvalidOperationException>(
            () => contagem.MarkSubmitted(Guid.CreateVersion7(), 1m, Instante));
    }

    [Fact]
    public void PendenteDeDecisao_NaoAceitaLinhaNova()
    {
        var contagem = ComUmaLinha(esperado: 10, contado: 4);
        contagem.MarkSubmitted(Guid.CreateVersion7(), 1_500m, Instante);

        Assert.Throws<InvalidOperationException>(
            () => contagem.AddLine(Guid.CreateVersion7(), 1, 0));
    }

    [Fact]
    public void Close_APartirDePendente_EPermitido()
    {
        var contagem = ComUmaLinha(esperado: 10, contado: 4);
        contagem.MarkSubmitted(Guid.CreateVersion7(), 1_500m, Instante);

        contagem.Close();

        Assert.Equal(InventoryCountStatus.Closed, contagem.Status);
    }

    [Fact]
    public void MarkRefused_SoAPartirDePendente()
    {
        var aberta = ComUmaLinha(esperado: 10, contado: 4);

        Assert.Throws<InvalidOperationException>(() => aberta.MarkRefused(Instante));
    }

    [Fact]
    public void Recusada_NaoReabreENaoAceitaLinhas()
    {
        var contagem = ComUmaLinha(esperado: 10, contado: 4);
        contagem.MarkSubmitted(Guid.CreateVersion7(), 1_500m, Instante);

        contagem.MarkRefused(Instante);

        Assert.Equal(InventoryCountStatus.Refused, contagem.Status);
        Assert.Equal(Instante, contagem.SettledAt);
        Assert.Throws<InvalidOperationException>(() => contagem.AddLine(Guid.CreateVersion7(), 1, 0));
        Assert.Throws<InvalidOperationException>(() => contagem.Close());
    }

    [Fact]
    public void LinesWithVariance_SoAsQueDivergem_DaMaiorFaltaParaAMaiorSobra()
    {
        var contagem = Aberta();
        contagem.AddLine(Guid.CreateVersion7(), countedQuantity: 10, expectedQuantity: 10);
        contagem.AddLine(Guid.CreateVersion7(), countedQuantity: 12, expectedQuantity: 10);
        contagem.AddLine(Guid.CreateVersion7(), countedQuantity: 1, expectedQuantity: 10);

        var divergentes = contagem.LinesWithVariance;

        Assert.Equal(2, divergentes.Count);
        Assert.Equal(-9, divergentes[0].Variance);
        Assert.Equal(2, divergentes[1].Variance);
    }
}
