namespace Rivo.Procurement.Contracts;

/// <summary>
/// Superfície publicada de `procurement`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>O que sai daqui é o Fornecedor</strong> — é o que `finance` precisa
/// de conhecer para registar a factura de compra e para saber a quem paga.
/// Requisição, Ordem de Compra e Recepção ficam por dentro: são o processo de
/// compra, e o handoff para `finance` acontece na factura, não antes
/// (`docs` §5, "Procurement possui requisição/OC/recepção; Financeiro possui
/// factura/pagável").
/// </para>
///
/// <para>
/// Ordem de Compra publica-se abaixo, em <see cref="IPurchaseOrderDirectory"/>,
/// para o 3-way match.
/// </para>
/// </summary>
public interface ISupplierDirectory
{
    /// <summary>
    /// Referência a um fornecedor. Os consumidores guardam o identificador e
    /// lêem os atributos por aqui.
    ///
    /// <para>
    /// <strong>Não copiam o nome nem o NIF para as suas tabelas.</strong> A
    /// excepção é o documento já emitido, que guarda o que vigorava à data — aí
    /// a cópia é o ponto, e não o defeito (BR-18).
    /// </para>
    /// </summary>
    Task<SupplierReference?> FindAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>
    /// Procura pelo NIF normalizado — sem espaços e em maiúsculas.
    ///
    /// <para>
    /// Existe para quem tem a factura na mão e não o identificador: é assim que
    /// `finance` reconhece o fornecedor de uma factura de compra sem obrigar
    /// quem a regista a procurá-lo primeiro.
    /// </para>
    /// </summary>
    Task<SupplierReference?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken);
}

/// <param name="TaxId">NIF, normalizado.</param>
/// <param name="Iban">
/// Nulo quando o fornecedor ainda não tem conta registada. Quem paga tem de
/// tratar a ausência — não se paga por transferência para lado nenhum.
/// </param>
public sealed record SupplierReference(
    Guid SupplierId,
    string Name,
    string TaxId,
    string? Iban,
    SupplierStatus Status);

public enum SupplierStatus
{
    Active,
    Inactive,
}

/// <summary>
/// A Ordem de Compra e a Recepção, publicadas juntas — é o segundo e o
/// terceiro lado do 3-way match. O primeiro é o próprio pedido de pagamento; o
/// terceiro fica de fora daqui porque a factura de compra é de `finance`, e é
/// lá que os três se encontram (`GetPurchaseInvoiceMatch`).
/// </summary>
public interface IPurchaseOrderDirectory
{
    /// <summary>
    /// Os consumidores guardam o identificador da ordem, nunca as suas linhas.
    /// Lêem-nas por aqui, sempre à leitura mais recente — a quantidade
    /// recebida muda a cada recepção registada.
    /// </summary>
    Task<PurchaseOrderReference?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken);
}

/// <param name="Total">Soma das linhas ao preço acordado. Sem imposto — a ordem não o tem.</param>
public sealed record PurchaseOrderReference(
    Guid PurchaseOrderId,
    Guid SupplierId,
    string Currency,
    decimal Total,
    PurchaseOrderReferenceStatus Status,
    IReadOnlyList<PurchaseOrderLineReference> Lines);

/// <param name="QuantityReceived">
/// Acumulado de todas as recepções em vigor contra esta linha, à leitura mais
/// recente. É o segundo lado do 3-way match — o primeiro é
/// <paramref name="QuantityOrdered"/>.
/// </param>
public sealed record PurchaseOrderLineReference(
    Guid LineId,
    string Description,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal UnitPrice,
    decimal LineTotal);

public enum PurchaseOrderReferenceStatus
{
    Issued,
    Cancelled,
}

/// <summary>
/// Catálogo de permissões de `procurement`, declarado pelo próprio módulo.
/// `identity` decide que perfis as recebem (ADR-005).
/// </summary>
public static class ProcurementPermissions
{
    public const string SuppliersRead = "procurement.suppliers.read";

    /// <summary>
    /// Qualificar fornecedores. <strong>É permissão sensível</strong>: quem
    /// regista o fornecedor fixa o IBAN para onde o dinheiro vai sair.
    /// </summary>
    public const string SuppliersWrite = "procurement.suppliers.write";

    public const string RequisitionsRead = "procurement.requisitions.read";

    /// <summary>
    /// Abrir, alterar e submeter requisições. Não decide nada — a decisão é de
    /// `approval`, e quem submete não pode decidir sobre o próprio pedido
    /// (BR-2).
    /// </summary>
    public const string RequisitionsWrite = "procurement.requisitions.write";

    public const string OrdersRead = "procurement.orders.read";

    /// <summary>
    /// Emitir e cancelar ordens de compra.
    ///
    /// <para>
    /// <strong>Separada de `requisitions.write` de propósito</strong>, e não
    /// porque quem pede não possa encomendar — na prática de uma PME é a mesma
    /// pessoa, e os dois perfis recebem as duas. É separada porque a decisão de
    /// as separar tem de continuar possível: no dia em que houver uma função de
    /// compras distinta de quem requisita, muda-se o catálogo e não o código.
    /// </para>
    ///
    /// <para>
    /// A segregação que importa está noutro sítio e já está imposta: entre a
    /// requisição e a ordem há uma decisão de `approval`, tomada por outra
    /// pessoa (BR-2).
    /// </para>
    /// </summary>
    public const string OrdersWrite = "procurement.orders.write";

    public const string ReceiptsRead = "procurement.receipts.read";

    /// <summary>
    /// Registar e anular recepções de mercadoria.
    ///
    /// <para>
    /// <strong>Separada de `orders.write`, e desta vez a separação é usada.</strong>
    /// Quem recebe a mercadoria confirma que chegou o que se encomendou; se
    /// fosse a mesma pessoa que encomenda, uma entrega a menos podia ser
    /// registada como completa sem que ninguém mais visse. É a metade de
    /// segregação que dá valor ao 3-way match.
    /// </para>
    /// </summary>
    public const string ReceiptsWrite = "procurement.receipts.write";

    public static readonly IReadOnlyList<string> All =
        [
            SuppliersRead, SuppliersWrite,
            RequisitionsRead, RequisitionsWrite,
            OrdersRead, OrdersWrite,
            ReceiptsRead, ReceiptsWrite,
        ];

    /// <summary>
    /// O que um requisitante precisa: ver fornecedores para saber a quem pedir,
    /// escrever requisições, e emitir a ordem depois de a decisão sair.
    /// <strong>Sem qualificar fornecedores</strong> — quem pede a compra não
    /// escolhe para que conta se paga.
    /// </summary>
    public static readonly IReadOnlyList<string> ForRequesters =
        [SuppliersRead, RequisitionsRead, RequisitionsWrite, OrdersRead, OrdersWrite, ReceiptsRead];
}
