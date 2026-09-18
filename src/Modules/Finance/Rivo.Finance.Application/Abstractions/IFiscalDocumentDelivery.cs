namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Entrega um documento fiscal ao cliente, por correio electrónico.
///
/// <para>
/// <strong>Declarada por `finance`, nas suas palavras, e ligada no composition
/// root</strong> — a mesma inversão de <c>IPaymentApproval</c> e
/// <c>IProcurementApprovalSubmission</c> (ADR-034). `finance` não sabe qual é o
/// canal, e a tabela de dependências entre módulos não ganha uma direcção nova.
/// </para>
///
/// <para>
/// <strong>Não passa por `notifications`, e é decisão.</strong> Uma notificação
/// dirige-se a um <em>utilizador</em> da aplicação e não leva anexos; isto
/// dirige-se a um <em>endereço</em> — o cliente pode nem ter conta — e o anexo é
/// o ponto. São coisas diferentes e misturá-las obrigava a distorcer o contrato
/// de `notifications` para caber um caso que não é o dele.
/// </para>
/// </summary>
public interface IFiscalDocumentDelivery
{
    Task<FiscalDocumentDeliveryResult> SendAsync(
        FiscalDocumentDelivery delivery,
        CancellationToken cancellationToken);
}

/// <param name="Content">O PDF. Já gerado e já guardado — a entrega não compõe nada.</param>
public sealed record FiscalDocumentDelivery(
    string ToAddress,
    string ToName,
    string Subject,
    string Body,
    string FileName,
    byte[] Content);

/// <param name="Error">
/// A mensagem do servidor de correio quando falha. Sobe até quem pediu o envio
/// em vez de ficar num log: quem carrega no botão é quem pode corrigir o
/// endereço.
/// </param>
public sealed record FiscalDocumentDeliveryResult(bool Sent, string? Error)
{
    public static FiscalDocumentDeliveryResult Ok() => new(true, null);

    public static FiscalDocumentDeliveryResult Failed(string error) => new(false, error);
}
