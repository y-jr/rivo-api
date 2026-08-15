using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Identity.Infrastructure.Persistence;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Adapta o <see cref="UserManager{TUser}"/> do ASP.NET Core Identity ao
/// contrato estreito que a camada Application definiu.
/// </summary>
public sealed class UserAccounts(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles) : IUserAccounts
{
    public async Task<CreateAccountOutcome> CreateAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            // UserName é obrigatório no Identity. Sem nome de utilizador
            // separado no domínio, o e-mail serve os dois papéis.
            UserName = email,
            Email = email,
        };

        var result = await users.CreateAsync(user, password);

        return result.Succeeded
            ? CreateAccountOutcome.Success(user.Id)
            : CreateAccountOutcome.Failure([.. result.Errors.Select(error => error.Description)]);
    }

    public async Task<AuthenticatedAccount?> VerifyPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            return null;
        }

        // CheckPasswordAsync compara em tempo constante e respeita o lockout
        // configurado. Não substituir por comparação manual de hashes.
        if (!await users.CheckPasswordAsync(user, password))
        {
            return null;
        }

        var userRoles = await users.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(userRoles);

        return new AuthenticatedAccount(user.Id, user.Email!, [.. userRoles], permissions);
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken) =>
        await users.Users
            .OrderBy(user => user.Email)
            .Select(user => new UserSummary(user.Id, user.Email!))
            .ToListAsync(cancellationToken);

    public async Task<AssignProfileOutcome> AssignProfileAsync(
        Guid userId,
        string profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return AssignProfileOutcome.UserNotFound;
        }

        // Só perfis do catálogo semeado. Recusar perfis desconhecidos impede
        // que um erro de escrita crie silenciosamente um papel sem permissões.
        if (!await roles.RoleExistsAsync(profile))
        {
            return AssignProfileOutcome.ProfileNotFound;
        }

        // AddToRoleAsync falha se o utilizador já pertencer ao perfil; verificar
        // antes torna a operação repetível sem erro.
        if (!await users.IsInRoleAsync(user, profile))
        {
            await users.AddToRoleAsync(user, profile);
        }

        return AssignProfileOutcome.Assigned;
    }

    /// <summary>
    /// Consolida as permissões de todos os perfis do utilizador, sem
    /// repetições. Corre apenas no login, não a cada pedido.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolvePermissionsAsync(IEnumerable<string> roleNames)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roleName in roleNames)
        {
            var role = await roles.FindByNameAsync(roleName);

            if (role is null)
            {
                continue;
            }

            var claims = await roles.GetClaimsAsync(role);

            foreach (var claim in claims.Where(c => c.Type == Permissions.ClaimType))
            {
                permissions.Add(claim.Value);
            }
        }

        return [.. permissions];
    }
}
