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
/// </summary>
public sealed class Session
{
    // Construtor sem parâmetros exigido pelo EF Core para materialização.
    private Session()
    {
        IpAddress = string.Empty;
    }

    private Session(Guid id, Guid userId, string ipAddress, string? userAgent, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Endereço de origem no momento da autenticação.</summary>
    public string IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Expiração absoluta. Não desliza com a actividade — ver nota abaixo.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Preenchido quando a sessão é terminada antes de expirar.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public static Session Start(
        Guid userId,
        string ipAddress,
        string? userAgent,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Uma sessão tem de pertencer a um utilizador.", nameof(userId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "A duração da sessão tem de ser positiva.");
        }

        return new Session(
            id: Guid.CreateVersion7(),
            userId: userId,
            // Sem IP conhecido (ex.: pedido interno), guarda-se marcador explícito
            // em vez de nulo, para a auditoria distinguir "desconhecido" de "em falta".
            ipAddress: string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            userAgent: userAgent,
            createdAt: now,
            expiresAt: now.Add(lifetime));
    }

    /// <summary>
    /// Uma sessão só serve enquanto não tiver sido revogada nem expirado.
    /// Verificado a cada pedido autenticado, e não apenas no login.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset instant) =>
        RevokedAt is null && instant < ExpiresAt;

    /// <summary>
    /// Termina a sessão. Idempotente: revogar duas vezes mantém o instante da
    /// primeira revogação, que é o que a auditoria precisa de saber.
    /// </summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
