namespace Rivo.Finance.Domain;

/// <summary>
/// Um período contabilístico, e o que o fecha.
///
/// <para>
/// <strong>Fechar é a única coisa que torna um livro confiável.</strong> Sem
/// fecho, um lançamento com data de Março pode entrar em Novembro e mudar
/// retroactivamente um balancete que já foi entregue. O período fechado recusa
/// escrita — e é essa recusa que faz de um número histórico um facto em vez de
/// uma vista sobre dados que ainda se mexem.
/// </para>
///
/// <para>
/// Um período por ano e número, com o número de 1 a 16 do SAF-T. Os que passam
/// de 12 são os de fecho e regularização: existem para que o apuramento de
/// resultados não se misture com Dezembro.
/// </para>
/// </summary>
public sealed class AccountingPeriod
{
    private AccountingPeriod(int fiscalYear, int number)
    {
        Id = Guid.CreateVersion7();
        FiscalYear = fiscalYear;
        Number = number;
        Status = PeriodStatus.Open;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private AccountingPeriod()
    {
    }

    public Guid Id { get; private set; }

    public int FiscalYear { get; private set; }

    /// <summary>1 a 16 (SAF-T AO). Acima de 12 são períodos de fecho.</summary>
    public int Number { get; private set; }

    public PeriodStatus Status { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Quem fechou. O fecho é acto de pessoa, não de sistema.</summary>
    public Guid? ClosedByEmployeeId { get; private set; }

    public DateTimeOffset? ReopenedAt { get; private set; }

    public string? ReopenReason { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025, BR-17). <strong>Aqui é a regra:</strong>
    /// um fecho simultâneo a um lançamento é exactamente a corrida que deixaria
    /// um movimento a cair dentro de um período já dado por fechado.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Períodos acima de 12 — fecho e regularização.</summary>
    public bool IsAdjustmentPeriod => Number > 12;

    public static AccountingPeriod Open(int fiscalYear, int number)
    {
        if (fiscalYear is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fiscalYear), fiscalYear,
                "O SAF-T AO só admite datas entre 2000 e 9999.");
        }

        if (number is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number), number, "O período contabilístico vai de 1 a 16 (SAF-T AO).");
        }

        return new AccountingPeriod(fiscalYear, number);
    }

    public void Close(Guid closedByEmployeeId, DateTimeOffset at)
    {
        if (closedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException("O fecho regista quem o fez.", nameof(closedByEmployeeId));
        }

        if (Status is PeriodStatus.Closed)
        {
            throw new InvalidOperationException(
                $"O período {FiscalYear}/{Number} já está fechado.");
        }

        Status = PeriodStatus.Closed;
        ClosedAt = at;
        ClosedByEmployeeId = closedByEmployeeId;

        // O motivo da reabertura anterior deixa de valer: é do ciclo que
        // acabou de fechar, e mantê-lo faria parecer que este fecho o herdou.
        ReopenReason = null;
    }

    /// <summary>
    /// Reabre um período fechado, com motivo.
    ///
    /// <para>
    /// <strong>Não é operação de rotina, e o motivo obrigatório é o ponto.</strong>
    /// Reabrir um período significa que números já dados por definitivos vão
    /// mudar; quem o faz tem de dizer porquê, e isso vai para a trilha.
    /// </para>
    /// </summary>
    public void Reopen(string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Reabrir um período fechado exige motivo: números já dados por " +
                "definitivos vão mudar.",
                nameof(reason));
        }

        if (Status is not PeriodStatus.Closed)
        {
            throw new InvalidOperationException(
                $"O período {FiscalYear}/{Number} não está fechado.");
        }

        Status = PeriodStatus.Open;
        ReopenedAt = at;
        ReopenReason = reason.Trim();
        ClosedAt = null;
        ClosedByEmployeeId = null;
    }

    public bool AcceptsPostings => Status is PeriodStatus.Open;
}

public enum PeriodStatus
{
    Open,
    Closed,
}

/// <summary>
/// Lançar num período fechado.
///
/// <para>
/// Excepção própria: é conflito com o estado dos livros, não defeito do
/// lançamento — o mesmo lançamento noutro período entraria sem objecção.
/// </para>
/// </summary>
public sealed class ClosedPeriodException(string message) : InvalidOperationException(message);
