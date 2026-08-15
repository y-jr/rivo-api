using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Cria uma conta de utilizador.
///
/// Não cria Colaborador: essa entidade pertence a `hr`, e um Colaborador pode
/// existir sem login (ADR-004). A ligação entre os dois é explícita e opcional.
/// </summary>
public sealed class RegisterUser(IUserAccounts accounts, IAuditTrail audit)
{
    public async Task<RegisterUserResult> ExecuteAsync(
        string email,
        string password,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.CreateAsync(email, password, cancellationToken);

        if (!outcome.Succeeded)
        {
            // Recusa por password fraca ou e-mail duplicado não é auditada:
            // é validação de entrada, não alteração de estado. Auditar todas as
            // tentativas de registo encheria a trilha de ruído.
            return RegisterUserResult.Rejected(outcome.Errors);
        }

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserRegistered,
                AuditEntityTypes.User,
                outcome.UserId!.Value.ToString(),
                context),
            cancellationToken);

        return RegisterUserResult.Success(outcome.UserId.Value);
    }
}

public sealed record RegisterUserResult(bool Succeeded, Guid? UserId, IReadOnlyList<string> Errors)
{
    public static RegisterUserResult Success(Guid userId) => new(true, userId, []);

    public static RegisterUserResult Rejected(IReadOnlyList<string> errors) => new(false, null, errors);
}
