using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Rivo.Api.Errors;

/// <summary>
/// Devolve a <see cref="BadHttpRequestException"/> ao código que ela própria
/// transporta — tipicamente <c>400</c>.
///
/// <para>
/// <strong>Existe por causa do <see cref="ConcurrencyConflictHandler"/>.</strong>
/// Antes de haver <c>UseExceptionHandler</c> no pipeline, um corpo JSON
/// malformado chegava ao Kestrel, que reconhece esta excepção e responde com o
/// <c>StatusCode</c> dela. Registar o middleware pôs-se à frente disso: ele
/// apanha tudo, e o que não é tratado sai como <c>500</c>.
/// </para>
///
/// <para>
/// Encontrado a exercitar os endpoints de `fiscal` — um `curl` mandou um corpo
/// mal codificado e a resposta foi <c>500</c> em vez de <c>400</c>. Confirmado
/// por comparação: com a linha comentada dá <c>400</c>, com ela dá <c>500</c>.
/// O preço de apanhar tudo é ter de devolver o que não é para apanhar.
/// </para>
/// </summary>
public sealed class BadRequestHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        // Sem detalhe da excepção na resposta: a mensagem do desserializador
        // nomeia o tipo .NET e a posição no fluxo de bytes, que é informação
        // sobre a implementação e não sobre o pedido.
        await Results.Problem(
                title: "Pedido inválido",
                detail: "O corpo do pedido não pôde ser lido. Confirme que é JSON válido em UTF-8.",
                statusCode: badRequest.StatusCode)
            .ExecuteAsync(httpContext);

        return true;
    }
}
