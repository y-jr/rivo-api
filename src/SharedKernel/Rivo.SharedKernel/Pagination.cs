namespace Rivo.SharedKernel;

/// <summary>
/// A página pedida por um cliente numa listagem paginável.
///
/// <para>
/// Primeira entrada no SharedKernel (ADR-068) — justificada pelos critérios
/// de <c>domain/shared-concepts.md</c>: primitiva estrutural sem conceito de
/// negócio, precisa por praticamente todos os módulos com listagens que
/// crescem sem limite, e sem dono natural (não é "de" nenhum módulo).
/// </para>
/// </summary>
public readonly record struct PageRequest(int Page, int PageSize);

/// <summary>
/// Interpreta os parâmetros <c>page</c>/<c>pageSize</c> de uma listagem.
///
/// <para>
/// <strong>Aditivo por desenho</strong>: sem os dois parâmetros, uma
/// listagem continua a devolver tudo, exactamente como antes de existir
/// paginação — só pagina quando pedido, para não quebrar quem já consome a
/// resposta como um array simples.
/// </para>
/// </summary>
public static class Pagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static bool TryParse(
        int? page,
        int? pageSize,
        out PageRequest? pedido,
        out string? erro)
    {
        if (page is null && pageSize is null)
        {
            pedido = null;
            erro = null;
            return true;
        }

        var numeroDaPagina = page ?? 1;
        var tamanho = pageSize ?? DefaultPageSize;

        if (numeroDaPagina < 1)
        {
            pedido = null;
            erro = "page tem de ser 1 ou superior.";
            return false;
        }

        if (tamanho < 1 || tamanho > MaxPageSize)
        {
            pedido = null;
            erro = $"pageSize tem de estar entre 1 e {MaxPageSize}.";
            return false;
        }

        pedido = new PageRequest(numeroDaPagina, tamanho);
        erro = null;
        return true;
    }
}
