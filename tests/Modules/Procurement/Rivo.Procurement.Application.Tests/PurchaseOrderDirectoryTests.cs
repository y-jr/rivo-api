using Rivo.Procurement.Application;
using Rivo.Procurement.Contracts;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// O contrato publicado da Ordem de Compra — o que `finance` lê para fechar o
/// 3-way match, pondo o encomendado e o recebido ao lado do facturado.
///
/// <para>
/// <strong>É a junção que o domínio nunca faz.</strong> A Ordem sabe o que
/// encomendou; as Recepções sabem o que chegou; ninguém no domínio as vê em
/// conjunto. Quem as junta é esta projecção, e um erro aqui não aparece em
/// nenhum teste de domínio — aparece numa factura paga a mais.
/// </para>
/// </summary>
public class PurchaseOrderDirectoryTests
{
    [Fact]
    public async Task Ordem_Inexistente_Da_Nulo()
    {
        var directorio = new PurchaseOrderDirectory(new FakeProcurementStore());

        Assert.Null(await directorio.FindAsync(Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>
    /// <strong>O caso que mais importa.</strong> Uma linha sem recepção nenhuma
    /// tem de aparecer com recebido <c>0</c> — não pode desaparecer da lista.
    ///
    /// <para>
    /// Se desaparecesse, `finance` veria uma ordem com menos linhas do que tem,
    /// e a factura dessa linha não teria contra que ser comparada. O 3-way
    /// match passaria de «encomendado vs recebido vs facturado» a «facturado
    /// contra nada», em silêncio.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Linha_Sem_Recepcao_Aparece_Com_Zero_Recebido()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m), ("Areia", 50m, 2000m));
        store.RecebidoAntes(ordem, linhas[0].Id, 40m);

        var vista = await new PurchaseOrderDirectory(store)
            .FindAsync(ordem.Id, CancellationToken.None);

        Assert.NotNull(vista);
        Assert.Equal(2, vista.Lines.Count);

        var cimento = vista.Lines.Single(l => l.Description == "Cimento");
        Assert.Equal(40m, cimento.QuantityReceived);

        var areia = vista.Lines.Single(l => l.Description == "Areia");
        Assert.Equal(0m, areia.QuantityReceived);
        Assert.Equal(50m, areia.QuantityOrdered);
    }

    [Fact]
    public async Task Recepcoes_Parciais_Somam_Se_Na_Mesma_Linha()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var linha = linhas[0];
        store.RecebidoAntes(ordem, linha.Id, 30m);
        store.RecebidoAntes(ordem, linha.Id, 25m);

        var vista = await new PurchaseOrderDirectory(store)
            .FindAsync(ordem.Id, CancellationToken.None);

        Assert.Equal(55m, vista!.Lines[0].QuantityReceived);
    }

    [Fact]
    public async Task Ordem_Sem_Recepcao_Nenhuma_Traz_Todas_As_Linhas_A_Zero()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m), ("Areia", 50m, 2000m));

        var vista = await new PurchaseOrderDirectory(store)
            .FindAsync(ordem.Id, CancellationToken.None);

        Assert.Equal(2, vista!.Lines.Count);
        Assert.All(vista.Lines, l => Assert.Equal(0m, l.QuantityReceived));
    }

    /// <summary>
    /// As recepções de outra ordem não se misturam. Parece óbvio, e é o tipo
    /// de coisa que um filtro esquecido no armazenamento parte sem ninguém
    /// reparar — até duas ordens do mesmo fornecedor se contaminarem.
    /// </summary>
    [Fact]
    public async Task Recepcoes_De_Outra_Ordem_Nao_Contam()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var (outra, linhasOutra) = store.Encomendar(("Cimento", 100m, 5000m));
        store.RecebidoAntes(outra, linhasOutra[0].Id, 80m);

        var vista = await new PurchaseOrderDirectory(store)
            .FindAsync(ordem.Id, CancellationToken.None);

        Assert.Equal(0m, vista!.Lines[0].QuantityReceived);
    }

    [Fact]
    public async Task A_Vista_Publica_O_Que_Finance_Precisa_De_Comparar()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));

        var vista = await new PurchaseOrderDirectory(store)
            .FindAsync(ordem.Id, CancellationToken.None);

        Assert.Equal(ordem.Id, vista!.PurchaseOrderId);
        Assert.Equal(ordem.SupplierId, vista.SupplierId);
        Assert.Equal("AOA", vista.Currency);
        Assert.Equal(PurchaseOrderReferenceStatus.Issued, vista.Status);

        // O total da ordem e o da linha vêm do domínio, não recalculados aqui:
        // recalcular seria uma segunda fonte de verdade para o mesmo número.
        Assert.Equal(ordem.Total, vista.Total);
        Assert.Equal(linhas[0].LineTotal, vista.Lines[0].LineTotal);
    }
}
