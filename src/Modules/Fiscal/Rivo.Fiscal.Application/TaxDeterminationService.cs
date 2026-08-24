using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Contracts;

namespace Rivo.Fiscal.Application;

/// <summary>
/// O contrato publicado de `fiscal`. É por aqui que `commercial` e `finance`
/// perguntam o imposto, sem conhecer nada além de `Rivo.Fiscal.Contracts`.
///
/// <para>
/// A direcção importa: eles perguntam, `fiscal` responde. `fiscal` não depende
/// deles (`modules/fiscal.md`, "duas direcções, duas capacidades").
/// </para>
/// </summary>
public sealed class TaxDeterminationService(ITaxRateStore store) : ITaxDetermination
{
    public async Task<TaxDeterminationResult> DetermineAsync(
        TaxDeterminationRequest request,
        CancellationToken cancellationToken)
    {
        var codigo = (request.TaxCode ?? string.Empty).Trim().ToUpperInvariant();

        // A recusa vem antes da consulta: mesmo que houvesse uma série de
        // isenção configurada, emitir com ela exigiria um `TaxExemptionCode`
        // que não existe. Deixar passar aqui empurraria a falha para a emissão,
        // onde já não se percebe que a causa é a lista de códigos em falta.
        if (Domain.TaxCodes.RequiresExemptionCode(codigo))
        {
            return TaxDeterminationResult.ExemptionCodeUnavailable();
        }

        var serie = await store.FindAsync(
            ListTaxRates.ToDomain(request.Kind), codigo, cancellationToken);

        // Determinação **à data do facto gerador** (ADR-011 §3). A data vem do
        // pedido e não de `UtcNow`: uma correcção emitida em 2027 sobre um
        // facto de 2026 aplica as regras de 2026.
        var versao = serie?.InForceOn(request.TaxPointDate);

        if (versao is null)
        {
            return TaxDeterminationResult.NoRateInForce();
        }

        return TaxDeterminationResult.Determined(
            new TaxDetermination(serie!.Code, versao.Percentage, versao.LegalInstrument));
    }
}
