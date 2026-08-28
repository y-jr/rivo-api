namespace Rivo.Finance.Domain;

/// <summary>
/// Conta bancária. É onde vive a **disponibilidade de tesouraria** que BR-5
/// manda verificar antes de qualquer execução de pagamento.
///
/// <para>
/// <strong>O saldo é o ponto de contenção do sistema.</strong> Duas execuções
/// simultâneas sobre a mesma conta competem por ele, e o contador de
/// concorrência faz uma perder com <c>409</c> em vez de as duas passarem com o
/// mesmo saldo lido. É o caso concreto que BR-17 nomeia — "concorrência
/// optimista na execução de pagamento" — e não uma precaução genérica.
/// </para>
/// </summary>
public sealed class BankAccount
{
    private readonly List<BankMovement> _movements = [];

    private BankAccount(Guid id, string name, string bank, string? iban, string currency)
    {
        Id = id;
        Name = name;
        Bank = bank;
        Iban = iban;
        Currency = currency;
        Balance = 0m;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private BankAccount()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Bank { get; private set; }

    public string? Iban { get; private set; }

    /// <summary>
    /// ISO 4217. Multi-moeda por conta, não por movimento: uma conta em AOA não
    /// paga em USD, e converter no acto esconderia o câmbio aplicado
    /// (`modules/finance.md`).
    /// </summary>
    public string Currency { get; private set; }

    /// <summary>
    /// Disponível. Pode ser negativo — uma conta a descoberto é um facto, e
    /// impedir o registo dele não faz o descoberto desaparecer. O que se impede
    /// é **criar** um descoberto ao pagar, em <see cref="Withdraw"/>.
    /// </summary>
    public decimal Balance { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025, BR-17). <strong>Aqui é a regra, não
    /// formalidade.</strong> O domínio nunca lhe toca.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// O extracto.
    ///
    /// <para>
    /// <strong>Nunca é carregada no caminho de escrita</strong>, e é de
    /// propósito: acrescentar um movimento não obriga a ler os anteriores, por
    /// isso pagar numa conta com dez anos de histórico custa o mesmo que numa
    /// conta nova. Quem lê o extracto lê-o pelo store, com filtro de datas.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<BankMovement> Movements => _movements;

    public static BankAccount Open(string name, string bank, string? iban, string currency)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Uma conta precisa de designação.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(bank))
        {
            throw new ArgumentException("Uma conta precisa do banco onde está.", nameof(bank));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217.", nameof(currency));
        }

        return new BankAccount(
            Guid.CreateVersion7(),
            name.Trim(),
            bank.Trim(),
            string.IsNullOrWhiteSpace(iban) ? null : iban.Replace(" ", string.Empty).Trim().ToUpperInvariant(),
            currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Entrada de fundos — carregamento, transferência recebida.
    ///
    /// <para>
    /// <strong>O movimento nasce aqui, e não em quem chama.</strong> Saldo e
    /// extracto alteram-se no mesmo acto, ou não se alteram: um chamador que se
    /// esquecesse de registar o movimento deixaria o extracto a mentir em
    /// silêncio, e ninguém daria por isso até à primeira reconciliação.
    /// </para>
    /// </summary>
    public BankMovement Deposit(decimal amount, DateTimeOffset at, string? description)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Um depósito é maior que zero.");
        }

        EnsureActive();
        Balance += amount;

        return Register(
            at, BankMovementDirection.Credit, amount,
            description ?? "Carregamento de conta", sourceType: null, sourceId: null);
    }

    /// <summary>
    /// Saída de fundos.
    ///
    /// <para>
    /// <strong>Recusa se não houver saldo.</strong> É a metade "saldo" da dupla
    /// barreira de BR-5 — a outra é o estado da decisão, e vive na camada
    /// Application porque depende de `approval`.
    /// </para>
    /// </summary>
    public BankMovement Withdraw(
        decimal amount,
        DateTimeOffset at,
        string description,
        string? sourceType = null,
        Guid? sourceId = null)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Uma saída é maior que zero.");
        }

        EnsureActive();

        if (amount > Balance)
        {
            throw new InsufficientFundsException(
                $"A conta {Name} tem {Balance:N2} {Currency} e o pagamento é de {amount:N2}.");
        }

        Balance -= amount;

        return Register(at, BankMovementDirection.Debit, amount, description, sourceType, sourceId);
    }

    /// <summary>
    /// Fecha a conta. Não elimina: os pagamentos executados por ela continuam a
    /// referenciá-la, e o histórico tem de continuar legível (BR-14).
    ///
    /// <para>
    /// <strong>Só com saldo zero.</strong> Fechar uma conta com dinheiro dentro
    /// esconderia esse dinheiro atrás de uma conta que diz não estar em uso — e
    /// é invariante de uma conta só, por isso vive aqui e não na camada
    /// Application. Reabrir não move saldo nenhum; é só o que estava lá.
    /// </para>
    /// </summary>
    public void Close()
    {
        if (Balance != 0m)
        {
            throw new InvalidOperationException(
                $"A conta {Name} tem saldo de {Balance:N2} {Currency}. Só se fecha com saldo zero — " +
                "transfira ou levante o que resta primeiro.");
        }

        IsActive = false;
    }

    public void Reopen()
    {
        IsActive = true;
    }

    private BankMovement Register(
        DateTimeOffset at,
        BankMovementDirection direction,
        decimal amount,
        string description,
        string? sourceType,
        Guid? sourceId)
    {
        // `Balance` já está actualizado quando isto corre — é o saldo *depois*
        // que a linha do extracto congela.
        var movimento = BankMovement.Record(
            Id, at, direction, amount, Balance, description, sourceType, sourceId);

        _movements.Add(movimento);

        return movimento;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException($"A conta {Name} está fechada.");
        }
    }
}

/// <summary>
/// Saldo insuficiente para executar um pagamento.
///
/// <para>
/// Excepção própria e não um <c>InvalidOperationException</c> qualquer: é
/// metade de BR-5, e a fronteira HTTP tem de a distinguir de um erro de estado
/// para dizer a quem paga *porque* é que não pôde.
/// </para>
/// </summary>
public sealed class InsufficientFundsException(string message) : InvalidOperationException(message);
