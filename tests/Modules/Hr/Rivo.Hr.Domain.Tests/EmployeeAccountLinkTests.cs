using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// O histórico do vínculo conta↔colaborador (ADR-053).
///
/// <para>
/// Ao contrário de <c>Employee.LinkToUser</c>, que é um setter sem regras,
/// esta entidade <strong>tem</strong> invariantes — e é por isso que elas
/// vivem aqui e não num caso de uso. Um episódio fechado não reabre, e não
/// existe intervalo negativo.
/// </para>
/// </summary>
public class EmployeeAccountLinkTests
{
    private static readonly DateTimeOffset Janeiro = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Abre_Aberto()
    {
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        Assert.True(episodio.IsOpen);
        Assert.Null(episodio.UnlinkedOn);
    }

    [Fact]
    public void Fechar_Marca_Quem_E_Quando()
    {
        var quem = Guid.NewGuid();
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        episodio.Close(Janeiro.AddDays(30), quem);

        Assert.False(episodio.IsOpen);
        Assert.Equal(Janeiro.AddDays(30), episodio.UnlinkedOn);
        Assert.Equal(quem, episodio.UnlinkedByUserId);
    }

    /// <summary>
    /// Refechar reescreveria a história de quem pôde aprovar em nome de quem —
    /// que é o que esta entidade existe para não deixar reescrever.
    /// </summary>
    [Fact]
    public void Fechar_Duas_Vezes_E_Recusado()
    {
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);
        episodio.Close(Janeiro.AddDays(30), null);

        Assert.Throws<InvalidOperationException>(() => episodio.Close(Janeiro.AddDays(60), null));

        // E não deixou o primeiro fecho corrompido pela tentativa.
        Assert.Equal(Janeiro.AddDays(30), episodio.UnlinkedOn);
    }

    [Fact]
    public void Desligar_Antes_De_Ligar_E_Recusado()
    {
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        Assert.Throws<InvalidOperationException>(() => episodio.Close(Janeiro.AddDays(-1), null));
        Assert.True(episodio.IsOpen);
    }

    [Fact]
    public void Episodio_Aberto_Cobre_Tudo_A_Partir_Do_Inicio()
    {
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);

        Assert.False(episodio.CobriaEm(Janeiro.AddDays(-1)));
        Assert.True(episodio.CobriaEm(Janeiro));
        Assert.True(episodio.CobriaEm(Janeiro.AddYears(10)));
    }

    /// <summary>
    /// Fechado no início, aberto no fim. No instante exacto em que se desliga
    /// já não se podia agir — e é essa a fronteira que uma investigação vai
    /// interrogar.
    /// </summary>
    [Fact]
    public void Episodio_Fechado_Nao_Cobre_O_Instante_Do_Desligamento()
    {
        var fim = Janeiro.AddDays(30);
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);
        episodio.Close(fim, null);

        Assert.True(episodio.CobriaEm(Janeiro));
        Assert.True(episodio.CobriaEm(fim.AddTicks(-1)));
        Assert.False(episodio.CobriaEm(fim));
        Assert.False(episodio.CobriaEm(fim.AddDays(1)));
    }

    /// <summary>
    /// Ligar e desligar no mesmo instante não cobre instante nenhum. É um
    /// episódio degenerado mas legítimo — um vínculo criado por engano e
    /// desfeito de imediato — e a consulta forense não deve dizer que essa
    /// conta pôde agir.
    /// </summary>
    [Fact]
    public void Episodio_Instantaneo_Nao_Cobre_Nada()
    {
        var episodio = EmployeeAccountLink.Open(Guid.NewGuid(), Guid.NewGuid(), Janeiro, null);
        episodio.Close(Janeiro, null);

        Assert.False(episodio.CobriaEm(Janeiro));
    }
}
