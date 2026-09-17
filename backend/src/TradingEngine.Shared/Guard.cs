// ═══════════════════════════════════════════════════════════════════
// Guard.cs - Defensive validation helpers.
// Used throughout the domain to fail fast on invalid arguments,
// preventing corrupted state and simplifying debugging.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Shared;

/// <summary>
/// Static guard clauses. Every public method throws immediately
/// if a pre-condition is violated, with a descriptive message.
/// </summary>
public static class Guard
{
    /// <summary>Throws if value is null, empty, or whitespace.</summary>
    public static string NotNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                $"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value;
    }

    /// <summary>Throws if value is null.</summary>
    public static T NotNull<T>(T value, string paramName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName,
                $"{paramName} cannot be null.");
        return value;
    }

    /// <summary>Throws if value is zero or negative.</summary>
    public static decimal Positive(decimal value, string paramName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName,
                $"{paramName} must be positive. Got: {value}");
        return value;
    }

    /// <summary>Throws if value is zero or negative.</summary>
    public static long Positive(long value, string paramName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName,
                $"{paramName} must be positive. Got: {value}");
        return value;
    }

    /// <summary>Throws if value is negative.</summary>
    public static decimal NonNegative(decimal value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName,
                $"{paramName} must be non-negative. Got: {value}");
        return value;
    }

    /// <summary>Throws if value is not in the inclusive range.</summary>
    public static decimal InRange(decimal value, string paramName, decimal min, decimal max)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(paramName,
                $"{paramName} must be between {min} and {max}. Got: {value}");
        return value;
    }

    /// <summary>Throws if the GUID is empty.</summary>
    public static Guid NotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException(
                $"{paramName} cannot be an empty GUID.", paramName);
        return value;
    }

    /// <summary>Throws if the string exceeds a maximum length.</summary>
    public static string MaxLength(string value, string paramName, int maxLength)
    {
        if (value.Length > maxLength)
            throw new ArgumentException(
                $"{paramName} exceeds maximum length of {maxLength}. Got: {value.Length}.",
                paramName);
        return value;
    }
}