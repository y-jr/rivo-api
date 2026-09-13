using System.Buffers.Text;
using System.Text;
using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Notifications.Contracts;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Convida alguém a ter conta: cria-a sem password, atribui-lhe o Perfil de
/// Acesso, e envia ao endereço o testemunho que permite escolher a password
/// (ADR-059).
///
/// <para>
/// <strong>Substitui o registo público.</strong> Até 2026-09-13 qualquer
/// pessoa criava conta em `POST /identity/register` e entrava — sem permissão
/// nenhuma, mas dentro. Numa aplicação de gestão de uma empresa, quem tem
/// conta é quem a empresa decidiu que tem: o acto passa a nascer de quem
/// administra, e não de quem chega.
/// </para>
///
/// <para>
/// <strong>O perfil é obrigatório, e é deliberado.</strong> Uma conta sem
/// perfil não faz nada — era o estado em que o registo público deixava toda a
/// gente, e que obrigava a um segundo acto que ninguém se lembrava de fazer.
/// Convidar alguém sem dizer para quê não é convidar.
/// </para>
/// </summary>
public sealed class InviteUser(
    IUserAccounts accounts,
    IAuditTrail audit,
    INotifier notifier)
{
    public async Task<InviteUserResult> ExecuteAsync(
        string email,
        string profile,
        string linkBase,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!AccessProfiles.AssignableProfiles.Contains(profile, StringComparer.Ordinal))
        {
            return InviteUserResult.UnknownProfile();
        }

        var invitation = await accounts.InviteAsync(email, cancellationToken);

        if (!invitation.Succeeded)
        {
            return InviteUserResult.Rejected(invitation.Errors);
        }

        var userId = invitation.UserId!.Value;

        // A ordem importa: primeiro o perfil, depois o convite. Se o perfil
        // falhasse depois de a pessoa já ter recebido o link, ela escolhia a
        // password e entrava numa conta que não faz nada.
        var atribuido = await accounts.AssignProfileAsync(userId, profile, cancellationToken);

        if (atribuido is not AssignProfileOutcome.Assigned)
        {
            // A conta já existe a esta altura, e não há transacção que abranja
            // as duas operações. Desactiva-se, que é o mais próximo de desfazer
            // que o BR-14 permite — eliminar está fora de questão —, e o
            // convite não chega a sair.
            await accounts.SetActiveAsync(userId, active: false, cancellationToken);

            return InviteUserResult.Rejected(
                ["Não foi possível atribuir o perfil à conta criada. A conta ficou desactivada."]);
        }

        // Auditado por BR-13, pela mesma razão que atribuir um perfil: isto
        // cria um acesso novo ao sistema, e é a operação mais sensível deste
        // módulo depois da própria atribuição.
        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserInvited,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"email":"{{email}}","profile":"{{profile}}"}"""),
            cancellationToken);

        // O testemunho vai no endereço, e não na resposta: é o envio para a
        // caixa de correio que prova que quem aceita é quem foi convidado. Se
        // voltasse a quem convidou, bastava convidar para entrar em nome de
        // outra pessoa.
        var link = $"{linkBase.TrimEnd('/')}/convite?u={userId}&t={Codificar(invitation.Token!)}";

        await notifier.QueueAsync(
            new NotificationRequest(
                RecipientUserId: userId,
                Type: NotificationTypes.UserInvited,
                Title: "Tem acesso ao Rivo",
                Message:
                    "Foi-lhe criada uma conta no Rivo, com o perfil " +
                    $"'{profile}'.\n\n" +
                    "Para escolher a sua password e entrar pela primeira vez, siga esta ligação:\n\n" +
                    $"{link}\n\n" +
                    "A ligação é de uso único e expira. Se não estava à espera deste convite, ignore esta mensagem — " +
                    "sem escolher uma password, ninguém entra na conta.",

                // **Sem isto o convite não sai, e nada o diz.** `SendEmail`
                // tem por omissão `false`, e uma notificação assim nasce
                // `NotRequired`: fica na caixa da aplicação — o único sítio
                // onde quem foi convidado ainda não consegue entrar. Foi
                // assim que o convite chegou a produção, e passou em todos
                // os testes porque nenhum verificava a entrega.
                SendEmail: true),
            cancellationToken);

        return InviteUserResult.Invited(userId);
    }

    /// <summary>
    /// O testemunho do Identity traz `+`, `/` e `=`, que não sobrevivem a uma
    /// query string sem serem reescritos.
    ///
    /// <para>
    /// <c>System.Buffers.Text</c> e não o <c>WebEncoders</c> do ASP.NET: esta
    /// camada não referencia a web, e não é para começar por causa de uma
    /// codificação que a biblioteca base já traz.
    /// </para>
    /// </summary>
    private static string Codificar(string token) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
}

public sealed record InviteUserResult(
    InviteUserOutcome Outcome,
    Guid? UserId,
    IReadOnlyList<string> Errors)
{
    public static InviteUserResult Invited(Guid userId) =>
        new(InviteUserOutcome.Invited, userId, []);

    public static InviteUserResult UnknownProfile() =>
        new(InviteUserOutcome.UnknownProfile, null, []);

    public static InviteUserResult Rejected(IReadOnlyList<string> errors) =>
        new(InviteUserOutcome.Rejected, null, errors);
}

public enum InviteUserOutcome
{
    Invited,

    /// <summary>Perfil que não existe, ou que não é atribuível (ADR-058).</summary>
    UnknownProfile,

    /// <summary>Endereço já com conta, tipicamente.</summary>
    Rejected,
}

/// <summary>
/// Aceita um convite: fixa a password escolhida por quem o recebeu.
///
/// <para>
/// Público por necessidade — quem aceita ainda não tem como se autenticar. O
/// que o protege é o testemunho: de uso único, com prazo, e entregue apenas no
/// endereço de correio da conta.
/// </para>
/// </summary>
public sealed class AcceptInvitation(IUserAccounts accounts, IAuditTrail audit)
{
    public async Task<PasswordChangeOutcome> ExecuteAsync(
        Guid userId,
        string token,
        string password,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            // Testemunho truncado pelo cliente de correio, ou adulterado. É
            // recusa, não avaria — e devolve-se o mesmo que um testemunho
            // errado, para não distinguir os dois casos a quem tenta.
            return PasswordChangeOutcome.Rejected(["Convite inválido ou expirado."]);
        }

        var outcome = await accounts.AcceptInvitationAsync(userId, decoded, password, cancellationToken);

        if (outcome.Result is not PasswordChangeResult.Changed)
        {
            return outcome;
        }

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.InvitationAccepted,
                AuditEntityTypes.User,
                userId.ToString(),
                context),
            cancellationToken);

        return outcome;
    }
}
