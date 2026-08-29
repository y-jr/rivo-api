namespace Rivo.Approval.Domain;

/// <summary>
/// Pedido de Aprovação — um processo concreto em curso.
///
/// <para>
/// <strong>É aqui que a segregação de funções é imposta</strong>, e o ADR-008
/// é explícito quanto a isso: a sede das invariantes é o domínio `approval`,
/// em código, testada ao nível do domínio. Uma regra que só existisse em SQL
/// seria defeito de arquitectura.
/// </para>
///
/// <para>
/// <strong>Não possui a transacção de negócio que aprova.</strong> Guarda uma
/// referência opaca à origem — módulo e identificador — e não a interpreta.
/// `approval` não sabe o que é uma atribuição de cargo; sabe que alguém pediu
/// para decidir sobre ela.
/// </para>
/// </summary>
public sealed class ApprovalRequest
{
    private readonly List<Assignment> _assignments = [];
    private readonly List<Decision> _decisions = [];

    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private ApprovalRequest()
    {
        ProcessType = string.Empty;
        SourceModule = string.Empty;
        SourceReference = string.Empty;
    }

    private ApprovalRequest(
        Guid id,
        string processType,
        string sourceModule,
        string sourceReference,
        Guid requestedByEmployeeId,
        decimal? amount,
        string? currency,
        Guid? departmentId,
        Guid appliedPolicyId,
        string? summary,
        DateTimeOffset submittedAt)
    {
        Id = id;
        ProcessType = processType;
        SourceModule = sourceModule;
        SourceReference = sourceReference;
        RequestedByEmployeeId = requestedByEmployeeId;
        Amount = amount;
        Currency = currency;
        DepartmentId = departmentId;
        AppliedPolicyId = appliedPolicyId;
        Summary = summary;
        SubmittedAt = submittedAt;
        Status = ApprovalStatus.InProgress;
        CurrentStep = 1;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Concorrência optimista (BR-17). Duas decisões simultâneas sobre o mesmo
    /// pedido — uma perde, em vez de as duas escreverem por cima uma da outra.
    /// </summary>
    public int Version { get; private set; }

    public string ProcessType { get; private set; }

    public string SourceModule { get; private set; }

    public string SourceReference { get; private set; }

    public Guid RequestedByEmployeeId { get; private set; }

    public decimal? Amount { get; private set; }

    public string? Currency { get; private set; }

    public Guid? DepartmentId { get; private set; }

    /// <summary>
    /// A política aplicada, <strong>para rasto e não como chave estrangeira
    /// viva</strong>.
    ///
    /// <para>
    /// O que manda neste processo são as <see cref="Assignments"/> já
    /// congeladas. A política pode ser alterada ou desactivada amanhã sem que
    /// isso mexa num processo em curso — que é precisamente o que BR-6 exige e
    /// o que `modules/approval.md` proíbe fazer de outra forma.
    /// </para>
    /// </summary>
    public Guid AppliedPolicyId { get; private set; }

    public string? Summary { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public ApprovalStatus Status { get; private set; }

    /// <summary>Passo em curso. Sem significado depois de o processo fechar.</summary>
    public int CurrentStep { get; private set; }

    public IReadOnlyList<Assignment> Assignments => _assignments;

    /// <summary>Decisões tomadas, por ordem. <strong>Imutáveis</strong> — acrescentam-se, nunca se alteram.</summary>
    public IReadOnlyList<Decision> Decisions => _decisions;

    /// <summary>
    /// Submete um pedido, congelando os aprovadores resolvidos (BR-6, BR-19).
    /// </summary>
    /// <param name="resolvedApprovers">
    /// Para cada passo da política, os Colaboradores que ocupam o Cargo
    /// <strong>naquele momento</strong>. Resolvidos por quem chama, através do
    /// contrato de `hr`, e nunca mais recalculados.
    /// </param>
    /// <exception cref="ArgumentException">Quando não há aprovadores resolvidos.</exception>
    public static ApprovalRequest Submit(
        string processType,
        string sourceModule,
        string sourceReference,
        Guid requestedByEmployeeId,
        decimal? amount,
        string? currency,
        Guid? departmentId,
        ApprovalPolicy policy,
        IReadOnlyList<ResolvedStep> resolvedApprovers,
        DateTimeOffset submittedAt,
        string? summary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(resolvedApprovers);

        if (requestedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException("Um pedido tem sempre requisitante.", nameof(requestedByEmployeeId));
        }

        if (resolvedApprovers.Count == 0 || resolvedApprovers.All(s => s.ApproverEmployeeIds.Count == 0))
        {
            // Um processo sem aprovadores nunca sairia de pendente, e ficaria
            // em silêncio à espera de alguém que não existe.
            throw new ArgumentException(
                "Nenhum aprovador foi resolvido para esta política.",
                nameof(resolvedApprovers));
        }

        var request = new ApprovalRequest(
            Guid.CreateVersion7(),
            processType.Trim(),
            sourceModule.Trim(),
            sourceReference.Trim(),
            requestedByEmployeeId,
            amount,
            currency,
            departmentId,
            policy.Id,
            string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            submittedAt);

        foreach (var step in resolvedApprovers.OrderBy(s => s.Order))
        {
            foreach (var approver in step.ApproverEmployeeIds.Distinct())
            {
                request._assignments.Add(
                    Assignment.Create(request.Id, step.Order, step.Mode, approver, step.SlaHours));
            }
        }

        return request;
    }

    /// <summary>Atribuições do passo em curso que ainda não decidiram.</summary>
    public IReadOnlyList<Assignment> PendingAssignments =>
        [.. _assignments.Where(a => a.Step == CurrentStep && !a.HasDecided)];

    public int TotalSteps => _assignments.Count == 0 ? 0 : _assignments.Max(a => a.Step);

    /// <summary>
    /// Regista uma decisão.
    ///
    /// <para>
    /// <strong>É o método onde a segregação vive.</strong> Por ordem, e a
    /// ordem importa — recusa-se pela razão mais fundamental primeiro:
    /// </para>
    ///
    /// <list type="number">
    /// <item>o processo tem de estar aberto;</item>
    /// <item><strong>BR-2</strong> — quem submeteu não decide;</item>
    /// <item><strong>BR-4</strong> — quem já decidiu não decide outra vez;</item>
    /// <item>quem decide tem de estar atribuído ao passo em curso.</item>
    /// </list>
    /// </summary>
    public void Decide(Guid decidedByEmployeeId, DecisionAction action, DateTimeOffset at, string? notes = null)
    {
        if (Status is not (ApprovalStatus.InProgress or ApprovalStatus.ClarificationRequested))
        {
            throw new InvalidOperationException("Este processo já está fechado.");
        }

        // BR-2. Vem primeiro porque é a regra que não admite excepção nenhuma:
        // nem sequer alguém atribuído por engano ao próprio pedido a contorna.
        if (decidedByEmployeeId == RequestedByEmployeeId)
        {
            throw new SegregationOfDutiesException(
                "Quem submete um pedido não pode decidir sobre ele (BR-2).");
        }

        // BR-4. Uma pessoa com dois cargos satisfaria sozinha um workflow de
        // dois passos — que é exactamente a acumulação de papéis conflituantes
        // que a regra existe para impedir.
        if (_decisions.Any(d => d.DecidedByEmployeeId == decidedByEmployeeId))
        {
            throw new SegregationOfDutiesException(
                "Esta pessoa já interveio neste processo e não pode decidir outra vez (BR-4).");
        }

        var assignment = _assignments.FirstOrDefault(a =>
            a.Step == CurrentStep && a.ApproverEmployeeId == decidedByEmployeeId && !a.HasDecided)
            ?? throw new InvalidOperationException(
                "Esta pessoa não está atribuída ao passo em curso deste processo.");

        assignment.MarkDecided();
        _decisions.Add(Decision.Record(Id, decidedByEmployeeId, action, CurrentStep, at, notes));

        switch (action)
        {
            // Uma rejeição termina o processo de imediato, em qualquer ponto e
            // em qualquer modo (ADR-034). Não há "rejeitado mas continua".
            case DecisionAction.Rejected:
                Status = ApprovalStatus.Rejected;
                ClosedAt = at;
                return;

            case DecisionAction.ClarificationRequested:
                Status = ApprovalStatus.ClarificationRequested;
                return;

            case DecisionAction.Approved:
                Status = ApprovalStatus.InProgress;
                AdvanceIfStepComplete(at);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>
    /// Cancela o processo. <strong>Só o requisitante o faz</strong> — quem
    /// decide, não: um aprovador que quisesse travar um processo rejeita-o, e
    /// a rejeição fica registada com autor. Fechado o K18: até aqui não havia
    /// verificação nenhuma, e qualquer titular de permissão de leitura
    /// conseguia cancelar o pedido de outra pessoa.
    /// </summary>
    public void Cancel(Guid cancelledByEmployeeId, DateTimeOffset at)
    {
        if (Status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Cancelled)
        {
            throw new InvalidOperationException("Este processo já está fechado.");
        }

        // Mesma família de BR-2/BR-4: quem não é dono do pedido não o desfaz.
        // Ao contrário de Decide(), aqui não há segunda pessoa nem passo em
        // curso a verificar — só esta comparação.
        if (cancelledByEmployeeId != RequestedByEmployeeId)
        {
            throw new SegregationOfDutiesException(
                "Só quem submeteu o pedido pode cancelá-lo (K18).");
        }

        Status = ApprovalStatus.Cancelled;
        ClosedAt = at;
    }

    /// <summary>
    /// Faz avançar o passo quando estiver satisfeito.
    ///
    /// <para>
    /// <strong>Por omissão basta um ocupante do Cargo</strong>
    /// (<see cref="StepMode.AnyApprover"/>): quem ocupa um Cargo representa-o, e
    /// exigir todos travaria o processo sempre que um estivesse ausente. Com
    /// <see cref="StepMode.AllApprovers"/>, o passo espera por todos.
    /// </para>
    /// </summary>
    private void AdvanceIfStepComplete(DateTimeOffset at)
    {
        var doPasso = _assignments.Where(a => a.Step == CurrentStep).ToList();

        var satisfeito = doPasso.Count != 0
            && (doPasso[0].Mode == StepMode.AllApprovers
                ? doPasso.All(a => a.HasDecided)
                : doPasso.Any(a => a.HasDecided));

        if (!satisfeito)
        {
            return;
        }

        if (CurrentStep >= TotalSteps)
        {
            Status = ApprovalStatus.Approved;
            ClosedAt = at;
            return;
        }

        CurrentStep++;
    }
}

/// <summary>
/// Um passo da política com os aprovadores já resolvidos, pronto a congelar.
/// </summary>
public sealed record ResolvedStep(
    int Order,
    StepMode Mode,
    IReadOnlyList<Guid> ApproverEmployeeIds,
    int? SlaHours);

/// <summary>
/// Pessoa concreta atribuída a um passo, resolvida na submissão e
/// <strong>nunca recalculada</strong> (BR-6).
/// </summary>
public sealed class Assignment
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private Assignment() { }

    private Assignment(Guid id, Guid requestId, int step, StepMode mode, Guid approverEmployeeId, int? slaHours)
    {
        Id = id;
        RequestId = requestId;
        Step = step;
        Mode = mode;
        ApproverEmployeeId = approverEmployeeId;
        SlaHours = slaHours;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public int Step { get; private set; }

    public StepMode Mode { get; private set; }

    public Guid ApproverEmployeeId { get; private set; }

    public int? SlaHours { get; private set; }

    public bool HasDecided { get; private set; }

    internal static Assignment Create(Guid requestId, int step, StepMode mode, Guid approverEmployeeId, int? slaHours) =>
        new(Guid.CreateVersion7(), requestId, step, mode, approverEmployeeId, slaHours);

    internal void MarkDecided() => HasDecided = true;
}

/// <summary>
/// Decisão tomada. <strong>Imutável por construção</strong> — sem setters
/// públicos e sem métodos que alterem estado.
///
/// <para>
/// É a mesma garantia que `audit` dá à trilha: uma decisão de aprovação é um
/// facto histórico. Corrigir uma decisão errada é tomar outra, não reescrever
/// a primeira.
/// </para>
/// </summary>
public sealed class Decision
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private Decision() { }

    private Decision(Guid id, Guid requestId, Guid decidedByEmployeeId, DecisionAction action, int step, DateTimeOffset at, string? notes)
    {
        Id = id;
        RequestId = requestId;
        DecidedByEmployeeId = decidedByEmployeeId;
        Action = action;
        Step = step;
        DecidedAt = at;
        Notes = notes;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid DecidedByEmployeeId { get; private set; }

    public DecisionAction Action { get; private set; }

    public int Step { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public string? Notes { get; private set; }

    internal static Decision Record(
        Guid requestId,
        Guid decidedByEmployeeId,
        DecisionAction action,
        int step,
        DateTimeOffset at,
        string? notes) =>
        new(Guid.CreateVersion7(), requestId, decidedByEmployeeId, action, step, at,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
}

public enum DecisionAction
{
    Approved,
    Rejected,
    ClarificationRequested,
}

public enum ApprovalStatus
{
    InProgress,
    ClarificationRequested,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Violação de segregação de funções.
///
/// <para>
/// Excepção própria e não <c>InvalidOperationException</c>: quem chama tem de
/// a poder distinguir de um erro de estado qualquer, porque uma tentativa de
/// violar BR-2 ou BR-4 <strong>é um evento de segurança</strong> e vai para a
/// trilha como tal, não como um 409 anónimo.
/// </para>
/// </summary>
public sealed class SegregationOfDutiesException(string message) : InvalidOperationException(message);
