using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rivo.Identity.Infrastructure.Persistence;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Cria os utilizadores iniciais e associa-lhes os Perfis de Acesso.
///
/// <para>
/// <strong>Corre depois de <see cref="AccessProfileSeeder"/>:</strong> não se
/// pode associar alguém a um perfil que ainda não existe.
/// </para>
///
/// <para>
/// <strong>Idempotente:</strong> cria o que falta, não toca no que existe.
/// Nunca altera a password de um utilizador já criado — se o fizesse, cada
/// arranque reporia a credencial configurada por cima de uma que o
/// utilizador possa ter mudado entretanto.
/// </para>
///
/// <para>
/// <strong>Não passa pelas regras de autorização, por desenho.</strong> Usa o
/// <see cref="UserManager{TUser}"/> directamente e não os casos de uso: o
/// bootstrap existe precisamente para o momento em que ainda não há ninguém
/// com autoridade para conceder autoridade. Isto não é uma excepção às regras
/// de autorização — é o passo anterior a elas existirem.
/// </para>
///
/// <para>
/// <strong>Limitação conhecida:</strong> só atribui Perfis de Acesso. A
/// autoridade de decisão prevista em ADR-015 vem do Cargo, que pertence ao
/// módulo `hr` e ainda não existe. Quando existir, é aqui que se estende.
/// </para>
/// </summary>
public sealed class BootstrapUserSeeder(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IOptions<BootstrapOptions> options,
    ILogger<BootstrapUserSeeder> logger)
{
    private readonly BootstrapOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (_options.Users.Count == 0)
        {
            logger.LogInformation(
                "Bootstrap sem utilizadores configurados. Nenhum utilizador inicial criado.");
            return;
        }

        foreach (var configured in _options.Users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SeedUserAsync(configured);
        }
    }

    private async Task SeedUserAsync(BootstrapUser configured)
    {
        // Perfis validados antes de criar o utilizador: mais vale falhar o
        // arranque do que deixar uma conta sem a autoridade pretendida.
        foreach (var profile in configured.Profiles)
        {
            if (!await roles.RoleExistsAsync(profile))
            {
                throw new InvalidOperationException(
                    $"Bootstrap: o perfil '{profile}' não existe. " +
                    "Confirme que consta do catálogo em AccessProfiles.");
            }
        }

        var user = await users.FindByEmailAsync(configured.Email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = configured.Email,
                Email = configured.Email,
                // Conta criada pelo sistema, não por um fluxo de registo com
                // confirmação por e-mail.
                EmailConfirmed = true,
            };

            var created = await users.CreateAsync(user, configured.Password);

            if (!created.Succeeded)
            {
                var reasons = string.Join("; ", created.Errors.Select(error => error.Description));
                throw new InvalidOperationException(
                    $"Bootstrap: não foi possível criar '{configured.Email}': {reasons}");
            }

            // Regista o e-mail, nunca a password.
            logger.LogInformation("Utilizador de bootstrap criado: {Email}", configured.Email);
        }
        else
        {
            logger.LogInformation(
                "Utilizador de bootstrap já existe, mantido inalterado: {Email}", configured.Email);
        }

        await AssignProfilesAsync(user, configured.Profiles);
    }

    private async Task AssignProfilesAsync(ApplicationUser user, IReadOnlyList<string> profiles)
    {
        foreach (var profile in profiles)
        {
            // Verificar antes de atribuir torna a operação repetível: o
            // AddToRoleAsync falharia se o utilizador já pertencesse ao perfil.
            if (await users.IsInRoleAsync(user, profile))
            {
                continue;
            }

            var assigned = await users.AddToRoleAsync(user, profile);

            if (!assigned.Succeeded)
            {
                var reasons = string.Join("; ", assigned.Errors.Select(error => error.Description));
                throw new InvalidOperationException(
                    $"Bootstrap: não foi possível atribuir '{profile}' a '{user.Email}': {reasons}");
            }

            logger.LogInformation(
                "Perfil {Profile} atribuído a {Email} pelo bootstrap", profile, user.Email);
        }
    }
}
