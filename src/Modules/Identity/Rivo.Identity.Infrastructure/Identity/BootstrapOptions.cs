using System.ComponentModel.DataAnnotations;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Utilizadores a criar no arranque de um ambiente novo, lidos da secção
/// <c>Bootstrap</c>.
///
/// <para>
/// Existe para resolver um problema de arranque: nenhum utilizador nasce com
/// autoridade, logo não há quem conceda a primeira (ADR-014, ADR-015). Sem
/// isto, um ambiente novo fica inutilizável sem intervenção manual na base de
/// dados.
/// </para>
///
/// <para>
/// <strong>Uma lista, não campos separados para Admin e decisor.</strong>
/// Ambos são entradas da mesma lista, com perfis diferentes. Um mecanismo só.
/// </para>
///
/// <para>
/// As passwords vêm de configuração — variáveis de ambiente ou gestão de
/// segredos —, nunca do repositório.
/// </para>
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>Vazio significa não semear ninguém. É estado válido.</summary>
    public IReadOnlyList<BootstrapUser> Users { get; init; } = [];
}

public sealed class BootstrapUser
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Tem de cumprir a política de password configurada. Se não cumprir, o
    /// arranque falha em vez de deixar o ambiente sem administrador.
    /// </summary>
    [Required]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Perfis de Acesso a atribuir. Têm de constar do catálogo semeado —
    /// um nome desconhecido faz o arranque falhar, e não cria nada em
    /// silêncio.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "Um utilizador de bootstrap sem perfis não teria autoridade nenhuma.")]
    public IReadOnlyList<string> Profiles { get; init; } = [];
}
