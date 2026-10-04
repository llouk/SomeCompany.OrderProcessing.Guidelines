namespace SomeCompany.OrderProcessing.SharedKernel;

/// <summary>Amount + currency. Never use a bare decimal for money across module boundaries.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Multiply(decimal factor) =>
        new(Math.Round(Amount * factor, 2, MidpointRounding.AwayFromZero), Currency);

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException($"Cannot add {left.Currency} to {right.Currency}.");

        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
