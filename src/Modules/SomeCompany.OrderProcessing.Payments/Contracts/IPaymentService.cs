using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Payments.Contracts;

/// <summary>
/// Public API of the Payments module (adapter over the payment provider).
/// We only ever handle a provider-issued payment token, never card data, which keeps us out of PCI scope.
/// Money movement follows the order lifecycle:
/// authorize on placement → capture on shipment (Phase 2) → void if cancelled before capture.
/// Refunds belong to a future returns process and get added to this contract then.
/// </summary>
public interface IPaymentService
{
    /// <returns>The provider's authorization id.</returns>
    Task<Result<string>> AuthorizeAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct);

    /// <summary>Takes the authorized money. Called when the carrier confirms dispatch.</summary>
    Task<Result> CaptureAsync(string authorizationId, Money amount, CancellationToken ct);

    /// <summary>Releases an authorization that was never captured (cancellation before shipment).</summary>
    Task<Result> VoidAsync(string authorizationId, CancellationToken ct);
}
