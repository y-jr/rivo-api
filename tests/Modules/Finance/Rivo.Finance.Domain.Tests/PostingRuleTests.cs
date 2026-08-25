using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// A regra de postagem, e a invariante que a torna útil.
///
/// <para>
/// <strong>A regra equilibra enquanto expressão, não para os números que se
/// testaram.</strong> Cada linha diz de que parcela do documento se serve, e
/// essas parcelas somam-se simbolicamente: `total = líquido + imposto`. Se os
/// dois lados não derem a mesma expressão, a regra é recusada na configuração —
/// e a partir daí <em>nenhum</em> documento pode produzir um lançamento
/// desequilibrado.
/// </para>
/// </summary>
public class PostingRuleTests
{
    private static PostingRule Regra(params NewPostingRuleLine[] linhas) =>
        PostingRule.Define(PostingEvent.SalesInvoiceIssued, "VND", "Vendas", linhas);

    private static NewPostingRuleLine Linha(
        string conta, EntrySide lado, PostingAmount parcela) =>
        new(conta, lado, parcela, "Linha");

    // ---- o que equilibra ----

    /// <summary>
    /// O lançamento de venda: debita o total ao cliente, credita o proveito e o
    /// imposto. `(1,1)` de um lado, `(1,0) + (0,1)` do outro.
    /// </summary>
    [Fact]
    public void FacturaDeVenda_Equilibra()
    {
        var regra = Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Net),
            Linha("3431", EntrySide.Credit, PostingAmount.Tax));

        Assert.Equal(3, regra.Lines.Count);
        Assert.True(regra.IsActive);
    }

    /// <summary>
    /// A factura de compra é o espelho: custo e imposto dedutível a débito,
    /// dívida ao fornecedor a crédito.
    /// </summary>
    [Fact]
    public void FacturaDeCompra_Equilibra()
    {
        var regra = PostingRule.Define(
            PostingEvent.PurchaseInvoiceRegistered, "CMP", "Compras",
            [
                Linha("6111", EntrySide.Debit, PostingAmount.Net),
                Linha("3432", EntrySide.Debit, PostingAmount.Tax),
                Linha("2211", EntrySide.Credit, PostingAmount.Gross),
            ]);

        Assert.Equal(PostingEvent.PurchaseInvoiceRegistered, regra.Event);
    }

    /// <summary>
    /// Recibo e pagamento não têm imposto a separar — o total contra o total.
    /// </summary>
    [Fact]
    public void RecebimentoEPagamento_EquilibramTotalContraTotal()
    {
        PostingRule.Define(
            PostingEvent.ReceiptRegistered, "TES", "Tesouraria",
            [
                Linha("1211", EntrySide.Debit, PostingAmount.Gross),
                Linha("2111", EntrySide.Credit, PostingAmount.Gross),
            ]);

        PostingRule.Define(
            PostingEvent.PaymentExecuted, "TES", "Tesouraria",
            [
                Linha("2211", EntrySide.Debit, PostingAmount.Gross),
                Linha("1211", EntrySide.Credit, PostingAmount.Gross),
            ]);
    }

    /// <summary>
    /// Líquido e imposto dos dois lados também equilibram — não é preciso que
    /// um dos lados use `Gross`.
    /// </summary>
    [Fact]
    public void LiquidoEImpostoDosDoisLados_Equilibram()
    {
        Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Net),
            Linha("2112", EntrySide.Debit, PostingAmount.Tax),
            Linha("7111", EntrySide.Credit, PostingAmount.Net),
            Linha("3431", EntrySide.Credit, PostingAmount.Tax));
    }

    // ---- o que não equilibra ----

    /// <summary>
    /// <strong>O caso que a invariante existe para apanhar.</strong> Debitar o
    /// total e creditar só o líquido esquece o imposto: equilibraria numa
    /// factura isenta e falharia em todas as outras — o pior tipo de defeito,
    /// porque passa nos testes fáceis.
    /// </summary>
    [Fact]
    public void TotalContraLiquido_ERecusado()
    {
        var erro = Assert.Throws<UnbalancedPostingRuleException>(() => Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Net)));

        Assert.Contains("não equilibra", erro.Message);
    }

    [Fact]
    public void LiquidoContraImposto_ERecusado()
    {
        Assert.Throws<UnbalancedPostingRuleException>(() => Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Net),
            Linha("3431", EntrySide.Credit, PostingAmount.Tax)));
    }

    /// <summary>
    /// Duas linhas de líquido de um lado contra uma do outro duplicaria o
    /// valor. A contagem de coeficientes apanha-o.
    /// </summary>
    [Fact]
    public void ParcelaRepetidaDeUmLado_ERecusada()
    {
        Assert.Throws<UnbalancedPostingRuleException>(() => Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Net),
            Linha("2112", EntrySide.Debit, PostingAmount.Net),
            Linha("7111", EntrySide.Credit, PostingAmount.Net)));
    }

    [Fact]
    public void SoDebitos_ERecusado()
    {
        var erro = Assert.Throws<UnbalancedPostingRuleException>(() => Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross)));

        Assert.Contains("crédito", erro.Message);
    }

    [Fact]
    public void SoCreditos_ERecusado()
    {
        var erro = Assert.Throws<UnbalancedPostingRuleException>(() => Regra(
            Linha("7111", EntrySide.Credit, PostingAmount.Gross)));

        Assert.Contains("débito", erro.Message);
    }

    // ---- forma ----

    [Fact]
    public void RegraSemLinhas_ERecusada()
    {
        Assert.Throws<ArgumentException>(
            () => PostingRule.Define(PostingEvent.SalesInvoiceIssued, "VND", "Vendas", []));
    }

    [Fact]
    public void RegraSemDiario_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "  ", "Vendas",
            [Linha("2111", EntrySide.Debit, PostingAmount.Gross)]));
    }

    [Fact]
    public void LinhaSemConta_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => Regra(
            new NewPostingRuleLine("  ", EntrySide.Debit, PostingAmount.Gross, "Linha"),
            Linha("7111", EntrySide.Credit, PostingAmount.Gross)));
    }

    [Fact]
    public void CodigosSaoNormalizados()
    {
        var regra = Regra(
            Linha(" 2111 ", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Gross));

        Assert.Equal("2111", regra.Lines[0].AccountCode);
        Assert.Equal("VND", regra.JournalCode);
    }

    [Fact]
    public void LinhasSaoNumeradasPelaOrdemDada()
    {
        var regra = Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Net),
            Linha("3431", EntrySide.Credit, PostingAmount.Tax));

        Assert.Equal([1, 2, 3], regra.Lines.Select(l => l.LineNumber));
    }

    /// <summary>
    /// Desactivar pára a tradução dos documentos futuros. Os lançamentos que a
    /// regra produziu ficam — são factos, não configuração.
    /// </summary>
    [Fact]
    public void DesactivarNaoApagaAsLinhas()
    {
        var regra = Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Gross));

        regra.Deactivate();

        Assert.False(regra.IsActive);
        Assert.Equal(2, regra.Lines.Count);
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDaRegra()
    {
        var regra = Regra(
            Linha("2111", EntrySide.Debit, PostingAmount.Gross),
            Linha("7111", EntrySide.Credit, PostingAmount.Gross));

        regra.Deactivate();

        Assert.Equal(0, regra.Version);
    }
}
