using System.Text;
using Microsoft.Data.Sqlite;
using Pedia.Core.Data;
using Pedia.Core.Models;
using Pedia.Core.Repositories;

namespace Pedia.Core.Importing;

public sealed class PediaImportRepository : IImportRepository
{
    private readonly ArticleRepository _articles;
    private readonly SqliteConnectionFactory _connections;
    private readonly string _languageCode;
    private readonly string _status;
    private readonly long? _destinationTopicId;

    public PediaImportRepository(
        ArticleRepository articles,
        SqliteConnectionFactory connections,
        string languageCode = "en",
        string status = ArticleStatuses.Draft,
        long? destinationTopicId = null)
    {
        _articles = articles ?? throw new ArgumentNullException(nameof(articles));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _languageCode = string.IsNullOrWhiteSpace(languageCode)
            ? throw new ArgumentException("An import language code is required.", nameof(languageCode))
            : languageCode.Trim();
        _status = string.IsNullOrWhiteSpace(status)
            ? throw new ArgumentException("An import status is required.", nameof(status))
            : status.Trim();
        _destinationTopicId = destinationTopicId;
    }

    public async Task<ExistingImportedArticle?> FindByTitleAsync(
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title
            FROM Articles
            WHERE DeletedAtUtc IS NULL
              AND Title = $title COLLATE NOCASE
            ORDER BY Id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$title", title.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ExistingImportedArticle(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    public Task<long> CreateAsync(ImportedArticleDraft article, CancellationToken cancellationToken) =>
        _articles.CreateAsync(MapArticle(article), cancellationToken);

    public Task ReplaceAsync(
        long articleId,
        ImportedArticleDraft article,
        CancellationToken cancellationToken) =>
        _articles.UpdateAsync(articleId, MapArticle(article), cancellationToken);

    public async Task<long> BeginRunAsync(ImportRunStart run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!Enum.IsDefined(run.DuplicateMode) || run.RequestedFileCount < 0)
        {
            throw new ArgumentException("The import run metadata is invalid.", nameof(run));
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImportRuns(
                ImportKind, SourceDescription, StartedAtUtc, Status,
                ImportedCount, SkippedCount, ErrorCount)
            VALUES (
                $importKind, $sourceDescription, $startedAtUtc, 'Running',
                0, 0, 0);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$importKind", $"LocalFiles/{run.DuplicateMode}");
        command.Parameters.AddWithValue(
            "$sourceDescription",
            run.RequestedFileCount == 1 ? "1 local file" : $"{run.RequestedFileCount} local files");
        command.Parameters.AddWithValue("$startedAtUtc", DatabaseValue.Date(run.StartedAtUtc));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task CompleteRunAsync(
        long runId,
        ImportRunCompletion completion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (runId < 1 || !Enum.IsDefined(completion.Outcome) ||
            completion.ImportedCount < 0 || completion.SkippedCount < 0 ||
            completion.ReplacedCount < 0 || completion.FailedCount < 0)
        {
            throw new ArgumentException("The import run completion is invalid.", nameof(completion));
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImportRuns
            SET CompletedAtUtc = $completedAtUtc,
                Status = $status,
                ImportedCount = $importedCount,
                SkippedCount = $skippedCount,
                ErrorCount = $errorCount,
                ErrorSummary = $errorSummary
            WHERE Id = $runId;
            """;
        command.Parameters.AddWithValue("$completedAtUtc", DatabaseValue.Date(completion.CompletedAtUtc));
        command.Parameters.AddWithValue("$status", completion.Outcome.ToString());
        command.Parameters.AddWithValue("$importedCount", completion.ImportedCount + completion.ReplacedCount);
        command.Parameters.AddWithValue("$skippedCount", completion.SkippedCount);
        command.Parameters.AddWithValue("$errorCount", completion.FailedCount);
        command.Parameters.AddWithValue("$errorSummary", (object?)CreateErrorSummary(completion.Files) ?? DBNull.Value);
        command.Parameters.AddWithValue("$runId", runId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new KeyNotFoundException($"Import run {runId} was not found.");
        }
    }

    private ArticleDraft MapArticle(ImportedArticleDraft imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(imported.Document);
        ArgumentNullException.ThrowIfNull(imported.Source);

        var sections = new List<ArticleSectionDraft>();
        if (imported.Document.LeadBlocks.Count > 0)
        {
            sections.Add(new ArticleSectionDraft(null, 1, RenderBlocks(imported.Document.LeadBlocks)));
        }

        sections.AddRange(imported.Document.Sections.Select(section =>
            new ArticleSectionDraft(
                section.Heading,
                Math.Clamp(section.Level, 2, 3),
                RenderBlocks(section.Blocks))));

        return new ArticleDraft
        {
            Title = imported.Document.Title,
            LanguageCode = _languageCode,
            ArticleType = ArticleTypes.General,
            Status = _status,
            Sections = sections,
            Sources =
            [
                new ArticleSourceDraft
                {
                    SourceType = imported.Source.Format == ImportFileFormat.Markdown
                        ? SourceTypes.LocalMarkdownFile
                        : SourceTypes.LocalTextFile,
                    Title = imported.Source.FileName,
                    ExternalPageId = imported.Source.FullPath,
                    ExternalRevisionId = imported.Source.Sha256,
                    RetrievedAtUtc = imported.ImportedAtUtc,
                    LastCheckedAtUtc = imported.Source.LastModifiedUtc,
                    Notes = $"{imported.Source.ByteLength} bytes"
                }
            ],
            TopicAssignments = _destinationTopicId is { } topicId
                ? [new ArticleTopicDraft(topicId, true)]
                : []
        };
    }

    private static string RenderBlocks(IReadOnlyList<ContentBlock> blocks)
    {
        var rendered = blocks.Select(block => block.Kind switch
        {
            ContentBlockKind.Paragraph => block.Text ?? string.Empty,
            ContentBlockKind.UnorderedList => string.Join('\n', block.Items.Select(item => $"- {item}")),
            ContentBlockKind.OrderedList => string.Join(
                '\n',
                block.Items.Select((item, index) => $"{index + 1}. {item}")),
            _ => throw new InvalidDataException($"Unsupported content block kind {block.Kind}.")
        });
        return string.Join("\n\n", rendered);
    }

    private static string? CreateErrorSummary(IReadOnlyList<ImportFileResult> files)
    {
        var errors = files
            .Where(file => file.Outcome == ImportFileOutcome.Failed && !string.IsNullOrWhiteSpace(file.Error))
            .Select(file => $"{Path.GetFileName(file.SourcePath)}: {file.Error!.Trim()}")
            .ToArray();
        if (errors.Length == 0)
        {
            return null;
        }

        const int maximumLength = 8_000;
        var summary = string.Join(Environment.NewLine, errors);
        return summary.Length <= maximumLength ? summary : summary[..maximumLength];
    }
}
