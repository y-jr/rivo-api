using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

// ---------- Plano de contas ----------

/// <summary>
/// Abre uma conta no plano.
///
/// <para>
/// <strong>O plano carrega-se, não vem semeado.</strong> O XSD do SAF-T fixa a
/// forma de uma conta; o plano de contas angolano não está em fonte primária
/// neste projecto, e inventá-lo seria pior do que não o ter — pareceria certo,
/// e a divergência só apareceria no primeiro ficheiro entregue à AGT.
/// </para>
/// </summary>
public sealed class OpenLedgerAccount(ILedgerStore store, IAuditTrail audit)
{
    public async Task<OpenLedgerAccountResult> ExecuteAsync(
        string code,
        string name,
        AccountCategory category,
        string? parentCode,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var normalizado = (code ?? string.Empty).Trim().ToUpperInvariant();

        // Unicidade é invariante sobre o conjunto: o agregado não vê as outras
        // contas. Duas contas com o mesmo código tornariam o `GroupingCode`
        // ambíguo e o ficheiro inválido.
        if (normalizado.Length > 0 &&
            await store.FindAccountByCodeAsync(normalizado, cancellationToken) is not null)
        {
            return OpenLedgerAccountResult.Duplicate(
                $"Já existe uma conta com o código '{normalizado}'.");
        }

        LedgerAccount? agregadora = null;

        if (!string.IsNullOrWhiteSpace(parentCode))
        {
            agregadora = await store.FindAccountByCodeAsync(
                parentCode.Trim().ToUpperInvariant(), cancellationToken);

            if (agregadora is null)
            {
                return OpenLedgerAccountResult.ParentNotFound(
                    $"A conta agregadora '{parentCode}' não existe. " +
                    "O plano carrega-se de cima para baixo.");
            }
        }

        LedgerAccount conta;

        try
        {
            conta = LedgerAccount.Open(normalizado, name, category, agregadora);
        }
        catch (ArgumentException error)
        {
            return OpenLedgerAccountResult.Rejected(error.Message);
        }

        await store.AddAccountAsync(conta, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountOpenedInChart,
                FinanceAuditEntityTypes.LedgerAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{conta.Code}}","name":"{{conta.Name}}","category":"{{conta.Category}}","parent":"{{conta.ParentCode}}"}"""),
            cancellationToken);

        return OpenLedgerAccountResult.Success(conta.Id, conta.Code);
    }
}

public sealed record OpenLedgerAccountResult(
    OpenLedgerAccountOutcome Outcome,
    Guid? AccountId,
    string? Code,
    string? Error)
{
    public static OpenLedgerAccountResult Success(Guid id, string code) =>
        new(OpenLedgerAccountOutcome.Opened, id, code, null);

    public static OpenLedgerAccountResult Duplicate(string error) =>
        new(OpenLedgerAccountOutcome.Duplicate, null, null, error);

    public static OpenLedgerAccountResult ParentNotFound(string error) =>
        new(OpenLedgerAccountOutcome.ParentNotFound, null, null, error);

    public static OpenLedgerAccountResult Rejected(string error) =>
        new(OpenLedgerAccountOutcome.Rejected, null, null, error);
}

public enum OpenLedgerAccountOutcome
{
    Opened,

    /// <summary>Código já usado. Conflito de estado — 409.</summary>
    Duplicate,

    /// <summary>A agregadora indicada não existe — 404.</summary>
    ParentNotFound,

    Rejected,
}

public sealed class ListLedgerAccounts(ILedgerStore store)
{
    public async Task<IReadOnlyList<LedgerAccountView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var contas = await store.ListAccountsAsync(includeInactive, cancellationToken);

        return [.. contas.Select(c => new LedgerAccountView(
            c.Id, c.Code, c.Name, c.Category.ToString(), c.ParentCode,
            c.AcceptsPostings, c.IsAnalytic, c.IsActive))];
    }
}

/// <param name="AcceptsPostings">
/// Só as contas de movimento (`GM`/`AM`) recebem lançamentos. É a distinção que
/// dá sentido às seis categorias do SAF-T.
/// </param>
public sealed record LedgerAccountView(
    Guid AccountId,
    string Code,
    string Name,
    string Category,
    string? ParentCode,
    bool AcceptsPostings,
    bool IsAnalytic,
    bool IsActive);

