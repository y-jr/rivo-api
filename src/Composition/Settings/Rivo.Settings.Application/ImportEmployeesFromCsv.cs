using System.Globalization;
using Rivo.Hr.Contracts;

namespace Rivo.Settings.Application;

/// <summary>
/// Importação em massa de Colaboradores via CSV (Analytics & IA, ADR-047) —
/// escreve através de <see cref="IEmployeeDirectory.HireAsync"/>.
///
/// <para>
/// Colunas obrigatórias: <c>Nome</c>, <c>DataAdmissao</c> (formato
/// <c>aaaa-mm-dd</c>). Opcional: <c>Departamento</c>, resolvido por nome
/// exacto — sem correspondência, a linha é rejeitada, não ignorada.
/// </para>
///
/// <para>
/// <strong>Sem detecção de duplicado.</strong> Ao contrário de Clientes e
/// Fornecedores, `hr` não tem NIF nem outra chave natural de colaborador —
/// duas linhas com o mesmo nome criam dois colaboradores, tal como o
/// formulário normal também criaria.
/// </para>
/// </summary>
public sealed class ImportEmployeesFromCsv(IEmployeeDirectory employees)
{
    private static readonly string[] RequiredColumns = ["Nome", "DataAdmissao"];

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

            var dataText = Get("DataAdmissao");

            if (!DateOnly.TryParseExact(dataText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var admissao))
            {
                results.Add(new CsvImportRowResult(
                    line, CsvImportRowOutcome.Rejected, $"Data de admissão inválida: '{dataText}' (use aaaa-mm-dd)."));
                continue;
            }

            var hiredOn = new DateTimeOffset(admissao.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var departamento = Get("Departamento");

            var result = await employees.HireAsync(
                Get("Nome"),
                string.IsNullOrWhiteSpace(departamento) ? null : departamento,
                hiredOn,
                actorId,
                cancellationToken);

            results.Add(result.Outcome switch
            {
                EmployeeHireOutcome.Hired =>
                    new CsvImportRowResult(line, CsvImportRowOutcome.Imported, result.EmployeeId!.Value.ToString()),
                _ => new CsvImportRowResult(line, CsvImportRowOutcome.Rejected, result.Error),
            });
        }

        return CsvImportResult.Success(Summarize(results));
    }

    private static CsvImportSummary Summarize(IReadOnlyList<CsvImportRowResult> rows) =>
        new(
            rows.Count,
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Imported),
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Duplicate),
            rows.Count(r => r.Outcome == CsvImportRowOutcome.Rejected),
            rows);
}
