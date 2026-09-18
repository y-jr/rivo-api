using Rivo.Approval.Contracts;
using Rivo.Identity.Application;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// Os prazos de sessão (ADR-067).
///
/// <para>
/// O que aqui se testa é a única parte com regra: <strong>quem recebe o limite
/// mais curto</strong>. Os requisitos falam de «perfis decisórios» e deixam em
/// aberto se o limite deve ser uniforme ou por perfil — resolver por permissão é
/// o que sobrevive a essa conversa.
/// </para>
/// </summary>
public sealed class SessionPolicyTests
{
    private static SessionPolicy Politica(
        int geral = 30,
        int decisoria = 15,
        int absoluta = 720) =>
        new(new SessionPolicyOptions
        {
            AbsoluteLifetimeMinutes = absoluta,
            IdleTimeoutMinutes = geral,
            DecisionIdleTimeoutMinutes = decisoria,
        });

    [Fact]
    public void QuemNaoDecide_RecebeOLimiteGeral() =>
        Assert.Equal(
            TimeSpan.FromMinutes(30),
            Politica().IdleTimeoutFor(["hr.employees.read", "finance.invoices.read"]));

    /// <summary>
    /// O requisito: 15 minutos para quem decide. Resolvido pela permissão de
    /// decidir aprovações, não pelo nome do perfil — assim um perfil novo com
    /// autoridade de decisão recebe o limite curto sem ninguém se lembrar dele.
    /// </summary>
    [Fact]
    public void QuemDecideAprovacoes_RecebeOLimiteCurto() =>
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            Politica().IdleTimeoutFor(["hr.employees.read", ApprovalPermissions.RequestsDecide]));

    [Fact]
    public void SemPermissoes_RecebeOLimiteGeral() =>
        Assert.Equal(TimeSpan.FromMinutes(30), Politica().IdleTimeoutFor([]));

    [Fact]
    public void ComparacaoDePermissaoIgnoraCaixa() =>
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            Politica().IdleTimeoutFor(["APPROVAL.REQUESTS.DECIDE"]));

    /// <summary>
    /// Se alguém configurar o limite decisório acima do geral, a intenção era
    /// apertar e não alargar. Alargar em silêncio para quem decide pagamentos
    /// seria o pior sítio onde falhar.
    /// </summary>
    [Fact]
    public void LimiteDecisorioAcimaDoGeral_NaoAlargaNada() =>
        Assert.Equal(
            TimeSpan.FromMinutes(30),
            Politica(geral: 30, decisoria: 60).IdleTimeoutFor([ApprovalPermissions.RequestsDecide]));

    [Fact]
    public void TectoAbsoluto_VemDaConfiguracao() =>
        Assert.Equal(TimeSpan.FromHours(12), Politica(absoluta: 720).AbsoluteLifetime);

    /// <summary>
    /// A lista de permissões decisórias é configurável — a conversa com o cliente
    /// sobre quem conta como decisor não deve exigir uma alteração de código.
    /// </summary>
    [Fact]
    public void PermissoesDecisorias_SaoConfiguraveis()
    {
        var politica = new SessionPolicy(new SessionPolicyOptions
        {
            IdleTimeoutMinutes = 30,
            DecisionIdleTimeoutMinutes = 5,
            DecisionPermissions = ["finance.payments.execute"],
        });

        Assert.Equal(TimeSpan.FromMinutes(5), politica.IdleTimeoutFor(["finance.payments.execute"]));

        // E quem decide aprovações deixa de contar, porque a lista foi
        // substituída e não acrescentada.
        Assert.Equal(TimeSpan.FromMinutes(30), politica.IdleTimeoutFor([ApprovalPermissions.RequestsDecide]));
    }
}
