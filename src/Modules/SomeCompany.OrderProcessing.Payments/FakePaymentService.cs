using SomeCompany.OrderProcessing.Payments.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Payments;

/// <summary>
/// Stand-in for the payment provider. The real adapter follows the same pattern as
/// InventoryApiClient (typed HttpClient + resilience handler + orderId as idempotency key).
/// The token "tok_declined" simulates a declined payment.
/// </summary>
internal sealed class FakePaymentService : IPaymentService
{
    public Task<Result<string>> AuthorizeAsync(Guid orderId, Money amount, string paymentToken, CancellationToken ct)
    {
        Result<string> result = paymentToken == "tok_declined"
            ? new Error(ErrorType.Conflict, "payments.declined", "Payment was declined.")
            : $"auth_{orderId:N}";

        return Task.FromResult(result);
    }

    public Task<Result> CaptureAsync(string authorizationId, Money amount, CancellationToken ct) => Task.FromResult(Result.Success());

    public Task<Result> VoidAsync(string authorizationId, CancellationToken ct) => Task.FromResult(Result.Success());
}
