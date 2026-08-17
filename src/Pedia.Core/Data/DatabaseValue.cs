using System.Globalization;

namespace Pedia.Core.Data;

internal static class DatabaseValue
{
    public static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static object NullableDate(DateTimeOffset? value) =>
        value is null ? DBNull.Value : Date(value.Value);

    public static DateTimeOffset ReadDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    public static string? OptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
