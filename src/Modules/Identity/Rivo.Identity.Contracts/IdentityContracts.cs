namespace Rivo.Identity.Contracts;

/// <summary>
/// Superfície publicada de `identity`. Primeiro consumidor: a camada de
/// composição `Rivo.Settings` (Configurações & Administração, ADR-041) —
/// até 2026-08-31 nenhum outro módulo lia `identity` por contrato, só a
/// compunha (o próprio `identity` lê o catálogo de permissões de todos os
/// outros, ver <c>AccessProfiles.Catalogue</c> em `Rivo.Identity.Application`).
/// </summary>
public interface IAccessProfileCatalogue
{
    /// <summary>
    /// Os sete Perfis de Acesso e as permissões de cada um. O catálogo é a
    /// fonte de verdade e vive em código — não se lê da base de dados, que
    /// guarda apenas as atribuições.
    /// </summary>
    IReadOnlyList<AccessProfileSummary> List();
}

public sealed record AccessProfileSummary(string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// Catálogo de permissões do módulo `identity`.
///
/// <para>
/// É intencionalmente uma lista de constantes e não uma tabela: o catálogo
/// tem de existir em código de qualquer forma (as policies referenciam-no), e
/// duplicá-lo na base de dados criaria duas fontes de verdade que podem
/// divergir. A tabela `app_role_claim` guarda apenas <em>que perfil tem que
/// permissão</em>, não quais existem.
/// </para>
///
/// <para>
/// <strong>Publicada aqui e não em `Rivo.Identity.Application` desde
/// 2026-08-31</strong> — mesmo lugar de <c>CommercialPermissions</c>,
/// <c>HrPermissions</c> e todos os outros catálogos de módulo. Ficou em
/// `Application` até aqui só porque `identity` nunca teve consumidor externo
/// (ADR-017); `Rivo.Settings` é o primeiro.
/// </para>
/// </summary>
public static class IdentityPermissions
{
    /// <summary>Tipo do claim que transporta uma permissão, no token e em `app_role_claim`.</summary>
    public const string ClaimType = "permission";

    public const string UsersRead = "identity.users.read";

    /// <summary>
    /// Repor a password de outra pessoa e activar ou desactivar contas.
    ///
    /// <para>
    /// <strong>Separada de `RolesAssign` de propósito.</strong> Quem atribui
    /// perfis decide o que uma pessoa pode fazer; quem tem esta decide
    /// <em>quem</em> a pessoa é — repõe-lhe a password e entra na conta dela.
    /// São poderes diferentes e a organização pode querer separá-los.
    /// </para>
    /// </summary>
    public const string UsersWrite = "identity.users.write";

    public const string RolesRead = "identity.roles.read";

    public const string RolesAssign = "identity.roles.assign";

    /// <summary>
    /// Usado para registar uma policy por permissão no arranque e para semear
    /// o perfil Admin. Acrescentar aqui é o único sítio a mexer.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        UsersRead,
        UsersWrite,
        RolesRead,
        RolesAssign,
    ];
}
