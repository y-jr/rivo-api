namespace Rivo.Finance.Domain;

/// <summary>
/// Um movimento numa conta bancária. É a linha do extracto.
///
/// <para>
/// <strong>Existe porque o saldo sozinho não é reconciliável.</strong> Uma
/// conta com <c>86.000,00</c> não diz como lá chegou, e a reconciliação
/// bancária — confrontar o que o Rivo diz com o que o banco diz — é uma
/// comparação entre *movimentos*, não entre saldos. Sem esta entidade, a única
/// resposta a "porque é que o saldo é este?" seria ler a trilha de auditoria,
/// que não é feita para isso.
/// </para>
///
/// <para>
/// <strong>Append-only, e não por convenção.</strong> Um extracto que se pode
/// editar não vale para reconciliar nada — a mesma razão que levou a trilha de
/// auditoria a ser imposta pelo motor (K9). Sem setters públicos em código, e
/// com gatilho a recusar <c>UPDATE</c> e <c>DELETE</c> na base de dados.
/// </para>
/// </summary>
public sealed class BankMovement
{
    private BankMovement(
        Guid bankAccountId,
        DateTimeOffset occurredAt,
        BankMovementDirection direction,
        decimal amount,
        decimal balanceAfter,
        string description,
        string? sourceType,
        Guid? sourceId)
    {
        // Guid v7 é ordenado no tempo, e isso não é detalhe: dois movimentos no
        // mesmo instante ordenam-se na mesma ordem em que nasceram, e um
        // extracto sem ordem estável não é um extracto.
        Id = Guid.CreateVersion7();
        BankAccountId = bankAccountId;
        OccurredAt = occurredAt;
        Direction = direction;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Description = description;
        SourceType = sourceType;
        SourceId = sourceId;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private BankMovement()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid BankAccountId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public BankMovementDirection Direction { get; private set; }

    /// <summary>Sempre positivo. O sentido está em <see cref="Direction"/>.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// O saldo <strong>depois</strong> deste movimento, congelado.
    ///
    /// <para>
    /// Guardado e não recalculado ao ler. É o que distingue um extracto de uma
    /// soma: se um dia o saldo da conta divergir da soma dos movimentos, é esta
    /// coluna que mostra onde e quando divergiu. Recalcular ao ler faria os dois
    /// baterem sempre — inclusive quando não devem.
    /// </para>
    /// </summary>
    public decimal BalanceAfter { get; private set; }

    public string Description { get; private set; }

    /// <summary>
    /// O que originou o movimento — <c>"payment_request"</c>, ou nulo num
    /// carregamento manual.
    ///
    /// <para>
    /// Um par texto/identificador em vez de uma chave estrangeira: a origem
    /// pode vir a ser de outro contexto interno, e uma FK obrigaria a tabela do
    /// extracto a conhecer todas as origens possíveis de antemão.
    /// </para>
    /// </summary>
    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    /// <summary>
    /// Só <see cref="BankAccount"/> cria movimentos, e apenas ao mexer no saldo.
    /// É o que impede um movimento sem alteração de saldo — ou o contrário.
    /// </summary>
    internal static BankMovement Record(
        Guid bankAccountId,
        DateTimeOffset occurredAt,
        BankMovementDirection direction,
        decimal amount,
        decimal balanceAfter,
        string description,
        string? sourceType,
        Guid? sourceId) =>
        new(bankAccountId, occurredAt, direction, amount, balanceAfter,
            string.IsNullOrWhiteSpace(description) ? "Movimento" : description.Trim(),
            sourceType, sourceId);
}

/// <summary>
/// Origens conhecidas de um movimento.
///
/// <para>
/// Constantes e não <c>enum</c>: <see cref="BankMovement.SourceType"/> é texto
/// livre de propósito, para que uma origem nova não obrigue a migrar a coluna.
/// Isto é a lista do que já existe, para quem lê não a escrever à mão.
/// </para>
/// </summary>
public static class BankMovementSources
{
    public const string PaymentRequest = "payment_request";
}

public enum BankMovementDirection
{
    /// <summary>Entrada.</summary>
    Credit,

    /// <summary>Saída.</summary>
    Debit,
}
