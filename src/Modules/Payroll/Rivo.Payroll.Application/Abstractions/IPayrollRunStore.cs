using Rivo.Payroll.Domain;

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

    Task<IReadOnlyList<PayrollRun>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(PayrollRun run, CancellationToken cancellationToken);

    Task AddPayrollItemDocumentAsync(PayrollItemDocument link, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollItemDocument>> ListPayrollItemDocumentsAsync(
        Guid payrollItemId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
