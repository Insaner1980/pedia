using Pedia.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pedia.Core.Importing;

public sealed class FileImportService
{
    private readonly IImportRepository _repository;
    private readonly ImportPreviewService _previewService;
    private readonly IClock _clock;
    private readonly ILogger<FileImportService> _logger;

    public FileImportService(
        IImportRepository repository,
        ImportPreviewService? previewService = null,
        IClock? clock = null,
        ILogger<FileImportService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _previewService = previewService ?? new ImportPreviewService();
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<FileImportService>.Instance;
    }

    public async Task<ImportBatchResult> ImportAsync(
        IReadOnlyList<string> filePaths,
        DuplicateMode duplicateMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (!Enum.IsDefined(duplicateMode))
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateMode));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var runId = await _repository.BeginRunAsync(
            new ImportRunStart(_clock.UtcNow, duplicateMode, filePaths.Count),
            cancellationToken);
        var results = new List<ImportFileResult>(filePaths.Count);

        try
        {
            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    results.Add(await ImportOneAsync(filePath, duplicateMode, cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Could not import local file {ImportFilePath}", filePath);
                    results.Add(new ImportFileResult(
                        filePath,
                        ImportFileOutcome.Failed,
                        null,
                        null,
                        exception.Message));
                }
            }

            await _repository.CompleteRunAsync(
                runId,
                CreateCompletion(ImportRunOutcome.Completed, results),
                cancellationToken);
            return new ImportBatchResult(runId, results);
        }
        catch (OperationCanceledException)
        {
            await _repository.CompleteRunAsync(
                runId,
                CreateCompletion(ImportRunOutcome.Cancelled, results),
                CancellationToken.None);
            throw;
        }
    }

    private async Task<ImportFileResult> ImportOneAsync(
        string filePath,
        DuplicateMode duplicateMode,
        CancellationToken cancellationToken)
    {
        var preview = await _previewService.PreviewAsync(filePath, cancellationToken);
        var existing = await _repository.FindByTitleAsync(preview.Document.Title, cancellationToken);
        if (existing is not null && duplicateMode == DuplicateMode.Skip)
        {
            return new ImportFileResult(
                preview.Source.FullPath,
                ImportFileOutcome.Skipped,
                existing.ArticleId,
                existing.Title,
                null);
        }

        if (existing is not null && duplicateMode == DuplicateMode.Replace)
        {
            var replacement = new ImportedArticleDraft(preview.Document, preview.Source, _clock.UtcNow);
            await _repository.ReplaceAsync(existing.ArticleId, replacement, cancellationToken);
            return new ImportFileResult(
                preview.Source.FullPath,
                ImportFileOutcome.Replaced,
                existing.ArticleId,
                preview.Document.Title,
                null);
        }

        var document = preview.Document;
        if (existing is not null)
        {
            document = await CreateCopyDocumentAsync(document, cancellationToken);
        }

        var draft = new ImportedArticleDraft(document, preview.Source, _clock.UtcNow);
        var articleId = await _repository.CreateAsync(draft, cancellationToken);
        return new ImportFileResult(
            preview.Source.FullPath,
            ImportFileOutcome.Imported,
            articleId,
            document.Title,
            null);
    }

    private async Task<ParsedDocument> CreateCopyDocumentAsync(
        ParsedDocument original,
        CancellationToken cancellationToken)
    {
        for (var copyNumber = 2; copyNumber < int.MaxValue; copyNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = $"{original.Title} ({copyNumber})";
            if (await _repository.FindByTitleAsync(candidate, cancellationToken) is null)
            {
                return original with { Title = candidate };
            }
        }

        throw new InvalidOperationException("No available copy title could be generated.");
    }

    private ImportRunCompletion CreateCompletion(
        ImportRunOutcome outcome,
        IReadOnlyList<ImportFileResult> results) => new(
        _clock.UtcNow,
        outcome,
        results.Count(result => result.Outcome == ImportFileOutcome.Imported),
        results.Count(result => result.Outcome == ImportFileOutcome.Skipped),
        results.Count(result => result.Outcome == ImportFileOutcome.Replaced),
        results.Count(result => result.Outcome == ImportFileOutcome.Failed),
        results.ToArray());
}
