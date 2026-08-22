namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Configuração do login com Google (ADR-032).
///
/// <para>
/// <strong>Sem `ValidateOnStart`, ao contrário de <c>JwtOptions</c> e
/// <c>BootstrapOptions</c>.</strong> Aqueles descrevem coisas sem as quais a
/// aplicação não serve para nada, e falhar o arranque é a resposta certa. O
/// Google é opcional: exigi-lo derrubaria o arranque em todos os ambientes de
/// desenvolvimento e no CI, que não têm nem vão ter credenciais da Google.
/// </para>
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    /// <summary>
    /// Identificador da aplicação junto da Google. <strong>Não é segredo</strong> —
    /// viaja no frontend — mas é ele que preenche a audiência esperada do ID
    /// token, e é essa validação que impede aceitar um token emitido para
    /// outra aplicação qualquer.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Verdadeiro quando há `ClientId` para validar a audiência.
    ///
    /// <para>
    /// Sem ele o caminho fica desligado em vez de validar sem audiência: um
    /// verificador que aceite qualquer audiência aceita o ID token que
    /// qualquer aplicação Google emitiu para o seu próprio utilizador, o que
    /// esvazia a garantia toda.
    /// </para>
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
