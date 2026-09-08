using Rivo.Api.Composition;

namespace Rivo.Api.Tests;

/// <summary>
/// O identificador de cliente que sai no SAF-T.
///
/// <para>
/// <strong>Um `Guid` não cabe.</strong> <c>CustomerID</c> é
/// <c>SAFAOtextTypeMandatoryMax30Car</c> — 1 a 30 caracteres — e a forma
/// canónica de um `Guid` tem 36. A tentação óbvia seria truncar, e é
/// exactamente isso que estes casos existem para impedir: o XSD tem uma
/// restrição de unicidade sobre este campo (<c>CustomerIDConstraint</c>)
/// porque é por ele que as facturas referenciam o cliente. Dois clientes com
/// o mesmo identificador não dão um erro — dão um ficheiro que atribui as
/// facturas de um ao outro.
/// </para>
/// </summary>
public class SaftCustomerIdTests
{
    [Fact]
    public void Cabe_NoLimiteDoXsd()
    {
        var id = SaftMasterData.Identificador(Guid.CreateVersion7());

        Assert.InRange(id.Length, 1, 30);
    }

    [Fact]
    public void NaoUsaCaracteresQueEstragamUmUrl()
    {
        // O XSD aceitaria `+` e `/`. Quem lê o ficheiro e cola o
        // identificador numa query string, não.
        var ids = Enumerable.Range(0, 500)
            .Select(_ => SaftMasterData.Identificador(Guid.CreateVersion7()))
            .ToList();

        Assert.All(ids, id => Assert.DoesNotContain('+', id));
        Assert.All(ids, id => Assert.DoesNotContain('/', id));
        Assert.All(ids, id => Assert.DoesNotContain('=', id));
    }

    [Fact]
    public void GuidsDistintos_DaoIdentificadoresDistintos()
    {
        // ⚠ O caso que apanha um truncamento. Com `ToString()[..30]`, dois
        // `Guid` da versão 7 gerados no mesmo milissegundo continuariam
        // distintos — mas um `[..8]` ou um prefixo qualquer colidiria aqui.
        var ids = Enumerable.Range(0, 5_000)
            .Select(_ => SaftMasterData.Identificador(Guid.CreateVersion7()))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(5_000, ids.Count);
    }

    [Fact]
    public void EReversivel()
    {
        // Não há caminho de volta no código — mas tem de existir, para quem
        // um dia cruzar o ficheiro entregue à AGT com a base de dados. Este
        // caso é a prova de que a informação não se perdeu.
        var original = Guid.CreateVersion7();

        var texto = SaftMasterData.Identificador(original);

        var bytes = Convert.FromBase64String(
            texto.Replace('-', '+').Replace('_', '/') + "==");

        Assert.Equal(original, new Guid(bytes));
    }
}
