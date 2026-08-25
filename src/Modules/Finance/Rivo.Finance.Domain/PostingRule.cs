namespace Rivo.Finance.Domain;

/// <summary>
/// Como um documento se traduz em lançamento contabilístico.
///
/// <para>
/// <strong>Tem de ser configuração, pela mesma razão que o plano de contas
/// é.</strong> O Rivo não sabe que conta é "Clientes" nem que conta é "IVA
/// liquidado" — esses códigos vêm do plano que o cliente carregou (ADR-037).
/// Uma tradução embutida em código teria de inventar o plano, que é
/// exactamente o que se recusou a fazer.
/// </para>
///
/// <para>
/// <strong>A regra equilibra por construção, e isso é verificável antes de
/// haver documento nenhum.</strong> Cada linha diz de que parcela do documento
/// se serve — líquido, imposto, ou total — e essas parcelas somam-se
/// simbolicamente: `total = líquido + imposto`. Se a soma dos débitos não for
/// igual à soma dos créditos <em>enquanto expressão</em>, a regra é recusada na
/// configuração. Depois disso, nenhum documento pode produzir um lançamento
/// desequilibrado — não porque se verifique a cada postagem, mas porque a regra
/// que a gera não o permite.
/// </para>
/// </summary>
public sealed class PostingRule
{
    private readonly List<PostingRuleLine> _lines = [];

