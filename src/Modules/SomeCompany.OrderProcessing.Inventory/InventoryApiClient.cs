using System.Net;
using System.Net.Http.Json;
using Polly;
using SomeCompany.OrderProcessing.Inventory.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Inventory;

/// <summary>
/// Adapter to the external inventory system. Translates their HTTP model into our contract so their
/// changes stop here. Retries, timeouts and circuit breaking come from the resilience handler
/// configured in <see cref="InventoryModule"/>. Endpoint shapes are assumptions to confirm with the provider.
/// </summary>
internal sealed class InventoryApiClient(HttpClient http) : IInventoryService
{
    public async Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockItem> items, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "reservations",
                new { reservationId = orderId, items = items.Select(i => new { sku = i.Sku, quantity = i.Quantity }) },
                ct);

            return response.StatusCode switch
            {
                HttpStatusCode.Created or HttpStatusCode.OK => Result.Success(),
                HttpStatusCode.Conflict => InventoryErrors.InsufficientStock,
                _ => InventoryErrors.Unavailable
            };
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return InventoryErrors.Unavailable;
        }
    }

    public async Task<Result> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            using var response = await http.DeleteAsync($"reservations/{orderId}", ct);

            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound
                ? Result.Success()
                : InventoryErrors.Unavailable;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            return InventoryErrors.Unavailable;
        }
    }

    // Retries exhausted, circuit open, or timed out: the provider is unavailable, which is an expected outcome, not a bug.
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or ExecutionRejectedException or TaskCanceledException;
}

internal static class InventoryErrors
{
    public static readonly Error InsufficientStock =
        new(ErrorType.Conflict, "inventory.insufficient_stock", "One or more products are out of stock.");

    public static readonly Error Unavailable =
        new(ErrorType.Unavailable, "inventory.unavailable", "Inventory system is unavailable.");
}
