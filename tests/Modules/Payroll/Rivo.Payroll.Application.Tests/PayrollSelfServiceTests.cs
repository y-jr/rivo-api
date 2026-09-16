using Rivo.Payroll.Application;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Application.Tests;

/// <summary>
/// Duplo escrito à mão, sem biblioteca de mocks — ADR-022. Só os dois métodos
/// que <see cref="PayrollSelfService"/> usa fazem algo; os restantes existem
/// porque a interface os tem.
/// </summary>
internal sealed class FakePayrollRunStore : IPayrollRunStore
{
    private readonly List<ApprovedPayrollItem> _aprovados = [];
    private readonly List<PayrollItemDocument> _documentos = [];

    /// <summary>Os identificadores com que o lote foi pedido, para provar que é um lote.</summary>
    public List<IReadOnlyList<Guid>> PedidosDeDocumentos { get; } = [];

    public FakePayrollRunStore WithApproved(ApprovedPayrollItem item)
    {
        _aprovados.Add(item);
        return this;
    }

    public FakePayrollRunStore WithDocument(PayrollItemDocument document)
    {
        _documentos.Add(document);
        return this;
    }

    public Task<IReadOnlyList<ApprovedPayrollItem>> ListApprovedItemsForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovedPayrollItem>>(
            [.. _aprovados.Where(a => a.Item.EmployeeId == employeeId)]);

    public Task<IReadOnlyList<PayrollItemDocument>> ListDocumentsForItemsAsync(
        IReadOnlyList<Guid> payrollItemIds, CancellationToken cancellationToken)
    {
        PedidosDeDocumentos.Add(payrollItemIds);

        // Data de anexação descendente, como a consulta real promete.
        return Task.FromResult<IReadOnlyList<PayrollItemDocument>>(
        [
            .. _documentos
                .Where(d => payrollItemIds.Contains(d.PayrollItemId))
                .OrderByDescending(d => d.AttachedAt)
        ]);
    }

    public Task<PayrollRun?> FindAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<PayrollRun?>(null);

    public Task<PayrollRun?> FindForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<PayrollRun?>(null);

    public Task<IReadOnlyList<PayrollRun>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PayrollRun>>([]);

    public Task AddAsync(PayrollRun run, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddPayrollItemDocumentAsync(PayrollItemDocument link, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<PayrollItemDocument>> ListPayrollItemDocumentsAsync(
        Guid payrollItemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PayrollItemDocument>>([]);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public class PayrollSelfServiceTests
{
    private static readonly Guid Colaborador = Guid.NewGuid();

    /// <summary>
    /// Um item aprovado de um mês, com o colaborador pedido. Passa pelo domínio
    /// (<see cref="PayrollRun.AddItem"/>) em vez de construir o item à mão, para
    /// que os números sejam os que o domínio calcula.
    /// </summary>
    private static ApprovedPayrollItem ItemDe(int ano, int mes, Guid employeeId, decimal bruto = 500_000m)
    {
        var run = PayrollRun.Open(ano, mes, Guid.NewGuid());
        var item = run.AddItem(employeeId, bruto, foodAllowance: 30_000m);

        return new ApprovedPayrollItem(item, ano, mes);
    }

    [Fact]
    public async Task ListPayslipsAsync_SemItensAprovados_NaoProcuraDocumentos()
    {
        var store = new FakePayrollRunStore();
        var service = new PayrollSelfService(store);

        var recibos = await service.ListPayslipsAsync(Colaborador, CancellationToken.None);

        Assert.Empty(recibos);

        // Sem itens não há documentos que procurar, e uma consulta em lote com
        // lista vazia é uma ida à base de dados que não devolve nada.
        Assert.Empty(store.PedidosDeDocumentos);
    }

    [Fact]
    public async Task ListPayslipsAsync_MapeiaOsValoresDoItem()
    {
        var aprovado = ItemDe(2026, 8, Colaborador);
        var service = new PayrollSelfService(new FakePayrollRunStore().WithApproved(aprovado));

        var recibo = Assert.Single(await service.ListPayslipsAsync(Colaborador, CancellationToken.None));

        Assert.Equal(aprovado.Item.Id, recibo.ItemId);
        Assert.Equal(2026, recibo.Year);
        Assert.Equal(8, recibo.Month);
        Assert.Equal(aprovado.Item.GrossSalary, recibo.GrossSalary);
        Assert.Equal(aprovado.Item.FoodAllowance, recibo.FoodAllowance);
        Assert.Equal(aprovado.Item.NetSalary, recibo.NetSalary);
        Assert.Equal(aprovado.Item.WithholdingTax, recibo.WithholdingTax);
        Assert.Null(recibo.DocumentId);
    }

    [Fact]
    public async Task ListPayslipsAsync_PedeOsDocumentosDeTodosOsItensNumSoLote()
    {
        var store = new FakePayrollRunStore();
        var meses = Enumerable.Range(1, 12)
            .Select(m => ItemDe(2026, m, Colaborador))
            .ToList();

        foreach (var item in meses)
        {
            store.WithApproved(item);
        }

        await new PayrollSelfService(store).ListPayslipsAsync(Colaborador, CancellationToken.None);

        var pedido = Assert.Single(store.PedidosDeDocumentos);
        Assert.Equal(12, pedido.Count);
    }

    [Fact]
    public async Task ListPayslipsAsync_ComVariosDocumentosNoMesmoItem_DevolveOMaisRecente()
    {
        var aprovado = ItemDe(2026, 8, Colaborador);
        var antigo = Guid.NewGuid();
        var recente = Guid.NewGuid();

        var store = new FakePayrollRunStore()
            .WithApproved(aprovado)
            .WithDocument(PayrollItemDocument.Attach(
                aprovado.Item.Id, antigo, "Payslip", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)))
            .WithDocument(PayrollItemDocument.Attach(
                aprovado.Item.Id, recente, "Payslip", new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero)));

        var recibo = Assert.Single(await new PayrollSelfService(store).ListPayslipsAsync(Colaborador, CancellationToken.None));

        // Um recibo reemitido substitui o anterior aos olhos de quem o recebe.
        Assert.Equal(recente, recibo.DocumentId);
    }

    [Fact]
    public async Task ListPayslipsAsync_DocumentoDeOutroItem_NaoSeColaAEste()
    {
        var meu = ItemDe(2026, 8, Colaborador);
        var outro = ItemDe(2026, 7, Colaborador);

        var store = new FakePayrollRunStore()
            .WithApproved(meu)
            .WithApproved(outro)
            .WithDocument(PayrollItemDocument.Attach(
                outro.Item.Id, Guid.NewGuid(), "Payslip", DateTimeOffset.UtcNow));

        var recibos = await new PayrollSelfService(store).ListPayslipsAsync(Colaborador, CancellationToken.None);

        Assert.Null(recibos.Single(r => r.ItemId == meu.Item.Id).DocumentId);
        Assert.NotNull(recibos.Single(r => r.ItemId == outro.Item.Id).DocumentId);
    }

    [Fact]
    public async Task ListPayslipsAsync_ItensDeOutroColaborador_NaoAparecem()
    {
        var store = new FakePayrollRunStore()
            .WithApproved(ItemDe(2026, 8, Colaborador))
            .WithApproved(ItemDe(2026, 8, Guid.NewGuid()));

        var recibos = await new PayrollSelfService(store).ListPayslipsAsync(Colaborador, CancellationToken.None);

        Assert.Single(recibos);
    }
}
