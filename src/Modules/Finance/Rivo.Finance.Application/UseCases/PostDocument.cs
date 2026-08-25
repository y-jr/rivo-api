using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Traduz um documento em lançamento contabilístico.
///
/// <para>
/// <strong>Não grava.</strong> Acrescenta o lançamento à mesma unidade de
/// trabalho de quem chama, e é o `SaveChanges` desse que compromete os dois —
/// documento e lançamento entram juntos ou não entra nenhum. É concretamente o
/// que o ADR-001 comprou ao escolher monólito modular: sem isso, seria preciso
/// um outbox e a contabilidade ficaria eventualmente consistente com os
/// documentos que a originam.
/// </para>
///
/// <para>
/// <strong>Sem regra configurada, não posta — e isso é estado legítimo.</strong>
/// O ciclo de venda funcionou meses sem contabilidade nenhuma, e ligar
/// Contabilidade não pode partir a facturação de quem ainda não carregou um
/// plano de contas. Postar é opt-in, uma regra de cada vez.
/// </para>
/// </summary>
public sealed class PostDocument(ILedgerStore store)
{
    public async Task<DocumentPostingResult> PostAsync(
        DocumentPosting posting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(posting);

        var regra = await store.FindActivePostingRuleAsync(posting.Event, cancellationToken);

        if (regra is null)
        {
            return DocumentPostingResult.NoRule();
        }

        var diario = await store.FindJournalByCodeAsync(regra.JournalCode, cancellationToken);

        if (diario is null || !diario.IsActive)
        {
            return DocumentPostingResult.Failed(
                $"A regra de postagem de {posting.Event} lança no diário '{regra.JournalCode}', " +
                "que não existe ou está desactivado.");
        }

        // O número de arquivo do SAF-T deriva da chave do documento, e é isso
        // que torna a postagem **idempotente por construção**: postar a mesma
        // factura duas vezes colide na chave única do lançamento em vez de
        // duplicar o movimento.
        if (!TryArchivalNumber(posting.ArchivalKey, out var arquivo, out var erro))
        {
            return DocumentPostingResult.Failed(erro);
        }

        var periodo = posting.Date.Month;

        var contabilistico = await store.FindPeriodAsync(
            posting.Date.Year, periodo, cancellationToken);

        if (contabilistico is not null && !contabilistico.AcceptsPostings)
        {
            return DocumentPostingResult.PeriodClosed(
                $"O período {posting.Date.Year}/{periodo:00} está fechado, e o documento " +
                $"{posting.DocumentNumber} tem data lá dentro.");
        }

        if (contabilistico is null)
        {
            // **Um período que ninguém abriu também nunca foi fechado.** A
            // linha existe para registar um *fecho*, não para dar licença — e
            // exigi-la faria a facturação parar no dia 1 de cada mês por causa
            // de arrumação contabilística.
            await store.AddPeriodAsync(
                AccountingPeriod.Open(posting.Date.Year, periodo), cancellationToken);
        }

        var codigos = regra.Lines
            .Select(l => l.AccountCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var contas = await store.FindAccountsByCodeAsync(codigos, cancellationToken);
        var linhas = new List<NewJournalLine>(regra.Lines.Count);

        foreach (var linha in regra.Lines.OrderBy(l => l.LineNumber))
        {
            if (!contas.TryGetValue(linha.AccountCode, out var conta))
            {
                return DocumentPostingResult.Failed(
                    $"A regra de postagem de {posting.Event} usa a conta '{linha.AccountCode}', " +
                    "que não existe no plano.");
            }

            if (!conta.IsActive || !conta.AcceptsPostings)
            {
                return DocumentPostingResult.Failed(
                    $"A conta '{conta.Code}' está desactivada ou não é de movimento, " +
                    $"e a regra de postagem de {posting.Event} lança nela.");
            }

            var valor = linha.Amount switch
            {
                PostingAmount.Net => posting.Net,
                PostingAmount.Tax => posting.Tax,
                _ => posting.Gross,
            };

            // Uma parcela a zero — imposto nulo numa factura isenta — não gera
            // linha: o SAF-T recusa valores não positivos, e uma linha de zero
            // não diz nada. **O equilíbrio aguenta**, porque tirar a mesma
            // parcela dos dois lados mantém a igualdade.
            if (valor <= 0)
            {
                continue;
            }

            linhas.Add(new NewJournalLine(
                conta.Id,
                conta.Code,
                linha.Side,
                valor,
                linha.Description,
                posting.CostCentreId,
                posting.DocumentNumber));
        }

        JournalEntry lancamento;

        try
        {
            lancamento = JournalEntry.Post(
                diario,
                arquivo,
                posting.Date,
                periodo,
                $"{posting.Description} ({posting.DocumentNumber})",
                TransactionType.N,
                posting.SourceId,
                linhas,
                posting.At);
        }
        catch (UnbalancedEntryException error)
        {
            // Não devia acontecer: a regra equilibra simbolicamente, e retirar
            // uma parcela nula tira-a dos dois lados. Se chegar aqui, é a
            // invariante da regra que está furada — e vale mais dizê-lo do que
            // gravar um lançamento torto.
            return DocumentPostingResult.Failed(
                $"A regra de postagem de {posting.Event} produziu um lançamento que não " +
                $"equilibra: {error.Message}");
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return DocumentPostingResult.Failed(error.Message);
        }

        await store.AddEntryAsync(lancamento, cancellationToken);

        return DocumentPostingResult.Posted(lancamento.Id, lancamento.TransactionId);
    }

    /// <summary>
    /// `FT S001/42` vira `FT-S001-42`.
    ///
    /// <para>
    /// O <c>DocArchivalNumber</c> do SAF-T não admite espaços e vai até 20
    /// caracteres. Uma série longa de mais é recusada em vez de truncada: o
    /// número de arquivo é como se encontra o documento no arquivo, e um
    /// truncado deixaria de o encontrar — e podia colidir com outro.
    /// </para>
    /// </summary>
    public static bool TryArchivalNumber(string documentNumber, out string archival, out string error)
    {
        archival = (documentNumber ?? string.Empty)
            .Trim()
            .Replace(' ', '-')
            .Replace('/', '-');

        if (archival.Length == 0)
        {
            error = "O documento não tem número, e o número de arquivo deriva dele.";
            return false;
        }

        if (archival.Length > 20)
        {
            error =
                $"O número de arquivo derivado de '{documentNumber}' tem {archival.Length} " +
                "caracteres, e o SAF-T admite 20. Encurte o código da série.";

            return false;
        }

        error = string.Empty;
        return true;
    }
}

/// <summary>
/// Quem consta como autor de um lançamento automático.
/// </summary>
public static class PostingSources
{
    /// <summary>
    /// <c>SourceID</c> do SAF-T numa postagem automática.
    ///
    /// <para>
    /// <strong>É o sistema, e não a pessoa</strong> — e isso é informação, não
    /// perda. Quem emitiu a factura está na trilha de auditoria e no próprio
    /// documento; o lançamento foi produzido por uma regra, sem ninguém o
    /// teclar. Um auditor que veja este valor sabe imediatamente que não houve
    /// mão humana na tradução.
    /// </para>
    ///
    /// <para>
    /// Também não caberia lá o identificador do actor: o campo admite 30
    /// caracteres e um <c>Guid</c> ocupa 36.
    /// </para>
    /// </summary>
    public const string Automatic = "rivo-auto";
}

/// <param name="Net">Base tributável. Num recibo ou pagamento é o próprio total.</param>
/// <param name="Tax">Imposto. Zero onde não há.</param>
/// <param name="CostCentreId">
/// Imputação analítica, quando o documento a tem. Vai para todas as linhas do
/// lançamento — a repartição por linha é contabilidade analítica a sério, e
/// essa depende de um plano analítico carregado.
/// </param>
/// <param name="DocumentNumber">
/// Como o documento se chama para quem o lê. Vai para a descrição do lançamento
/// e para o <c>SourceDocumentID</c> de cada linha.
/// </param>
/// <param name="ArchivalKey">
/// De onde sai o <c>DocArchivalNumber</c>. <strong>Não é sempre o número do
/// documento</strong>, e a distinção não é cosmética: o `TransactionID` do
/// SAF-T é `(data, diário, número de arquivo)` e tem de ser único.
///
/// <para>
/// Nos documentos que o Rivo numera — factura, nota de crédito, recibo — o
/// número é único por construção e serve. **Num documento numerado por
/// terceiros não serve:** dois fornecedores emitem `FT 100` no mesmo dia sem
/// nada de errado, e a chave colidiria. Aí usa-se a identidade do registo, e o
/// número do fornecedor fica na descrição e nas linhas, que é onde se procura.
/// </para>
/// </param>
public sealed record DocumentPosting(
    PostingEvent Event,
    string DocumentNumber,
    string ArchivalKey,
    string Description,
    DateOnly Date,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string SourceId,
    DateTimeOffset At,
    Guid? CostCentreId = null)
{
    /// <summary>
    /// Chave de arquivo para um documento que o Rivo não numera: um prefixo
    /// legível mais os 16 últimos dígitos hexadecimais do identificador.
    ///
    /// <para>
    /// Os <strong>últimos</strong> e não os primeiros: o `Guid` v7 começa pelo
    /// carimbo temporal, e dois registos criados no mesmo milissegundo
    /// partilhariam esse prefixo. A cauda é aleatória.
    /// </para>
    /// </summary>
    public static string KeyFor(string prefix, Guid id) =>
        $"{prefix}-{id.ToString("N")[^16..]}".ToUpperInvariant();
}

public sealed record DocumentPostingResult(
    DocumentPostingOutcome Outcome,
    Guid? EntryId,
    string? TransactionId,
    string? Error)
{
    public static DocumentPostingResult Posted(Guid id, string transactionId) =>
        new(DocumentPostingOutcome.Posted, id, transactionId, null);

    public static DocumentPostingResult NoRule() =>
        new(DocumentPostingOutcome.NoRule, null, null, null);

    public static DocumentPostingResult PeriodClosed(string error) =>
        new(DocumentPostingOutcome.PeriodClosed, null, null, error);

    public static DocumentPostingResult Failed(string error) =>
        new(DocumentPostingOutcome.Failed, null, null, error);
}

public enum DocumentPostingOutcome
{
    Posted,

    /// <summary>
    /// Não há regra para este acontecimento. <strong>Não é erro</strong> — é
    /// contabilidade automática por ligar.
    /// </summary>
    NoRule,

    /// <summary>
    /// O período do documento está fechado. **Trava a operação inteira**: um
    /// documento com data dentro de um período fechado não devia ser emitido,
    /// e emiti-lo sem lançar deixaria um buraco nos livros que ninguém vê.
    /// </summary>
    PeriodClosed,

    /// <summary>
    /// A regra existe e não se consegue honrar — conta em falta, desactivada,
    /// ou diário morto. **Também trava**: quem configurou a regra disse que
    /// estes documentos lançam.
    /// </summary>
    Failed,
}
