using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Contracts;

namespace Rivo.Fiscal.Application;

/// <summary>
/// O contrato publicado de limiares de isenção de subsídios. É por aqui que
/// `payroll` pede o "isento até" de Alimentação e Transporte — ver a nota em
/// <see cref="ISubsidyExemptionDetermination"/> sobre porquê só estes dois.
/// </summary>
public sealed class SubsidyExemptionDeterminationService(ISubsidyExemptionStore store)
    : ISubsidyExemptionDetermination
{
    public async Task<SubsidyExemptionResult> DetermineAsync(
        SubsidyExemptionRequest request,
        CancellationToken cancellationToken)
    {
        var dominio = SubsidyKindMapping.ToDomain(request.Kind);
        var serie = await store.FindAsync(dominio, cancellationToken);

        // Determinação à data do facto gerador (ADR-011 §3) — mesma regra de
        // `TaxDeterminationService` e `IncomeTaxDeterminationService`.
        var versao = serie?.InForceOn(request.TaxPointDate);

        if (versao is null)
        {
            return SubsidyExemptionResult.NoThresholdInForce();
        }

        return SubsidyExemptionResult.Determined(new SubsidyExemption(versao.Amount, versao.LegalInstrument));
    }
}
