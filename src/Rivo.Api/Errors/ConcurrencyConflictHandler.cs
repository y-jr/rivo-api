using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Rivo.Api.Errors;

/// <summary>
/// Traduz uma colisão de concorrência optimista em <c>409 Conflict</c>.
/// Fecha o K15 (ADR-035).
///
/// <para>
/// O ADR-025 fez com que uma escrita sobre uma versão desactualizada lance
/// <see cref="DbUpdateConcurrencyException"/> em vez de sobrepor em silêncio.
/// Faltava a outra metade: nenhum handler a tratava, a excepção subia até ao
/// topo e o cliente recebia <c>500</c>. Semanticamente errado — não é falha do
/// servidor, é conflito de estado, e o cliente pode reler e repetir.
/// </para>
///
/// <para>
/// <strong>Vive no composition root, e não em cada módulo.</strong> Duas
/// razões, e a segunda é a que decide: nenhuma camada Application referencia
/// o EF Core — nem pode, pelas regras de dependência —, logo não há onde
/// apanhar esta excepção dentro de um módulo sem lhe arrastar a
/// infraestrutura; e registada aqui, aplica-se aos seis módulos de uma vez,
/// sem que um módulo novo se possa esquecer dela.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictHandler(ILogger<ConcurrencyConflictHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException conflict)
        {
            // Qualquer outra coisa segue o caminho normal e continua a ser 500.
            // Alargar isto a `DbUpdateException` esconderia violações de chave
            // e de restrição, que são defeitos e devem falhar ruidosamente.
            return false;
        }

        // O tipo da entidade é diagnóstico útil — uma sequência de colisões
        // sobre o mesmo agregado é um padrão de contenção, não azar. Fica no
        // log, nunca na resposta.
        var entidade = conflict.Entries.Count > 0
            ? conflict.Entries[0].Entity.GetType().Name
            : "desconhecida";

        logger.LogWarning(
            "Colisão de concorrência em {Entidade} ao processar {Metodo} {Caminho}",
            entidade,
            httpContext.Request.Method,
            httpContext.Request.Path);

        // Sem detalhe interno na resposta: a mensagem do EF Core nomeia tabela
        // e tipo de entidade, e devolvê-la contraria standards/error-handling.
        //
        // Sem repetição automática, também de propósito. Repetir sozinho uma
        // decisão de aprovação aplicá-la-ia sobre um estado que o autor não
        // viu — que é exactamente o que BR-17 existe para impedir. Quem
        // chama relê e decide de novo.
        await Results.Problem(
                title: "Conflito de concorrência",
                detail: "O registo foi alterado entretanto. Recarregue e repita a operação.",
                statusCode: StatusCodes.Status409Conflict)
            .ExecuteAsync(httpContext);

        return true;
    }
}
