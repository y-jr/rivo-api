using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Domain.Tests;

/// <summary>
/// Sessão — existe porque um JWT, por si só, não é revogável. Ligar cada token
/// a uma sessão persistida é o que permite o "bloqueio técnico" herdado do
/// SGAP (ADR-013).
/// </summary>
public class SessionTests
{
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>Tolerancia a inactividade usada pelos casos que nao a estudam.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    // --- Arranque ---------------------------------------------------------

    [Fact]
    public void Start_ExpiresAfterTheGivenLifetime()
    {
        var session = Session.Start(User, "197.149.0.1", "Firefox", Now, Hour, Idle);

        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now.Add(Hour), session.ExpiresAt);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public void Start_WithoutUser_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Session.Start(Guid.Empty, "197.149.0.1", null, Now, Hour, Idle));
    }

    [Fact]
    public void Start_WithNonPositiveLifetime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Session.Start(User, "197.149.0.1", null, Now, TimeSpan.Zero, Idle));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Session.Start(User, "197.149.0.1", null, Now, TimeSpan.FromMinutes(-1), Idle));
    }

    /// <summary>
    /// Sem IP conhecido guarda-se um marcador explícito, não nulo: a auditoria
    /// tem de conseguir distinguir "origem desconhecida" de "campo em falta"
    /// (BR-9). São coisas diferentes e um nulo colapsa-as.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Start_WithoutKnownAddress_RecordsAnExplicitMarker(string ipAddress)
    {
        var session = Session.Start(User, ipAddress, null, Now, Hour, Idle);

        Assert.Equal("unknown", session.IpAddress);
    }

    // --- Validade ---------------------------------------------------------

    /// <summary>
    /// Antes do tecto absoluto e com actividade, serve.
    ///
    /// <para>
    /// <strong>Este teste mudou com o ADR-067, e a mudança é o ponto.</strong>
    /// Antes bastava estar antes do tecto: uma sessão parada 59 minutos era
    /// considerada activa. Agora não é — e não ser é precisamente a protecção que
    /// faltava. Para a sessão chegar aos 59 minutos tem de ter sido usada, e é
    /// isso que o <c>Touch</c> aqui representa.
    /// </para>
    /// </summary>
    [Fact]
    public void IsActiveAt_BeforeExpiry_WithActivity_IsTrue()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        session.Touch(Now.AddMinutes(40), TimeSpan.FromSeconds(60));

        Assert.True(session.IsActiveAt(Now.AddMinutes(59)));
    }

    /// <summary>
    /// O contraponto: sem actividade, os mesmos 59 minutos já não servem. É a
    /// diferença entre «estava a trabalhar» e «deixou a sessão aberta».
    /// </summary>
    [Fact]
    public void IsActiveAt_BeforeExpiry_WithoutActivity_IsFalse()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.False(session.IsActiveAt(Now.AddMinutes(59)));
        Assert.Equal(SessionEndReason.Idle, session.EndReasonAt(Now.AddMinutes(59)));
    }

    /// <summary>A expiração é exclusiva: no instante exacto já não serve.</summary>
    [Fact]
    public void IsActiveAt_ExactlyAtExpiry_IsFalse()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.False(session.IsActiveAt(session.ExpiresAt));
    }

    /// <summary>
    /// A razão de existir da entidade: uma sessão revogada deixa de servir
    /// <em>imediatamente</em>, mesmo com o token ainda dentro da validade.
    /// Se este teste passasse com a verificação de revogação apagada, o
    /// bloqueio técnico não existiria.
    /// </summary>
    [Fact]
    public void IsActiveAt_AfterRevocation_IsFalseEvenBeforeExpiry()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        session.Revoke(Now.AddMinutes(10));

        Assert.False(session.IsActiveAt(Now.AddMinutes(11)));
    }

    // --- Revogação --------------------------------------------------------

    /// <summary>
    /// Idempotente por desenho: o que interessa à auditoria é <em>quando a
    /// sessão deixou de valer</em>, e isso foi na primeira revogação. Deixar a
    /// segunda sobrepor-se falsificaria o instante.
    /// </summary>
    [Fact]
    public void Revoke_KeepsTheInstantOfTheFirstRevocation()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);
        var first = Now.AddMinutes(10);

        session.Revoke(first);
        session.Revoke(Now.AddMinutes(30));

        Assert.Equal(first, session.RevokedAt);
    }

    // --- Expiração por inactividade (ADR-067) -----------------------------

    /// <summary>
    /// O defeito que isto resolve: com expiração só absoluta, uma sessão de 60
    /// minutos expulsava quem estava a trabalhar e mantinha aberta a de quem
    /// tinha saído da secretária. Os dois prazos são agora independentes.
    /// </summary>
    [Fact]
    public void SessaoNasceComOsDoisPrazos()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.Equal(Now.Add(Hour), session.ExpiresAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal((int)Idle.TotalSeconds, session.IdleTimeoutSeconds);
        Assert.Equal(Now.Add(Idle), session.IdleDeadline);
    }

    [Fact]
    public void ParadaMaisDoQueTolera_DeixaDeServir()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.True(session.IsActiveAt(Now.AddMinutes(29)));
        Assert.False(session.IsActiveAt(Now.AddMinutes(31)));

        // E o tecto absoluto ainda estava longe — quem a matou foi a
        // inactividade.
        Assert.Equal(SessionEndReason.Idle, session.EndReasonAt(Now.AddMinutes(31)));
    }

    /// <summary>
    /// A parte que responde à queixa: trabalhar não expulsa ninguém. Cada pedido
    /// empurra o prazo de inactividade para a frente.
    /// </summary>
    [Fact]
    public void ActividadeEmpurraOPrazoDeInactividade()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        // Um pedido aos 20 minutos, quando faltavam 10 para a sessão morrer.
        Assert.True(session.Touch(Now.AddMinutes(20), TimeSpan.FromSeconds(60)));

        // Aos 45 continuaria viva, o que não aconteceria sem o pedido.
        Assert.True(session.IsActiveAt(Now.AddMinutes(45)));
        Assert.Equal(Now.AddMinutes(50), session.IdleDeadline);
    }

    /// <summary>
    /// A actividade não fura o tecto. É metade da protecção: sem isto, um cliente
    /// que faça um pedido a cada minuto mantinha uma sessão viva para sempre.
    /// </summary>
    [Fact]
    public void ActividadeNaoProlongaAlemDoTectoAbsoluto()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        session.Touch(Now.AddMinutes(55), TimeSpan.FromSeconds(60));

        Assert.False(session.IsActiveAt(Now.AddMinutes(61)));
        Assert.Equal(SessionEndReason.Expired, session.EndReasonAt(Now.AddMinutes(61)));
    }

    [Fact]
    public void PrazoQueValeEOMaisProximoDosDois()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        // Recém-criada, é sempre o de inactividade.
        Assert.Equal(session.IdleDeadline, session.EffectiveExpiry);

        // Depois de um pedido tardio, passa a ser o absoluto.
        session.Touch(Now.AddMinutes(50), TimeSpan.FromSeconds(60));

        Assert.Equal(session.ExpiresAt, session.EffectiveExpiry);
    }

    /// <summary>
    /// A janela de agrupamento existe para não escrever na base de dados a cada
    /// pedido. O critério vive no domínio para ser testável sem base de dados.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(59, false)]
    [InlineData(61, true)]
    public void MarcaDeActividade_SoValeEscritaForaDaJanela(int segundos, bool esperado)
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.Equal(esperado, session.Touch(Now.AddSeconds(segundos), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void MarcaNoPassado_EIgnorada()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        // Relógios podem andar para trás (ajuste de NTP). Aceitar uma marca
        // anterior à última encurtaria a sessão de quem está a trabalhar.
        Assert.False(session.Touch(Now.AddMinutes(-5), TimeSpan.FromSeconds(60)));
        Assert.Equal(Now, session.LastSeenAt);
    }

    /// <summary>
    /// Tolerância maior do que o tecto não é erro de quem chama — é configuração
    /// sem efeito. Fica limitada, para `IdleDeadline` não prometer mais do que a
    /// sessão pode dar.
    /// </summary>
    [Fact]
    public void ToleranciaMaiorQueOTecto_FicaLimitadaAoTecto()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, TimeSpan.FromHours(8));

        Assert.Equal((int)Hour.TotalSeconds, session.IdleTimeoutSeconds);
        Assert.Equal(session.ExpiresAt, session.IdleDeadline);
    }

    [Fact]
    public void ToleranciaNaoPositiva_ERecusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Session.Start(User, "197.149.0.1", null, Now, Hour, TimeSpan.Zero));
    }

    /// <summary>
    /// Os três motivos são distinguíveis porque a mensagem tem de ser diferente:
    /// «terminou por inactividade» é accionável, «foi terminada» pode querer
    /// dizer que alguém desactivou a conta.
    /// </summary>
    [Fact]
    public void RevogacaoManda_MesmoComPrazosPorCumprir()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        session.Revoke(Now.AddMinutes(5));

        Assert.Equal(SessionEndReason.Revoked, session.EndReasonAt(Now.AddMinutes(6)));
    }

    [Fact]
    public void SessaoViva_NaoTemMotivoDeFim()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour, Idle);

        Assert.Null(session.EndReasonAt(Now.AddMinutes(10)));
    }
}
