using Pedia.Core.Utilities;

namespace Pedia.Tests;

public sealed class UtilityTests
{
    [Theory]
    [InlineData("  Åland: birds?.md  ", "Åland birds.md")]
    [InlineData("CON", "_CON")]
    [InlineData("..", "Untitled")]
    [InlineData("日本語 🐦", "日本語 🐦")]
    public void Filename_sanitization_is_Unicode_safe_and_Windows_compatible(string input, string expected)
    {
        Assert.Equal(expected, FileNameUtilities.SanitizeFileName(input));
    }

    [Fact]
    public void Utc_helpers_normalize_and_round_trip_iso_values()
    {
        var localOffset = new DateTimeOffset(2026, 8, 12, 12, 30, 0, TimeSpan.FromHours(3));

        var text = UtcDateTime.FormatIso(localOffset);
        var restored = UtcDateTime.ParseIso(text);

        Assert.Equal("2026-08-12T09:30:00.0000000Z", text);
        Assert.Equal(TimeSpan.Zero, restored.Offset);
        Assert.Equal(localOffset, restored);
    }
}
