namespace Rivo.Identity.Domain.Sessions;

/// <summary>
/// Sessão de um utilizador autenticado.
///
/// Existe porque o JWT, por si só, não é revogável: uma vez emitido, é válido
/// até expirar. Ao ligar cada token a uma sessão persistida, passa a ser
/// possível terminá-la de imediato — requisito de "bloqueio técnico" herdado
/// do SGAP.
///
/// O endereço IP é registado por exigência de auditoria (BR-9).
///
/// <para>
/// <strong>Tem dois prazos, e é isso que resolve o problema antigo.</strong> A
/// primeira versão tinha só expiração absoluta de 60 minutos, o que dava o pior
/// dos dois mundos: expulsava quem estava a trabalhar e mantinha aberta a sessão
/// de quem tinha saído da secretária. Agora:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <see cref="ExpiresAt"/> — o tecto <strong>absoluto</strong>. Longo, porque não
/// há razão para interromper quem está a trabalhar. Não desliza.
/// </item>
/// <item>
/// <see cref="IdleDeadline"/> — a expiração por <strong>inactividade</strong>,
/// que desliza a cada pedido. É o requisito que faltava
/// (`docs/rivo-dados-integracoes-seguranca-v1.md` §segurança, 15 min como
/// referência para perfis decisórios).
/// </item>
/// </list>
///
/// <para>
/// A sessão morre no <strong>primeiro</strong> dos dois. Trocar um prazo pelo
/// outro seria perder metade da protecção: sem o absoluto, uma sessão mantida
/// viva por um cliente que faz pedidos periódicos duraria para sempre; sem o de
/// inactividade, uma secretária abandonada continua autenticada.
/// </para>
/// </summary>
public sealed class Session
{
    // Construtor sem parâmetros exigido pelo EF Core para materialização.
    private Session()
    {
        IpAddress = string.Empty;
    }

