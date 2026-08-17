using Pedia.Models;

namespace Pedia.Services;

public sealed class PediaSettings
{
    public string DefaultLanguageCode { get; set; } = "en";
    public string DefaultArticleStatus { get; set; } = "Draft";
    public bool RestoreLastArticle { get; set; } = true;
    public bool ConfirmBeforeTrash { get; set; } = true;
    public int PageSize { get; set; } = 50;
    public bool IncludeSubtopicsByDefault { get; set; }
    public double ArticleBodyFontSize { get; set; } = 16;
    public double ArticleLineSpacing { get; set; } = 24;
    public double MaximumReadingWidth { get; set; } = 860;
    public bool RememberScrollPositions { get; set; } = true;
    public bool CompactDensity { get; set; } = true;
    public WindowLayoutState Window { get; set; } = new();
    public Dictionary<long, double> ArticleScrollPositions { get; set; } = [];
}

public sealed class WindowLayoutState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 950;
    public bool IsMaximized { get; set; }
    public double TopicPaneWidth { get; set; } = 290;
    public double ArticlePaneWidth { get; set; } = 560;
    public bool IsTopicPaneCollapsed { get; set; }
    public long? SelectedTopicId { get; set; }
    public long? SelectedArticleId { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public bool IncludeSubtopics { get; set; }
    public ArticleSearchScopeKind SearchScope { get; set; } = ArticleSearchScopeKind.AllText;
    public string? SelectedLanguageCode { get; set; }
    public bool IncludeEnglish { get; set; }
    public bool IncludeFinnish { get; set; }
    public string? ArticleType { get; set; }
    public string? ArticleStatus { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool? HasSources { get; set; }
    public int? MinimumWordCount { get; set; }
    public int? MaximumWordCount { get; set; }
    public DateTimeOffset? CreatedFrom { get; set; }
    public DateTimeOffset? CreatedTo { get; set; }
    public DateTimeOffset? UpdatedFrom { get; set; }
    public DateTimeOffset? UpdatedTo { get; set; }
    public bool? IsArchived { get; set; }
    public bool? IsSample { get; set; }
    public ArticleSortField SortField { get; set; } = ArticleSortField.Relevance;
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    public int PageNumber { get; set; } = 1;
}
