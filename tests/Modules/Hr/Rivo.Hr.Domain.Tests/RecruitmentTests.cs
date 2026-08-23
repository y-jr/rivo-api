using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

public class RecruitmentTests
{
    private static readonly DateOnly Applied = new(2026, 3, 1);

    private static JobOpening Opening() =>
        JobOpening.Open("Contabilista Sénior", Guid.CreateVersion7(), 2, "Fecho mensal", "5 anos");

    private static Candidate Applicant(JobOpening opening) =>
        Candidate.Apply(opening, "João Cabral", "joao@exemplo.ao", "+244900000000", Applied);

    [Fact]
    public void Open_StartsOpen()
    {
        var opening = Opening();

        Assert.Equal(JobOpeningStatus.Open, opening.Status);
        Assert.Equal(2, opening.Vacancies);
    }

    [Fact]
    public void Open_WithoutVacancies_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobOpening.Open("Sem lugares", null, 0));
    }

    [Fact]
    public void Close_Twice_IsRejected()
    {
        var opening = Opening();
        opening.Close();

        Assert.Throws<InvalidOperationException>(opening.Close);
    }

    /// <summary>
    /// Aceitar candidaturas a uma vaga fechada encheria o funil de gente que
    /// ninguém vai avaliar.
    /// </summary>
    [Fact]
    public void Apply_ToClosedOpening_IsRejected()
    {
        var opening = Opening();
        opening.Close();

        Assert.Throws<InvalidOperationException>(() => Applicant(opening));
    }

    [Fact]
    public void Apply_StartsAtAppliedStage()
    {
        var candidate = Applicant(Opening());

        Assert.Equal(CandidateStage.Applied, candidate.Stage);
        Assert.Null(candidate.HiredEmployeeId);
    }

    [Fact]
    public void AdvanceTo_MovesOneStepForward()
    {
        var candidate = Applicant(Opening());

        candidate.AdvanceTo(CandidateStage.Screening);
        candidate.AdvanceTo(CandidateStage.Interview);
        candidate.AdvanceTo(CandidateStage.Offer);

        Assert.Equal(CandidateStage.Offer, candidate.Stage);
    }

    /// <summary>
    /// Um funil que aceita saltos deixa de dizer o que quer que seja sobre o
    /// processo — não daria para saber quantas pessoas chegaram a entrevista.
    /// </summary>
    [Fact]
    public void AdvanceTo_SkippingStages_IsRejected()
    {
        var candidate = Applicant(Opening());

        Assert.Throws<InvalidOperationException>(() => candidate.AdvanceTo(CandidateStage.Offer));
    }

    [Fact]
    public void AdvanceTo_GoingBackwards_IsRejected()
    {
        var candidate = Applicant(Opening());
        candidate.AdvanceTo(CandidateStage.Screening);

        Assert.Throws<InvalidOperationException>(() => candidate.AdvanceTo(CandidateStage.Applied));
    }

    /// <summary>Rejeitar é o único desvio, e vale a partir de qualquer fase.</summary>
    [Fact]
    public void AdvanceTo_RejectedFromAnyStage_IsAllowed()
    {
        var candidate = Applicant(Opening());

        candidate.AdvanceTo(CandidateStage.Rejected);

        Assert.Equal(CandidateStage.Rejected, candidate.Stage);
    }

    [Fact]
    public void AdvanceTo_AfterLeavingTheFunnel_IsRejected()
    {
        var candidate = Applicant(Opening());
        candidate.AdvanceTo(CandidateStage.Rejected);

        Assert.Throws<InvalidOperationException>(() => candidate.AdvanceTo(CandidateStage.Screening));
    }

    /// <summary>
    /// Contratar não é avançar mais um passo: exige o Colaborador criado, e por
    /// isso tem método próprio.
    /// </summary>
    [Fact]
    public void AdvanceTo_Hired_IsRejectedAndPointsToHire()
    {
        var candidate = Applicant(Opening());
        candidate.AdvanceTo(CandidateStage.Screening);
        candidate.AdvanceTo(CandidateStage.Interview);
        candidate.AdvanceTo(CandidateStage.Offer);

        Assert.Throws<InvalidOperationException>(() => candidate.AdvanceTo(CandidateStage.Hired));
    }

    [Fact]
    public void Hire_FromOffer_LinksTheEmployee()
    {
        var candidate = Applicant(Opening());
        candidate.AdvanceTo(CandidateStage.Screening);
        candidate.AdvanceTo(CandidateStage.Interview);
        candidate.AdvanceTo(CandidateStage.Offer);

        var employeeId = Guid.CreateVersion7();
        candidate.Hire(employeeId);

        Assert.Equal(CandidateStage.Hired, candidate.Stage);
        Assert.Equal(employeeId, candidate.HiredEmployeeId);
    }

    /// <summary>
    /// Contratar quem nunca foi entrevistado apagaria o processo — que é o que
    /// o recrutamento existe para registar.
    /// </summary>
    [Fact]
    public void Hire_WithoutAnOffer_IsRejected()
    {
        var candidate = Applicant(Opening());

        Assert.Throws<InvalidOperationException>(() => candidate.Hire(Guid.CreateVersion7()));
    }

    [Fact]
    public void Hire_WithoutEmployee_IsRejected()
    {
        var candidate = Applicant(Opening());
        candidate.AdvanceTo(CandidateStage.Screening);
        candidate.AdvanceTo(CandidateStage.Interview);
        candidate.AdvanceTo(CandidateStage.Offer);

        Assert.Throws<ArgumentException>(() => candidate.Hire(Guid.Empty));
    }
}
