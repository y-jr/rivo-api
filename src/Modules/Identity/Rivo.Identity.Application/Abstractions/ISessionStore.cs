using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Persistência de sessões. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface ISessionStore
{
    Task AddAsync(Session session, CancellationToken cancellationToken);

    Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Sessões de um utilizador, mais recentes primeiro. Inclui as revogadas e
    /// as expiradas: quem vê a lista precisa de saber de onde entrou, e não só
    /// de onde está.
    /// </summary>
    Task<IReadOnlyList<Session>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revoga todas as sessões activas de um utilizador.
    ///
    /// <para>
    /// É o que dá efeito imediato a desactivar uma conta ou a repor uma
    /// password. Sem isto, um token já emitido continuava a servir até expirar
    /// — e a conta ficava desactivada no papel e aberta na prática.
    /// </para>
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Persiste alterações a uma sessão já materializada (ex.: revogação).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marca actividade numa sessão, se a marca anterior já for antiga o
    /// suficiente para valer a escrita.
    ///
    /// <para>
    /// <strong>Escrita directa, sem rastreio e sem tocar no contador de
    /// concorrência</strong> — e isso é o ponto, não uma optimização. Se passasse
    /// pelo caminho normal, dois pedidos em paralelo do mesmo utilizador
    /// colidiriam no <c>version</c> e um deles falharia. Perder uma marca de
    /// actividade não é conflito nenhum: o outro pedido já a escreveu.
    /// </para>
    ///
    /// <para>
    /// A condição da janela vai na própria instrução, para que duas chamadas
    /// simultâneas não escrevam duas vezes.
    /// </para>
    /// </summary>
    /// <param name="resolution">
    /// Só escreve se <c>last_seen_at</c> for anterior a <c>now - resolution</c>.
    /// </param>
    Task TouchAsync(
        Guid sessionId,
        DateTimeOffset now,
        TimeSpan resolution,
        CancellationToken cancellationToken);
}
