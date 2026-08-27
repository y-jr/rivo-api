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
/// Ordem de Compra e Recepção virão a publicar-se, para o 3-way match. Ainda
/// não existem.
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

    public static readonly IReadOnlyList<string> All =
        [SuppliersRead, SuppliersWrite, RequisitionsRead, RequisitionsWrite];

    /// <summary>
    /// O que um requisitante precisa: ver fornecedores para saber a quem pedir,
    /// e escrever requisições. <strong>Sem qualificar fornecedores</strong> —
    /// quem pede a compra não escolhe para que conta se paga.
    /// </summary>
    public static readonly IReadOnlyList<string> ForRequesters =
        [SuppliersRead, RequisitionsRead, RequisitionsWrite];
}
