namespace Rivo.Dashboard.Contracts;

/// <summary>
/// Catálogo de permissões da camada de composição do Dashboard Executivo
/// (Fase 8, ADR-041) — mesmo lugar de `FinancePermissions`, `HrPermissions`
/// e todos os outros: uma camada de composição não tem Domain nem
/// Infrastructure, mas precisa do mesmo consumidor que qualquer módulo tem
/// — `identity`, para conceder a permissão a um Perfil de Acesso.
///
/// <para>
/// <strong>Permissão própria, não a soma de permissões alheias.</strong>
/// `Rivo.Settings` (ADR-041) e `GET /portal/me` (ADR-042) não inventaram
/// permissão nenhuma porque bastava reutilizar as que já existiam — a
/// audiência coincidia exactamente. Aqui não coincide:
/// `docs/rivo-suite-descricao-modulos.md` diz que `Manager` vê o Dashboard,
/// mas `Manager` não tem `finance.invoices.read` (só `Finance` tem, via
/// `ForTreasury`) — exigir os dois contratos subjacentes excluiria a
/// audiência que o documento de produto nomeia. Uma permissão à parte narrow
/// (só leitura agregada, nunca as facturas em si) resolve sem alargar o
/// que `Manager` vê hoje das listagens de `finance`.
/// </para>
/// </summary>
public static class DashboardPermissions
{
    public const string OverviewRead = "dashboard.overview.read";

    public static readonly IReadOnlyList<string> All = [OverviewRead];
}
