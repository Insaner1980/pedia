using System.Globalization;
using Pedia.Models;
using Pedia.Services;

namespace Pedia.ViewModels;

public sealed class ArticleRowViewModel(ArticleListData data, IStringService strings)
{
    public long Id { get; } = data.Id;
    public string Title { get; } = data.Title;
    public string LanguageCode { get; } = data.LanguageCode;
    public string LanguageDisplay { get; } = GetLanguageDisplay(data.LanguageCode);
    public int WordCount { get; } = data.WordCount;
    public string WordCountDisplay { get; } = data.WordCount.ToString("N0");
    public string Status { get; } = data.Status;
    public string StatusDisplay { get; } = strings.Get(data.Status switch
    {
        "Draft" => "DraftStatus",
        "Ready" => "ReadyStatus",
        "Needs review" => "NeedsReviewStatus",
        "Archived" => "ArchivedStatus",
        _ => "UnknownStatus"
    });
    public DateTimeOffset UpdatedAtUtc { get; } = data.UpdatedAtUtc;
    public string UpdatedDisplay { get; } = data.UpdatedAtUtc.ToLocalTime().ToString("g");
    public bool IsFavorite { get; } = data.IsFavorite;
    public bool IsDeleted { get; } = data.IsDeleted;
    public string? MatchSnippet { get; } = data.MatchSnippet;
    public bool HasMatchSnippet => !string.IsNullOrWhiteSpace(MatchSnippet);
    public string AccessibleName => strings.Format(
        "ArticleRowAccessibleNameFormat",
        Title,
        LanguageDisplay,
        WordCount,
        StatusDisplay,
        UpdatedAtUtc.ToLocalTime());

    private static string GetLanguageDisplay(string languageCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageCode).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return languageCode;
        }
    }
}
