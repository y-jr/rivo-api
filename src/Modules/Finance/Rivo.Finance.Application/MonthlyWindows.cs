namespace Rivo.Finance.Application;

/// <summary>
/// Enumera os meses civis dentro de uma janela, cada um cortado aos limites
/// pedidos — o primeiro e o último mês podem ficar incompletos, os do meio
/// nunca. Partilhado entre <see cref="ReceivablesOverview"/> e
/// <see cref="PayablesOverview"/>: as duas séries mensais (Analytics & IA,
/// módulo 10) medem-se da mesma forma.
/// </summary>
internal static class MonthlyWindows
{
    public static IEnumerable<(int Year, int Month, DateOnly Start, DateOnly End)> Enumerate(DateOnly from, DateOnly to)
    {
        var mes = new DateOnly(from.Year, from.Month, 1);

        while (mes <= to)
        {
            var fimDoMes = mes.AddMonths(1).AddDays(-1);

            yield return (
                mes.Year,
                mes.Month,
                mes < from ? from : mes,
                fimDoMes > to ? to : fimDoMes);

            mes = mes.AddMonths(1);
        }
    }
}
