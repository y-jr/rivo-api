using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Domain.Tests;

public class PurchaseRequisitionTests
{
    private static readonly Guid Requisitante = Guid.CreateVersion7();
    private static readonly Guid Departamento = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static PurchaseRequisition Rascunho() =>
        PurchaseRequisition.Open(
            Requisitante,
            Departamento,
            "Substituir os dois portáteis avariados da contabilidade.",
            "AOA",
            new DateOnly(2026, 8, 27));

    private static PurchaseRequisition Pendente()
    {
        var requisicao = Rascunho();
        requisicao.AddLine("Portátil 14\", 16 GB", 2, 850_000m);
        requisicao.MarkSubmitted(Guid.CreateVersion7(), Agora);

        return requisicao;
    }

    [Fact]
    public void Open_StartsAsDraft()
    {
        var requisicao = Rascunho();

        Assert.Equal(RequisitionStatus.Draft, requisicao.Status);
        Assert.True(requisicao.IsEditable);
        Assert.Null(requisicao.ApprovalRequestId);
        Assert.Equal(0m, requisicao.EstimatedTotal);
    }

    [Fact]
    public void Open_NormalizesCurrency()
    {
        var requisicao = PurchaseRequisition.Open(
            Requisitante, null, "Justificação.", "aoa", new DateOnly(2026, 8, 27));

        Assert.Equal("AOA", requisicao.Currency);
    }

