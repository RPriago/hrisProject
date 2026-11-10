namespace PeopleFlow.Kernel.ValueObjects;

public readonly record struct EffectiveDate
{
    public DateOnly From { get; init; }
    public DateOnly? To { get; init; }

    public EffectiveDate(DateOnly from, DateOnly? to)
    {
        if (to.HasValue && to.Value < from)
        {
            throw new ArgumentException(
                $"To date ({to.Value:yyyy-MM-dd}) cannot be earlier than From date ({from:yyyy-MM-dd}).",
                nameof(to));
        }

        From = from;
        To = to;
    }

    public bool IsActiveAt(DateOnly date)
    {
        return date >= From && (!To.HasValue || date <= To.Value);
    }

    public bool IsActiveAt(DateTimeOffset dateTimeOffset)
    {
        return IsActiveAt(DateOnly.FromDateTime(dateTimeOffset.UtcDateTime));
    }

    public override string ToString() => To.HasValue
        ? $"{From:yyyy-MM-dd}..{To.Value:yyyy-MM-dd}"
        : $"{From:yyyy-MM-dd}..*";
}
