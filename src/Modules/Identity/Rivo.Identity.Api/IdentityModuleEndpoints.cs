using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Rivo.Audit.Contracts;
using Rivo.Identity.Api.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Identity.Application.UseCases;
using Rivo.Identity.Contracts;

namespace Rivo.Identity.Api;

public static class IdentityModuleEndpoints
{
    /// <summary>
    /// Namespace de rotas do módulo. Cada módulo expõe a sua superfície sob o
    /// seu próprio prefixo; o host não agrega endpoints de módulos.
    /// </summary>
    /// <param name="rateLimitPolicy">
    /// Política de tecto de pedidos a aplicar às três rotas públicas — as
    /// únicas que se alcançam sem token, e portanto as únicas onde um
    /// desconhecido pode insistir.
    ///
    /// <para>
    /// <strong>Vem por parâmetro, e não de uma constante deste módulo.</strong>
    /// O tecto é infraestrutura do host: é ele que sabe que há um reverse proxy
    /// à frente e que o endereço do cliente é de confiar. Um módulo de negócio
    /// não tem como saber isso, e invocar aqui um nome definido no host
    /// inverteria a direcção da dependência que o ADR-018 fixa. Nulo deixa as
    /// rotas sem tecto, que é o que qualquer teste que monte o módulo sozinho
    /// precisa.
    /// </para>
    /// </param>
    public static IEndpointRouteBuilder MapIdentityModule(
        this IEndpointRouteBuilder endpoints,
        string? rateLimitPolicy = null)
    {
        var group = endpoints.MapGroup("/identity");

        // ⚠ `POST /register` **saiu a 2026-09-13** (ADR-059). Qualquer pessoa
        // criava conta e entrava — sem permissão nenhuma, mas dentro da
        // aplicação. Numa ferramenta de gestão de uma empresa, quem tem conta
        // é quem a empresa decidiu que tem.
        //
        // Quem substitui é o par de rotas de convite, abaixo.
        var entrar = group.MapPost("/login", LogInAsync);
        var entrarComGoogle = group.MapPost("/login/google", LogInWithGoogleAsync);

        // Aceitar um convite é público por necessidade: quem aceita ainda não
        // tem como se autenticar. O que o protege é o testemunho — de uso
        // único, com prazo, e entregue só no endereço de correio da conta.
        var aceitarConvite = group.MapPost("/invitations/acceptance", AcceptInvitationAsync);

        // Recuperar a password é público pela mesma razão, e mais forte: quem a
        // perdeu não tem como se autenticar de forma nenhuma (ADR-065). As duas
        // rotas do par respondem sem revelar se o endereço tem conta.
        var pedirRecuperacao = group.MapPost("/password-recovery", RecoverPasswordAsync);
        var concluirRecuperacao = group.MapPost("/password-recovery/completion", CompletePasswordRecoveryAsync);

        if (!string.IsNullOrWhiteSpace(rateLimitPolicy))
        {
            entrar.RequireRateLimiting(rateLimitPolicy);
            entrarComGoogle.RequireRateLimiting(rateLimitPolicy);

            // Também com tecto: é a outra rota pública que aceita adivinhação,
            // e um testemunho tentado à bruta é tão password como a outra.
            aceitarConvite.RequireRateLimiting(rateLimitPolicy);

            // O par da recuperação, pelas duas razões juntas: o pedido é um
            // gerador de correio que não exige autenticação — sem tecto,
            // enche a caixa de alguém a partir de fora —, e a conclusão aceita
            // testemunhos adivinhados como qualquer outra.
            pedirRecuperacao.RequireRateLimiting(rateLimitPolicy);
            concluirRecuperacao.RequireRateLimiting(rateLimitPolicy);
        }

        // Convidar é acto de quem administra contas — a mesma permissão de
        // repor passwords e activar contas, e pela mesma razão: decide **quem**
        // uma pessoa é no sistema.
        group.MapPost("/invitations", InviteUserAsync)
            .RequireAuthorization(IdentityPermissions.UsersWrite);

        group.MapPost("/logout", LogOutAsync).RequireAuthorization();
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        // Autorização declarada aqui, no endpoint. Os handlers não verificam
        // permissões: se o pedido chega ao handler, já está autorizado.
        group.MapGet("/users", ListUsersAsync)
            .RequireAuthorization(IdentityPermissions.UsersRead);

        group.MapGet("/roles", ListRoles)
            .RequireAuthorization(IdentityPermissions.RolesRead);

        group.MapPost("/users/{userId:guid}/roles", AssignRoleAsync)
            .RequireAuthorization(IdentityPermissions.RolesAssign);


        // --- Conta própria. Só exigem sessão válida: o recurso é quem chama.

        group.MapPost("/me/password", ChangePasswordAsync).RequireAuthorization();
        group.MapGet("/me/sessions", ListSessionsAsync).RequireAuthorization();

        group.MapPost("/me/sessions/{sessionId:guid}/revocation", RevokeSessionAsync)
            .RequireAuthorization();

        // --- Contas de terceiros. Exigem permissão.

        group.MapPost("/users/{userId:guid}/password-reset", ResetPasswordAsync)
            .RequireAuthorization(IdentityPermissions.UsersWrite);

        group.MapPost("/users/{userId:guid}/status", SetStatusAsync)
            .RequireAuthorization(IdentityPermissions.UsersWrite);

        group.MapPost("/users/{userId:guid}/roles/{profile}/removal", RemoveRoleAsync)
            .RequireAuthorization(IdentityPermissions.RolesAssign);

        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        ListUsers listUsers,
        CancellationToken cancellationToken) =>
        Results.Ok(await listUsers.ExecuteAsync(cancellationToken));

