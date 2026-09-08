namespace Rivo.Settings.Application.Tests;

public class ImportSuppliersFromCsvTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public async Task ExecuteAsync_ImportaCadaLinhaValida()
    {
        var suppliers = new FakeSupplierDirectory();
        var import = new ImportSuppliersFromCsv(suppliers);
        const string csv = """
            Nome,NIF,Morada,Cidade,Pais,IBAN,Email,Telefone
            Fornecedor A,111222333,Rua Che Guevara 88,Luanda,AO,AO0600000000,geral@a.ao,923000000
            Fornecedor B,444555666,Rua Amilcar Cabral 3,Benguela,AO,,,
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.Imported, result.Outcome);
        Assert.Equal(2, result.Summary!.Imported);
        Assert.Equal("AO0600000000", suppliers.Registered[0].Iban);
        Assert.Null(suppliers.Registered[1].Iban);
    }

    [Fact]
    public async Task ExecuteAsync_NifRepetido_ContaComoDuplicado()
    {
        var suppliers = new FakeSupplierDirectory();
        var import = new ImportSuppliersFromCsv(suppliers);
        const string csv = """
            Nome,NIF,Morada,Cidade,Pais
            Fornecedor A,111222333,Rua A,Luanda,AO
            Fornecedor A outra vez,111222333,Rua B,Luanda,AO
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Imported);
        Assert.Equal(1, result.Summary.Duplicates);
    }

    [Fact]
    public async Task ExecuteAsync_CabecalhoSemNif_Recusa()
    {
        var import = new ImportSuppliersFromCsv(new FakeSupplierDirectory());
        const string csv = """
            Nome
            Fornecedor A
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.InvalidHeader, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_FicheiroAntigoSemMorada_Recusa()
    {
        // ⚠ Quebra deliberada. Os ficheiros que funcionavam antes de
        // 2026-09-08 param aqui, e é isso que se quer: importá-los criaria
        // fornecedores sem morada, que a exportação SAF-T não consegue emitir
        // — e nessa altura já ninguém sabe a morada de cada um.
        var import = new ImportSuppliersFromCsv(new FakeSupplierDirectory());
        const string csv = """
            Nome,NIF,IBAN
            Fornecedor A,111222333,
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.InvalidHeader, result.Outcome);

        // A mensagem tem de nomear as colunas em falta: "cabeçalho inválido"
        // sozinho não diz a quem prepara o ficheiro o que acrescentar.
        Assert.Contains("Morada", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_FicheiroVazio_DevolveVazio()
    {
        var import = new ImportSuppliersFromCsv(new FakeSupplierDirectory());

        var result = await import.ExecuteAsync(string.Empty, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.Empty, result.Outcome);
    }
}
