namespace Rivo.Commercial.Domain;

/// <summary>
/// Um episódio da relação entre uma conta de `identity` e um Cliente
/// (ADR-055).
///
/// <para>
/// Mesmo desenho de <c>EmployeeAccountLink</c> em `hr` (ADR-053):
/// <c>Customer.UserId</c> continua a ser o vínculo <strong>activo</strong> e o
/// que o Portal do Cliente lê para resolver «o próprio»; isto é história.
/// </para>
///
/// <para>
/// <strong>Duplicado de propósito, e não partilhado.</strong> São bounded
/// contexts distintos: um episódio de `hr` liga uma conta a quem trabalha na
/// empresa, um de `commercial` liga-a a quem lhe compra. Pô-los no
/// SharedKernel por terem a mesma forma seria acoplar dois domínios por
/// coincidência de estrutura — exactamente o que a regra do SharedKernel mínimo
/// existe para evitar.
/// </para>
/// </summary>
public sealed class CustomerAccountLink
{
    private CustomerAccountLink() { }

    private CustomerAccountLink(
        Guid id,
        Guid customerId,
        Guid userId,
        DateTimeOffset linkedOn,
        Guid? linkedByUserId)
    {
        Id = id;
        CustomerId = customerId;
        UserId = userId;
        LinkedOn = linkedOn;
        LinkedByUserId = linkedByUserId;
    }

    public Guid Id { get; private set; }

    /// <summary>Concorrência optimista (ADR-002, ADR-025).</summary>
    public int Version { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset LinkedOn { get; private set; }

    /// <summary>
    /// Quem ordenou a ligação. <strong>Nulo é desconhecido</strong>, não
    /// «ninguém»: os episódios criados pela migração de retroactivo não têm
    /// autor, porque o vínculo existia antes de haver quem o registasse.
    /// </summary>
    public Guid? LinkedByUserId { get; private set; }

    /// <summary>Nulo enquanto o episódio estiver aberto.</summary>
    public DateTimeOffset? UnlinkedOn { get; private set; }

    public Guid? UnlinkedByUserId { get; private set; }

    public bool IsOpen => UnlinkedOn is null;

    public static CustomerAccountLink Open(
        Guid customerId,
        Guid userId,
        DateTimeOffset linkedOn,
        Guid? linkedByUserId) =>
        new(Guid.CreateVersion7(), customerId, userId, linkedOn, linkedByUserId);

    /// <exception cref="InvalidOperationException">
    /// Se já estiver fechado, ou se o fecho for anterior à abertura. Reescrever
    /// um episódio apagaria o registo de quem pôde agir como aquele cliente —
    /// e submeter comprovativos de pagamento em nome dele.
    /// </exception>
    public void Close(DateTimeOffset unlinkedOn, Guid? unlinkedByUserId)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Este episódio de ligação já está fechado.");
        }

        if (unlinkedOn < LinkedOn)
        {
            throw new InvalidOperationException("Não se pode desligar antes de ligar.");
        }

        UnlinkedOn = unlinkedOn;
        UnlinkedByUserId = unlinkedByUserId;
    }

    /// <summary>
    /// Se este episódio cobria o instante indicado. Fechado no início e aberto
    /// no fim: no instante exacto do desligamento já não se podia agir.
    /// </summary>
    public bool CobriaEm(DateTimeOffset instante) =>
        instante >= LinkedOn && (UnlinkedOn is null || instante < UnlinkedOn);
}
