namespace Rivo.Identity.Application.Authorization;

/// <summary>
/// Catálogo de permissões do módulo `identity`.
///
/// É intencionalmente uma lista de constantes e não uma tabela: o catálogo
/// tem de existir em código de qualquer forma (as policies referenciam-no), e
/// duplicá-lo na base de dados criaria duas fontes de verdade que podem
/// divergir. A tabela `app_role_claim` guarda apenas <em>que perfil tem que
/// permissão</em>, não quais existem.
///
/// Cada módulo declarará o seu próprio catálogo quando for implementado.
/// Nomeadas como "modulo.recurso.operacao" para que a origem seja legível.
/// </summary>
public static class Permissions
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
