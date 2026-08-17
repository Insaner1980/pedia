using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pedia.Models;

public enum LibraryScopeKind
{
    AllArticles,
    Favorites,
    RecentlyEdited,
    Uncategorized,
    Trash,
    Topic
}

public enum ArticleSortField
{
    Relevance,
    Title,
    Language,
    WordCount,
    Status,
    Updated
}

public enum SortDirection
{
    Ascending,
    Descending
}

public enum ExportFormat
{
    PlainText,
    Markdown,
    PediaJson
}

public enum ArticleBulkActionKind
{
    AddToTopics,
    RemoveFromCurrentTopic,
    ChangeStatus,
    MoveToTrash
}

public enum ImportDuplicateHandling
{
    Skip,
    CreateCopy,
    Replace
}

public enum ArticleSearchScopeKind
{
    AllText,
    TitleOnly,
    CurrentTopic,
    CurrentTopicAndDescendants,
    EntireLibrary
}

public sealed record SearchScopeOption(ArticleSearchScopeKind Kind, string Label);

public sealed record NullableBooleanFilterOption(string Label, bool? Value);

public sealed record ValueLabelOption(string? Value, string Label);

public sealed record TopicData(
    long Id,
    long? ParentId,
    string Name,
    string? Description,
    int SortOrder,
    int ArticleCount,
    IReadOnlyList<TopicData> Children);

public sealed record ArticleListData(
    long Id,
    string Title,
    string LanguageCode,
    int WordCount,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    bool IsFavorite,
    bool IsDeleted,
    string? MatchSnippet);

public sealed record ArticleSectionData(
    long Id,
    string? Heading,
    int HeadingLevel,
    string Body,
    int SortOrder);

public sealed record ArticleSourceData(
    long Id,
    string SourceType,
    string? Title,
    string? Url,
    string? ExternalPageId,
    string? ExternalRevisionId,
    string? LicenseName,
    string? AttributionText,
    DateTimeOffset? RetrievedAtUtc,
    DateTimeOffset? LastCheckedAtUtc,
    string? Notes,
    int SortOrder)
{
    public bool HasValidUrl => Uri.TryCreate(Url, UriKind.Absolute, out _);
}

public sealed record ArticleTopicData(
    long TopicId,
    string Path,
    bool IsPrimary);

public sealed record ArticleDocumentData(
    long Id,
    string Title,
    string? Subtitle,
    string? Summary,
    string LanguageCode,
    string ArticleType,
    string Status,
    string? Notes,
    bool IsFavorite,
    int WordCount,
    bool IsSample,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAtUtc,
    IReadOnlyList<ArticleSectionData> Sections,
    IReadOnlyList<ArticleSourceData> Sources,
    IReadOnlyList<ArticleTopicData> Topics);

public sealed record ArticleQuery(
    LibraryScopeKind Scope,
    long? TopicId,
    bool IncludeDescendants,
    string SearchText,
    ArticleSearchScopeKind SearchScope,
    IReadOnlyList<string> LanguageCodes,
    string? ArticleType,
    string? Status,
    bool FavoritesOnly,
    bool? HasSources,
    int? MinimumWordCount,
    int? MaximumWordCount,
    DateTimeOffset? CreatedFromUtc,
    DateTimeOffset? CreatedToUtc,
    DateTimeOffset? UpdatedFromUtc,
    DateTimeOffset? UpdatedToUtc,
    bool? IsArchived,
    bool? IsSample,
    ArticleSortField SortField,
    SortDirection SortDirection,
    int PageNumber,
    int PageSize);

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record LibraryStatistics(
    int ArticleCount,
    int TopicCount,
    int SourceCount,
    DateTimeOffset? LastImportAtUtc,
    string SearchIndexState,
    int SchemaVersion,
    long DatabaseSizeBytes,
    string DatabasePath);

public sealed record ImportPreviewItem(
    string FilePath,
    string FileName,
    string ProposedTitle,
    string Format,
    bool HasTitleConflict,
    bool WillImport,
    string? Error);

public sealed record ImportPreviewResult(IReadOnlyList<ImportPreviewItem> Items);

public sealed record ImportRequest(
    IReadOnlyList<string> FilePaths,
    long? DestinationTopicId,
    string LanguageCode,
    string Status,
    ImportDuplicateHandling DuplicateHandling);

public sealed record ImportOperationResult(int ImportedCount, int SkippedCount, int ErrorCount, IReadOnlyList<string> Errors);

public sealed record BackupValidationResult(bool IsValid, string? ErrorMessage);

public sealed partial class EditableArticle : ObservableObject
{
    [ObservableProperty] public partial long Id { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Subtitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string Summary { get; set; } = string.Empty;
    [ObservableProperty] public partial string LanguageCode { get; set; } = "en";
    [ObservableProperty] public partial string ArticleType { get; set; } = "General";
    [ObservableProperty] public partial string Status { get; set; } = "Draft";
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    public ObservableCollection<EditableSection> Sections { get; } = [];
    public ObservableCollection<EditableSource> Sources { get; } = [];
    public ObservableCollection<EditableTopicAssignment> Topics { get; } = [];
}

public sealed partial class EditableSection : ObservableObject
{
    [ObservableProperty] public partial long Id { get; set; }
    [ObservableProperty] public partial string Heading { get; set; } = string.Empty;
    [ObservableProperty] public partial int HeadingLevel { get; set; } = 2;
    [ObservableProperty] public partial string Body { get; set; } = string.Empty;
}

public sealed partial class EditableSource : ObservableObject
{
    [ObservableProperty] public partial long Id { get; set; }
    [ObservableProperty] public partial string SourceType { get; set; } = "Manual";
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Url { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExternalPageId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExternalRevisionId { get; set; } = string.Empty;
    [ObservableProperty] public partial string LicenseName { get; set; } = string.Empty;
    [ObservableProperty] public partial string AttributionText { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset? RetrievedAtUtc { get; set; }
    [ObservableProperty] public partial DateTimeOffset? LastCheckedAtUtc { get; set; }
    [ObservableProperty] public partial string Notes { get; set; } = string.Empty;
}

public sealed partial class EditableTopicAssignment : ObservableObject
{
    [ObservableProperty] public partial long TopicId { get; set; }
    [ObservableProperty] public partial string Path { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsPrimary { get; set; }
}
