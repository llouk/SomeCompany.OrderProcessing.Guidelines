using Microsoft.Extensions.DependencyInjection;
using SomeCompany.OrderProcessing.Payments.Contracts;

namespace SomeCompany.OrderProcessing.Payments;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddSingleton<IPaymentService, FakePaymentService>();
        return services;
    }
}
