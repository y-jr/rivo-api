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

    // --- Arranque ---------------------------------------------------------

    [Fact]
    public void Start_ExpiresAfterTheGivenLifetime()
    {
        var session = Session.Start(User, "197.149.0.1", "Firefox", Now, Hour);

        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now.Add(Hour), session.ExpiresAt);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public void Start_WithoutUser_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Session.Start(Guid.Empty, "197.149.0.1", null, Now, Hour));
    }

    [Fact]
    public void Start_WithNonPositiveLifetime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Session.Start(User, "197.149.0.1", null, Now, TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Session.Start(User, "197.149.0.1", null, Now, TimeSpan.FromMinutes(-1)));
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
        var session = Session.Start(User, ipAddress, null, Now, Hour);

        Assert.Equal("unknown", session.IpAddress);
    }

    // --- Validade ---------------------------------------------------------

    [Fact]
    public void IsActiveAt_BeforeExpiry_IsTrue()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour);

        Assert.True(session.IsActiveAt(Now.AddMinutes(59)));
    }

    /// <summary>A expiração é exclusiva: no instante exacto já não serve.</summary>
    [Fact]
    public void IsActiveAt_ExactlyAtExpiry_IsFalse()
    {
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour);

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
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour);

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
        var session = Session.Start(User, "197.149.0.1", null, Now, Hour);
        var first = Now.AddMinutes(10);

        session.Revoke(first);
        session.Revoke(Now.AddMinutes(30));

        Assert.Equal(first, session.RevokedAt);
    }
}
