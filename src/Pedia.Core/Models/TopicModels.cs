namespace Pedia.Core.Models;

public sealed record TopicSummary(
    long Id,
    long? ParentId,
    string Name,
    string? Description,
    int SortOrder,
    bool IsSample,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int DirectArticleCount,
    int SubtreeArticleCount);

public sealed record TopicDeleteResult(int ReparentedChildCount, int RemovedArticleAssignmentCount);
