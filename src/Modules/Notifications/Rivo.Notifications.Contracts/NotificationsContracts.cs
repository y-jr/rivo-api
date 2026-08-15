namespace Rivo.Notifications.Contracts;

/// <summary>
/// Superfície publicada de `notifications`. Assembly sem dependências
/// (ADR-017).
///
/// <para>
/// <strong>Enfileirar, não entregar.</strong> A chamada grava a notificação e
/// devolve; a entrega acontece depois, num worker. É isso que impede que uma
/// falha de envio derrube a operação de negócio que a originou.
/// </para>
///
/// <para>
/// O módulo de origem decide <em>quando</em> notificar e escreve o conteúdo.
/// `notifications` não sabe o que é uma factura ou uma aprovação — só entrega.
/// </para>
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Enfileira uma notificação.
    ///
    /// <para>
    /// <strong>Não lança por falha de entrega</strong>, porque não entrega
    /// nada. Lança apenas se o pedido for inválido ou se a gravação falhar —
    /// e mesmo aí, quem chama deve ponderar se vale a pena falhar a operação
    /// de negócio por causa de uma notificação.
    /// </para>
    /// </summary>
    Task QueueAsync(NotificationRequest request, CancellationToken cancellationToken);
}

/// <param name="RecipientUserId">
/// Utilizador de `identity`. Guardado como identificador — `notifications` não
/// resolve quem a pessoa é, o que evita dependência de compilação.
/// </param>
/// <param name="Type">
/// Categoria da notificação, para o cliente agrupar ou filtrar. Formato
/// "modulo.assunto", como nas permissões e nas acções de auditoria.
/// </param>
/// <param name="SendEmail">
/// Se além da notificação na aplicação deve seguir e-mail. Por omissão não:
/// a maioria não justifica interromper alguém na caixa de correio.
/// </param>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    string Type,
    string Title,
    string Message,
    bool SendEmail = false);

/// <summary>Tipos de notificação emitidos pelos módulos.</summary>
public static class NotificationTypes
{
    public const string AccessProfileAssigned = "identity.access_profile_assigned";
}
