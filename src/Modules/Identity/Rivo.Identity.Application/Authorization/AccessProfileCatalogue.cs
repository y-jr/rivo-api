using Rivo.Identity.Contracts;

namespace Rivo.Identity.Application.Authorization;

/// <summary>
/// O contrato publicado de `identity` (ADR-017). Primeiro consumidor:
/// `Rivo.Settings`, a camada de composição de Configurações & Administração
/// (ADR-041).
///
/// <para>
/// Separado de <see cref="UseCases.ListAccessProfiles"/> pela mesma razão de
/// <c>CustomerDirectory</c> em `commercial`: a vista interna
/// (<c>AccessProfileView</c>, usada por <c>GET /identity/roles</c>) e a vista
/// publicada (<see cref="AccessProfileSummary"/>) podem divergir sem que um
/// consumidor externo tenha de saber — hoje são a mesma forma, mas só uma das
/// duas é contrato.
/// </para>
/// </summary>
public sealed class AccessProfileCatalogue : IAccessProfileCatalogue
{
    /// <summary>
    /// <see cref="AccessProfiles.AssignableProfiles"/> e não o catálogo
    /// inteiro, pela mesma razão que <c>GET /identity/roles</c> (ADR-058):
    /// `SuperAdmin` existe no catálogo para o seed lhe dar permissões, mas
    /// não é perfil de negócio. A vista de governança de `Rivo.Settings`
    /// mostra a alguém que administra a empresa o que ele pode atribuir —
    /// anunciar-lhe uma conta de arranque que não pode usar nem conceder
    /// seria mostrar uma porta sem lhe dar a chave.
    /// </summary>
    public IReadOnlyList<AccessProfileSummary> List() =>
        [.. AccessProfiles.AssignableProfiles
            .Select(profile => new AccessProfileSummary(profile, AccessProfiles.Catalogue[profile]))];
}
