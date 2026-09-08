using Microsoft.Extensions.Options;
using Rivo.Fiscal.Application;

namespace Rivo.Fiscal.Infrastructure;

/// <summary>
/// Recusa o arranque quando a identidade da empresa não serve para produzir um
/// SAF-T (ADR-058).
///
/// <para>
/// <strong>Existe para a mensagem, e não para a decisão.</strong> A decisão
/// está em <see cref="CompanyOptions.CamposEmFalta"/>; o que isto acrescenta é
/// dizer <em>qual</em> o problema. O <c>.Validate(predicado, mensagem)</c> do
/// `Microsoft.Extensions.Options` só aceita uma cadeia constante, e uma
/// mensagem constante mente assim que a verificação cresce: dizia "sem
/// `Company:Name` e `Company:TaxRegistrationNumber` não há como exportar", e
/// quem tivesse um NIF de nove dígitos leria que não os tinha preenchido.
/// </para>
///
/// <para>
/// Numa falha de arranque, a mensagem é a única coisa que quem está a instalar
/// tem. Apontar para o campo errado é pior do que não dizer nada — manda
/// procurar onde não está.
/// </para>
/// </summary>
public sealed class ValidarCompanyOptions : IValidateOptions<CompanyOptions>
{
    public ValidateOptionsResult Validate(string? name, CompanyOptions options)
    {
        var problemas = options.CamposEmFalta();

        if (problemas.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            problemas.Select(problema =>
                $"Identidade da empresa (ADR-058): {problema}"));
    }
}
