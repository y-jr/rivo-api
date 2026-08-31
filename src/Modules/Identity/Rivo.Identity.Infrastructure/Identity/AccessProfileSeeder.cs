using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Rivo.Identity.Application.Authorization;
using Rivo.Identity.Contracts;
using Rivo.Identity.Infrastructure.Persistence;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Semeia os Perfis de Acesso e as suas permissões.
///
/// <para>
/// <strong>Idempotente por construção:</strong> cria o que falta e não toca no
/// que existe. Correr duas vezes produz o mesmo estado que correr uma.
/// </para>
///
/// <para>
/// <strong>Não cria utilizadores.</strong> É essa a separação face a dados de
/// negócio, e evita ter uma password administrativa em código. A consequência
/// é que o primeiro Admin precisa de atribuição fora de banda — ver README.
/// </para>
///
/// <para>
/// Nunca remove perfis nem permissões: uma remoção acidental do catálogo em
/// código deixaria utilizadores sem acesso sem ninguém dar por isso. Retirar
/// um perfil é operação deliberada, não efeito secundário de um deploy.
/// </para>
/// </summary>
public sealed class AccessProfileSeeder(
    RoleManager<ApplicationRole> roles,
    ILogger<AccessProfileSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var (profileName, permissions) in AccessProfiles.Catalogue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await roles.FindByNameAsync(profileName);

            if (role is null)
            {
                role = new ApplicationRole { Name = profileName };
                var created = await roles.CreateAsync(role);

                if (!created.Succeeded)
                {
                    var reasons = string.Join("; ", created.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Não foi possível criar o perfil '{profileName}': {reasons}");
                }

                logger.LogInformation("Perfil de Acesso criado: {Profile}", profileName);
            }

            await SyncPermissionsAsync(role, permissions);
        }
    }

    private async Task SyncPermissionsAsync(ApplicationRole role, IReadOnlyList<string> permissions)
    {
        var existing = await roles.GetClaimsAsync(role);

        var alreadyGranted = existing
            .Where(claim => claim.Type == IdentityPermissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !alreadyGranted.Contains(permission)))
        {
            await roles.AddClaimAsync(role, new Claim(IdentityPermissions.ClaimType, permission));
            logger.LogInformation("Permissão {Permission} atribuída ao perfil {Profile}", permission, role.Name);
        }
    }
}