/// <summary>
/// Desactiva uma conta. Não elimina (BR-14).
/// </summary>
public sealed class DeactivateLedgerAccount(ILedgerStore store, IAuditTrail audit)
{
    public async Task<DeactivateAccountOutcome> ExecuteAsync(
        Guid accountId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var conta = await store.FindAccountForUpdateAsync(accountId, cancellationToken);

        if (conta is null)
        {
            return DeactivateAccountOutcome.NotFound;
        }

        // Uma agregadora com filhas activas não se desactiva: a árvore ficaria
        // com um buraco no meio, e o `GroupingCode` das filhas apontaria a uma
        // conta que já não conta.
        if (await store.HasChildrenAsync(accountId, cancellationToken))
        {
            return DeactivateAccountOutcome.HasChildren;
        }

        conta.Deactivate();
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountOpenedInChart,
                FinanceAuditEntityTypes.LedgerAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{conta.Code}}","isActive":false}"""),
            cancellationToken);

        return DeactivateAccountOutcome.Done;
    }
}

public enum DeactivateAccountOutcome
{
    Done,
    NotFound,

    /// <summary>Tem contas penduradas. Conflito de estado — 409.</summary>
    HasChildren,
}

// ---------- Diários ----------

public sealed class OpenJournal(ILedgerStore store, IAuditTrail audit)
{
    public async Task<OpenJournalResult> ExecuteAsync(
        string code,
        string name,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var normalizado = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizado.Length > 0 &&
            await store.FindJournalByCodeAsync(normalizado, cancellationToken) is not null)
        {
            return OpenJournalResult.Duplicate(
                $"Já existe um diário com o código '{normalizado}'. " +
                "O SAF-T exige `JournalID` único no ficheiro.");
        }

        Journal diario;

        try
        {
            diario = Journal.Open(normalizado, name);
        }
        catch (ArgumentException error)
        {
            return OpenJournalResult.Rejected(error.Message);
        }

        await store.AddJournalAsync(diario, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.JournalOpened,
                FinanceAuditEntityTypes.Journal,
                diario.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{diario.Code}}","name":"{{diario.Name}}"}"""),
            cancellationToken);

        return OpenJournalResult.Success(diario.Id, diario.Code);
    }
}

public sealed record OpenJournalResult(
    OpenJournalOutcome Outcome,
    Guid? JournalId,
    string? Code,
    string? Error)
{
    public static OpenJournalResult Success(Guid id, string code) =>
        new(OpenJournalOutcome.Opened, id, code, null);

    public static OpenJournalResult Duplicate(string error) =>
        new(OpenJournalOutcome.Duplicate, null, null, error);

    public static OpenJournalResult Rejected(string error) =>
        new(OpenJournalOutcome.Rejected, null, null, error);
}

public enum OpenJournalOutcome
{
    Opened,
    Duplicate,
    Rejected,
}

public sealed class ListJournals(ILedgerStore store)
{
    public async Task<IReadOnlyList<JournalView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var diarios = await store.ListJournalsAsync(includeInactive, cancellationToken);

        return [.. diarios.Select(d => new JournalView(d.Id, d.Code, d.Name, d.IsActive))];
    }
}

public sealed record JournalView(Guid JournalId, string Code, string Name, bool IsActive);

// ---------- Lançamentos ----------