    private static IResult ListRoles(ListAccessProfiles listProfiles) =>
        Results.Ok(listProfiles.Execute());

    private static async Task<IResult> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        AssignAccessProfile assignProfile,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await assignProfile.ExecuteAsync(
            userId, request.Profile, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AssignProfileOutcome.Assigned => Results.NoContent(),

            // 404 porque o utilizador é o recurso que o URI identifica.
            AssignProfileOutcome.UserNotFound => Results.NotFound(new { erro = "Utilizador não encontrado." }),

            // 400 e não 404: o perfil vem do corpo, e o recurso que o URI
            // identifica — o utilizador — existe. Um 404 aqui manda procurar o
            // defeito no `userId`, que é o sítio errado. Os perfis válidos
            // seguem na resposta para que o erro se corrija sem consultar
            // outra rota, como já acontece no registo.
            AssignProfileOutcome.ProfileNotFound => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["profile"] =
                    [
                        $"'{request.Profile}' não é um Perfil de Acesso. " +
                        $"Válidos: {string.Join(", ", AccessProfiles.AssignableProfiles)}.",
                    ],
                }),

            _ => Results.Problem("Resultado inesperado ao atribuir o perfil."),
        };
    }

    /// <summary>
    /// Constrói o contexto de auditoria a partir do pedido. É a camada API que
    /// conhece o transporte — o actor, o endereço de origem e o identificador
    /// de correlação não existem mais abaixo.
    /// </summary>
    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }

    private static async Task<IResult> InviteUserAsync(
        InviteUserRequest request,
        InviteUser inviteUser,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // A ligação do convite aponta para o frontend, não para a API. Quem
        // sabe onde o frontend vive é a configuração do ambiente — em
        // desenvolvimento é o Vite, em produção é o domínio publicado.
        var linkBase = configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(linkBase))
        {
            // 501 e não 500: a capacidade não está configurada neste ambiente,
            // e não é defeito do pedido. Mesmo tratamento do Google sem
            // ClientId (ADR-032).
            return Results.Problem(
                "Convidar exige `Frontend:BaseUrl` configurado — é para lá que a ligação do convite aponta.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var result = await inviteUser.ExecuteAsync(
            request.Email, request.Profile, linkBase, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            InviteUserOutcome.Invited =>
                Results.Created($"/identity/users/{result.UserId}", new { userId = result.UserId }),

            InviteUserOutcome.UnknownProfile => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["profile"] =
                    [
                        $"'{request.Profile}' não é um Perfil de Acesso. " +
                        $"Válidos: {string.Join(", ", AccessProfiles.AssignableProfiles)}.",
                    ],
                }),

            InviteUserOutcome.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["convite"] = [.. result.Errors] }),

            _ => Results.Problem("Resultado inesperado ao convidar."),
        };
    }

    /// <summary>
    /// Pede a recuperação da password de um endereço.
    ///
    /// <para>
    /// <strong>Responde 204 sempre</strong> — endereço com conta, sem conta, ou
    /// com conta desactivada. É a decisão de segurança desta rota: distinguir os
    /// casos daria a quem chega um verificador de quem trabalha na empresa.
    /// </para>
    /// </summary>
    private static async Task<IResult> RecoverPasswordAsync(
        RecoverPasswordRequest request,
        RecoverPassword recoverPassword,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            // Corpo vazio é engano de quem chama, e não uma tentativa: aqui
            // recusa-se, porque não há endereço nenhum sobre o qual mentir.
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["email"] = ["Indique o endereço de correio."] });
        }

        var linkBase = configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(linkBase))
        {
            // 501, como o convite: a capacidade não está configurada neste
            // ambiente, e não é defeito do pedido.
            return Results.Problem(
                "Recuperar a password exige `Frontend:BaseUrl` configurado — é para lá que a ligação aponta.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        await recoverPassword.ExecuteAsync(
            request.Email, linkBase, BuildAuditContext(http), cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> CompletePasswordRecoveryAsync(
        CompletePasswordRecoveryRequest request,
        CompletePasswordRecovery completeRecovery,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await completeRecovery.ExecuteAsync(
            request.UserId, request.Token, request.Password, BuildAuditContext(http), cancellationToken);

        return outcome.Result switch
        {
            PasswordChangeResult.Changed => Results.NoContent(),

            // Conta inexistente devolve o mesmo que testemunho inválido — mesma
            // disciplina do convite: quem tenta não fica a saber que
            // identificadores existem.
            PasswordChangeResult.UserNotFound => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["recuperacao"] = ["Ligação inválida ou expirada."] }),

            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["recuperacao"] = [.. outcome.Errors] }),
        };
    }

    private static async Task<IResult> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        AcceptInvitation acceptInvitation,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await acceptInvitation.ExecuteAsync(
            request.UserId, request.Token, request.Password, BuildAuditContext(http), cancellationToken);

        return outcome.Result switch
        {
            PasswordChangeResult.Changed => Results.NoContent(),

            // Conta inexistente devolve o mesmo que testemunho inválido: quem
            // tenta não fica a saber que identificadores existem.
            PasswordChangeResult.UserNotFound => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["convite"] = ["Convite inválido ou expirado."] }),

            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["convite"] = [.. outcome.Errors] }),
        };
    }

    private static async Task<IResult> LogInAsync(
        LoginRequest request,
        LogIn logIn,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // O IP e o user agent vivem no transporte; é a camada API que os conhece
        // e os passa ao caso de uso, que os regista na sessão para auditoria.
        var ipAddress = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = http.Request.Headers.UserAgent.ToString();

        var result = await logIn.ExecuteAsync(
            request.Email,
            request.Password,
            ipAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            http.TraceIdentifier,
            cancellationToken);

        // 401 sem detalhe: não revelar se o endereço existe.
        return result.Succeeded
            ? Results.Ok(new LoginResponse(result.AccessToken!, result.ExpiresAt!.Value, result.IdleTimeoutSeconds!.Value))
            : Results.Unauthorized();
    }

    /// <summary>
    /// Entrada por Google (ADR-032). O corpo traz o ID token que o frontend
    /// obteve junto da Google; o servidor valida-o e, se corresponder a uma
    /// conta existente, devolve o mesmo <see cref="LoginResponse"/> do login
    /// por password — mesmo token, mesma sessão revogável.
    /// </summary>
    private static async Task<IResult> LogInWithGoogleAsync(
        GoogleLoginRequest request,
        LogInWithGoogle logIn,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var ipAddress = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = http.Request.Headers.UserAgent.ToString();

        var result = await logIn.ExecuteAsync(
            request.IdToken,
            ipAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            http.TraceIdentifier,
            cancellationToken);

        return result.Outcome switch
        {
            GoogleLogInOutcome.Succeeded =>
                Results.Ok(new LoginResponse(result.AccessToken!, result.ExpiresAt!.Value, result.IdleTimeoutSeconds!.Value)),

            // 501 e não 401: neste ambiente o Google não está ligado de todo.
            // Um 401 mandaria procurar o defeito na conta de quem tentou, que
            // é o sítio errado — mesma lógica do 501 de `hr` (ADR-015).
            GoogleLogInOutcome.NotConfigured =>
                Results.Problem(
                    "Autenticação com Google não está configurada neste ambiente.",
                    statusCode: StatusCodes.Status501NotImplemented),

            // 401 sem detalhe: não revelar se o token era inválido, se o
            // e-mail não estava verificado ou se não existe conta.
            _ => Results.Unauthorized(),
        };
    }

    private static async Task<IResult> LogOutAsync(
        LogOut logOut,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var sessionId = ReadSessionId(http.User);

        if (sessionId is null)
        {
            return Results.Unauthorized();
        }

        await logOut.ExecuteAsync(sessionId.Value, BuildAuditContext(http), cancellationToken);

        return Results.NoContent();
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? string.Empty;

        if (!Guid.TryParse(userId, out var id))
        {
            return Results.Unauthorized();
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();

        // Devolver as permissões permite ao cliente esconder o que o utilizador
        // não pode fazer. É conveniência de interface — a decisão que conta é a
        // do servidor, não a do cliente.
        var permissions = principal.FindAll(IdentityPermissions.ClaimType).Select(claim => claim.Value).ToArray();

        return Results.Ok(new CurrentUserResponse(id, email, roles, permissions));
    }

    /// <summary>
    /// Lê o identificador da sessão do token. O handler do JWT reescreve "sid"
    /// para o URI de <see cref="ClaimTypes.Sid"/>, por isso tentam-se ambos.
    /// </summary>

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ChangeOwnPassword changePassword,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = ReadUserId(http.User);
        var sessionId = ReadSessionId(http.User);

        if (userId is null || sessionId is null)
        {
            return Results.Unauthorized();
        }

        var resultado = await changePassword.ExecuteAsync(
            userId.Value,
            sessionId.Value,
            request.CurrentPassword,
            request.NewPassword,
            BuildAuditContext(http),
            cancellationToken);

        return resultado.Result switch
        {
            PasswordChangeResult.Changed => Results.NoContent(),

            // 401 e não 403: é a credencial que falha, não a autorização. Quem
            // recebe isto tem de reintroduzir a password, não pedir permissão.
            PasswordChangeResult.WrongCurrentPassword =>
                Results.Problem(
                    "A password actual não confere.",
                    statusCode: StatusCodes.Status401Unauthorized),

            PasswordChangeResult.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["newPassword"] = [.. resultado.Errors] }),

            // O token é válido e a conta desapareceu entretanto.
            PasswordChangeResult.UserNotFound => Results.Unauthorized(),

            _ => Results.Problem("Resultado inesperado ao mudar a password."),
        };
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid userId,
        ResetPasswordRequest request,
        ResetUserPassword resetPassword,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await resetPassword.ExecuteAsync(
            userId, request.NewPassword, BuildAuditContext(http), cancellationToken);

        return resultado.Result switch
        {
            PasswordChangeResult.Changed => Results.NoContent(),
            PasswordChangeResult.UserNotFound => Results.NotFound(new { erro = "Utilizador não encontrado." }),

            PasswordChangeResult.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["newPassword"] = [.. resultado.Errors] }),

            _ => Results.Problem("Resultado inesperado ao repor a password."),
        };
    }

    private static async Task<IResult> SetStatusAsync(
        Guid userId,
        SetAccountStatusRequest request,
        SetAccountStatus setStatus,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                // Fechar o acesso de alguém sem dizer porquê deixa quem audita
                // a olhar para um registo que não explica nada.
                ["reason"] = ["Indique a razão: fica na trilha de auditoria."],
            });
        }

        // Desactivar a própria conta trancava quem administra fora do sistema,
        // e a recuperação exigiria mexer na base de dados à mão.
        if (ReadUserId(http.User) == userId && !request.Active)
        {
            return Results.Conflict(new { erro = "Não é possível desactivar a própria conta." });
        }

        var resultado = await setStatus.ExecuteAsync(
            userId, request.Active, request.Reason, BuildAuditContext(http), cancellationToken);

        return resultado switch
        {
            AccountStatusOutcome.Changed => Results.NoContent(),
            AccountStatusOutcome.UserNotFound => Results.NotFound(new { erro = "Utilizador não encontrado." }),
            _ => Results.Problem("Resultado inesperado ao alterar o estado da conta."),
        };
    }

    private static async Task<IResult> RemoveRoleAsync(
        Guid userId,
        string profile,
        RemoveAccessProfile removeProfile,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await removeProfile.ExecuteAsync(
            userId, profile, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AssignProfileOutcome.Assigned => Results.NoContent(),
            AssignProfileOutcome.UserNotFound => Results.NotFound(new { erro = "Utilizador não encontrado." }),

            // 404 e não 400, ao contrário da atribuição: aqui o perfil vem no
            // URI, e é parte do recurso que não foi encontrado.
            AssignProfileOutcome.ProfileNotFound => Results.NotFound(new { erro = "Perfil de acesso não encontrado." }),

            _ => Results.Problem("Resultado inesperado ao retirar o perfil."),
        };
    }

    private static async Task<IResult> ListSessionsAsync(
        ListOwnSessions listSessions,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = ReadUserId(http.User);
        var sessionId = ReadSessionId(http.User);

        if (userId is null || sessionId is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await listSessions.ExecuteAsync(
            userId.Value, sessionId.Value, cancellationToken));
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        RevokeOwnSession revokeSession,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userId = ReadUserId(http.User);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var resultado = await revokeSession.ExecuteAsync(
            userId.Value, sessionId, BuildAuditContext(http), cancellationToken);

        return resultado switch
        {
            RevokeSessionOutcome.Revoked => Results.NoContent(),

            // Sessão de outra pessoa devolve o mesmo que sessão inexistente —
            // ver a nota em `RevokeOwnSession`.
            RevokeSessionOutcome.NotFound => Results.NotFound(new { erro = "Sessão não encontrada." }),

            _ => Results.Problem("Resultado inesperado ao terminar a sessão."),
        };
    }

    /// <summary>Identificador do utilizador autenticado, lido do token.</summary>
    private static Guid? ReadUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : null;
    }
    private static Guid? ReadSessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.Sid)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sid);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
