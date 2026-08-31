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
    public IReadOnlyList<AccessProfileSummary> List() =>
        [.. AccessProfiles.Catalogue.Select(entry => new AccessProfileSummary(entry.Key, entry.Value))];
}
