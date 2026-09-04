namespace Rivo.Settings.Application.Tests;

public class ImportEmployeesFromCsvTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public async Task ExecuteAsync_ImportaComEDataDepartamento()
    {
        var employees = new FakeEmployeeDirectory("Comercial");
        var import = new ImportEmployeesFromCsv(employees);
        const string csv = """
            Nome,Departamento,DataAdmissao
            Ana Silva,Comercial,2026-01-15
            Bruno Costa,,2026-02-01
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.Imported, result.Outcome);
        Assert.Equal(2, result.Summary!.Imported);
        Assert.Equal(2, employees.Hired.Count);
        Assert.Equal("Comercial", employees.Hired[0].DepartmentName);
        Assert.Null(employees.Hired[1].DepartmentName);
    }

    [Fact]
    public async Task ExecuteAsync_DepartamentoDesconhecido_Rejeita()
    {
        var employees = new FakeEmployeeDirectory("Comercial");
        var import = new ImportEmployeesFromCsv(employees);
        const string csv = """
            Nome,Departamento,DataAdmissao
            Ana Silva,Financeiro,2026-01-15
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Rejected);
        Assert.Empty(employees.Hired);
    }

    [Fact]
    public async Task ExecuteAsync_DataInvalida_RejeitaSemChamarOContrato()
    {
        var employees = new FakeEmployeeDirectory();
        var import = new ImportEmployeesFromCsv(employees);
        const string csv = """
            Nome,DataAdmissao
            Ana Silva,15/01/2026
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Rejected);
        Assert.Empty(employees.Hired);
    }

    [Fact]
    public async Task ExecuteAsync_CabecalhoSemColunaObrigatoria_Recusa()
    {
        var import = new ImportEmployeesFromCsv(new FakeEmployeeDirectory());
        const string csv = """
            Nome
            Ana Silva
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.InvalidHeader, result.Outcome);
    }
}
