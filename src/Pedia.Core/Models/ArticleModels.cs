namespace Pedia.Core.Models;

public static class ArticleTypes
{
    public const string General = "General";
    public const string Person = "Person";
    public const string Place = "Place";
    public const string Event = "Event";
    public const string Concept = "Concept";
    public const string Organization = "Organization";
    public const string Timeline = "Timeline";
    public const string Other = "Other";
}

public static class ArticleStatuses
{
    public const string Draft = "Draft";
    public const string Ready = "Ready";
    public const string NeedsReview = "Needs review";
    public const string Archived = "Archived";
}

public static class SourceTypes
{
    public const string Manual = "Manual";
    public const string LocalTextFile = "Local text file";
    public const string LocalMarkdownFile = "Local Markdown file";
    public const string Book = "Book";
    public const string Website = "Website";
    public const string Encyclopedia = "Encyclopedia";
    public const string Other = "Other";
}

public sealed record ArticleSectionDraft(string? Heading, int HeadingLevel, string Body);

public sealed record ArticleTopicDraft(long TopicId, bool IsPrimary = false);

public sealed record ArticleSourceDraft
{
    public string SourceType { get; init; } = SourceTypes.Manual;
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? ExternalPageId { get; init; }
    public string? ExternalRevisionId { get; init; }
    public string? LicenseName { get; init; }
    public string? AttributionText { get; init; }
    public DateTimeOffset? RetrievedAtUtc { get; init; }
    public DateTimeOffset? LastCheckedAtUtc { get; init; }
    public string? Notes { get; init; }
}

public sealed record ArticleDraft
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Summary { get; init; }
    public string LanguageCode { get; init; } = "en";
    public string ArticleType { get; init; } = ArticleTypes.General;
    public string Status { get; init; } = ArticleStatuses.Draft;
    public string? Notes { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsSample { get; init; }
    public IReadOnlyList<ArticleSectionDraft> Sections { get; init; } = [];
    public IReadOnlyList<ArticleSourceDraft> Sources { get; init; } = [];
    public IReadOnlyList<ArticleTopicDraft> TopicAssignments { get; init; } = [];
}

public sealed record ArticleSection(
    long Id,
    string? Heading,
    int HeadingLevel,
    string Body,
    int SortOrder);

public sealed record ArticleSource(
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
    int SortOrder);

public sealed record ArticleTopicAssignment(
    long TopicId,
    string TopicName,
    string TopicPath,
    bool IsPrimary,
    DateTimeOffset CreatedAtUtc);

public sealed record ArticleDetails(
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
    IReadOnlyList<ArticleSection> Sections,
    IReadOnlyList<ArticleSource> Sources,
    IReadOnlyList<ArticleTopicAssignment> TopicAssignments);

public sealed record SampleDeletionResult(int DeletedArticleCount, int DeletedTopicCount);

public sealed record LibraryStatistics(
    int ActiveArticleCount,
    int TrashedArticleCount,
    int ActiveTopicCount,
    int SourceCount);
