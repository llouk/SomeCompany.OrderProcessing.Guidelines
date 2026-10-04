using Microsoft.Extensions.DependencyInjection;
using SomeCompany.OrderProcessing.Pricing.Contracts;

namespace SomeCompany.OrderProcessing.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddSingleton<IProductCatalog, InMemoryProductCatalog>();
        services.AddSingleton<ITaxRateProvider, InMemoryTaxRateProvider>();
        services.AddSingleton<IChargeRule, ShippingFeeRule>();
        services.AddScoped<IPricingService, PricingService>();
        return services;
    }
}
