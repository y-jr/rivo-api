using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Commercial.Application.UseCases;
using Rivo.Commercial.Contracts;

namespace Rivo.Commercial.Api;

public static class CommercialModuleEndpoints
{
    public static IEndpointRouteBuilder MapCommercialModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/commercial");

        group.MapGet("/customers", ListAsync)
            .RequireAuthorization(CommercialPermissions.CustomersRead);

        group.MapGet("/customers/{customerId:guid}", GetAsync)
            .RequireAuthorization(CommercialPermissions.CustomersRead);

        group.MapPost("/customers", RegisterAsync)
            .RequireAuthorization(CommercialPermissions.CustomersWrite);

        group.MapPost("/customers/{customerId:guid}/details", UpdateAsync)
            .RequireAuthorization(CommercialPermissions.CustomersWrite);

        // Desactivar, nunca eliminar (BR-14). Não há DELETE nesta superfície, e
        // é a regra a aparecer na forma da API.
        group.MapPost("/customers/{customerId:guid}/status", SetStatusAsync)
            .RequireAuthorization(CommercialPermissions.CustomersWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListCustomers listCustomers,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await listCustomers.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid customerId,
        GetCustomer getCustomer,
        CancellationToken cancellationToken)
    {
        var cliente = await getCustomer.ExecuteAsync(customerId, cancellationToken);

        return cliente is null
            ? Results.NotFound(new { erro = "Cliente não encontrado." })
            : Results.Ok(cliente);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterCustomerRequest request,
        RegisterCustomer registerCustomer,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await registerCustomer.ExecuteAsync(
            request.Name,
            request.TaxId,
            request.AddressDetail,
            request.City,
            request.Country ?? "AO",
            request.Email,
            request.Phone,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            RegisterCustomerOutcome.Registered =>
                Results.Created($"/commercial/customers/{result.CustomerId}", new { customerId = result.CustomerId }),

            // 409 e não 400: o pedido está bem formado, colide é com o que já
            // existe. O identificador do existente vai na resposta porque é o
            // que quem chamou quase de certeza quer a seguir.
            RegisterCustomerOutcome.DuplicateTaxId =>
                Results.Conflict(new
                {
                    erro = "Já existe um cliente com este NIF.",
                    customerId = result.CustomerId,
                }),

            RegisterCustomerOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["cliente"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao registar o cliente."),
        };
    }

    private static async Task<IResult> UpdateAsync(
        Guid customerId,
        UpdateCustomerRequest request,
        UpdateCustomer updateCustomer,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await updateCustomer.ExecuteAsync(
            customerId,
            request.Name,
            request.AddressDetail,
            request.City,
            request.Country,
            request.Email,
            request.Phone,
            BuildAuditContext(http),
            cancellationToken);

        return outcome switch
        {
            UpdateCustomerOutcome.Updated => Results.NoContent(),

            UpdateCustomerOutcome.NotFound => Results.NotFound(new { erro = "Cliente não encontrado." }),

            UpdateCustomerOutcome.PartialAddress => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["morada"] =
                    [
                        "A morada substitui-se inteira: indique detalhe, cidade e país, ou nenhum dos três.",
                    ],
                }),

            _ => Results.Problem("Resultado inesperado ao alterar o cliente."),
        };
    }

    private static async Task<IResult> SetStatusAsync(
        Guid customerId,
        SetStatusRequest request,
        SetCustomerStatus setStatus,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var encontrado = await setStatus.ExecuteAsync(
            customerId, request.Active, BuildAuditContext(http), cancellationToken);

        return encontrado
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Cliente não encontrado." });
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }
}

/// <param name="Country">ISO 3166-1 alpha-2. Omitido, assume-se `AO`.</param>
public sealed record RegisterCustomerRequest(
    string Name,
    string TaxId,
    string AddressDetail,
    string City,
    string? Country,
    string? Email,
    string? Phone);

public sealed record UpdateCustomerRequest(
    string? Name,
    string? AddressDetail,
    string? City,
    string? Country,
    string? Email,
    string? Phone);

public sealed record SetStatusRequest(bool Active);
