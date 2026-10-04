using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SomeCompany.OrderProcessing.Inventory.Contracts;

namespace SomeCompany.OrderProcessing.Inventory;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Inventory:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            services.AddSingleton<IInventoryService, FakeInventoryService>();
            return services;
        }

        // Standard handler = retry with backoff + per-attempt timeout + circuit breaker.
        // Retrying POST is safe because reservations are keyed by orderId (idempotent on the provider side).
        services.AddHttpClient<IInventoryService, InventoryApiClient>(client => client.BaseAddress = new Uri(baseUrl))
            .AddStandardResilienceHandler();

        return services;
    }
}
