using Microsoft.AspNetCore.Identity;

namespace Rivo.Identity.Infrastructure.Persistence;

/// <summary>
/// Chave <see cref="Guid"/> por exigência de ADR-002 (chaves substitutas UUID).
/// O ASP.NET Core Identity usaria <see cref="string"/> por omissão.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>;
