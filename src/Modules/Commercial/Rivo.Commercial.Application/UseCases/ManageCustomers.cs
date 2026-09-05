using Rivo.Audit.Contracts;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Contracts;
using Rivo.Commercial.Domain;
using Rivo.Hr.Contracts;

namespace Rivo.Commercial.Application.UseCases;

public sealed class ListCustomers(ICustomerStore store)
{
    public async Task<IReadOnlyList<CustomerReference>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var clientes = await store.ListAsync(includeInactive, cancellationToken);

        return [.. clientes.Select(CustomerDirectory.ToReference)];
    }
}

public sealed class GetCustomer(ICustomerStore store)
{
    public async Task<CustomerReference?> ExecuteAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var cliente = await store.FindAsync(customerId, cancellationToken);

        return cliente is null ? null : CustomerDirectory.ToReference(cliente);
    }
}

/// <summary>Regista um cliente.</summary>
public sealed class RegisterCustomer(ICustomerStore store, IAuditTrail audit)
{
    public async Task<RegisterCustomerResult> ExecuteAsync(
        string name,
        string taxId,
        string addressDetail,
        string city,
        string country,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Customer cliente;

        try
        {
            cliente = Customer.Register(
                name,
                taxId,
                new Domain.BillingAddress(addressDetail, city, country));
        }
        catch (Exception error) when (error is ArgumentException or ArgumentNullException)
        {
            return RegisterCustomerResult.Rejected(error.Message);
        }

        // Unicidade do NIF: o agregado não vê o conjunto, logo a verificação é
        // desta camada. Não substitui o índice único — duas chamadas
        // simultâneas passam as duas aqui, e é a base de dados que decide.
        if (await store.FindByTaxIdAsync(cliente.TaxId, cancellationToken) is { } existente)
        {
            return RegisterCustomerResult.Duplicate(existente.Id);
        }

        cliente.ChangeContacts(email, phone);

        await store.AddAsync(cliente, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerRegistered,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{cliente.Name}}","taxId":"{{cliente.TaxId}}"}"""),
            cancellationToken);

        return RegisterCustomerResult.Success(cliente.Id);
    }
}

public sealed record RegisterCustomerResult(
    RegisterCustomerOutcome Outcome,
    Guid? CustomerId,
    string? Error)
{
    public static RegisterCustomerResult Success(Guid customerId) =>
        new(RegisterCustomerOutcome.Registered, customerId, null);

    public static RegisterCustomerResult Rejected(string error) =>
        new(RegisterCustomerOutcome.Rejected, null, error);

    /// <param name="existingId">
    /// Devolvido de propósito: quem tentou registar quase de certeza quer
    /// trabalhar com o cliente que já existe, e sem o identificador teria de o
    /// procurar às cegas.
    /// </param>
    public static RegisterCustomerResult Duplicate(Guid existingId) =>
        new(RegisterCustomerOutcome.DuplicateTaxId, existingId, null);
}

public enum RegisterCustomerOutcome
{
    Registered,
    Rejected,
    DuplicateTaxId,
}

/// <summary>Altera os dados de um cliente.</summary>
public sealed class UpdateCustomer(ICustomerStore store, IAuditTrail audit)
{
    public async Task<UpdateCustomerOutcome> ExecuteAsync(
        Guid customerId,
        string? name,
        string? addressDetail,
        string? city,
        string? country,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return UpdateCustomerOutcome.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            cliente.Rename(name);
        }

        // A morada substitui-se inteira ou não se toca: é objecto de valor, e
        // aceitar só a cidade deixaria uma morada meio antiga e meio nova.
        if (addressDetail is not null || city is not null || country is not null)
        {
            if (string.IsNullOrWhiteSpace(addressDetail)
                || string.IsNullOrWhiteSpace(city)
                || string.IsNullOrWhiteSpace(country))
            {
                return UpdateCustomerOutcome.PartialAddress;
            }

            cliente.ChangeBillingAddress(new Domain.BillingAddress(addressDetail, city, country));
        }

