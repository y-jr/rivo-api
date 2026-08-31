using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>Regista uma despesa de frota — combustível, portagem ou estacionamento.</summary>
public sealed class RegisterExpense(IVehicleStore store, IAuditTrail audit)
{
    public async Task<RegisterExpenseResult> ExecuteAsync(
        Guid vehicleId,
        FleetExpenseCategory category,
        decimal amount,
        DateOnly occurredOn,
        string? description,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return RegisterExpenseResult.VehicleNotFound();
        }

        FleetExpense despesa;

        try
        {
            despesa = veiculo.RegisterExpense(category, amount, occurredOn, description);
        }
        catch (ArgumentException error)
        {
            return RegisterExpenseResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return RegisterExpenseResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.ExpenseRegistered,
                FleetAuditEntityTypes.Expense,
                despesa.Id.ToString(),
                context,
                NewValue: $$"""{"vehicleId":"{{vehicleId}}","category":"{{despesa.Category}}","amount":{{despesa.Amount}}}"""),
            cancellationToken);

        return RegisterExpenseResult.Success(despesa.Id);
    }
}

public sealed record RegisterExpenseResult(RegisterExpenseOutcome Outcome, Guid? ExpenseId, string? Error)
{
    public static RegisterExpenseResult Success(Guid expenseId) =>
        new(RegisterExpenseOutcome.Registered, expenseId, null);

    public static RegisterExpenseResult VehicleNotFound() =>
        new(RegisterExpenseOutcome.VehicleNotFound, null, "Viatura não encontrada.");

    public static RegisterExpenseResult Rejected(string error) =>
        new(RegisterExpenseOutcome.Rejected, null, error);

    public static RegisterExpenseResult Conflict(string error) =>
        new(RegisterExpenseOutcome.Conflict, null, error);
}

public enum RegisterExpenseOutcome
{
    Registered,
    VehicleNotFound,

    /// <summary>Pedido malformado — valor não positivo. 400.</summary>
    Rejected,

    /// <summary>Viatura inactiva. 409.</summary>
    Conflict,
}
