using Rivo.Payroll.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Payroll.Application.Abstractions;

/// <summary>
/// Persistência de `payroll`. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IPayrollRunStore
{
    /// <summary>Sem rastreio, com itens incluídos: quem lê não altera.</summary>
    Task<PayrollRun?> FindAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Rastreado, com itens incluídos: quem procura assim vai alterar.</summary>
    Task<PayrollRun?> FindForUpdateAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="pagina"/> nulo devolve tudo, como antes de existir
    /// paginação (ADR-068) — o total só vem preenchido quando há página.
    /// </summary>
    Task<(IReadOnlyList<PayrollRun> Items, int? TotalCount)> ListAsync(
        PageRequest? pagina, CancellationToken cancellationToken);

    Task AddAsync(PayrollRun run, CancellationToken cancellationToken);

    Task AddPayrollItemDocumentAsync(PayrollItemDocument link, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollItemDocument>> ListPayrollItemDocumentsAsync(
        Guid payrollItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Itens de folhas **aprovadas** de um colaborador, com a folha a que
    /// pertencem, do mais recente para o mais antigo.
    ///
    /// <para>
    /// O filtro por estado vive na consulta, e não em quem a chama: um item de
    /// rascunho é um número por confirmar, e mostrá-lo ao próprio prometeria um
    /// vencimento que ainda pode mudar.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ApprovedPayrollItem>> ListApprovedItemsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Documentos de vários itens de uma vez. Em lote de propósito: um recibo
    /// por mês dá doze consultas por ano de histórico, e o portal lê o histórico
    /// todo de cada vez que abre.
    /// </summary>
    Task<IReadOnlyList<PayrollItemDocument>> ListDocumentsForItemsAsync(
        IReadOnlyList<Guid> payrollItemIds,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}


/// <summary>Um item aprovado, com o ano e mês da folha que o contém.</summary>
public sealed record ApprovedPayrollItem(PayrollItem Item, int Year, int Month);