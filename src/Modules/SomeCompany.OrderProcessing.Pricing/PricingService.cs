using SomeCompany.OrderProcessing.Pricing.Contracts;
using SomeCompany.OrderProcessing.SharedKernel;

namespace SomeCompany.OrderProcessing.Pricing;

/// <summary>
/// Pricing pipeline: unit prices → subtotal → additional charges → tax (on subtotal + charges) → total.
/// New charges are added by registering another <see cref="IChargeRule"/>; this class does not change.
/// </summary>
internal sealed class PricingService(
    IProductCatalog catalog,
    IEnumerable<IChargeRule> chargeRules,
    ITaxRateProvider taxRates) : IPricingService
{
    public async Task<Result<PriceQuote>> QuoteAsync(PriceQuoteRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
            return new Error(ErrorType.Validation, "pricing.no_lines", "Nothing to price.");

        var taxRate = taxRates.GetRate(request.CountryCode);
        if (taxRate is null)
            return new Error(ErrorType.Validation, "pricing.unsupported_country", $"No tax rate for '{request.CountryCode}'.");

        var lines = new List<PricedLine>();
        foreach (var line in request.Lines)
        {
            var unitPrice = await catalog.GetUnitPriceAsync(line.Sku, ct);
            if (unitPrice is null)
                return new Error(ErrorType.Validation, "pricing.unknown_product", $"Product '{line.Sku}' is not sold.");

            lines.Add(new PricedLine(line.Sku, line.Quantity, unitPrice.Value, unitPrice.Value.Multiply(line.Quantity)));
        }

        var subtotal = lines.Select(l => l.LineTotal).Aggregate((a, b) => a + b);
        var charges = chargeRules.Select(rule => rule.Apply(subtotal)).OfType<Charge>().ToList();
        var taxable = charges.Aggregate(subtotal, (sum, charge) => sum + charge.Amount);
        var tax = taxable.Multiply(taxRate.Value);

        return new PriceQuote(lines, subtotal, charges, tax, taxable + tax);
    }
}
