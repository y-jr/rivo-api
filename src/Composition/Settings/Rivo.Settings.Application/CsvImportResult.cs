namespace Rivo.Settings.Application;

/// <summary>
/// Resultado partilhado pelas três importações CSV (Clientes, Colaboradores,
/// Fornecedores — ADR-047). Uma linha malformada não pára o ficheiro:
/// segue para a linha seguinte e fica registada em <see cref="CsvImportSummary.Rows"/>,
/// o mesmo raciocínio de "não recusar o pedido inteiro por um item" que
/// `RegisterInventoryItem` já segue para o resto do sistema.
/// </summary>
public sealed record CsvImportResult(CsvImportOutcome Outcome, CsvImportSummary? Summary, string? Error)
{
    public static CsvImportResult Success(CsvImportSummary summary) =>
        new(CsvImportOutcome.Imported, summary, null);

    public static CsvImportResult InvalidHeader(string error) =>
        new(CsvImportOutcome.InvalidHeader, null, error);

    public static CsvImportResult Empty() =>
        new(CsvImportOutcome.Empty, null, "O ficheiro não tem linhas de dados.");
}

public enum CsvImportOutcome
{
    Imported,

    /// <summary>Falta alguma coluna obrigatória no cabeçalho — 400.</summary>
    InvalidHeader,

    /// <summary>Sem cabeçalho, ou sem nenhuma linha de dados — 400.</summary>
    Empty,
}

public sealed record CsvImportSummary(
    int TotalRows, int Imported, int Duplicates, int Rejected, IReadOnlyList<CsvImportRowResult> Rows);

/// <param name="Line">Número da linha no ficheiro (1 é o cabeçalho, a primeira linha de dados é 2) — para quem corrige a folha e reimporta.</param>
/// <param name="Detail">O identificador criado (ou já existente, em duplicado) quando importada; a razão da rejeição quando rejeitada.</param>
public sealed record CsvImportRowResult(int Line, CsvImportRowOutcome Outcome, string? Detail);

public enum CsvImportRowOutcome
{
    Imported,

    /// <summary>NIF já registado — a linha não cria nada, mas não é erro: reimportar o mesmo ficheiro é idempotente.</summary>
    Duplicate,

    Rejected,
}
