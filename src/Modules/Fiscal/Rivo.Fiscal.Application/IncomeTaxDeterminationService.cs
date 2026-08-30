using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Contracts;

namespace Rivo.Fiscal.Application;

/// <summary>
/// O contrato publicado de IRT de `fiscal`. É por aqui que `payroll` pede o
/// imposto já calculado — ver a nota em <see cref="IIncomeTaxDetermination"/>
/// sobre porquê o cálculo, e não só a taxa, sai daqui.
/// </summary>
public sealed class IncomeTaxDeterminationService(IIncomeTaxScheduleStore store) : IIncomeTaxDetermination
{
    public async Task<IncomeTaxDeterminationResult> DetermineAsync(
        IncomeTaxDeterminationRequest request,
        CancellationToken cancellationToken)
    {
        var tabela = await store.FindAsync(cancellationToken);

        // Determinação à data do facto gerador (ADR-011 §3) — mesma regra de
        // `TaxDeterminationService`.
        var versao = tabela?.InForceOn(request.TaxPointDate);

        if (versao is null)
        {
            return IncomeTaxDeterminationResult.NoScheduleInForce();
        }

        var escalao = versao.SelectBracket(request.TaxableIncome);
        var montante = versao.Compute(request.TaxableIncome);

        return IncomeTaxDeterminationResult.Determined(
            new IncomeTaxDetermination(montante, escalao.Rate, escalao.FixedPortion, escalao.LowerBound, versao.LegalInstrument));
    }
}
