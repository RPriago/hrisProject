using PeopleFlow.Kernel.Common;

namespace PeopleFlow.Kernel.ValueObjects;

public readonly record struct Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }

    public Money(decimal amount, string currency)
    {
        Guard.AgainstNullOrWhiteSpace(currency, nameof(currency));

        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money RoundTo(int decimals, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        return new Money(Math.Round(Amount, decimals, rounding), Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot operate on money with different currencies: '{Currency}' and '{other.Currency}'.");
        }
    }

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
    public static Money operator *(Money left, decimal multiplier) => new(left.Amount * multiplier, left.Currency);
    public static Money operator *(decimal multiplier, Money right) => right * multiplier;
    public static Money operator /(Money left, decimal divisor) => new(left.Amount / divisor, left.Currency);

    public override string ToString() => $"{Amount:0.00##} {Currency}";
}
