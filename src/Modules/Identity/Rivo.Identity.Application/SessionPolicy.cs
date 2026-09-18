using Rivo.Approval.Contracts;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Application;

/// <summary>
/// Os dois prazos de uma sessão, resolvidos a partir de configuração.
///
/// <para>
/// Vive em Application e não em Infrastructure porque é <strong>regra</strong>,
/// não mecanismo: decide quanto tempo uma autorização dura e a quem se aplica o
/// limite mais curto. A infraestrutura só liga os valores da configuração a este
/// objecto.
/// </para>
/// </summary>
public sealed class SessionPolicy(SessionPolicyOptions options) : ISessionPolicy
{
    public TimeSpan AbsoluteLifetime => TimeSpan.FromMinutes(options.AbsoluteLifetimeMinutes);

    public TimeSpan IdleTimeoutFor(IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var decisor = permissions.Any(permissao => options.DecisionPermissions.Contains(
            permissao, StringComparer.OrdinalIgnoreCase));

        // O menor dos dois, sempre. Se alguém configurar o limite decisório
        // acima do geral, a intenção era claramente apertar e não alargar — e
        // alargar em silêncio para quem decide pagamentos seria o pior sítio
        // onde falhar.
        var minutos = decisor
            ? Math.Min(options.DecisionIdleTimeoutMinutes, options.IdleTimeoutMinutes)
            : options.IdleTimeoutMinutes;

        return TimeSpan.FromMinutes(minutos);
    }
}

/// <summary>
/// Configuração dos prazos de sessão. Secção <c>Session</c>.
///
/// <para>
/// <strong>Os valores por omissão são a referência dos documentos, não uma
/// escolha minha.</strong> `docs/rivo-dados-integracoes-seguranca-v1.md` dá 15
/// minutos para perfis decisórios como ponto de partida e deixa expressamente em
/// aberto se o limite deve ser uniforme ou por perfil — decisão do cliente. Está
/// tudo em configuração para essa conversa não exigir uma alteração de código.
/// </para>
/// </summary>
public sealed class SessionPolicyOptions
{
    public const string SectionName = "Session";

    /// <summary>
    /// O tecto absoluto, em minutos. Por omissão 12 horas.
    ///
    /// <para>
    /// <strong>Era 60 minutos, e era o defeito.</strong> Com expiração só
    /// absoluta, uma hora expulsava quem estava a trabalhar — a meio de um
    /// formulário, sem aviso — e não protegia nada de quem se levantava da
    /// secretária. Agora o trabalho contínuo não é interrompido e quem para é
    /// desligado; são preocupações diferentes e passaram a ter prazos diferentes.
    /// </para>
    /// </summary>
    public int AbsoluteLifetimeMinutes { get; init; } = 12 * 60;

    /// <summary>Inactividade tolerada por omissão, em minutos.</summary>
    public int IdleTimeoutMinutes { get; init; } = 30;

    /// <summary>
    /// Inactividade tolerada por quem tem autoridade de decisão. Referência dos
    /// documentos: 15 minutos.
    /// </summary>
    public int DecisionIdleTimeoutMinutes { get; init; } = 15;

    /// <summary>
    /// Que permissões marcam alguém como decisor.
    ///
    /// <para>
    /// Por permissão e não por nome de perfil, para sobreviver a um perfil
    /// renomeado e a um perfil novo que ganhe autoridade de decisão sem ninguém
    /// se lembrar de o acrescentar a uma lista.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> DecisionPermissions { get; init; } =
        [ApprovalPermissions.RequestsDecide];

    /// <summary>
    /// Janela de agrupamento da marca de actividade, em segundos.
    ///
    /// <para>
    /// Gravar <c>last_seen_at</c> a cada pedido seria uma escrita por cada
    /// leitura de página. Com 60 segundos, a escrita acontece no máximo uma vez
    /// por minuto e por sessão, e o erro na conta da inactividade é no máximo um
    /// minuto — sempre a favor da segurança, porque a marca fica atrasada e nunca
    /// adiantada.
    /// </para>
    /// </summary>
    public int ActivityResolutionSeconds { get; init; } = 60;
}
