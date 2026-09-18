using Rivo.Audit.Contracts;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Guarda o papel gerado no repositório de ficheiros do sistema.
///
/// <para>
/// <strong>Declarada por `finance` e ligada no composition root</strong>, e não
/// acrescentada a <c>Rivo.Documents.Contracts</c>. A razão é concreta: esse
/// assembly não tem dependências por decisão do ADR-017, e guardar um ficheiro
/// precisa de <see cref="AuditContext"/> — que vive em <c>Rivo.Audit.Contracts</c>.
/// Inverter aqui custa uma interface; acrescentar lá custava a propriedade que o
/// ADR-017 protege.
/// </para>
///
/// <para>
/// A implementação está em <c>Rivo.Api/Composition</c>, ao lado das outras
/// ligações do género, e assenta no caso de uso <c>UploadDocument</c> de
/// `documents` — que já calcula o SHA-256 do que guarda, e é por isso que este
/// contrato devolve o resumo em vez de o recalcular.
/// </para>
/// </summary>
public interface IFiscalDocumentArchive
{
    Task<ArchivedDocument> StoreAsync(
        string fileName,
        byte[] content,
        AuditContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Devolve o conteúdo de um ficheiro já arquivado, ou <c>null</c> se
    /// desaparecer.
    ///
    /// <para>
    /// <strong>O <c>null</c> não é teórico.</strong> A linha que aponta ao
    /// ficheiro e o ficheiro em si vivem em sítios diferentes — tabela de
    /// `finance`, armazenamento de `documents` — e o K12 registra precisamente o
    /// caso do ficheiro órfão. Quem chama trata a ausência compondo de novo, em
    /// vez de responder um 500 que ninguém sabe ler.
    /// </para>
    /// </summary>
    Task<byte[]?> FetchAsync(Guid documentId, CancellationToken cancellationToken);
}

public sealed record ArchivedDocument(Guid DocumentId, string ContentHash);
