using System.Text.RegularExpressions;

namespace Pedia.Core.Search;

public static partial class WordCounter
{
    public static int Count(IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return texts.Sum(text => string.IsNullOrWhiteSpace(text) ? 0 : WordPattern().Matches(text).Count);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
