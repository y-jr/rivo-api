using Microsoft.AspNetCore.Identity;
using Rivo.Identity.Contracts;
using Rivo.Identity.Infrastructure.Persistence;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Implementação de <see cref="IUserDirectory"/>. Lê da tabela do ASP.NET
/// Core Identity, que é onde o endereço vive.
///
/// <para>
/// Separada de <see cref="UserAccounts"/> de propósito: aquela é a porta
/// interna do módulo, com criação, autenticação e gestão de conta; esta é a
/// leitura estreita que sai para fora (ADR-017). Quem consome um contrato
/// publicado não deve ganhar, de lambuja, acesso a tudo o que o módulo faz.
/// </para>
/// </summary>
public sealed class UserDirectory(UserManager<ApplicationUser> users) : IUserDirectory
{
    public async Task<UserContact?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        return user?.Email is null ? null : new UserContact(user.Id, user.Email);
    }
}