        cliente.ChangeContacts(email, phone);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerUpdated,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{cliente.Name}}"}"""),
            cancellationToken);

        return UpdateCustomerOutcome.Updated;
    }
}

public enum UpdateCustomerOutcome
{
    Updated,
    NotFound,

    /// <summary>Morada indicada em parte. Ou vai inteira, ou não vai.</summary>
    PartialAddress,
}

/// <summary>
/// Desactiva ou reactiva um cliente.
///
/// <para>
/// Não há eliminação, e é BR-14: um cliente referenciado por facturas emitidas
/// é parte desses documentos.
/// </para>
/// </summary>
public sealed class SetCustomerStatus(ICustomerStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid customerId,
        bool active,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return false;
        }

        if (active)
        {
            cliente.Reactivate();
        }
        else
        {
            cliente.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active ? CommercialAuditActions.CustomerReactivated : CommercialAuditActions.CustomerDeactivated,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"status":"{{cliente.Status}}"}"""),
            cancellationToken);

        return true;
    }
}

/// <summary>
/// Liga uma conta de `identity` a um Cliente já existente (ADR-043 — Portal
/// do Cliente, identidade externa).
///
/// <para>
/// <strong>Nunca por auto-declaração.</strong> O NIF é informação pública —
/// quem o sabe não prova que representa a empresa. Só Sales/Admin, que já
/// conhece o cliente por outra via, confirma a ligação. Mesmo desenho de
/// <c>HireEmployee</c> (ADR-042), com os papéis invertidos: aqui o registo de
/// negócio já existe, e é a conta que chega depois.
/// </para>
/// </summary>
public sealed class LinkCustomerAccount(ICustomerStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<LinkCustomerAccountResult> ExecuteAsync(
        Guid customerId,
        Guid userId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Auto-ligação recusada (ADR-055), e não por simetria com `hr`: aqui a
        // consequência é própria. Quem ligue a sua conta a um cliente age como
        // esse cliente no portal — incluindo submeter comprovativos de
        // pagamento, que `finance` confirma manualmente (ADR-044). Recusado
        // antes de saber se o cliente existe, para não depender de o atacante
        // ter acertado num identificador.
        if (context.ActorId is { } actor && actor == userId)
        {
            return LinkCustomerAccountResult.SelfLinkRefused();
        }

        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return LinkCustomerAccountResult.NotFound();
        }

        // Repetível sem erro e sem auditar: ligar de novo a mesma conta produz
        // o estado pretendido na mesma.
        if (cliente.UserId == userId)
        {
            return LinkCustomerAccountResult.Success();
        }

        // ⚠ **Isto é a correcção do ADR-055.** Até aqui, ligar uma conta a um
        // cliente que já tinha outra substituía-a em silêncio: o portal do
        // cliente mudava de dono sem que nada o registasse, e a conta anterior
        // perdia o acesso sem explicação. Agora recusa-se, e desfazer exige
        // desligar primeiro — que deixa rasto.
        if (cliente.UserId is not null)
        {
            return LinkCustomerAccountResult.CustomerAlreadyLinked();
        }

        if (await store.FindByUserIdAsync(userId, cancellationToken) is not null)
        {
            return LinkCustomerAccountResult.UserAlreadyLinked();
        }

        cliente.LinkToUser(userId);

        // Histórico, na mesma transacção que o campo (ADR-055).
        await store.AddAccountLinkAsync(
            CustomerAccountLink.Open(cliente.Id, userId, clock.GetUtcNow(), context.ActorId),
            cancellationToken);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerAccountLinked,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                PreviousValue: null,
                NewValue: $$"""{"userId":"{{userId}}"}"""),
            cancellationToken);

        return LinkCustomerAccountResult.Success();
    }
}

public enum LinkCustomerAccountOutcome
{
    Linked,
    NotFound,

    /// <summary>
    /// A conta indicada já está ligada a outro cliente — conflito com o
    /// estado, não pedido malformado (mesma razão de
    /// <c>LinkEmployeeAccountOutcome.UserAlreadyLinked</c>).
    /// </summary>
    UserAlreadyLinked,

    /// <summary>Este cliente já tem outra conta ligada (ADR-055).</summary>
    CustomerAlreadyLinked,

    /// <summary>
    /// O actor tentou ligar a conta com que está autenticado. Segregação de
    /// funções, não conflito de estado — traduz-se em 403.
    /// </summary>
    SelfLinkRefused,
}

public sealed record LinkCustomerAccountResult(LinkCustomerAccountOutcome Outcome, string? Error)
{
    public static LinkCustomerAccountResult Success() => new(LinkCustomerAccountOutcome.Linked, null);

    public static LinkCustomerAccountResult NotFound() =>
        new(LinkCustomerAccountOutcome.NotFound, "Cliente não encontrado.");

    public static LinkCustomerAccountResult UserAlreadyLinked() =>
        new(LinkCustomerAccountOutcome.UserAlreadyLinked, "Esta conta já está associada a outro cliente.");

    public static LinkCustomerAccountResult CustomerAlreadyLinked() =>
        new(LinkCustomerAccountOutcome.CustomerAlreadyLinked, "Este cliente já tem outra conta associada.");

    public static LinkCustomerAccountResult SelfLinkRefused() =>
        new(
            LinkCustomerAccountOutcome.SelfLinkRefused,
            "Não pode ligar a sua própria conta a um cliente. Outra pessoa tem de o fazer.");
}

/// <summary>
/// Desliga a conta de um Cliente (ADR-055).
///
/// <para>
/// <strong>Não invalida nada do que aquela conta já fez.</strong> Comprovativos
/// de pagamento e mensagens submetidos pelo portal ficam registados contra o
/// <em>Cliente</em>, não contra a conta — desligar remove a capacidade de agir
/// daqui para a frente e não reescreve o passado. Mesma conclusão do ADR-052
/// para `hr`, e pela mesma razão: o facto gravado nunca foi «a conta A fez».
/// </para>
/// </summary>
public sealed class UnlinkCustomerAccount(ICustomerStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<UnlinkCustomerAccountResult> ExecuteAsync(
        Guid customerId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return UnlinkCustomerAccountResult.NotFound();
        }

        // Repetível sem erro, e sem encher a trilha.
        if (cliente.UserId is not { } contaAnterior)
        {
            return UnlinkCustomerAccountResult.Success();
        }

        cliente.LinkToUser(null);

        // Pode não existir episódio: os vínculos anteriores à migração de
        // retroactivo ficaram cobertos, mas um criado por escrita directa em
        // base não. Desliga-se na mesma — o campo é a verdade operacional.
        var episodio = await store.FindOpenAccountLinkAsync(cliente.Id, cancellationToken);
        episodio?.Close(clock.GetUtcNow(), context.ActorId);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerAccountUnlinked,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                PreviousValue: $$"""{"userId":"{{contaAnterior}}"}""",
                NewValue: null),
            cancellationToken);

        return UnlinkCustomerAccountResult.Success();
    }
}

public enum UnlinkCustomerAccountOutcome
{
    Unlinked,
    NotFound,
}

public sealed record UnlinkCustomerAccountResult(UnlinkCustomerAccountOutcome Outcome, string? Error)
{
    public static UnlinkCustomerAccountResult Success() =>
        new(UnlinkCustomerAccountOutcome.Unlinked, null);

    public static UnlinkCustomerAccountResult NotFound() =>
        new(UnlinkCustomerAccountOutcome.NotFound, "Cliente não encontrado.");
}

/// <summary>
/// Que contas já puderam agir como este Cliente, e quando (ADR-055).
/// </summary>
public sealed class GetCustomerAccountHistory(ICustomerStore store)
{
    public async Task<IReadOnlyList<CustomerAccountLinkView>?> ExecuteAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // Lista vazia e 404 dizem coisas diferentes: «nunca teve conta» e «não
        // há tal cliente».
        if (await store.FindAsync(customerId, cancellationToken) is null)
        {
            return null;
        }

        var episodios = await store.ListAccountLinksAsync(customerId, cancellationToken);

        return [.. episodios.Select(l => new CustomerAccountLinkView(
            l.UserId, l.LinkedOn, l.LinkedByUserId, l.UnlinkedOn, l.UnlinkedByUserId))];
    }
}

/// <param name="LinkedByUserId">Nulo é <strong>desconhecido</strong>, não «ninguém».</param>
public sealed record CustomerAccountLinkView(
    Guid UserId,
    DateTimeOffset LinkedOn,
    Guid? LinkedByUserId,
    DateTimeOffset? UnlinkedOn,
    Guid? UnlinkedByUserId);

/// <summary>
/// Atribui o vendedor responsável por um cliente (ADR-045) — para quem vai a
/// notificação de uma mensagem nova, nada mais. Não é controlo de acesso:
/// qualquer utilizador com permissão de escrever em conversas continua a ver
/// e a responder a qualquer uma.
/// </summary>
public sealed class AssignCustomerOwner(ICustomerStore store, IEmployeeDirectory employees, IAuditTrail audit)
{
    public async Task<AssignCustomerOwnerResult> ExecuteAsync(
        Guid customerId,
        Guid? employeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return AssignCustomerOwnerResult.NotFound();
        }

        if (employeeId is { } id)
        {
            var colaborador = await employees.FindAsync(id, DateTimeOffset.UtcNow, cancellationToken);

            if (colaborador is null || colaborador.Status is not EmployeeStatus.Active)
            {
                return AssignCustomerOwnerResult.EmployeeNotFound();
            }
        }

        cliente.AssignOwner(employeeId);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerOwnerAssigned,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"assignedToEmployeeId":{{(employeeId is { } eid ? $"\"{eid}\"" : "null")}}}"""),
            cancellationToken);

        return AssignCustomerOwnerResult.Success();
    }
}

