using Rivo.Audit.Contracts;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Contracts;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.UseCases;

/// <summary>
/// Lê a identidade fiscal da empresa.
///
/// <para>
/// Devolve <c>null</c> quando ainda não foi declarada, e quem chama tem de
/// tratar esse caso: sem emitente não há documento fiscal, e é melhor recusar a
/// emissão do ficheiro do que produzir um papel sem cabeçalho.
/// </para>
/// </summary>
public sealed class GetTaxEntityProfile(ITaxEntityProfileStore store)
{
    public async Task<TaxEntityProfileView?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var perfil = await store.FindAsync(cancellationToken);

        return perfil is null ? null : ToView(perfil);
    }

    internal static TaxEntityProfileView ToView(TaxEntityProfile p) => new(
        p.CompanyName,
        p.BusinessName,
        p.TaxRegistrationNumber,
        p.AddressDetail,
        p.City,
        p.PostalCode,
        p.Country,
        p.Email,
        p.Phone,
        p.SoftwareValidationNumber);
}

/// <summary>
/// Declara ou corrige a identidade fiscal da empresa.
///
/// <para>
/// <strong>Uma operação só, e não duas.</strong> Quem configura o sistema não
/// tem de saber se já existe — a primeira chamada declara, as seguintes
/// corrigem. O desfecho diz qual das duas aconteceu, para a auditoria poder
/// distinguir o arranque de uma alteração.
/// </para>
/// </summary>
public sealed class DeclareTaxEntityProfile(ITaxEntityProfileStore store, IAuditTrail audit)
{
    public async Task<TaxEntityProfileResult> ExecuteAsync(
        TaxEntityProfileInput input,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.CompanyName)
            || string.IsNullOrWhiteSpace(input.TaxRegistrationNumber)
            || string.IsNullOrWhiteSpace(input.AddressDetail)
            || string.IsNullOrWhiteSpace(input.City)
            || string.IsNullOrWhiteSpace(input.Country))
        {
            return new TaxEntityProfileResult(
                TaxEntityProfileOutcome.Invalid,
                null,
                "Razão social, NIF, endereço, cidade e país são obrigatórios.");
        }

        var existente = await store.FindAsync(cancellationToken);
        var anterior = existente is null ? null : Retrato(existente);

        TaxEntityProfile perfil;
        TaxEntityProfileOutcome desfecho;

        if (existente is null)
        {
            perfil = TaxEntityProfile.Declare(
                input.CompanyName, input.TaxRegistrationNumber,
                input.AddressDetail, input.City, input.Country);

            perfil.Describe(
                input.BusinessName, input.PostalCode, input.Email,
                input.Phone, input.SoftwareValidationNumber);

            await store.AddAsync(perfil, cancellationToken);
            desfecho = TaxEntityProfileOutcome.Declared;
        }
        else
        {
            perfil = existente;

            perfil.Correct(
                input.CompanyName, input.TaxRegistrationNumber,
                input.AddressDetail, input.City, input.Country);

            perfil.Describe(
                input.BusinessName, input.PostalCode, input.Email,
                input.Phone, input.SoftwareValidationNumber);

            desfecho = TaxEntityProfileOutcome.Corrected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                desfecho == TaxEntityProfileOutcome.Declared
                    ? FiscalAuditActions.TaxEntityDeclared
                    : FiscalAuditActions.TaxEntityCorrected,
                FiscalAuditEntityTypes.TaxEntityProfile,
                perfil.Id.ToString(),
                context,
                PreviousValue: anterior,
                NewValue: Retrato(perfil)),
            cancellationToken);

        return new TaxEntityProfileResult(desfecho, GetTaxEntityProfile.ToView(perfil), null);
    }

    /// <summary>
    /// O que fica na auditoria. Razão social, NIF e sede: os três campos que
    /// mudam o documento emitido, e portanto os três que interessa poder
    /// comparar entre o antes e o depois.
    /// </summary>
    private static string Retrato(TaxEntityProfile p) =>
        $$"""{"companyName":"{{p.CompanyName}}","taxRegistrationNumber":"{{p.TaxRegistrationNumber}}","city":"{{p.City}}"}""";
}

public sealed record TaxEntityProfileInput(
    string CompanyName,
    string TaxRegistrationNumber,
    string AddressDetail,
    string City,
    string Country,
    string? BusinessName = null,
    string? PostalCode = null,
    string? Email = null,
    string? Phone = null,
    string? SoftwareValidationNumber = null);

public sealed record TaxEntityProfileResult(
    TaxEntityProfileOutcome Outcome,
    TaxEntityProfileView? Profile,
    string? Error);

public enum TaxEntityProfileOutcome
{
    Declared,
    Corrected,
    Invalid,
}
