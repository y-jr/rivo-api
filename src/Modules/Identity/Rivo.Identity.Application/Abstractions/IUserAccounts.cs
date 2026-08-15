namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Acesso ao armazenamento de contas.
///
/// Existe porque o ASP.NET Core Identity (UserManager, SignInManager) é
/// infraestrutura, e a camada Application não pode depender dela
/// (architecture/dependency-rules.md). Esta interface é a fronteira: expõe só
/// o que os casos de uso precisam, em vez do UserManager inteiro.
/// </summary>
public interface IUserAccounts
{
    /// <summary>
    /// Cria uma conta. Devolve os erros de validação em vez de lançar excepção:
    /// password fraca ou e-mail duplicado são resultados esperados, não falhas
    /// técnicas (standards/error-handling.md).
    /// </summary>
    Task<CreateAccountOutcome> CreateAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Valida as credenciais. Devolve <c>null</c> quando não conferem —
    /// deliberadamente sem distinguir "utilizador inexistente" de "password
    /// errada", para não revelar que endereços estão registados.
    /// </summary>
    Task<AuthenticatedAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atribui um Perfil de Acesso a um utilizador.
    ///
    /// O efeito só se reflecte no token do utilizador no login seguinte,
    /// porque as permissões são resolvidas na autenticação (ADR-014). Para
    /// forçar a actualização, revoga-se a sessão.
    /// </summary>
    Task<AssignProfileOutcome> AssignProfileAsync(Guid userId, string profile, CancellationToken cancellationToken);
}

public sealed record UserSummary(Guid UserId, string Email);

public enum AssignProfileOutcome
{
    Assigned,
    UserNotFound,
    ProfileNotFound,
}

/// <param name="Succeeded">Falso quando a criação foi recusada.</param>
/// <param name="UserId">Preenchido apenas em caso de sucesso.</param>
/// <param name="Errors">Motivos da recusa, próprios para devolver ao chamador.</param>
public sealed record CreateAccountOutcome(bool Succeeded, Guid? UserId, IReadOnlyList<string> Errors)
{
    public static CreateAccountOutcome Success(Guid userId) => new(true, userId, []);

    public static CreateAccountOutcome Failure(IReadOnlyList<string> errors) => new(false, null, errors);
}

/// <param name="Roles">Perfis de Acesso do utilizador. Não confundir com Cargo, que pertence a `hr` (ADR-005).</param>
/// <param name="Permissions">
/// Permissões resultantes dos perfis, já consolidadas e sem repetições. São
/// resolvidas aqui, na autenticação, e não a cada pedido: seguem dentro do
/// token para que a verificação de autorização não toque na base de dados.
/// </param>
public sealed record AuthenticatedAccount(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