    private PostingRule(PostingEvent postingEvent, string journalCode, string description)
    {
        Id = Guid.CreateVersion7();
        Event = postingEvent;
        JournalCode = journalCode;
        Description = description;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PostingRule()
    {
        JournalCode = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Que acontecimento a dispara. Uma regra activa por acontecimento.</summary>
    public PostingEvent Event { get; private set; }

    /// <summary>Em que diário lança. Congelado no lançamento que produzir.</summary>
    public string JournalCode { get; private set; }

    public string Description { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<PostingRuleLine> Lines => _lines;

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    public static PostingRule Define(
        PostingEvent postingEvent,
        string journalCode,
        string description,
        IReadOnlyList<NewPostingRuleLine> lines)
    {
        if (string.IsNullOrWhiteSpace(journalCode))
        {
            throw new ArgumentException("Uma regra de postagem diz em que diário lança.", nameof(journalCode));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Uma regra de postagem precisa de descrição.", nameof(description));
        }

        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("Uma regra de postagem tem linhas.", nameof(lines));
        }

        var regra = new PostingRule(
            postingEvent, journalCode.Trim().ToUpperInvariant(), description.Trim());

        var numero = 1;

        foreach (var linha in lines)
        {
            regra._lines.Add(PostingRuleLine.From(regra.Id, numero++, linha));
        }

        regra.EnsureBalances();

        return regra;
    }

    /// <summary>
    /// Verifica que a regra equilibra <strong>simbolicamente</strong>, somando
    /// os coeficientes de líquido e de imposto de cada lado.
    ///
    /// <para>
    /// `Net` conta (1, 0), `Tax` conta (0, 1) e `Gross` conta (1, 1) — porque o
    /// total é o líquido mais o imposto. Uma factura que debita o total e
    /// credita líquido e imposto dá (1,1) contra (1,1): equilibra para
    /// <em>qualquer</em> factura, e não só para a que se testou.
    /// </para>
    /// </summary>
    private void EnsureBalances()
    {
        var (debitoLiquido, debitoImposto) = Coefficients(EntrySide.Debit);
        var (creditoLiquido, creditoImposto) = Coefficients(EntrySide.Credit);

        if (debitoLiquido == 0 && debitoImposto == 0)
        {
            throw new UnbalancedPostingRuleException(
                "Uma regra de postagem tem pelo menos uma linha a débito.");
        }

        if (creditoLiquido == 0 && creditoImposto == 0)
        {
            throw new UnbalancedPostingRuleException(
                "Uma regra de postagem tem pelo menos uma linha a crédito.");
        }

        if (debitoLiquido != creditoLiquido || debitoImposto != creditoImposto)
        {
            throw new UnbalancedPostingRuleException(
                $"A regra não equilibra: a débito {Describe(debitoLiquido, debitoImposto)}, " +
                $"a crédito {Describe(creditoLiquido, creditoImposto)}. " +
                "Um lançamento gerado por ela nunca equilibraria.");
        }
    }

    private (int Net, int Tax) Coefficients(EntrySide side)
    {
        var liquido = 0;
        var imposto = 0;

        foreach (var linha in _lines.Where(l => l.Side == side))
        {
            switch (linha.Amount)
            {
                case PostingAmount.Net:
                    liquido++;
                    break;

                case PostingAmount.Tax:
                    imposto++;
                    break;

                case PostingAmount.Gross:
                    liquido++;
                    imposto++;
                    break;
            }
        }

        return (liquido, imposto);
    }

    private static string Describe(int net, int tax) =>
        $"{net} x líquido + {tax} x imposto";

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

public sealed class PostingRuleLine
{
    private PostingRuleLine(
        Guid postingRuleId,
        int lineNumber,
        string accountCode,
        EntrySide side,
        PostingAmount amount,
        string description)
    {
        Id = Guid.CreateVersion7();
        PostingRuleId = postingRuleId;
        LineNumber = lineNumber;
        AccountCode = accountCode;
        Side = side;
        Amount = amount;
        Description = description;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PostingRuleLine()
    {
        AccountCode = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid PostingRuleId { get; private set; }

    public int LineNumber { get; private set; }

    /// <summary>
    /// O código da conta no plano carregado. <strong>Não é validado aqui</strong>
    /// — a regra não vê o plano. Quem a define é que confirma que a conta existe
    /// e é de movimento.
    /// </summary>
    public string AccountCode { get; private set; }

    public EntrySide Side { get; private set; }

    /// <summary>De que parcela do documento esta linha se serve.</summary>
    public PostingAmount Amount { get; private set; }

    public string Description { get; private set; }

    internal static PostingRuleLine From(Guid ruleId, int lineNumber, NewPostingRuleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line.AccountCode))
        {
            throw new ArgumentException("A linha da regra diz em que conta lança.", nameof(line));
        }

        if (string.IsNullOrWhiteSpace(line.Description))
        {
            throw new ArgumentException("A linha da regra tem descrição.", nameof(line));
        }

        return new PostingRuleLine(
            ruleId,
            lineNumber,
            line.AccountCode.Trim().ToUpperInvariant(),
            line.Side,
            line.Amount,
            line.Description.Trim());
    }
}

public sealed record NewPostingRuleLine(
    string AccountCode,
    EntrySide Side,
    PostingAmount Amount,
    string Description);

/// <summary>
/// De que parcela do documento uma linha da regra se serve.
///
/// <para>
/// São três e não mais: é o que basta para exprimir os lançamentos de venda, de
/// compra, de recebimento e de pagamento, e é o que permite verificar o
/// equilíbrio simbolicamente. Num recibo ou num pagamento não há imposto
/// separado — o líquido é o total e o imposto é zero, e os três valores
/// coincidem.
/// </para>
/// </summary>
public enum PostingAmount
{
    /// <summary>Base tributável.</summary>
    Net,

    /// <summary>Imposto.</summary>
    Tax,

    /// <summary>Líquido mais imposto.</summary>
    Gross,
}

/// <summary>
/// Os acontecimentos que podem gerar lançamento.
///
/// <para>
/// Enumerados e não texto livre: cada um corresponde a um ponto concreto do
/// código que chama a postagem, e um valor que ninguém consome seria uma regra
/// configurada que nunca dispara.
/// </para>
/// </summary>
public enum PostingEvent
{
    /// <summary>Factura de venda emitida. Dívida do cliente e proveito.</summary>
    SalesInvoiceIssued,

    /// <summary>Nota de crédito emitida. O inverso da factura.</summary>
    CreditNoteIssued,

    /// <summary>Recibo registado. Entra dinheiro, a dívida do cliente baixa.</summary>
    ReceiptRegistered,

    /// <summary>Factura de compra registada. Custo e dívida ao fornecedor.</summary>
    PurchaseInvoiceRegistered,

    /// <summary>Pagamento executado. Sai dinheiro, a dívida ao fornecedor baixa.</summary>
    PaymentExecuted,
}

/// <summary>
/// A regra não equilibra enquanto expressão.
///
/// <para>
/// Excepção própria porque é recusa na <strong>configuração</strong>, não na
/// postagem: nenhum documento chegou a existir, e quem a vê está a definir a
/// regra, não a emitir nada.
/// </para>
/// </summary>
public sealed class UnbalancedPostingRuleException(string message)
    : InvalidOperationException(message);
