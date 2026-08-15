using Microsoft.AspNetCore.Identity;

namespace Rivo.Identity.Infrastructure.Persistence;

/// <summary>
/// Perfil de Acesso — "o que este utilizador pode ver/fazer no sistema".
/// Não confundir com Cargo, que é posição organizacional e pertence a `hr` (ADR-005).
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>;
