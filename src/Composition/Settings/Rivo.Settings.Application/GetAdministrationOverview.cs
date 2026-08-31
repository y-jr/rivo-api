using Rivo.Approval.Contracts;
using Rivo.Identity.Contracts;

namespace Rivo.Settings.Application;

/// <summary>
/// Vista agregada de Configurações & Administração (ADR-041) — perfis de
/// acesso e regras de aprovação, lado a lado, para um administrador ver a
/// governança do sistema num só ecrã.
///
/// <para>
/// <strong>Camada de composição, não módulo.</strong> Não possui dados
/// próprios: lê `identity` e `approval` pelos seus contratos publicados, e
/// nada aqui altera nenhum dos dois. `docs/rivo-arquitetura-global-v1.md`
/// §1.4 e `domain/domain-map.md` já resolviam isto em prosa — este ficheiro
/// é a primeira aplicação concreta do desenho.
/// </para>
/// </summary>
public sealed class GetAdministrationOverview(
    IAccessProfileCatalogue profiles,
    IApprovalPolicyCatalogue policies)
{
    public async Task<AdministrationOverview> ExecuteAsync(CancellationToken cancellationToken)
    {
        var perfis = profiles.List();
        var regras = await policies.ListAsync(cancellationToken);

        var porModulo = regras
            .GroupBy(Modulo)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ApprovalRuleGroup(
                group.Key,
                [.. group
                    .OrderBy(rule => rule.ProcessType, StringComparer.Ordinal)
                    .Select(rule => new ApprovalRuleOverview(
                        rule.PolicyId, rule.ProcessType, rule.IsActive, rule.StepCount, rule.RequiresBudgetCheck))]))
            .ToList();

        return new AdministrationOverview(
            [.. perfis
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new AccessProfileOverview(p.Name, p.Permissions))],
            porModulo);
    }

    /// <summary>
    /// O módulo é o que vem antes do primeiro ponto de <c>ProcessType</c>
    /// (ex.: <c>"hr.leave_request"</c> → <c>"hr"</c>) — a mesma convenção de
    /// nomeação que <see cref="ApprovalProcessTypes"/> já usa, lida em vez de
    /// duplicada.
    /// </summary>
    private static string Modulo(ApprovalPolicySummary rule) => rule.ProcessType.Split('.', 2)[0];
}

public sealed record AdministrationOverview(
    IReadOnlyList<AccessProfileOverview> AccessProfiles,
    IReadOnlyList<ApprovalRuleGroup> ApprovalRulesByModule);

public sealed record AccessProfileOverview(string Name, IReadOnlyList<string> Permissions);

public sealed record ApprovalRuleGroup(string Module, IReadOnlyList<ApprovalRuleOverview> Rules);

public sealed record ApprovalRuleOverview(
    Guid PolicyId, string ProcessType, bool IsActive, int StepCount, bool RequiresBudgetCheck);
