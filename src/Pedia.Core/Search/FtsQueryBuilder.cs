using System.Text.RegularExpressions;
using Pedia.Core.Models;

namespace Pedia.Core.Search;

public static partial class FtsQueryBuilder
{
    public static string? Build(string? input, ArticleSearchScope scope = ArticleSearchScope.AllText)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var clauses = new List<string>();
        foreach (Match match in QueryPartPattern().Matches(input))
        {
            var phrase = match.Groups["phrase"];
            if (phrase.Success)
            {
                var phraseTerms = TermPattern().Matches(phrase.Value).Select(item => item.Value).ToArray();
                if (phraseTerms.Length > 0)
                {
                    clauses.Add(Quote(string.Join(' ', phraseTerms)));
                }

                continue;
            }

            var term = match.Groups["term"].Value;
            if (term.Length > 0)
            {
                clauses.Add(Quote(term) + "*");
            }
        }

        if (clauses.Count == 0)
        {
            return null;
        }

        return scope == ArticleSearchScope.TitleOnly
            ? string.Join(' ', clauses.Select(clause => $"Title : {clause}"))
            : string.Join(' ', clauses);
    }

    public static bool ShouldUseTitleFallback(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var terms = TermPattern().Matches(input).Select(match => match.Value).ToArray();
        return terms.Length == 0 || terms.All(term => term.EnumerateRunes().Count() < 2);
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    [GeneratedRegex("\\\"(?<phrase>[^\\\"]+)\\\"|(?<term>[\\p{L}\\p{N}]+(?:['’\\-][\\p{L}\\p{N}]+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex QueryPartPattern();

    [GeneratedRegex("[\\p{L}\\p{N}]+(?:['’\\-][\\p{L}\\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex TermPattern();
}
