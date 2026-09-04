namespace Rivo.Analytics.Contracts;

/// <summary>
/// Catálogo de permissões da camada de composição Analytics &amp; IA (módulo
/// 10, Fase 8, ADR-041/ADR-047) — mesmo papel que `Rivo.Dashboard.Contracts`
/// já tem: uma camada de composição não tem Domain nem Infrastructure, mas
/// precisa do mesmo consumidor que qualquer módulo tem — `identity`, para
/// conceder a permissão a um Perfil de Acesso.
///
/// <para>
/// <strong>Permissão à parte, mesmo precedente do Dashboard.</strong>
/// `docs/rivo-suite-descricao-modulos.md` nomeia `Manager` para "Dashboard,
/// Frota, Projectos, Analytics, Aprovações" — mas `Manager` não tem as
/// permissões de leitura subjacentes de `finance`/`fleet`/`inventory`
/// (`ForPayables` não é `ItemsRead`/`FinancePermissions.All`). Exigir os
/// contratos subjacentes excluiria a audiência que o documento nomeia; uma
/// permissão própria, só para a leitura agregada, resolve sem alargar o que
/// `Manager` vê hoje das listagens de cada módulo — ver
/// `Rivo.Dashboard.Contracts` para o mesmo raciocínio já registado.
/// </para>
/// </summary>
public static class AnalyticsPermissions
{
    public const string OverviewRead = "analytics.overview.read";

    public static readonly IReadOnlyList<string> All = [OverviewRead];
}
