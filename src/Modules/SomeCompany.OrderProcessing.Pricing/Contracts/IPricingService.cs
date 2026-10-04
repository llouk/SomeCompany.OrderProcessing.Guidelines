using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Pricing.Contracts;

/// <summary>
/// Public API of the Pricing module. Prices are always calculated server-side;
/// the client only sends SKUs and quantities.
/// </summary>
public interface IPricingService
{
    Task<Result<PriceQuote>> QuoteAsync(PriceQuoteRequest request, CancellationToken ct);
}

public sealed record PriceQuoteRequest(string CountryCode, IReadOnlyList<PriceQuoteLine> Lines);

public sealed record PriceQuoteLine(string Sku, int Quantity);

public sealed record PriceQuote(
    IReadOnlyList<PricedLine> Lines,
    Money Subtotal,
    IReadOnlyList<Charge> Charges,
    Money Tax,
    Money Total);

public sealed record PricedLine(string Sku, int Quantity, Money UnitPrice, Money LineTotal);

public sealed record Charge(string Code, Money Amount);
