namespace Rivo.Hr.Domain;

/// <summary>
/// Vaga aberta ao recrutamento.
///
/// <para>
/// <strong>Não é um Cargo.</strong> Cargo é a posição organizacional que
/// alguém ocupa depois de contratado (ADR-005, ADR-015); a vaga é a intenção de
/// contratar. Uma vaga pode existir sem que o Cargo esteja criado, e um Cargo
/// existe sem que haja vaga — confundi-los faria o catálogo de Cargos crescer
/// com posições que ninguém ocupa.
/// </para>
/// </summary>
public sealed class JobOpening
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private JobOpening() => Title = string.Empty;

    private JobOpening(Guid id, string title, Guid? departmentId, int vacancies, string? description, string? requirements)
    {
        Id = id;
        Title = title;
        DepartmentId = departmentId;
        Vacancies = vacancies;
        Description = description;
        Requirements = requirements;
        Status = JobOpeningStatus.Open;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public string Title { get; private set; }

    public Guid? DepartmentId { get; private set; }

    /// <summary>Quantas pessoas se pretende contratar. Pelo menos uma, senão não é vaga.</summary>
    public int Vacancies { get; private set; }

    public string? Description { get; private set; }

    public string? Requirements { get; private set; }

    public JobOpeningStatus Status { get; private set; }

    public static JobOpening Open(
        string title,
        Guid? departmentId,
        int vacancies,
        string? description = null,
        string? requirements = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vacancies);

        return new JobOpening(
            Guid.CreateVersion7(),
            title.Trim(),
            departmentId,
            vacancies,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(requirements) ? null : requirements.Trim());
    }

    /// <summary>
    /// Fecha a vaga. Candidatos já em processo não são afectados — fechar a
    /// vaga é parar de a divulgar, não descartar quem se candidatou.
    /// </summary>
    public void Close()
    {
        if (Status != JobOpeningStatus.Open)
        {
            throw new InvalidOperationException("Esta vaga já está fechada.");
        }

        Status = JobOpeningStatus.Closed;
    }
}

public enum JobOpeningStatus
{
    Open,
    Closed,
}

/// <summary>
/// Candidato a uma vaga, com a fase em que se encontra.
///
/// <para>
/// <strong>Um candidato não é um Colaborador.</strong> Só passa a sê-lo quando
/// é contratado — e nesse momento a ligação fica explícita em
/// <see cref="HiredEmployeeId"/>. Modelá-lo como Colaborador desde a
/// candidatura poluiria o quadro de pessoal com gente que nunca entrou.
/// </para>
/// </summary>
public sealed class Candidate
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private Candidate() => FullName = string.Empty;

    private Candidate(Guid id, Guid jobOpeningId, string fullName, string? email, string? phone, DateOnly appliedOn)
    {
        Id = id;
        JobOpeningId = jobOpeningId;
        FullName = fullName;
        Email = email;
        Phone = phone;
        AppliedOn = appliedOn;
        Stage = CandidateStage.Applied;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid JobOpeningId { get; private set; }

    public string FullName { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public DateOnly AppliedOn { get; private set; }

    public CandidateStage Stage { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// O Colaborador criado quando este candidato foi contratado. Nulo em
    /// todas as outras fases.
    /// </summary>
    public Guid? HiredEmployeeId { get; private set; }

    public static Candidate Apply(
        JobOpening opening,
        string fullName,
        string? email,
        string? phone,
        DateOnly appliedOn)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        // Candidatar-se a uma vaga fechada não é recusa de dados — é um estado
        // que não faz sentido, e aceitá-lo encheria o funil de candidatos que
        // ninguém vai avaliar.
        if (opening.Status != JobOpeningStatus.Open)
        {
            throw new InvalidOperationException("Esta vaga já não aceita candidaturas.");
        }

        return new Candidate(
            Guid.CreateVersion7(),
            opening.Id,
            fullName.Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            appliedOn);
    }

    /// <summary>
    /// Faz avançar o candidato na sequência do funil.
    ///
    /// <para>
    /// <strong>Só para a frente, e um passo de cada vez.</strong> Um funil que
    /// aceita saltos deixa de dizer o que quer que seja sobre o processo — e
    /// um que aceita recuos torna impossível saber quantas pessoas chegaram a
    /// entrevista. Rejeitar é o único desvio, e pode acontecer de qualquer
    /// fase.
    /// </para>
    /// </summary>
    public void AdvanceTo(CandidateStage stage)
    {
        if (Stage is CandidateStage.Hired or CandidateStage.Rejected)
        {
            throw new InvalidOperationException(
                "Este candidato já saiu do funil e não pode mudar de fase.");
        }

        if (stage == CandidateStage.Rejected)
        {
            Stage = CandidateStage.Rejected;
            return;
        }

        // Contratar não é avançar: exige criar o Colaborador, e por isso tem
        // método próprio.
        if (stage == CandidateStage.Hired)
        {
            throw new InvalidOperationException(
                "Para contratar, use Hire — a contratação cria o vínculo ao colaborador.");
        }

        if (stage != Stage + 1)
        {
            throw new InvalidOperationException(
                $"O funil avança um passo de cada vez. De {Stage} segue-se {Stage + 1}, não {stage}.");
        }

        Stage = stage;
    }

    /// <summary>
    /// Contrata o candidato, ligando-o ao Colaborador criado.
    ///
    /// <para>
    /// <strong>Só a partir de proposta.</strong> Contratar alguém que nunca foi
    /// entrevistado é possível na vida real, mas registá-lo assim apagaria o
    /// processo — e o processo é o que o recrutamento existe para registar.
    /// </para>
    /// </summary>
    public void Hire(Guid employeeId)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A contratação exige o colaborador criado.", nameof(employeeId));
        }

        if (Stage != CandidateStage.Offer)
        {
            throw new InvalidOperationException(
                "Só se contrata um candidato com proposta feita.");
        }

        Stage = CandidateStage.Hired;
        HiredEmployeeId = employeeId;
    }

    public void Annotate(string? notes) =>
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}

/// <summary>
/// Fases do funil, pela ordem em que acontecem. <strong>A ordem dos valores é
/// significativa</strong> — <see cref="Candidate.AdvanceTo"/> depende dela para
/// impedir saltos.
/// </summary>
public enum CandidateStage
{
    Applied = 0,
    Screening = 1,
    Interview = 2,
    Offer = 3,
    Hired = 4,
    Rejected = 5,
}
