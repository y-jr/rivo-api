namespace Rivo.Hr.Domain;

/// <summary>
/// Um episódio da relação entre uma conta de `identity` e um Colaborador
/// (ADR-053).
///
/// <para>
/// <strong>Não substitui <c>Employee.UserId</c>, acompanha-o.</strong> O campo
/// continua a ser o vínculo <em>activo</em>, e é ele que
/// <c>FindEmployeeByUserIdAsync</c> lê — a única consulta que decide quem pode
/// aprovar desde o ADR-050. Pôr um filtro nessa consulta seria arriscar, no
/// sítio mais sensível do sistema, a mesma classe de falha que o ADR-050
/// corrigiu. Esta entidade é história, não estado.
/// </para>
///
/// <para>
/// Existe porque o ADR-050 tornou forense a pergunta «que conta podia agir por
/// esta pessoa no dia D». Sem isto, a resposta só se reconstruía com
/// <c>LIKE</c> sobre o JSON da trilha de auditoria.
/// </para>
///
/// <para>
/// Episódios fechados nunca se alteram nem se apagam: é o registo de quem pôde
/// agir por quem, e é precisamente o que não pode desaparecer (BR-14, no
/// espírito — a letra fala de entidades sob retenção legal, e esta ainda não é
/// uma delas).
/// </para>
/// </summary>
public sealed class EmployeeAccountLink
{
    private EmployeeAccountLink() { }

    private EmployeeAccountLink(
        Guid id,
        Guid employeeId,
        Guid userId,
        DateTimeOffset linkedOn,
        Guid? linkedByUserId)
    {
        Id = id;
        EmployeeId = employeeId;
        UserId = userId;
        LinkedOn = linkedOn;
        LinkedByUserId = linkedByUserId;
    }

    public Guid Id { get; private set; }

    /// <summary>Concorrência optimista (ADR-002, ADR-025).</summary>
    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset LinkedOn { get; private set; }

    /// <summary>
    /// Quem ordenou a ligação. <strong>Nulo significa desconhecido</strong>, e
    /// não «ninguém»: os episódios criados pela migração de retroactivo não
    /// têm autor, porque o vínculo existia antes de haver quem o registasse.
    /// </summary>
    public Guid? LinkedByUserId { get; private set; }

    /// <summary>Nulo enquanto o episódio estiver aberto.</summary>
    public DateTimeOffset? UnlinkedOn { get; private set; }

    public Guid? UnlinkedByUserId { get; private set; }

    public bool IsOpen => UnlinkedOn is null;

    public static EmployeeAccountLink Open(
        Guid employeeId,
        Guid userId,
        DateTimeOffset linkedOn,
        Guid? linkedByUserId) =>
        new(Guid.CreateVersion7(), employeeId, userId, linkedOn, linkedByUserId);

    /// <summary>
    /// Fecha o episódio.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Se já estiver fechado. Reabrir ou refechar um episódio reescreveria
    /// história — e a história de quem pôde aprovar em nome de quem é o que
    /// esta entidade existe para não deixar reescrever.
    /// </exception>
    public void Close(DateTimeOffset unlinkedOn, Guid? unlinkedByUserId)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException(
                "Este episódio de ligação já está fechado.");
        }

        // Fechar antes de abrir seria um intervalo negativo, e a consulta
        // forense passaria a poder devolver "podia agir" para um instante em
        // que não podia.
        if (unlinkedOn < LinkedOn)
        {
            throw new InvalidOperationException(
                "Não se pode desligar antes de ligar.");
        }

        UnlinkedOn = unlinkedOn;
        UnlinkedByUserId = unlinkedByUserId;
    }

    /// <summary>
    /// Se este episódio cobria o instante indicado — a pergunta forense que
    /// justifica a entidade existir.
    ///
    /// <para>
    /// Intervalo fechado no início e aberto no fim: no instante exacto em que
    /// se desliga, já não se podia agir.
    /// </para>
    /// </summary>
    public bool CobriaEm(DateTimeOffset instante) =>
        instante >= LinkedOn && (UnlinkedOn is null || instante < UnlinkedOn);
}
