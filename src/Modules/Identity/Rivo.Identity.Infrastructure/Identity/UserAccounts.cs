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

        // **Conta desactivada falha como se a password estivesse errada.**
        // Distinguir os dois casos diria a quem tenta que o endereço existe e
        // que a conta foi fechada — informação que não se dá a quem não entrou.
        //
        // Tem de ser verificado aqui: `CheckPasswordAsync` compara o hash e
        // **não** olha ao bloqueio, ao contrário do que o comentário anterior
        // dizia. Quem olha é o `SignInManager`, que este módulo não usa.
        if (await users.IsLockedOutAsync(user))
        {
            return null;
        }

        // Compara em tempo constante. Não substituir por comparação manual de
        // hashes.
        if (!await users.CheckPasswordAsync(user, password))
        {
            return null;
        }

        return await ToAuthenticatedAccountAsync(user);
    }

    public async Task<AuthenticatedAccount?> FindByExternalLoginAsync(
        string provider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Procura em `identity.app_user_login`, que o ASP.NET Core Identity já
        // mantém. É por isto que o ADR-032 não precisou de migração nenhuma.
        var user = await users.FindByLoginAsync(provider, providerKey);

        // Desactivar uma conta tem de fechar **todas** as portas. Sem isto, o
        // Google continuava a abrir a que a password já não abria.
        if (user is null || await users.IsLockedOutAsync(user))
        {
            return null;
        }

        return await ToAuthenticatedAccountAsync(user);
    }

    public async Task<LinkExternalLoginResult> LinkExternalLoginAsync(
        string email,
        string provider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByEmailAsync(email);

        // Sem conta com este e-mail não se cria nada: a criação de contas é
        // acto deliberado de quem administra (ADR-016, ADR-032).
        if (user is null)
        {
            return LinkExternalLoginResult.AccountNotFound();
        }

        // O terceiro argumento é o nome legível do provider, mostrado ao
        // utilizador quando lista as suas credenciais.
        var result = await users.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));

        // Recusa típica: esta identidade externa já está ligada a outra conta.
        // Devolve-se sem detalhe — quem chama não pode revelar o motivo.
        if (!result.Succeeded)
        {
            return LinkExternalLoginResult.Rejected();
        }

        return LinkExternalLoginResult.Linked(await ToAuthenticatedAccountAsync(user));
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var contas = await users.Users.OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var agora = DateTimeOffset.UtcNow;
        var resumo = new List<UserSummary>(contas.Count);

        // Uma consulta por conta para os perfis. É N+1, e é aceitável aqui: a
        // lista de contas de uma PME cabe num ecrã, e a alternativa era juntar
        // três tabelas do Identity à mão.
        foreach (var conta in contas)
        {
            var perfis = await users.GetRolesAsync(conta);
            resumo.Add(new UserSummary(conta.Id, conta.Email!, IsActive(conta, agora), [.. perfis]));
        }

        return resumo;
    }

    /// <summary>
    /// Uma conta está activa enquanto não tiver bloqueio no futuro. É a leitura
    /// inversa de <c>SetActiveAsync</c>, e vive aqui para as duas não poderem
    /// divergir.
    /// </summary>
    private static bool IsActive(ApplicationUser user, DateTimeOffset now) =>
        user.LockoutEnd is null || user.LockoutEnd <= now;


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

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return PasswordChangeOutcome.UserNotFound();
        }

        // Verificação explícita antes de `ChangePasswordAsync`, para distinguir
        // "a actual está errada" de "a nova não passa as regras". O Identity
        // devolve as duas como o mesmo insucesso, e quem chama precisa de as
        // separar: uma é 401, a outra é 400.
        if (!await users.CheckPasswordAsync(user, currentPassword))
        {
            return PasswordChangeOutcome.WrongCurrentPassword();
        }

        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);

        return result.Succeeded
            ? PasswordChangeOutcome.Changed()
            : PasswordChangeOutcome.Rejected([.. result.Errors.Select(error => error.Description)]);
    }

    public async Task<PasswordChangeOutcome> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return PasswordChangeOutcome.UserNotFound();
        }

        // Token gerado e consumido no mesmo acto. É a via suportada do Identity
        // para repor sem conhecer a password actual — mexer no hash à mão
        // saltaria as regras de password e o carimbo de segurança.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, newPassword);

        return result.Succeeded
            ? PasswordChangeOutcome.Changed()
            : PasswordChangeOutcome.Rejected([.. result.Errors.Select(error => error.Description)]);
    }

    public async Task<AccountStatusOutcome> SetActiveAsync(
        Guid userId,
        bool active,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return AccountStatusOutcome.UserNotFound;
        }

        // O bloqueio do Identity, e não uma coluna nova: as duas que isto usa
        // já existem na tabela desde o primeiro dia. `MaxValue` é o idioma da
        // biblioteca para "bloqueado até ordem em contrário".
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);

        return AccountStatusOutcome.Changed;
    }

    public async Task<AssignProfileOutcome> RemoveProfileAsync(
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

        if (!await roles.RoleExistsAsync(profile))
        {
            return AssignProfileOutcome.ProfileNotFound;
        }

        // Repetível sem erro, como a atribuição: retirar um perfil que já não
        // está atribuído produz o estado pretendido na mesma.
        if (await users.IsInRoleAsync(user, profile))
        {
            await users.RemoveFromRoleAsync(user, profile);
        }

        return AssignProfileOutcome.Assigned;
    }

    /// <summary>
    /// Constrói a identidade autenticada a partir da conta, resolvendo perfis
    /// e permissões.
    ///
    /// <para>
    /// Está num sítio só porque é o que todos os caminhos de autenticação
    /// produzem — password e Google hoje (ADR-032). Um caminho que resolvesse
    /// os perfis à sua maneira poderia entregar um token com permissões
    /// diferentes das do outro, para a mesma pessoa.
    /// </para>
    /// </summary>
    private async Task<AuthenticatedAccount> ToAuthenticatedAccountAsync(ApplicationUser user)
    {
        var userRoles = await users.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(userRoles);

        return new AuthenticatedAccount(user.Id, user.Email!, [.. userRoles], permissions);
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