public enum AssignCustomerOwnerOutcome
{
    Assigned,
    NotFound,
    EmployeeNotFound,
}

public sealed record AssignCustomerOwnerResult(AssignCustomerOwnerOutcome Outcome, string? Error)
{
    public static AssignCustomerOwnerResult Success() => new(AssignCustomerOwnerOutcome.Assigned, null);

    public static AssignCustomerOwnerResult NotFound() =>
        new(AssignCustomerOwnerOutcome.NotFound, "Cliente não encontrado.");

    public static AssignCustomerOwnerResult EmployeeNotFound() =>
        new(AssignCustomerOwnerOutcome.EmployeeNotFound, "Colaborador não encontrado, ou inactivo.");
}

/// <summary>Acções de `commercial` na trilha de auditoria.</summary>
public static class CommercialAuditActions
{
    public const string CustomerRegistered = "commercial.customer.registered";
    public const string CustomerUpdated = "commercial.customer.updated";
    public const string CustomerDeactivated = "commercial.customer.deactivated";
    public const string CustomerReactivated = "commercial.customer.reactivated";
    public const string CustomerAccountLinked = "commercial.customer.account_linked";

    /// <summary>
    /// Uma conta deixou de agir como este cliente (ADR-055). Guarda a conta
    /// removida em <c>PreviousValue</c>, para uma transferência feita em dois
    /// passos ficar legível.
    /// </summary>
    public const string CustomerAccountUnlinked = "commercial.customer.account_unlinked";
    public const string CustomerOwnerAssigned = "commercial.customer.owner_assigned";
}

public static class CommercialAuditEntityTypes
{
    public const string Customer = "commercial.customer";
}
