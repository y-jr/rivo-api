using System.Buffers.Text;
using System.Text;
using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Notifications.Contracts;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Recuperação de password pelo próprio, por correio electrónico (ADR-065) — o
/// «esqueci-me da password» do ecrã de entrada.
///
/// <para>
/// <strong>Responde sempre o mesmo, e é a decisão central.</strong> Endereço com
/// conta, endereço sem conta, conta desactivada: a resposta é igual nos três
/// casos. Esta rota é pública por necessidade — quem perdeu a password não se
/// consegue autenticar — e uma resposta que distinguisse os casos transformava-a
/// num verificador de quem trabalha na empresa, que é informação que não se dá a
/// quem chega.
/// </para>
///
/// <para>
/// O que faz a diferença é o correio: só sai para um endereço que tenha conta
/// activa. Quem não recebe nada não sabe se foi por não ter conta ou por ter
/// escrito o endereço errado — e é assim que deve ser.
/// </para>
/// </summary>
public sealed class RecoverPassword(
    IUserAccounts accounts,
    IAuditTrail audit,
    INotifier notifier)
{
    public async Task ExecuteAsync(
        string email,
        string linkBase,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var inicio = await accounts.BeginPasswordRecoveryAsync(email, cancellationToken);

        if (!inicio.Found)
        {
            // **Auditado mesmo quando não há conta.** É o registo de que alguém
            // tentou recuperar um endereço — e uma sequência destes contra
            // endereços diferentes é precisamente o que se quer poder ver
            // depois. O endereço vai no registo; a resposta a quem pediu não
            // muda.
            await audit.RecordAsync(
                new AuditRecord(
                    AuditActions.PasswordRecoveryRequested,
                    AuditEntityTypes.User,

                    // Sem conta não há identificador: a entidade é o endereço
                    // tentado, que é o que se pode registar sem inventar nada.
                    email.Trim(),
                    context,
                    NewValue: """{"found":false}"""),
                cancellationToken);

            return;
        }

        var userId = inicio.UserId!.Value;

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.PasswordRecoveryRequested,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"email":"{{email.Trim()}}","found":true}"""),
            cancellationToken);

        // O testemunho vai no endereço, e não na resposta — mesma disciplina do
        // convite (ADR-059). Se voltasse a quem pediu, bastava pedir em nome de
        // outra pessoa para lhe entrar na conta.
        var link = $"{linkBase.TrimEnd('/')}/recuperar?u={userId}&t={Codificar(inicio.Token!)}";

        await notifier.QueueAsync(
            new NotificationRequest(
                RecipientUserId: userId,
                Type: NotificationTypes.PasswordRecovery,
                Title: "Recuperar a sua password do Rivo",
                Message:
                    "Foi pedida a recuperação da password desta conta.\n\n" +
                    "Se foi você, siga a ligação abaixo para escolher uma password nova.\n\n" +
                    "A ligação é de uso único e expira. **Se não foi você, ignore esta mensagem** — "
                    + "a sua password actual continua a funcionar e ninguém entrou na conta.",

                // Sem entrega externa esta notificação não serve para nada: quem
                // perdeu a password não entra na aplicação para a ler lá dentro.
                // O tipo está na lista dos que exigem entrega, e enfileirá-lo
                // sem isto rebenta em vez de ficar calado.
                SendEmail: true,
                ActionUrl: link,
                ActionLabel: "Escolher uma password nova"),
            cancellationToken);
    }

    /// <summary>
    /// O testemunho do Identity traz <c>+</c>, <c>/</c> e <c>=</c>, que não
    /// sobrevivem a uma query string sem serem reescritos. Mesma codificação do
    /// convite.
    /// </summary>
    private static string Codificar(string token) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
}

/// <summary>
/// Conclui a recuperação: consome o testemunho e fixa a password nova.
///
/// <para>
/// Público por necessidade, como aceitar um convite. O que o protege é o
/// testemunho — de uso único, com prazo, e entregue apenas no endereço de
/// correio da conta.
/// </para>
/// </summary>
public sealed class CompletePasswordRecovery(IUserAccounts accounts, IAuditTrail audit)
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
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            // Um testemunho que não descodifica é indistinguível de um expirado
            // para quem o enviou — e dizer «malformado» ensinaria a distinguir
            // uma tentativa inválida de uma que chegou tarde.
            return PasswordChangeOutcome.Rejected(["Ligação inválida ou expirada."]);
        }

        var outcome = await accounts.CompletePasswordRecoveryAsync(
            userId, decoded, password, cancellationToken);

        if (outcome.Result is not PasswordChangeResult.Changed)
        {
            // **A falha também fica registada.** Um testemunho recusado é o
            // rasto de uma ligação já usada, expirada, ou de alguém a tentar
            // adivinhar — e é isso que se quer ver na trilha.
            await audit.RecordAsync(
                new AuditRecord(
                    AuditActions.PasswordRecoveryFailed,
                    AuditEntityTypes.User,
                    userId.ToString(),
                    context),
                cancellationToken);

            return outcome;
        }

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.PasswordRecoveryCompleted,
                AuditEntityTypes.User,
                userId.ToString(),
                context),
            cancellationToken);

        return outcome;
    }
}
