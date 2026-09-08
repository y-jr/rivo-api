using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rivo.Fiscal.Application;

namespace Rivo.Fiscal.Infrastructure.Tests;

/// <summary>
/// A verificação de arranque, através do registo real do módulo.
///
/// <para>
/// <strong>Os testes de <c>CamposEmFalta</c> provam a decisão; estes provam a
/// ligação.</strong> São coisas diferentes, e esta sessão já mostrou duas
/// vezes que a segunda falha sozinha — um <c>using</c> em falta que só o
/// teste de arquitectura apanhou, e um <c>@import</c> que o PostCSS descartou
/// em silêncio. Uma verificação escrita e não ligada é pior do que nenhuma:
/// dá a sensação de estar coberta.
/// </para>
///
/// <para>
/// Resolver <c>IOptions&lt;T&gt;.Value</c> dispara os <c>IValidateOptions</c>
/// registados — é isso que aqui se exercita. O <c>ValidateOnStart</c> faz o
/// mesmo acontecer no arranque em vez de na primeira utilização, e essa parte
/// é comportamento da plataforma.
/// </para>
/// </summary>
public class ArranqueComEmpresaInvalidaTests
{
    private static IServiceProvider Construir(params (string Chave, string Valor)[] empresa)
    {
        var definicoes = new Dictionary<string, string?>
        {
            // O módulo exige-a antes de chegar às opções da empresa. Não se
            // liga a nada nestes testes: nenhum deles toca na base de dados.
            ["ConnectionStrings:Rivo"] = "Server=nao-usado;Database=nao-usado;",
        };

        foreach (var (chave, valor) in empresa)
        {
            definicoes[$"Company:{chave}"] = valor;
        }

        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(definicoes)
            .Build();

        return new ServiceCollection()
            .AddFiscalModule(configuracao)
            .BuildServiceProvider();
    }

    private static OptionsValidationException Recusa(IServiceProvider fornecedor) =>
        Assert.Throws<OptionsValidationException>(
            () => fornecedor.GetRequiredService<IOptions<CompanyOptions>>().Value);

    [Fact]
    public void SemNomeNemNif_Recusa()
    {
        var erro = Recusa(Construir());

        Assert.Contains(erro.Failures, f => f.Contains("Company:Name"));
        Assert.Contains(erro.Failures, f => f.Contains("Company:TaxRegistrationNumber"));
    }

    [Fact]
    public void ComNifCurto_Recusa_EDizQuantosCaracteresTem()
    {
        // ⚠ O caso que faltava. Nove dígitos passavam a verificação anterior e
        // produziam depois um SAF-T que a AGT recusa.
        var erro = Recusa(Construir(
            ("Name", "Angotech, Lda."),
            ("TaxRegistrationNumber", "541700000")));

        var falha = Assert.Single(erro.Failures);

        Assert.Contains("Company:TaxRegistrationNumber", falha);

        // A mensagem tem de dizer o que está errado, e não repetir "está por
        // preencher" a quem preencheu — foi por isso que a validação deixou de
        // ser uma cadeia constante.
        Assert.Contains("9 caracteres", falha);
        Assert.DoesNotContain("por preencher", falha);
    }

    [Fact]
    public void ComNifValido_Arranca()
    {
        var fornecedor = Construir(
            ("Name", "Angotech, Lda."),
            ("TaxRegistrationNumber", "5417000000"));

        var opcoes = fornecedor.GetRequiredService<IOptions<CompanyOptions>>().Value;

        Assert.Equal("Angotech, Lda.", opcoes.Name);

        // E os valores por omissão que não vêm da configuração continuam lá.
        Assert.Equal("0", opcoes.SoftwareValidationNumber);
        Assert.Equal("Luanda", opcoes.Address.City);
    }
}
