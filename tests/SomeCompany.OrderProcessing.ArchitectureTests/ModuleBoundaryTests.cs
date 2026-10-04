using System.Reflection;
using SomeCompany.OrderProcessing.Inventory;
using SomeCompany.OrderProcessing.Ordering;
using SomeCompany.OrderProcessing.Payments;
using SomeCompany.OrderProcessing.Pricing;
using SomeCompany.OrderProcessing.Shipping;

namespace SomeCompany.OrderProcessing.ArchitectureTests;

/// <summary>
/// Turns the module rules in docs/decisions.md (ADR-002) into failing builds instead of code-review comments.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly Ordering = typeof(OrderingModule).Assembly;

    private static readonly Assembly[] SupportingModules =
    [
        typeof(PricingModule).Assembly,
        typeof(InventoryModule).Assembly,
        typeof(PaymentsModule).Assembly,
        typeof(ShippingModule).Assembly
    ];

    [Fact]
    public void Modules_expose_only_their_contracts_and_registration_class()
    {
        var leaked = SupportingModules.Append(Ordering)
            .SelectMany(module => module.GetExportedTypes())
            .Where(type => !(type.Namespace?.EndsWith(".Contracts") ?? false) && !type.Name.EndsWith("Module"))
            .Select(type => type.FullName);

        Assert.Empty(leaked);
    }

    [Fact]
    public void Supporting_modules_do_not_depend_on_other_modules()
    {
        var moduleNames = SupportingModules.Append(Ordering).Select(m => m.GetName().Name).ToHashSet();

        var violations = SupportingModules.SelectMany(module => module
            .GetReferencedAssemblies()
            .Where(reference => moduleNames.Contains(reference.Name))
            .Select(reference => $"{module.GetName().Name} -> {reference.Name}"));

        Assert.Empty(violations);
    }
}
