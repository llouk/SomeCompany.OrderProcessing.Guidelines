using SomeCompany.OrderProcessing.Pricing.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Pricing;

/// <summary>Source of list prices. Today in-memory; later the product catalogue / PIM.</summary>
internal interface IProductCatalog
{
    Task<Money?> GetUnitPriceAsync(string sku, CancellationToken ct);
}

/// <summary>Tax rate per destination country. Swap for an external tax engine without touching the pipeline.</summary>
internal interface ITaxRateProvider
{
    decimal? GetRate(string countryCode);
}

/// <summary>An additional charge (shipping fee, handling, surcharge…). Returns null when it does not apply.</summary>
internal interface IChargeRule
{
    Charge? Apply(Money subtotal);
}