/// <summary>
/// Lança nos livros.
///
/// <para>
/// A partida dobrada é do agregado. <strong>O que se monta aqui são as três
/// coisas que ele não vê:</strong> se o período aceita escrita, se as contas
/// existem e recebem lançamentos, e se a chave do SAF-T já foi usada.
/// </para>
/// </summary>
public sealed class PostJournalEntry(ILedgerStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<PostEntryResult> ExecuteAsync(
        string journalCode,
        string archivalNumber,
        DateOnly transactionDate,
        int fiscalYear,
        int period,
        string description,
        TransactionType type,
        string sourceId,
        IReadOnlyList<JournalLineInput> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return PostEntryResult.Rejected("Um lançamento tem linhas.");
        }

        var diario = await store.FindJournalByCodeAsync(
            (journalCode ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);

        if (diario is null)
        {
            return PostEntryResult.JournalNotFound();
        }

        // **O período, antes de tudo o resto.** Um período fechado recusa
        // escrita, e é essa recusa que faz de um balancete já entregue um facto
        // em vez de uma vista sobre dados que ainda se mexem.
        var contabilistico = await store.FindPeriodAsync(fiscalYear, period, cancellationToken);

        if (contabilistico is null)
        {
            return PostEntryResult.PeriodNotOpen(
                $"O período {fiscalYear}/{period} não existe. Abra-o antes de lançar.");
        }

        if (!contabilistico.AcceptsPostings)
        {
            return PostEntryResult.PeriodClosed(
                $"O período {fiscalYear}/{period} está fechado. " +
                "Corrigir um período fechado faz-se por lançamento de regularização noutro período.");
        }

        // A chave do SAF-T é composta por três coisas que quem lança escolhe.
        // Nada impede repeti-las por engano, e o ficheiro só seria recusado
        // meses depois.
        var arquivo = (archivalNumber ?? string.Empty).Trim();

        if (await store.EntryExistsAsync(transactionDate, diario.Code, arquivo, cancellationToken))
        {
            return PostEntryResult.DuplicateTransaction(
                $"Já existe um lançamento com a chave '{transactionDate:yyyy-MM-dd} {diario.Code} {arquivo}'. " +
                "O `TransactionID` é único no ficheiro SAF-T.");
        }

        var codigos = lines
            .Select(l => (l.AccountCode ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var contas = await store.FindAccountsByCodeAsync(codigos, cancellationToken);
        var resolvidas = new List<NewJournalLine>(lines.Count);

        foreach (var linha in lines)
        {
            var codigo = (linha.AccountCode ?? string.Empty).Trim().ToUpperInvariant();

            if (!contas.TryGetValue(codigo, out var conta))
            {
                return PostEntryResult.AccountNotFound(
                    $"A conta '{linha.AccountCode}' não existe no plano.");
            }

            if (!conta.IsActive)
            {
                return PostEntryResult.Rejected(
                    $"A conta {conta.Code} está desactivada e não recebe lançamentos.");
            }

            // Lançar numa agregadora faria o total dela deixar de ser a soma
            // das filhas — o erro clássico que um plano hierárquico existe para
            // impedir.
            if (!conta.AcceptsPostings)
            {
                return PostEntryResult.Rejected(
                    $"A conta {conta.Code} é {conta.Category} — agregadora, não de movimento. " +
                    "Lance numa conta de movimento.");
            }

            resolvidas.Add(new NewJournalLine(
                conta.Id, conta.Code, linha.Side, linha.Amount,
                linha.Description, linha.CostCentreId, linha.SourceDocumentId));
        }

        JournalEntry lancamento;

        try
        {
            lancamento = JournalEntry.Post(
                diario, arquivo, transactionDate, period, description, type,
                sourceId, resolvidas, clock.GetUtcNow());
        }
        catch (UnbalancedEntryException error)
        {
            return PostEntryResult.Unbalanced(error.Message);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return PostEntryResult.Rejected(error.Message);
        }

        await store.AddEntryAsync(lancamento, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.EntryPosted,
                FinanceAuditEntityTypes.JournalEntry,
                lancamento.Id.ToString(),
                context,
                NewValue: $$"""{"transactionId":"{{lancamento.TransactionId}}","period":{{lancamento.Period}},"total":{{lancamento.TotalDebit}},"type":"{{lancamento.Type}}"}"""),
            cancellationToken);

        return PostEntryResult.Success(lancamento.Id, lancamento.TransactionId);
    }
}

public sealed record JournalLineInput(
    string AccountCode,
    EntrySide Side,
    decimal Amount,
    string Description,
    Guid? CostCentreId = null,
    string? SourceDocumentId = null);

public sealed record PostEntryResult(
    PostEntryOutcome Outcome,
    Guid? EntryId,
    string? TransactionId,
    string? Error)
{
    public static PostEntryResult Success(Guid id, string transactionId) =>
        new(PostEntryOutcome.Posted, id, transactionId, null);

    public static PostEntryResult JournalNotFound() =>
        new(PostEntryOutcome.JournalNotFound, null, null, null);

    public static PostEntryResult AccountNotFound(string error) =>
        new(PostEntryOutcome.AccountNotFound, null, null, error);

    public static PostEntryResult PeriodNotOpen(string error) =>
        new(PostEntryOutcome.PeriodNotOpen, null, null, error);

    public static PostEntryResult PeriodClosed(string error) =>
        new(PostEntryOutcome.PeriodClosed, null, null, error);

    public static PostEntryResult DuplicateTransaction(string error) =>
        new(PostEntryOutcome.DuplicateTransaction, null, null, error);

    public static PostEntryResult Unbalanced(string error) =>
        new(PostEntryOutcome.Unbalanced, null, null, error);

    public static PostEntryResult Rejected(string error) =>
        new(PostEntryOutcome.Rejected, null, null, error);
}

public enum PostEntryOutcome
{
    Posted,
    JournalNotFound,
    AccountNotFound,

    /// <summary>O período não existe — 404: não há onde lançar.</summary>
    PeriodNotOpen,

    /// <summary>
    /// O período está fechado. <strong>409, não 400:</strong> o lançamento está
    /// bem formado e noutro período entrava sem objecção.
    /// </summary>
    PeriodClosed,

    /// <summary>Chave do SAF-T repetida — 409.</summary>
    DuplicateTransaction,

    /// <summary>
    /// Débitos e créditos não batem. <strong>400 e com razão própria:</strong>
    /// é a invariante central da contabilidade, e quem a viola merece ouvir
    /// exactamente isso em vez de "pedido inválido".
    /// </summary>
    Unbalanced,

    Rejected,
}

public sealed class ListJournalEntries(ILedgerStore store)
{
    public async Task<IReadOnlyList<JournalEntryView>> ExecuteAsync(
        Guid? journalId,
        int? fiscalYear,
        int? period,
        CancellationToken cancellationToken)
    {
        var lancamentos = await store.ListEntriesAsync(journalId, fiscalYear, period, cancellationToken);

        return [.. lancamentos.Select(ToView)];
    }

    internal static JournalEntryView ToView(JournalEntry e) =>
        new(
            e.Id,
            e.TransactionId,
            e.JournalCode,
            e.ArchivalNumber,
            e.TransactionDate,
            e.Period,
            e.Description,
            e.Type.ToString(),
            e.SourceId,
            e.PostedAt,
            e.TotalDebit,
            e.TotalCredit,
            e.IsVoided,
            e.VoidedAt,
            e.VoidReason,
            [.. e.Lines
                .OrderBy(l => l.RecordNumber)
                .Select(l => new JournalEntryLineView(
                    l.RecordNumber, l.AccountCode, l.Side.ToString(), l.Amount,
                    l.Description, l.CostCentreId, l.SourceDocumentId))]);
}

public sealed record JournalEntryView(
    Guid EntryId,
    string TransactionId,
    string JournalCode,
    string ArchivalNumber,
    DateOnly TransactionDate,
    int Period,
    string Description,
    string Type,
    string SourceId,
    DateTimeOffset PostedAt,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsVoided,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    IReadOnlyList<JournalEntryLineView> Lines);

public sealed record JournalEntryLineView(
    int RecordNumber,
    string AccountCode,
    string Side,
    decimal Amount,
    string Description,
    Guid? CostCentreId,
    string? SourceDocumentId);

public sealed class GetJournalEntry(ILedgerStore store)
{
    public async Task<JournalEntryView?> ExecuteAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var lancamento = await store.FindEntryAsync(entryId, cancellationToken);

        return lancamento is null ? null : ListJournalEntries.ToView(lancamento);
    }
}

