using System.Text;
using System.Text.RegularExpressions;

namespace Pedia.Core.Utilities;

public static partial class FileNameUtilities
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeFileName(string? value, int maximumLength = 120)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);

        var builder = new StringBuilder();
        foreach (var rune in (value ?? string.Empty).EnumerateRunes())
        {
            if (rune.Value < 32 || rune.Value is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                continue;
            }

            builder.Append(rune.ToString());
        }

        var sanitized = WhitespacePattern().Replace(builder.ToString(), " ").Trim(' ', '.');
        if (sanitized.Length == 0)
        {
            return "Untitled";
        }

        sanitized = TruncateWithoutSplittingSurrogate(sanitized, maximumLength).TrimEnd(' ', '.');
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(stem))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized.Length == 0 ? "Untitled" : sanitized;
    }

    public static string GetCollisionPath(string directoryPath, string fileName, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        if (number == 1)
        {
            return Path.Combine(directoryPath, fileName);
        }

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directoryPath, $"{stem} ({number}){extension}");
    }

    private static string TruncateWithoutSplittingSurrogate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
