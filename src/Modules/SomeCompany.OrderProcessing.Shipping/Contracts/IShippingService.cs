using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Shipping.Contracts;

/// <summary>
/// Public API of the Shipping module (adapter over the carrier / 3PL).
/// The carrier reports "shipped" and "delivered" back asynchronously (webhook, Phase 2).
/// </summary>
public interface IShippingService
{
    /// <returns>The carrier's shipment id.</returns>
    Task<Result<string>> CreateShipmentAsync(ShipmentRequest request, CancellationToken ct);

    Task<Result> CancelShipmentAsync(string shipmentId, CancellationToken ct);
}

public sealed record ShipmentRequest(Guid OrderId, ShippingAddress Address, IReadOnlyList<ShipmentItem> Items);

public sealed record ShippingAddress(string Line1, string City, string PostalCode, string CountryCode);

public sealed record ShipmentItem(string Sku, int Quantity);
