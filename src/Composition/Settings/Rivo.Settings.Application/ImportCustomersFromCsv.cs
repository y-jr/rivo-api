using Rivo.Commercial.Contracts;

namespace Rivo.Settings.Application;

/// <summary>
/// Importação em massa de Clientes via CSV (Analytics & IA, ADR-047) —
/// escreve através de <see cref="ICustomerDirectory.RegisterAsync"/>, mesma
/// validação e mesma verificação de NIF duplicado do formulário normal.
///
/// <para>
/// Colunas obrigatórias: <c>Nome</c>, <c>NIF</c>, <c>Morada</c>,
/// <c>Cidade</c>, <c>Pais</c>. Opcionais: <c>Email</c>, <c>Telefone</c>.
/// Ordem livre — resolvidas pelo nome no cabeçalho, não pela posição.
/// </para>
/// </summary>
public sealed class ImportCustomersFromCsv(ICustomerDirectory customers)
{
    private static readonly string[] RequiredColumns = ["Nome", "NIF", "Morada", "Cidade", "Pais"];

    public async Task<CsvImportResult> ExecuteAsync(string csvContent, Guid actorId, CancellationToken cancellationToken)
    {
        var parsed = CsvDocument.Parse(csvContent);

        if (parsed is null)
        {
            return CsvImportResult.Empty();
        }

        var (header, rows) = parsed.Value;
        var columns = CsvDocument.IndexColumns(header, RequiredColumns);

        if (columns is null)
        {
            return CsvImportResult.InvalidHeader(
                $"O cabeçalho tem de incluir as colunas: {string.Join(", ", RequiredColumns)}.");
        }

        if (rows.Count == 0)
        {
            return CsvImportResult.Empty();
        }

        var results = new List<CsvImportRowResult>(rows.Count);

        foreach (var (line, fields) in rows)
        {
            string Get(string column) => columns.TryGetValue(column, out var i) && i < fields.Count ? fields[i].Trim() : string.Empty;

            var result = await customers.RegisterAsync(
                Get("Nome"),
                Get("NIF"),
                Get("Morada"),
                Get("Cidade"),
                Get("Pais"),
                OrNull(Get("Email")),
                OrNull(Get("Telefone")),
                actorId,
                cancellationToken);

            results.Add(result.Outcome switch
            {
                CustomerRegistrationOutcome.Registered =>
                    new CsvImportRowResult(line, CsvImportRowOutcome.Imported, result.CustomerId!.Value.ToString()),
                CustomerRegistrationOutcome.DuplicateTaxId =>
                    new CsvImportRowResult(line, CsvImportRowOutcome.Duplicate, result.CustomerId!.Value.ToString()),
                _ => new CsvImportRowResult(line, CsvImportRowOutcome.Rejected, result.Error),
            });
        }

        return CsvImportResult.Success(Summarize(results));
    }

    private static string? OrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static CsvImportSummary Summarize(IReadOnlyList<CsvImportRowResult> rows) =>
        new(
            rows.Count,
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Imported),
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Duplicate),
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Rejected),
            rows);
}