/// <summary>
/// Anula um lançamento. Não elimina (BR-14).
///
/// <para>
/// <strong>Só num período aberto.</strong> Anular num período fechado mudaria
/// um balancete já dado por definitivo sem deixar rasto no próprio período —
/// para isso existe o lançamento de regularização, que é visível.
/// </para>
/// </summary>
public sealed class VoidJournalEntry(ILedgerStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<VoidEntryResult> ExecuteAsync(
        Guid entryId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var lancamento = await store.FindEntryForUpdateAsync(entryId, cancellationToken);

        if (lancamento is null)
        {
            return VoidEntryResult.NotFound();
        }

        var contabilistico = await store.FindPeriodAsync(
            lancamento.TransactionDate.Year, lancamento.Period, cancellationToken);

        if (contabilistico is not null && !contabilistico.AcceptsPostings)
        {
            return VoidEntryResult.PeriodClosed(
                $"O período {contabilistico.FiscalYear}/{contabilistico.Number} está fechado. " +
                "Corrija por lançamento de regularização, que fica visível, em vez de anular " +
                "o que já foi reportado.");
        }

        try
        {
            lancamento.Void(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return VoidEntryResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.EntryVoided,
                FinanceAuditEntityTypes.JournalEntry,
                lancamento.Id.ToString(),
                context,
                NewValue: $$"""{"transactionId":"{{lancamento.TransactionId}}","reason":"{{lancamento.VoidReason}}"}"""),
            cancellationToken);

        return VoidEntryResult.Success();
    }
}

