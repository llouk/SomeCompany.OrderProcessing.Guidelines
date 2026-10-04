using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SomeCompany.OrderProcessing.Ordering.Api;
using SomeCompany.OrderProcessing.Ordering.Application;
using SomeCompany.OrderProcessing.Ordering.Infrastructure;
using StackExchange.Redis;

namespace SomeCompany.OrderProcessing.Ordering;

/// <summary>
/// Entry point of the Ordering module. Expects the host to register Pricing, Inventory, Payments and Shipping.
/// </summary>
public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Redis is required: there is no silent in-memory fallback for idempotency (it must be shared by all instances).
        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnection))
            throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrderingModule(this IEndpointRouteBuilder app) => app.MapOrderEndpoints();
}