    [Fact]
    public void Open_WithoutRequester_Throws()
    {
        // É contra o requisitante que BR-2 é verificada em `approval`. Sem ele,
        // a segregação de funções não teria contra quem verificar.
        Assert.Throws<ArgumentException>(() => PurchaseRequisition.Open(
            Guid.Empty, Departamento, "Justificação.", "AOA", new DateOnly(2026, 8, 27)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_WithoutJustification_Throws(string justification)
    {
        // Sem justificação, quem decide aprova às cegas ou vai perguntar — e a
        // segunda hipótese não deixa rasto nenhum no sistema.
        Assert.Throws<ArgumentException>(() => PurchaseRequisition.Open(
            Requisitante, Departamento, justification, "AOA", new DateOnly(2026, 8, 27)));
    }

    [Theory]
    [InlineData("KZ")]
    [InlineData("KWANZA")]
    [InlineData("")]
    public void Open_WithMalformedCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => PurchaseRequisition.Open(
            Requisitante, Departamento, "Justificação.", currency, new DateOnly(2026, 8, 27)));
    }

    [Fact]
    public void Open_WithoutDepartment_IsAllowed()
    {
        // `EmployeeReference.DepartmentId` é ele próprio anulável: nem todo o
        // colaborador tem departamento. Sem ele aplica-se a política genérica.
        var requisicao = PurchaseRequisition.Open(
            Requisitante, null, "Justificação.", "AOA", new DateOnly(2026, 8, 27));

        Assert.Null(requisicao.DepartmentId);
    }

    [Fact]
    public void EstimatedTotal_SumsTheLines()
    {
        var requisicao = Rascunho();

        requisicao.AddLine("Portátil", 2, 850_000m);
        requisicao.AddLine("Rato sem fios", 2, 12_500m);

        Assert.Equal(1_725_000m, requisicao.EstimatedTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLine_WithNonPositiveQuantity_Throws(decimal quantity)
    {
        var requisicao = Rascunho();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => requisicao.AddLine("Portátil", quantity, 850_000m));
    }

    [Fact]
    public void AddLine_WithZeroPrice_IsAllowed()
    {
        // Há pedidos por cotar: sabe-se o que se quer e ainda não o preço.
        var requisicao = Rascunho();

        var linha = requisicao.AddLine("Portátil, preço por cotar", 2, 0m);

        Assert.Equal(0m, linha.EstimatedTotal);
    }

    [Fact]
    public void AddLine_WithNegativePrice_Throws()
    {
        var requisicao = Rascunho();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => requisicao.AddLine("Portátil", 2, -1m));
    }

    [Fact]
    public void MarkSubmitted_MovesToPendingAndKeepsTheApprovalId()
    {
        var requisicao = Rascunho();
        requisicao.AddLine("Portátil", 2, 850_000m);
        var processo = Guid.CreateVersion7();

        requisicao.MarkSubmitted(processo, Agora);

        Assert.Equal(RequisitionStatus.PendingApproval, requisicao.Status);
        Assert.Equal(processo, requisicao.ApprovalRequestId);
        Assert.Equal(Agora, requisicao.SubmittedAt);
        Assert.False(requisicao.IsEditable);
    }

    [Fact]
    public void MarkSubmitted_WithoutLines_Throws()
    {
        // Uma requisição sem linhas não diz o que se pretende comprar, e não há
        // sobre o que decidir.
        var requisicao = Rascunho();

        Assert.Throws<InvalidOperationException>(
            () => requisicao.MarkSubmitted(Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void MarkSubmitted_Twice_Throws()
    {
        var requisicao = Pendente();

        Assert.Throws<InvalidOperationException>(
            () => requisicao.MarkSubmitted(Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void AddLine_AfterSubmission_Throws()
    {
        // **É a regra que mais custa se falhar.** Acrescentar uma linha depois
        // de submeter mudaria o objecto da decisão debaixo de quem a está a
        // tomar — e o valor que seleccionou a faixa da alçada já foi congelado
        // do lado de `approval` (BR-6).
        var requisicao = Pendente();

        Assert.Throws<InvalidOperationException>(
            () => requisicao.AddLine("Mais um portátil", 1, 850_000m));
    }

    [Fact]
    public void RemoveLine_AfterSubmission_Throws()
    {
        var requisicao = Rascunho();
        var linha = requisicao.AddLine("Portátil", 2, 850_000m);
        requisicao.MarkSubmitted(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(() => requisicao.RemoveLine(linha.Id));
    }

    [Fact]
    public void ChangeJustification_AfterSubmission_Throws()
    {
        var requisicao = Pendente();

        Assert.Throws<InvalidOperationException>(
            () => requisicao.ChangeJustification("Outra razão qualquer."));
    }

    [Fact]
    public void RemoveLine_WhileDraft_RemovesIt()
    {
        var requisicao = Rascunho();
        var linha = requisicao.AddLine("Portátil", 2, 850_000m);
        requisicao.AddLine("Rato", 2, 12_500m);

        requisicao.RemoveLine(linha.Id);

        Assert.Single(requisicao.Lines);
        Assert.Equal(25_000m, requisicao.EstimatedTotal);
    }

    [Fact]
    public void MarkApproved_FromPending_Closes()
    {
        var requisicao = Pendente();

        requisicao.MarkApproved(Agora);

        Assert.Equal(RequisitionStatus.Approved, requisicao.Status);
        Assert.Equal(Agora, requisicao.ClosedAt);
    }

    [Fact]
    public void MarkApproved_FromDraft_Throws()
    {
        // Aprovar sem ter sido submetida saltaria a decisão por inteiro — é o
        // caminho por onde uma compra passaria sem alçada nenhuma.
        var requisicao = Rascunho();
        requisicao.AddLine("Portátil", 2, 850_000m);

        Assert.Throws<InvalidOperationException>(() => requisicao.MarkApproved(Agora));
    }

    [Fact]
    public void MarkApproved_Twice_Throws()
    {
        var requisicao = Pendente();
        requisicao.MarkApproved(Agora);

        Assert.Throws<InvalidOperationException>(() => requisicao.MarkApproved(Agora));
    }

    [Fact]
    public void MarkRefused_KeepsTheReason()
    {
        var requisicao = Pendente();

        requisicao.MarkRefused("Fora do orçamento do trimestre.", Agora);

        Assert.Equal(RequisitionStatus.Refused, requisicao.Status);
        Assert.Equal("Fora do orçamento do trimestre.", requisicao.ClosingReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkRefused_WithoutReason_Throws(string reason)
    {
        // Sem razão, o requisitante não sabe o que corrigir para voltar a pedir.
        var requisicao = Pendente();

        Assert.Throws<ArgumentException>(() => requisicao.MarkRefused(reason, Agora));
    }

    [Fact]
    public void Cancel_WhileDraft_Closes()
    {
        var requisicao = Rascunho();

        requisicao.Cancel("Já não é preciso.", Agora);

        Assert.Equal(RequisitionStatus.Cancelled, requisicao.Status);
        Assert.Equal("Já não é preciso.", requisicao.ClosingReason);
    }

    [Fact]
    public void Cancel_WhilePending_Closes()
    {
        // Desistir de um pedido em curso é legítimo.
        var requisicao = Pendente();

        requisicao.Cancel("Resolvido de outra forma.", Agora);

        Assert.Equal(RequisitionStatus.Cancelled, requisicao.Status);
    }

    [Fact]
    public void Cancel_AfterApproval_Throws()
    {
        // Depois de aprovada já há decisão registada, e desfazê-la aqui apagaria
        // a decisão de outra pessoa. O que se cancela nesse ponto é a Ordem de
        // Compra, não a requisição.
        var requisicao = Pendente();
        requisicao.MarkApproved(Agora);

        Assert.Throws<InvalidOperationException>(() => requisicao.Cancel("Desisti.", Agora));
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var requisicao = Rascunho();
        requisicao.Cancel("Já não é preciso.", Agora);

        Assert.Throws<InvalidOperationException>(() => requisicao.Cancel("Outra vez.", Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutReason_Throws(string reason)
    {
        var requisicao = Rascunho();

        Assert.Throws<ArgumentException>(() => requisicao.Cancel(reason, Agora));
    }

    [Fact]
    public void Version_IsNeverTouchedByTheDomain()
    {
        var requisicao = Pendente();
        requisicao.MarkApproved(Agora);

        Assert.Equal(0, requisicao.Version);
    }
}