public sealed record VoidEntryResult(VoidEntryOutcome Outcome, string? Error)
{
    public static VoidEntryResult Success() => new(VoidEntryOutcome.Voided, null);

    public static VoidEntryResult NotFound() => new(VoidEntryOutcome.NotFound, null);

    public static VoidEntryResult PeriodClosed(string error) =>
        new(VoidEntryOutcome.PeriodClosed, error);

    public static VoidEntryResult Rejected(string error) =>
        new(VoidEntryOutcome.Rejected, error);
}

public enum VoidEntryOutcome
{
    Voided,
    NotFound,
    PeriodClosed,
    Rejected,
}

// ---------- Fecho ----------

public sealed class ManageAccountingPeriods(ILedgerStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<OpenPeriodResult> OpenAsync(
        int fiscalYear,
        int number,
        CancellationToken cancellationToken)
    {
        if (await store.FindPeriodAsync(fiscalYear, number, cancellationToken) is not null)
        {
            return OpenPeriodResult.AlreadyExists();
        }

        AccountingPeriod periodo;

        try
        {
            periodo = AccountingPeriod.Open(fiscalYear, number);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return OpenPeriodResult.Rejected(error.Message);
        }

        await store.AddPeriodAsync(periodo, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return OpenPeriodResult.Success(periodo.Id);
    }

    public async Task<IReadOnlyList<AccountingPeriodView>> ListAsync(
        int fiscalYear,
        CancellationToken cancellationToken)
    {
        var periodos = await store.ListPeriodsAsync(fiscalYear, cancellationToken);

        return [.. periodos.Select(p => new AccountingPeriodView(
            p.Id, p.FiscalYear, p.Number, p.Status.ToString(), p.IsAdjustmentPeriod,
            p.ClosedAt, p.ClosedByEmployeeId, p.ReopenedAt, p.ReopenReason))];
    }

    public async Task<ClosePeriodResult> CloseAsync(
        int fiscalYear,
        int number,
        Guid closedByEmployeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var periodo = await store.FindPeriodForUpdateAsync(fiscalYear, number, cancellationToken);

        if (periodo is null)
        {
            return ClosePeriodResult.NotFound();
        }

        try
        {
            periodo.Close(closedByEmployeeId, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return ClosePeriodResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PeriodClosed,
                FinanceAuditEntityTypes.AccountingPeriod,
                periodo.Id.ToString(),
                context,
                NewValue: $$"""{"fiscalYear":{{periodo.FiscalYear}},"period":{{periodo.Number}},"closedBy":"{{closedByEmployeeId}}"}"""),
            cancellationToken);

        return ClosePeriodResult.Success();
    }

    public async Task<ClosePeriodResult> ReopenAsync(
        int fiscalYear,
        int number,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var periodo = await store.FindPeriodForUpdateAsync(fiscalYear, number, cancellationToken);

        if (periodo is null)
        {
            return ClosePeriodResult.NotFound();
        }

        try
        {
            periodo.Reopen(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return ClosePeriodResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        // Acção própria na trilha: uma reabertura faz números já reportados
        // voltarem a mexer-se, e quem audita tem de a encontrar sem a procurar
        // entre os fechos.
        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PeriodReopened,
                FinanceAuditEntityTypes.AccountingPeriod,
                periodo.Id.ToString(),
                context,
                NewValue: $$"""{"fiscalYear":{{periodo.FiscalYear}},"period":{{periodo.Number}},"reason":"{{periodo.ReopenReason}}"}"""),
            cancellationToken);

        return ClosePeriodResult.Success();
    }
}

public sealed record AccountingPeriodView(
    Guid PeriodId,
    int FiscalYear,
    int Number,
    string Status,
    bool IsAdjustmentPeriod,
    DateTimeOffset? ClosedAt,
    Guid? ClosedByEmployeeId,
    DateTimeOffset? ReopenedAt,
    string? ReopenReason);

public sealed record OpenPeriodResult(bool Succeeded, Guid? PeriodId, string? Error)
{
    public static OpenPeriodResult Success(Guid id) => new(true, id, null);

    public static OpenPeriodResult AlreadyExists() =>
        new(false, null, "O período já existe.");

    public static OpenPeriodResult Rejected(string error) => new(false, null, error);
}

public sealed record ClosePeriodResult(ClosePeriodOutcome Outcome, string? Error)
{
    public static ClosePeriodResult Success() => new(ClosePeriodOutcome.Done, null);

    public static ClosePeriodResult NotFound() => new(ClosePeriodOutcome.NotFound, null);

    public static ClosePeriodResult Rejected(string error) =>
        new(ClosePeriodOutcome.Rejected, error);
}

public enum ClosePeriodOutcome
{
    Done,
    NotFound,

    /// <summary>Já fechado, ou não estava fechado para reabrir. 409.</summary>
    Rejected,
}

// ---------- Balancete ----------

/// <summary>
/// O balancete: por conta, o acumulado a débito e a crédito e o saldo.
///
/// <para>
/// <strong>É a leitura que o SAF-T precisa por conta</strong> —
/// <c>OpeningDebitBalance</c>, <c>OpeningCreditBalance</c>,
/// <c>ClosingDebitBalance</c> e <c>ClosingCreditBalance</c>. `fiscal` lê isto
/// quando gerar o ficheiro; o relatório formatado (balancete, demonstração de
/// resultados, balanço) é dele, não de `finance`.
/// </para>
///
/// <para>
/// A abertura de um período é o fecho do anterior — não é número guardado à
/// parte, e por isso não pode divergir.
/// </para>
/// </summary>
public sealed class GetTrialBalance(ILedgerStore store)
{
    public async Task<TrialBalanceView> ExecuteAsync(
        int fiscalYear,
        int? period,
        CancellationToken cancellationToken)
    {
        var contas = await store.ListAccountsAsync(includeInactive: true, cancellationToken);

        var fecho = await store.AccountMovementsAsync(fiscalYear, period, cancellationToken);

        // Abertura = fecho do período anterior. Com `period` nulo o balancete é
        // do ano inteiro, e a abertura é zero.
        var abertura = period is > 1
            ? await store.AccountMovementsAsync(fiscalYear, period - 1, cancellationToken)
            : [];

        var porConta = abertura.ToDictionary(m => m.AccountId);

        var linhas = contas
            .Where(c => c.AcceptsPostings)
            .Select(conta =>
            {
                var f = fecho.FirstOrDefault(m => m.AccountId == conta.Id);
                var a = porConta.GetValueOrDefault(conta.Id);

                var aberturaDebito = a?.UptoDebit ?? 0m;
                var aberturaCredito = a?.UptoCredit ?? 0m;
                var fechoDebito = f?.UptoDebit ?? 0m;
                var fechoCredito = f?.UptoCredit ?? 0m;

                return new TrialBalanceLine(
                    conta.Code,
                    conta.Name,
                    aberturaDebito,
                    aberturaCredito,
                    fechoDebito - aberturaDebito,
                    fechoCredito - aberturaCredito,
                    fechoDebito,
                    fechoCredito);
            })
            .Where(l => l.ClosingDebit != 0m || l.ClosingCredit != 0m)
            .OrderBy(l => l.AccountCode, StringComparer.Ordinal)
            .ToList();

        return new TrialBalanceView(
            fiscalYear,
            period,
            linhas.Sum(l => l.ClosingDebit),
            linhas.Sum(l => l.ClosingCredit),

            // Se isto for falso, alguma linha entrou sem par — e o balancete
            // deve dizê-lo em vez de somar e calar.
            linhas.Sum(l => l.ClosingDebit) == linhas.Sum(l => l.ClosingCredit),
            linhas);
    }
}

/// <param name="IsBalanced">
/// O total a débito iguala o total a crédito. Com a partida dobrada imposta no
/// agregado isto é sempre verdade — <strong>e é por isso que vale a pena
/// mostrar</strong>: no dia em que for falso, alguma coisa entrou por fora.
/// </param>
public sealed record TrialBalanceView(
    int FiscalYear,
    int? Period,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsBalanced,
    IReadOnlyList<TrialBalanceLine> Lines);

public sealed record TrialBalanceLine(
    string AccountCode,
    string AccountName,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit);
