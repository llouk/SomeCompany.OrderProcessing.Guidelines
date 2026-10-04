using SomeCompany.OrderProcessing.Pricing;
using SomeCompany.OrderProcessing.Pricing.Contracts;

namespace SomeCompany.OrderProcessing.UnitTests.Pricing;

/// <summary>Runs the real pipeline (catalogue → charges → tax) over the module's in-memory data.</summary>
public class PricingServiceTests
{
    private readonly PricingService _pricing = new(new InMemoryProductCatalog(), [new ShippingFeeRule()], new InMemoryTaxRateProvider());

    [Fact]
    public async Task Below_threshold_adds_shipping_and_taxes_subtotal_plus_charges()
    {
        var quote = (await Quote("GR", ("SKU-KEYBOARD", 1), ("SKU-MOUSE", 2))).Value;

        Assert.Equal(89.70m, quote.Subtotal.Amount);
        Assert.Equal(4.90m, Assert.Single(quote.Charges).Amount.Amount);
        Assert.Equal(22.70m, quote.Tax.Amount);    // 24% of 94.60, rounded
        Assert.Equal(117.30m, quote.Total.Amount);
    }

    [Fact]
    public async Task At_or_above_threshold_shipping_is_free()
    {
        var quote = (await Quote("DE", ("SKU-MONITOR", 1))).Value;

        Assert.Empty(quote.Charges);
        Assert.Equal(37.81m, quote.Tax.Amount);    // 19% of 199.00
        Assert.Equal(236.81m, quote.Total.Amount);
    }

    [Fact]
    public async Task Unknown_product_is_a_validation_error()
    {
        var result = await Quote("GR", ("SKU-NOPE", 1));

        Assert.Equal("pricing.unknown_product", result.Error!.Code);
    }

    [Fact]
    public async Task Unsupported_country_is_a_validation_error()
    {
        var result = await Quote("US", ("SKU-MOUSE", 1));

        Assert.Equal("pricing.unsupported_country", result.Error!.Code);
    }

    private Task<SharedKernel.Result<PriceQuote>> Quote(string country, params (string Sku, int Quantity)[] lines) =>
        _pricing.QuoteAsync(new PriceQuoteRequest(country, [.. lines.Select(l => new PriceQuoteLine(l.Sku, l.Quantity))]), CancellationToken.None);
}
