using Microsoft.Extensions.DependencyInjection;
using SomeCompany.OrderProcessing.Shipping.Contracts;

namespace SomeCompany.OrderProcessing.Shipping;

public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services)
    {
        services.AddSingleton<IShippingService, FakeShippingService>();
        return services;
    }
}
