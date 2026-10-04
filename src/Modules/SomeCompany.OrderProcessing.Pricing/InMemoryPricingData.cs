using SomeCompany.OrderProcessing.Pricing.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Pricing;

// Pricing's in-memory database for the PoC (products and tax rates). During development these move to
// PostgreSQL or SQL Server in Pricing's own schema, or to an external catalogue / tax engine, behind the same ports (ADR-006).

internal sealed class InMemoryProductCatalog : IProductCatalog
{
    private static readonly Dictionary<string, Money> Prices = new()
    {
        ["SKU-KEYBOARD"] = new Money(49.90m, "EUR"),
        ["SKU-MOUSE"] = new Money(19.90m, "EUR"),
        ["SKU-MONITOR"] = new Money(199.00m, "EUR")
    };

    public Task<Money?> GetUnitPriceAsync(string sku, CancellationToken ct) =>
        Task.FromResult(Prices.TryGetValue(sku, out var price) ? price : (Money?)null);
}

internal sealed class InMemoryTaxRateProvider : ITaxRateProvider
{
    private static readonly Dictionary<string, decimal> Rates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GR"] = 0.24m,
        ["DE"] = 0.19m,
        ["NL"] = 0.21m
    };

    public decimal? GetRate(string countryCode) => Rates.TryGetValue(countryCode, out var rate) ? rate : null;
}

/// <summary>Flat shipping fee, waived above a threshold.</summary>
internal sealed class ShippingFeeRule : IChargeRule
{
    private const decimal FreeShippingThreshold = 100m;
    private const decimal Fee = 4.90m;

    public Charge? Apply(Money subtotal) =>
        subtotal.Amount >= FreeShippingThreshold ? null : new Charge("shipping", new Money(Fee, subtotal.Currency));
}
