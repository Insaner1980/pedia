using System.Globalization;

namespace Pedia.Core.Utilities;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset value) : IClock
{
    public DateTimeOffset UtcNow { get; } = value.ToUniversalTime();
}

public static class UtcDateTime
{
    public static string FormatIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseIso(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new FormatException("The value is not a valid ISO 8601 timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    public static DateTimeOffset EnsureUtc(DateTimeOffset value) => value.ToUniversalTime();
}
