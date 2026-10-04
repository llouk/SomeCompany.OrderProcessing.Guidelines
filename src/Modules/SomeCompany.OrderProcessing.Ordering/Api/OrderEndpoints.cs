using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Ordering.Domain;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Ordering.Api;

internal static class OrderEndpoints
{
    private const string GetOrderRoute = "GetOrder";
    private const int MaxIdempotencyKeyLength = 64;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization();

        orders.MapPost("/", PlaceOrder);
        orders.MapGet("/{id:guid}", GetOrder).WithName(GetOrderRoute);
        orders.MapPost("/{id:guid}/cancel", CancelOrder);

        return app;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal user,
        PlaceOrderHandler handler,
        CancellationToken ct)
    {
        // Shape validation at the edge; business rules live in the domain.
        if (request.ShippingAddress is not { } address
            || new[] { address.Line1, address.City, address.PostalCode, address.CountryCode }.Any(string.IsNullOrWhiteSpace)
            || request.Lines is null
            || string.IsNullOrWhiteSpace(request.PaymentToken))
            return InvalidRequest("shippingAddress (all fields), lines and paymentToken are required.");

        if (idempotencyKey is { Length: 0 or > MaxIdempotencyKeyLength })
            return InvalidRequest($"Idempotency-Key must be 1 to {MaxIdempotencyKeyLength} characters.");

        var result = await handler.HandleAsync(
            new PlaceOrderCommand(
                user.CustomerId(),
                idempotencyKey,
                new Address(address.Line1, address.City, address.PostalCode, address.CountryCode),
                [.. request.Lines.Select(l => new OrderLine(l.Sku, l.Quantity))],
                request.PaymentToken),
            ct);

        return result.IsSuccess
            ? Results.CreatedAtRoute(GetOrderRoute, new { id = result.Value.Id }, OrderResponse.From(result.Value))
            : result.Error!.ToProblem();
    }

    private static async Task<IResult> GetOrder(Guid id, ClaimsPrincipal user, GetOrderHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(id, user.CustomerId(), ct);
        return result.IsSuccess ? Results.Ok(OrderResponse.From(result.Value)) : result.Error!.ToProblem();
    }

    private static async Task<IResult> CancelOrder(
        Guid id,
        CancelOrderRequest? request,
        ClaimsPrincipal user,
        CancelOrderHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new CancelOrderCommand(id, user.CustomerId(), request?.Reason), ct);
        return result.IsSuccess ? Results.Ok(OrderResponse.From(result.Value)) : result.Error!.ToProblem();
    }

    private static IResult InvalidRequest(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "request.invalid", detail: detail);

    private static string CustomerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Authenticated user has no subject claim.");

    private static IResult ToProblem(this Error error) =>
        Results.Problem(
            statusCode: error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            },
            title: error.Code,
            detail: error.Message);
}
