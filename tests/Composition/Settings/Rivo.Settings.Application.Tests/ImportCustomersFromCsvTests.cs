namespace Rivo.Settings.Application.Tests;

public class ImportCustomersFromCsvTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public async Task ExecuteAsync_ImportaCadaLinhaValida()
    {
        var customers = new FakeCustomerDirectory();
        var import = new ImportCustomersFromCsv(customers);
        const string csv = """
            Nome,NIF,Morada,Cidade,Pais,Email,Telefone
            Kianda Lda,123456789,Rua A,Luanda,AO,geral@kianda.ao,923000000
            Muxima SA,987654321,Rua B,Benguela,AO,,
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.Imported, result.Outcome);
        Assert.Equal(2, result.Summary!.Imported);
        Assert.Equal(0, result.Summary.Rejected);
        Assert.Equal(2, customers.Registered.Count);
        Assert.All(customers.Registered, r => Assert.Equal(ActorId, r.ActorId));
    }

    [Fact]
    public async Task ExecuteAsync_NifRepetido_ContaComoDuplicadoNaoComoErro()
    {
        var customers = new FakeCustomerDirectory();
        var import = new ImportCustomersFromCsv(customers);
        const string csv = """
            Nome,NIF,Morada,Cidade,Pais
            Kianda Lda,123456789,Rua A,Luanda,AO
            Kianda Lda (repetido),123456789,Rua A,Luanda,AO
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Imported);
        Assert.Equal(1, result.Summary.Duplicates);
        Assert.Single(customers.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_LinhaSemNome_RejeitadaSemPararOFicheiro()
    {
        var customers = new FakeCustomerDirectory();
        var import = new ImportCustomersFromCsv(customers);
        const string csv = """
            Nome,NIF,Morada,Cidade,Pais
            ,123456789,Rua A,Luanda,AO
            Muxima SA,987654321,Rua B,Benguela,AO
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Rejected);
        Assert.Equal(1, result.Summary.Imported);

        var rejeitada = result.Summary.Rows.Single(r => r.Outcome == CsvImportRowOutcome.Rejected);
        Assert.Equal(2, rejeitada.Line);
    }

    [Fact]
    public async Task ExecuteAsync_CabecalhoSemColunaObrigatoria_Recusa()
    {
        var import = new ImportCustomersFromCsv(new FakeCustomerDirectory());
        const string csv = """
            Nome,NIF
            Kianda Lda,123456789
            """;

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.InvalidHeader, result.Outcome);
        Assert.Null(result.Summary);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_SoCabecalhoSemLinhas_DevolveVazio()
    {
        var import = new ImportCustomersFromCsv(new FakeCustomerDirectory());
        const string csv = "Nome,NIF,Morada,Cidade,Pais";

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(CsvImportOutcome.Empty, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_CampoComVirgulaEntreAspas_LidoInteiro()
    {
        var customers = new FakeCustomerDirectory();
        var import = new ImportCustomersFromCsv(customers);
        const string csv = "Nome,NIF,Morada,Cidade,Pais\n\"Kianda, Lda\",123456789,Rua A,Luanda,AO";

        var result = await import.ExecuteAsync(csv, ActorId, CancellationToken.None);

        Assert.Equal(1, result.Summary!.Imported);
        Assert.Equal("Kianda, Lda", customers.Registered.Single().Name);
    }
}