    private Session(
        Guid id,
        Guid userId,
        string ipAddress,
        string? userAgent,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        int idleTimeoutSeconds)
    {
        Id = id;
        UserId = userId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        LastSeenAt = createdAt;
        IdleTimeoutSeconds = idleTimeoutSeconds;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Contador de concorrência optimista (ADR-002, ADR-025).
    ///
    /// Incrementado pela infraestrutura ao gravar, nunca pelo domínio. O
    /// <c>private set</c> existe só para o EF Core o materializar.
    ///
    /// <para>
    /// ⚠ <strong>A marca de actividade não passa por aqui.</strong>
    /// <see cref="LastSeenAt"/> é escrito por instrução directa, sem rastreio e
    /// sem tocar nesta coluna — ver a nota em <c>ISessionStore.TouchAsync</c>.
    /// Se passasse, dois pedidos em paralelo do mesmo utilizador colidiam e um
    /// deles falhava por conflito de concorrência, o que seria absurdo: perder
    /// uma marca de actividade não é conflito nenhum.
    /// </para>
    /// </summary>
    public int Version { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Endereço de origem no momento da autenticação.</summary>
    public string IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Expiração absoluta — o tecto. Não desliza com a actividade.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Instante do último pedido autenticado feito com esta sessão.
    ///
    /// <para>
    /// Não é exacto ao segundo, de propósito: a escrita é agrupada numa janela
    /// (ver <c>ISessionStore.TouchAsync</c>), porque gravar a cada pedido
    /// custaria uma escrita por leitura de página. O erro introduzido é no
    /// máximo o tamanho da janela, e é sempre a favor da segurança — a marca
    /// fica ligeiramente atrasada, nunca adiantada.
    /// </para>
    /// </summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>
    /// Quanto tempo de inactividade esta sessão tolera, em segundos.
    ///
    /// <para>
    /// <strong>Guardado na sessão e não lido da configuração a cada pedido</strong>,
    /// e isso é decisão. Uma sessão nasce com a regra que vigorava quando foi
    /// aberta: baixar o limite na configuração não mata sessões já abertas de
    /// surpresa, e subi-lo não prolonga retroactivamente as que deviam morrer.
    /// A verificação por pedido fica também sem depender de I/O de configuração.
    /// </para>
    /// </summary>
    public int IdleTimeoutSeconds { get; private set; }

    /// <summary>Preenchido quando a sessão é terminada antes de expirar.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Quando a sessão morre por inactividade, se nada mais acontecer.</summary>
    public DateTimeOffset IdleDeadline => LastSeenAt.AddSeconds(IdleTimeoutSeconds);

    /// <summary>
    /// O prazo que vale agora: o mais próximo dos dois.
    ///
    /// <para>
    /// É o que o cliente precisa de saber para avisar antes de expulsar alguém —
    /// e é por isso que vai na resposta do login e na lista de sessões.
    /// </para>
    /// </summary>
    public DateTimeOffset EffectiveExpiry =>
        IdleDeadline < ExpiresAt ? IdleDeadline : ExpiresAt;

    public static Session Start(
        Guid userId,
        string ipAddress,
        string? userAgent,
        DateTimeOffset now,
        TimeSpan lifetime,
        TimeSpan idleTimeout)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Uma sessão tem de pertencer a um utilizador.", nameof(userId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "A duração da sessão tem de ser positiva.");
        }

        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleTimeout), "A tolerância a inactividade tem de ser positiva.");
        }

        // Uma tolerância maior do que o tecto absoluto não é erro de quem chama
        // — é configuração que simplesmente não tem efeito. Fica limitada ao
        // tecto para que `IdleDeadline` nunca prometa mais do que a sessão pode
        // dar, e para a lista de sessões não mostrar uma data impossível.
        var tolerancia = idleTimeout > lifetime ? lifetime : idleTimeout;

        return new Session(
            id: Guid.CreateVersion7(),
            userId: userId,
            // Sem IP conhecido (ex.: pedido interno), guarda-se marcador explícito
            // em vez de nulo, para a auditoria distinguir "desconhecido" de "em falta".
            ipAddress: string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            userAgent: userAgent,
            createdAt: now,
            expiresAt: now.Add(lifetime),
            idleTimeoutSeconds: (int)Math.Ceiling(tolerancia.TotalSeconds));
    }

    /// <summary>
    /// Uma sessão só serve enquanto não tiver sido revogada, não tiver passado o
    /// tecto absoluto, e não tiver estado parada mais do que tolera.
    /// Verificado a cada pedido autenticado, e não apenas no login.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset instant) =>
        RevokedAt is null && instant < ExpiresAt && instant < IdleDeadline;

    /// <summary>
    /// Porque é que a sessão não serve. Para a mensagem poder ser diferente:
    /// «terminou por inactividade» é accionável — volta a entrar e continuas —,
    /// «foi terminada» não é a mesma conversa.
    /// </summary>
    public SessionEndReason? EndReasonAt(DateTimeOffset instant) =>
        RevokedAt is not null ? SessionEndReason.Revoked
        : instant >= ExpiresAt ? SessionEndReason.Expired
        : instant >= IdleDeadline ? SessionEndReason.Idle
        : null;

    /// <summary>
    /// Marca actividade. Devolve <c>false</c> quando a marca anterior é recente
    /// o suficiente para não valer uma escrita.
    ///
    /// <para>
    /// O domínio decide o critério; quem persiste só obedece. Assim a janela de
    /// agrupamento é testável sem base de dados.
    /// </para>
    /// </summary>
    public bool Touch(DateTimeOffset now, TimeSpan resolution)
    {
        if (now <= LastSeenAt || now - LastSeenAt < resolution)
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }

    /// <summary>
    /// Termina a sessão. Idempotente: revogar duas vezes mantém o instante da
    /// primeira revogação, que é o que a auditoria precisa de saber.
    /// </summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public enum SessionEndReason
{
    /// <summary>Alguém a terminou — o próprio, um administrador, ou a desactivação da conta.</summary>
    Revoked,

    /// <summary>Passou o tecto absoluto.</summary>
    Expired,

    /// <summary>Esteve parada mais tempo do que tolera.</summary>
    Idle,
}
