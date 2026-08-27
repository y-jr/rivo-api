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

    /// <summary>
    /// Procura a conta já ligada a uma identidade de provider externo
    /// (ADR-032). É o caminho de todas as entradas menos a primeira.
    /// </summary>
    /// <param name="providerKey">
    /// O `sub` do provider, não o e-mail: é estável e sobrevive a uma mudança
    /// de endereço do lado do provider.
    /// </param>
    Task<AuthenticatedAccount?> FindByExternalLoginAsync(
        string provider,
        string providerKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Liga uma identidade externa à conta Rivo com este e-mail. É o que
    /// acontece na primeira entrada por Google.
    ///
    /// <para>
    /// <strong>Nunca cria contas.</strong> Sem conta correspondente devolve
    /// <see cref="LinkExternalLoginOutcome.AccountNotFound"/> e não escreve
    /// nada — a criação de contas é acto deliberado de quem administra
    /// (ADR-016), e não consequência de alguém se ter autenticado num provider.
    /// </para>
    /// </summary>
    /// <param name="email">
    /// Endereço <strong>já verificado pelo provider</strong>. Quem chama
    /// garante-o; ligar por um endereço não verificado é via de tomada de conta.
    /// </param>
    Task<LinkExternalLoginResult> LinkExternalLoginAsync(
        string email,
        string provider,
        string providerKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atribui um Perfil de Acesso a um utilizador.
    ///
    /// O efeito só se reflecte no token do utilizador no login seguinte,
    /// porque as permissões são resolvidas na autenticação (ADR-014). Para
    /// forçar a actualização, revoga-se a sessão.
    /// </summary>
    Task<AssignProfileOutcome> AssignProfileAsync(Guid userId, string profile, CancellationToken cancellationToken);

    /// <summary>
    /// Muda a password do próprio, exigindo a actual.
    ///
    /// <para>
    /// <strong>A password actual é obrigatória, e é o ponto todo.</strong> Sem
    /// ela, um token roubado mudava a password e trancava o dono fora da sua
    /// própria conta — a sessão passava a valer mais do que a credencial.
    /// </para>
    /// </summary>
    Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Repõe a password de outra conta, sem exigir a actual.
    ///
    /// <para>
    /// É o caminho de quem administra, para quem perdeu o acesso. É também o
    /// caminho óbvio de uma tomada de conta — por isso fica na trilha com acção
    /// própria, e não confundida com uma mudança de password normal.
    /// </para>
    /// </summary>
    Task<PasswordChangeOutcome> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Activa ou desactiva uma conta.
    ///
    /// <para>
    /// <strong>Não elimina</strong> (BR-14): a conta é referenciada pela trilha
    /// de auditoria e por tudo o que a pessoa fez. Desactivar é o que existe.
    /// </para>
    ///
    /// <para>
    /// Usa o bloqueio do ASP.NET Core Identity, que já tem colunas para isso —
    /// não foi preciso schema novo. Uma conta desactivada falha a autenticação
    /// como se a password estivesse errada.
    /// </para>
    /// </summary>
    Task<AccountStatusOutcome> SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken);

    /// <summary>
    /// Retira um Perfil de Acesso a um utilizador.
    ///
    /// <para>
    /// Um utilizador sem perfil nenhum é estado válido: fica autenticado e sem
    /// permissões. É o que se quer para alguém que muda de funções e ainda não
    /// tem as novas.
    /// </para>
    /// </summary>
    Task<AssignProfileOutcome> RemoveProfileAsync(Guid userId, string profile, CancellationToken cancellationToken);
}

/// <param name="IsActive">
/// Falso quando a conta está desactivada. Uma listagem que não distinguisse
/// activos de desactivados deixava quem administra sem saber quem ainda entra.
/// </param>
public sealed record UserSummary(Guid UserId, string Email, bool IsActive, IReadOnlyList<string> Roles);

/// <param name="Account">Preenchido apenas quando a ligação foi feita.</param>
public sealed record LinkExternalLoginResult(LinkExternalLoginOutcome Outcome, AuthenticatedAccount? Account)
{
    public static LinkExternalLoginResult Linked(AuthenticatedAccount account) =>
        new(LinkExternalLoginOutcome.Linked, account);

    public static LinkExternalLoginResult AccountNotFound() =>
        new(LinkExternalLoginOutcome.AccountNotFound, null);

    public static LinkExternalLoginResult Rejected() =>
        new(LinkExternalLoginOutcome.Rejected, null);
}

public enum LinkExternalLoginOutcome
{
    Linked,

    /// <summary>
    /// Não existe conta com este e-mail. <strong>Não é erro</strong> — é a
    /// política do ADR-032 a funcionar: o Google entra em contas que já
    /// existem, não cria contas novas.
    /// </summary>
    AccountNotFound,

    /// <summary>
    /// A conta existe mas o armazenamento recusou a ligação — tipicamente
    /// porque esta identidade externa já está ligada a outra conta.
    /// </summary>
    Rejected,
}

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

/// <param name="Errors">
/// Motivos da recusa, vindos das regras de password do Identity. Próprios para
/// devolver a quem chamou — dizem o que corrigir.
/// </param>
public sealed record PasswordChangeOutcome(
    PasswordChangeResult Result,
    IReadOnlyList<string> Errors)
{
    public static PasswordChangeOutcome Changed() => new(PasswordChangeResult.Changed, []);

    public static PasswordChangeOutcome UserNotFound() => new(PasswordChangeResult.UserNotFound, []);

    public static PasswordChangeOutcome WrongCurrentPassword() =>
        new(PasswordChangeResult.WrongCurrentPassword, []);

    public static PasswordChangeOutcome Rejected(IReadOnlyList<string> errors) =>
        new(PasswordChangeResult.Rejected, errors);
}

public enum PasswordChangeResult
{
    Changed,
    UserNotFound,

    /// <summary>
    /// A password actual não confere. <strong>401 e não 403</strong>: é a
    /// credencial que falha, não a autorização.
    /// </summary>
    WrongCurrentPassword,

    /// <summary>A nova password não passa as regras. 400, com os motivos.</summary>
    Rejected,
}

public enum AccountStatusOutcome
{
    Changed,
    UserNotFound,
}
