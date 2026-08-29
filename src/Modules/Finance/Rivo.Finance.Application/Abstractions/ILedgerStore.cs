using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Persistência da Contabilidade. Terceiro store de `finance`, ao lado de
/// <see cref="ISalesInvoiceStore"/> e <see cref="IPayablesStore"/> — são
/// contextos internos distintos (`modules/finance.md`), e uma interface só
/// daria algo que ninguém consegue implementar sem conhecer tudo.
/// </summary>
public interface ILedgerStore
{
    Task<LedgerAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<LedgerAccount?> FindAccountByCodeAsync(string code, CancellationToken cancellationToken);

    Task<LedgerAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Várias contas de uma vez, por código.
    ///
    /// <para>
    /// Um lançamento toca em duas ou mais contas, e resolvê-las uma a uma daria
    /// uma consulta por linha. Devolve um dicionário para que quem chama saiba
    /// exactamente qual faltou.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, LedgerAccount>> FindAccountsByCodeAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LedgerAccount>> ListAccountsAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verdadeiro se alguma conta aponta a esta como agregadora. Uma conta com
    /// filhas não se desactiva sem deixar a árvore partida.
    /// </summary>
    Task<bool> HasChildrenAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Verdadeiro se já há linhas lançadas nesta conta.</summary>
    Task<bool> HasPostingsAsync(Guid accountId, CancellationToken cancellationToken);

    Task AddAccountAsync(LedgerAccount account, CancellationToken cancellationToken);

    Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken);

    Task<Journal?> FindJournalByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Journal>> ListJournalsAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task AddJournalAsync(Journal journal, CancellationToken cancellationToken);

    Task<JournalEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken);

    Task<JournalEntry?> FindEntryForUpdateAsync(Guid entryId, CancellationToken cancellationToken);

    /// <summary>
    /// O lançamento que um documento produziu, pelo número de arquivo — a
    /// mesma chave que <see cref="EntryExistsAsync"/> usa para a idempotência
    /// da postagem (`PostDocument`). É como o estorno automático encontra o
    /// que tem de inverter: o número do documento é único por construção
    /// (série), e é ele que vira <c>ArchivalNumber</c> na postagem.
    /// </summary>
    Task<JournalEntry?> FindEntryByArchivalNumberAsync(
        string archivalNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<JournalEntry>> ListEntriesAsync(
        Guid? journalId,
        int? fiscalYear,
        int? period,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verdadeiro se já existe lançamento com este <c>TransactionID</c>.
    ///
    /// <para>
    /// O SAF-T exige que a chave seja única no ficheiro, e ela é composta por
    /// data, diário e número de arquivo — três coisas que quem lança escolhe.
    /// Nada impede repeti-las por engano, e o resultado seria um ficheiro
    /// inválido descoberto meses depois.
    /// </para>
    /// </summary>
    Task<bool> EntryExistsAsync(
        DateOnly transactionDate,
        string journalCode,
        string archivalNumber,
        CancellationToken cancellationToken);

    Task AddEntryAsync(JournalEntry entry, CancellationToken cancellationToken);

    Task<AccountingPeriod?> FindPeriodAsync(
        int fiscalYear,
        int number,
        CancellationToken cancellationToken);

    Task<AccountingPeriod?> FindPeriodForUpdateAsync(
        int fiscalYear,
        int number,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountingPeriod>> ListPeriodsAsync(
        int fiscalYear,
        CancellationToken cancellationToken);

    Task AddPeriodAsync(AccountingPeriod period, CancellationToken cancellationToken);

    /// <summary>
    /// A regra activa de um acontecimento, com as suas linhas.
    ///
    /// <para>
    /// Uma só: duas regras activas para o mesmo acontecimento tornariam a
    /// tradução ambígua, e o sistema não escolhe por si.
    /// </para>
    /// </summary>
    Task<PostingRule?> FindActivePostingRuleAsync(
        PostingEvent postingEvent,
        CancellationToken cancellationToken);

    Task<PostingRule?> FindPostingRuleAsync(Guid ruleId, CancellationToken cancellationToken);

    Task<PostingRule?> FindPostingRuleForUpdateAsync(Guid ruleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PostingRule>> ListPostingRulesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task AddPostingRuleAsync(PostingRule rule, CancellationToken cancellationToken);

    /// <summary>
    /// Movimento por conta num ano, contando só lançamentos **não anulados**.
    ///
    /// <para>
    /// É o balancete, e é o que o SAF-T precisa por conta:
    /// <c>OpeningDebitBalance</c>, <c>OpeningCreditBalance</c>,
    /// <c>ClosingDebitBalance</c> e <c>ClosingCreditBalance</c>.
    /// </para>
    ///
    /// <para>
    /// Calculado das linhas e não guardado em coluna: um saldo materializado
    /// seria ponto de contenção a cada lançamento e ficaria errado em silêncio
    /// no dia em que alguém anulasse um lançamento sem o recalcular. Mesma
    /// razão do saldo em aberto de uma factura.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<AccountMovement>> AccountMovementsAsync(
        int fiscalYear,
        int? uptoPeriod,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <param name="UptoDebit">Acumulado a débito até ao período pedido, inclusive.</param>
public sealed record AccountMovement(
    Guid AccountId,
    string AccountCode,
    decimal UptoDebit,
    decimal UptoCredit);
