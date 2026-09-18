using Rivo.Approval.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.UseCases;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// A ligação entre a política e a sessão que nasce (ADR-067).
///
/// <para>
/// Os prazos têm teste próprio em <see cref="SessionPolicyTests"/>. O que aqui se
/// verifica é a <strong>montagem</strong>: que o login usa a política em vez de
/// um valor fixo, que congela a tolerância na sessão, e que a devolve ao cliente.
/// Era aqui que um esquecimento passava despercebido — a política podia estar
/// certa e não ser chamada.
/// </para>
/// </summary>
public sealed class SessionIssuerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);

    private static AuthenticatedAccount Conta(params string[] permissoes) =>
        new(Guid.CreateVersion7(), "alguem@rivo.ao", ["Manager"], permissoes);

    private static (SessionIssuer Emissor, FakeSessionStore Sessoes) Montar(FakeSessionPolicy politica)
    {
        var sessoes = new FakeSessionStore();

        var emissor = new SessionIssuer(
            sessoes,
            new FakeAccessTokenIssuer(),
            politica,
            new FakeAuditTrail(),
            new RelogioFixo(Agora));

        return (emissor, sessoes);
    }

    [Fact]
    public async Task SessaoNasceComOsPrazosDaPolitica()
    {
        var (emissor, sessoes) = Montar(new FakeSessionPolicy(
            absoluta: TimeSpan.FromHours(12),
            geral: TimeSpan.FromMinutes(30)));

        await emissor.IssueAsync(
            Conta("hr.employees.read"), AuthenticationMethods.Password,
            "10.0.0.1", null, null, CancellationToken.None);

        var sessao = Assert.Single(sessoes.Added);

        Assert.Equal(Agora.AddHours(12), sessao.ExpiresAt);
        Assert.Equal(1800, sessao.IdleTimeoutSeconds);
        Assert.Equal(Agora, sessao.LastSeenAt);
    }

    /// <summary>
    /// O requisito, verificado no caminho real e não só na política: quem decide
    /// aprovações recebe o limite curto.
    /// </summary>
    [Fact]
    public async Task QuemDecide_AbreSessaoComToleranciaCurta()
    {
        var (emissor, sessoes) = Montar(new FakeSessionPolicy(
            geral: TimeSpan.FromMinutes(30),
            decisoria: TimeSpan.FromMinutes(15)));

        await emissor.IssueAsync(
            Conta(ApprovalPermissions.RequestsDecide), AuthenticationMethods.Password,
            "10.0.0.1", null, null, CancellationToken.None);

        Assert.Equal(900, Assert.Single(sessoes.Added).IdleTimeoutSeconds);
    }

    /// <summary>
    /// O cliente tem de receber a janela, senão não pode avisar antes de expulsar
    /// alguém — e a inactividade passou a ser a causa mais comum de fim de sessão.
    /// </summary>
    [Fact]
    public async Task ODesfechoLevaAJanelaDeInactividadeParaOCliente()
    {
        var (emissor, _) = Montar(new FakeSessionPolicy(geral: TimeSpan.FromMinutes(30)));

        var emitida = await emissor.IssueAsync(
            Conta("hr.employees.read"), AuthenticationMethods.Password,
            "10.0.0.1", null, null, CancellationToken.None);

        Assert.Equal(1800, emitida.IdleTimeoutSeconds);

        // Recém-criada, o prazo que vale é o de inactividade e não o absoluto.
        Assert.Equal(Agora.AddMinutes(30), emitida.EffectiveExpiry);
    }

    /// <summary>
    /// O prazo do token é o tecto absoluto, não o de inactividade: a inactividade
    /// é verificada no servidor a cada pedido, e um token curto obrigaria a um
    /// mecanismo de renovação para dar a mesma garantia.
    /// </summary>
    [Fact]
    public async Task OTokenExpiraNoTectoAbsoluto()
    {
        var (emissor, _) = Montar(new FakeSessionPolicy(
            absoluta: TimeSpan.FromHours(12),
            geral: TimeSpan.FromMinutes(30)));

        var emitida = await emissor.IssueAsync(
            Conta("hr.employees.read"), AuthenticationMethods.Password,
            "10.0.0.1", null, null, CancellationToken.None);

        Assert.Equal(Agora.AddHours(12), emitida.Token.ExpiresAt);
    }
}
