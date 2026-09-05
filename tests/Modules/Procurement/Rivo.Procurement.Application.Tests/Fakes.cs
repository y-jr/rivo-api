using Rivo.Audit.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// Ordens e recepções em memória.
///
/// <para>
/// O <see cref="ReceivedByOrderLineAsync"/> é calculado a partir das recepções
/// guardadas, e não devolvido de um dicionário fixo. É de propósito: o
/// acumulado é o que a guarda de excesso consulta, e um valor pregado deixaria
/// de reflectir as recepções que o próprio teste registou.
/// </para>
/// </summary>
internal sealed class FakeProcurementStore : ProcurementStoreParcial
{
    private readonly List<PurchaseOrder> _ordens = [];
    private readonly List<GoodsReceipt> _recepcoes = [];

    public int Gravacoes { get; private set; }

    public IReadOnlyList<GoodsReceipt> Recepcoes => _recepcoes;

    /// <summary>
    /// Emite uma ordem e devolve-a com as linhas criadas.
    ///
    /// <para>
    /// As linhas vêm à parte porque <c>PurchaseOrder.Lines</c> é um
    /// <c>IReadOnlyCollection</c> — sem índice, de propósito: a ordem das
    /// linhas não é garantia do domínio, e um teste que dependesse dela estaria
    /// a assumir mais do que o modelo promete.
    /// </para>
    /// </summary>
    public (PurchaseOrder Ordem, IReadOnlyList<PurchaseOrderLine> Linhas) Encomendar(
        params (string Descricao, decimal Quantidade, decimal Preco)[] linhas)
    {
        var ordem = PurchaseOrder.Issue(
            Guid.NewGuid(), Guid.NewGuid(), "AOA",
            new DateOnly(2026, 1, 10), expectedOn: null);

        var criadas = linhas
            .Select(l => ordem.AddLine(l.Descricao, l.Quantidade, l.Preco))
            .ToList();

        _ordens.Add(ordem);
        return (ordem, criadas);
    }

    /// <summary>Regista uma recepção já feita, para o acumulado ter passado.</summary>
    public void RecebidoAntes(PurchaseOrder ordem, Guid linhaId, decimal quantidade)
    {
        var recepcao = GoodsReceipt.Register(
            ordem.Id, new DateOnly(2026, 1, 15), Guid.NewGuid(), deliveryNote: null);
        recepcao.AddLine(linhaId, quantidade);
        _recepcoes.Add(recepcao);
    }

    private readonly List<PurchaseRequisition> _requisicoes = [];
    private readonly List<Supplier> _fornecedores = [];

    public IReadOnlyList<PurchaseOrder> Ordens => _ordens;

    /// <summary>Uma requisição aprovada, com o total estimado que se pedir.</summary>
    public PurchaseRequisition Requisitar(decimal quantidade, decimal preco, bool aprovada = true)
    {
        var requisicao = PurchaseRequisition.Open(
            Guid.NewGuid(), null, "Material de obra", "AOA", new DateOnly(2026, 1, 5));
        requisicao.AddLine("Cimento", quantidade, preco);

        if (aprovada)
        {
            requisicao.MarkSubmitted(Guid.NewGuid(), new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero));
            requisicao.MarkApproved(new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero));
        }

        _requisicoes.Add(requisicao);
        return requisicao;
    }

    public Supplier Fornecedor(bool activo = true)
    {
        var fornecedor = Supplier.Register($"Fornecedor {_fornecedores.Count}", $"NIF{_fornecedores.Count:D9}");
        if (!activo) fornecedor.Deactivate();
        _fornecedores.Add(fornecedor);
        return fornecedor;
    }

    public override Task<PurchaseRequisition?> FindRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) =>
        Task.FromResult(_requisicoes.SingleOrDefault(r => r.Id == requisitionId));

    public override Task<Supplier?> FindSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        Task.FromResult(_fornecedores.SingleOrDefault(f => f.Id == supplierId));

    /// <summary>
    /// Somado das ordens já emitidas contra a requisição — calculado, não
    /// pregado, para reflectir as que o próprio teste emitiu.
    /// </summary>
    public override Task<decimal> OrderedAgainstRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) =>
        Task.FromResult(_ordens.Where(o => o.RequisitionId == requisitionId).Sum(o => o.Total));

    public override Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken)
    {
        _ordens.Add(order);
        return Task.CompletedTask;
    }

    public override Task<PurchaseOrder?> FindOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(_ordens.SingleOrDefault(o => o.Id == purchaseOrderId));

    public override Task<IReadOnlyDictionary<Guid, decimal>> ReceivedByOrderLineAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(
            _recepcoes
                .Where(r => r.PurchaseOrderId == purchaseOrderId)
                .SelectMany(r => r.Lines)
                .GroupBy(l => l.PurchaseOrderLineId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.QuantityReceived)));

    public override Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken)
    {
        _recepcoes.Add(receipt);
        return Task.CompletedTask;
    }

    public override Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Directório de colaboradores. Só <c>FindAsync</c> é usado por
/// `procurement` — quem recebeu tem de existir em `hr` (ADR-010).
/// </summary>
internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly HashSet<Guid> _existentes = [];

    public Guid Existente()
    {
        var id = Guid.NewGuid();
        _existentes.Add(id);
        return id;
    }

    public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult(_existentes.Contains(employeeId)
            ? new EmployeeReference(employeeId, "Colaborador", EmployeeStatus.Active, null, null, null)
            : null);

    public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a FindByUserIdAsync.");

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a FindByPositionAsync.");

    public Task<EmployeeHireResult> HireAsync(
        string fullName,
        string? departmentName,
        DateTimeOffset hiredOn,
        Guid actorId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a HireAsync.");
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
