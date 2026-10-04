using SomeCompany.OrderProcessing.SharedKernel;
using SomeCompany.OrderProcessing.Shipping.Contracts;

namespace SomeCompany.OrderProcessing.Shipping;

/// <summary>Stand-in for the carrier. The real adapter follows the InventoryApiClient pattern.</summary>
internal sealed class FakeShippingService : IShippingService
{
    public Task<Result<string>> CreateShipmentAsync(ShipmentRequest request, CancellationToken ct) =>
        Task.FromResult<Result<string>>($"shp_{request.OrderId:N}");

    public Task<Result> CancelShipmentAsync(string shipmentId, CancellationToken ct) => Task.FromResult(Result.Success());
}
