using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Domain.Tests;

/// <summary>
/// O histórico do vínculo conta↔cliente (ADR-055).
///
/// <para>
/// Mesmas invariantes de <c>EmployeeAccountLink</c>, e testadas à parte de
/// propósito: são entidades de bounded contexts distintos, e partilhar os
/// testes acoplaria os dois domínios por coincidência de estrutura.
/// </para>
/// </summary>
public class CustomerAccountLinkTests
{
    private static readonly DateTimeOffset Janeiro = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Abre_Aberto()
    {
        var episodio = CustomerAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        Assert.True(episodio.IsOpen);
        Assert.Null(episodio.UnlinkedOn);
    }

    [Fact]
    public void Fechar_Marca_Quem_E_Quando()
    {
        var quem = Guid.NewGuid();
        var episodio = CustomerAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        episodio.Close(Janeiro.AddDays(30), quem);

        Assert.False(episodio.IsOpen);
        Assert.Equal(Janeiro.AddDays(30), episodio.UnlinkedOn);
        Assert.Equal(quem, episodio.UnlinkedByUserId);
    }

    /// <summary>
    /// Reescrever um episódio apagaria o registo de quem pôde submeter
    /// comprovativos de pagamento em nome daquele cliente.
    /// </summary>
    [Fact]
    public void Fechar_Duas_Vezes_E_Recusado()
    {
        var episodio = CustomerAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);
        episodio.Close(Janeiro.AddDays(30), null);

        Assert.Throws<InvalidOperationException>(() => episodio.Close(Janeiro.AddDays(60), null));
        Assert.Equal(Janeiro.AddDays(30), episodio.UnlinkedOn);
    }

    [Fact]
    public void Desligar_Antes_De_Ligar_E_Recusado()
    {
        var episodio = CustomerAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        Assert.Throws<InvalidOperationException>(() => episodio.Close(Janeiro.AddDays(-1), null));
        Assert.True(episodio.IsOpen);
    }

    [Fact]
    public void Episodio_Fechado_Nao_Cobre_O_Instante_Do_Desligamento()
    {
        var fim = Janeiro.AddDays(30);
        var episodio = CustomerAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);
        episodio.Close(fim, null);

        Assert.True(episodio.CobriaEm(Janeiro));
        Assert.True(episodio.CobriaEm(fim.AddTicks(-1)));
        Assert.False(episodio.CobriaEm(fim));
    }

    /// <summary>
    /// O retroactivo desta migração usa `0001-01-01` como sentinela, porque
    /// `commercial.customer` não tem coluna de data nenhuma a que atribuir o
    /// vínculo. A consequência tem de ser esta: cobre qualquer instante, e
    /// erra para o lado de não excluir ninguém indevidamente.
    /// </summary>
    [Fact]
    public void Episodio_Com_Sentinela_Cobre_Qualquer_Instante()
    {
        var episodio = CustomerAccountLink.Open(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.MinValue, linkedByUserId: null);

        Assert.True(episodio.CobriaEm(Janeiro));
        Assert.True(episodio.CobriaEm(DateTimeOffset.MinValue));
        Assert.True(episodio.CobriaEm(Janeiro.AddYears(100)));

        // E continua a distinguir-se por não ter autor.
        Assert.Null(episodio.LinkedByUserId);
    }
}
