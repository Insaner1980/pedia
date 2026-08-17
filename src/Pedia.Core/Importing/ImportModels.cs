namespace Pedia.Core.Importing;

public enum ImportFileFormat
{
    PlainText,
    Markdown
}

public enum DuplicateMode
{
    Skip,
    CreateCopy,
    Replace
}

public enum ImportFileOutcome
{
    Imported,
    Skipped,
    Replaced,
    Failed
}

public enum ImportRunOutcome
{
    Completed,
    Cancelled
}

public sealed record LocalFileSourceMetadata(
    string FullPath,
    string FileName,
    ImportFileFormat Format,
    long ByteLength,
    DateTimeOffset LastModifiedUtc,
    string Sha256);

public sealed record ImportPreview(ParsedDocument Document, LocalFileSourceMetadata Source);

public sealed record ImportedArticleDraft(
    ParsedDocument Document,
    LocalFileSourceMetadata Source,
    DateTimeOffset ImportedAtUtc);

public sealed record ExistingImportedArticle(long ArticleId, string Title);

public sealed record ImportFileResult(
    string SourcePath,
    ImportFileOutcome Outcome,
    long? ArticleId,
    string? EffectiveTitle,
    string? Error);

public sealed record ImportBatchResult(long RunId, IReadOnlyList<ImportFileResult> Files);

public sealed record ImportRunStart(
    DateTimeOffset StartedAtUtc,
    DuplicateMode DuplicateMode,
    int RequestedFileCount);

public sealed record ImportRunCompletion(
    DateTimeOffset CompletedAtUtc,
    ImportRunOutcome Outcome,
    int ImportedCount,
    int SkippedCount,
    int ReplacedCount,
    int FailedCount,
    IReadOnlyList<ImportFileResult> Files);

public interface IImportRepository
{
    Task<ExistingImportedArticle?> FindByTitleAsync(string title, CancellationToken cancellationToken);

    Task<long> CreateAsync(ImportedArticleDraft article, CancellationToken cancellationToken);

    Task ReplaceAsync(long articleId, ImportedArticleDraft article, CancellationToken cancellationToken);

    Task<long> BeginRunAsync(ImportRunStart run, CancellationToken cancellationToken);

    Task CompleteRunAsync(long runId, ImportRunCompletion completion, CancellationToken cancellationToken);
}
