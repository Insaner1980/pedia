namespace Pedia.Core.Models;

public enum ArticleSmartView
{
    All,
    Favorites,
    RecentlyEdited,
    Uncategorized,
    Trash
}

public enum ArticleSearchScope
{
    AllText,
    TitleOnly
}

public enum ArticleSortField
{
    Relevance,
    Title,
    Language,
    WordCount,
    Status,
    Created,
    Updated
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed record ArticleQuery
{
    public string? SearchText { get; init; }
    public ArticleSearchScope SearchScope { get; init; } = ArticleSearchScope.AllText;
    public ArticleSmartView View { get; init; } = ArticleSmartView.All;
    public long? TopicId { get; init; }
    public bool IncludeDescendantTopics { get; init; }
    public IReadOnlyCollection<string> LanguageCodes { get; init; } = [];
    public IReadOnlyCollection<string> ArticleTypes { get; init; } = [];
    public IReadOnlyCollection<string> Statuses { get; init; } = [];
    public bool? IsFavorite { get; init; }
    public bool? HasSources { get; init; }
    public int? MinimumWordCount { get; init; }
    public int? MaximumWordCount { get; init; }
    public DateTimeOffset? CreatedFromUtc { get; init; }
    public DateTimeOffset? CreatedToUtc { get; init; }
    public DateTimeOffset? UpdatedFromUtc { get; init; }
    public DateTimeOffset? UpdatedToUtc { get; init; }
    public bool? IsArchived { get; init; }
    public bool? IsSample { get; init; }
    public ArticleSortField SortField { get; init; } = ArticleSortField.Relevance;
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record ArticleSummary(
    long Id,
    string Title,
    string? Subtitle,
    string? Summary,
    string LanguageCode,
    string ArticleType,
    string Status,
    bool IsFavorite,
    int WordCount,
    bool IsSample,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    int TopicCount,
    int SourceCount,
    string? Snippet,
    double? Rank);

public sealed record ArticlePage(
    IReadOnlyList<ArticleSummary> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
