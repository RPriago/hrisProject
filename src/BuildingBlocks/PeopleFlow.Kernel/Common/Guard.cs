namespace PeopleFlow.Kernel.Common;

public static class Guard
{
    public static T AgainstNull<T>(T? value, string paramName, string? message = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName, message ?? $"Value cannot be null: {paramName}");
        }

        return value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, string paramName, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message ?? $"Value cannot be null or whitespace: {paramName}", paramName);
        }

        return value;
    }

    public static string AgainstNullOrEmpty(string? value, string paramName, string? message = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException(message ?? $"Value cannot be null or empty: {paramName}", paramName);
        }

        return value;
    }

    public static decimal AgainstNegative(decimal value, string paramName, string? message = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, message ?? $"Value cannot be negative: {paramName}");
        }

        return value;
    }
}
