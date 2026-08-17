using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Logging;
using Pedia.Core.Backup;
using Pedia.Core.Data;
using Pedia.Core.Exporting;
using Pedia.Core.Importing;
using Pedia.Core.Repositories;
using Pedia.Core.Search;
using Pedia.Models;
using CoreArticleDraft = Pedia.Core.Models.ArticleDraft;
using CoreArticleQuery = Pedia.Core.Models.ArticleQuery;
using CoreArticleSectionDraft = Pedia.Core.Models.ArticleSectionDraft;
using CoreArticleSourceDraft = Pedia.Core.Models.ArticleSourceDraft;
using CoreArticleTopicDraft = Pedia.Core.Models.ArticleTopicDraft;
using CoreQuerySortDirection = Pedia.Core.Models.SortDirection;
using CoreQuerySortField = Pedia.Core.Models.ArticleSortField;

namespace Pedia.Services;

public sealed class CorePediaDataService : IPediaDataService
{
    private readonly SqliteConnectionFactory _connections;
    private readonly DatabaseInitializer _initializer;
    private readonly TopicRepository _topics;
    private readonly ArticleRepository _articles;
    private readonly IArticleQueryService _queries;
    private readonly DatabaseInformationService _databaseInformation;
    private readonly ImportPreviewService _importPreview;
    private readonly DocumentExportService _exports;
    private readonly BackupService _backups;
    private readonly IStringService _strings;
    private readonly ILogger<CorePediaDataService> _logger;
    private DatabaseInitializationResult? _initialization;

    public bool IsNewDatabase => _initialization?.IsNewDatabase == true;

    [SuppressMessage(
        "Maintainability",
        "S107",
        Justification = "The dependency-injection constructor explicitly declares the service's required collaborators.")]
    public CorePediaDataService(
        SqliteConnectionFactory connections,
        DatabaseInitializer initializer,
        TopicRepository topics,
        ArticleRepository articles,
        IArticleQueryService queries,
        DatabaseInformationService databaseInformation,
        ImportPreviewService importPreview,
        DocumentExportService exports,
        BackupService backups,
        IStringService strings,
        ILogger<CorePediaDataService> logger)
    {
        _connections = connections;
        _initializer = initializer;
        _topics = topics;
        _articles = articles;
        _queries = queries;
        _databaseInformation = databaseInformation;
        _importPreview = importPreview;
        _exports = exports;
        _backups = backups;
        _strings = strings;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => InitializeCoreAsync(cancellationToken), cancellationToken);

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        _initialization = await _initializer.InitializeAsync(seedSamples: true, cancellationToken);
        if (!await _queries.VerifyFts5Async(cancellationToken))
        {
            throw new InvalidOperationException(_strings.Get("Fts5UnavailableText"));
        }
    }

    public Task<IReadOnlyList<TopicData>> GetTopicsAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => GetTopicsCoreAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<TopicData>> GetTopicsCoreAsync(CancellationToken cancellationToken)
    {
        var topics = await _topics.GetTreeAsync(cancellationToken);
        var byParent = topics
            .GroupBy(topic => topic.ParentId ?? 0L)
            .ToDictionary(group => group.Key, group => group.OrderBy(topic => topic.SortOrder).ThenBy(topic => topic.Name).ToArray());

        IReadOnlyList<TopicData> BuildChildren(long? parentId)
        {
            if (!byParent.TryGetValue(parentId ?? 0L, out var children))
            {
                return [];
            }

            return children.Select(topic => new TopicData(
                topic.Id,
                topic.ParentId,
                topic.Name,
                topic.Description,
                topic.SortOrder,
                topic.DirectArticleCount,
                BuildChildren(topic.Id))).ToArray();
        }

        return BuildChildren(null);
    }

    public Task<PageResult<ArticleListData>> QueryArticlesAsync(
        ArticleQuery query,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => QueryArticlesCoreAsync(query, cancellationToken), cancellationToken);

    private async Task<PageResult<ArticleListData>> QueryArticlesCoreAsync(
        ArticleQuery query,
        CancellationToken cancellationToken)
    {
        var coreQuery = new CoreArticleQuery
        {
            SearchText = NullIfWhiteSpace(query.SearchText),
            SearchScope = query.SearchScope == ArticleSearchScopeKind.TitleOnly
                ? Pedia.Core.Models.ArticleSearchScope.TitleOnly
                : Pedia.Core.Models.ArticleSearchScope.AllText,
            View = query.Scope switch
            {
                LibraryScopeKind.Favorites => Pedia.Core.Models.ArticleSmartView.Favorites,
                LibraryScopeKind.RecentlyEdited => Pedia.Core.Models.ArticleSmartView.RecentlyEdited,
                LibraryScopeKind.Uncategorized => Pedia.Core.Models.ArticleSmartView.Uncategorized,
                LibraryScopeKind.Trash => Pedia.Core.Models.ArticleSmartView.Trash,
                _ => Pedia.Core.Models.ArticleSmartView.All
            },
            TopicId = query.TopicId,
            IncludeDescendantTopics = query.IncludeDescendants,
            LanguageCodes = query.LanguageCodes,
            ArticleTypes = query.ArticleType is null ? [] : [query.ArticleType],
            Statuses = query.Status is null ? [] : [query.Status],
            IsFavorite = query.FavoritesOnly ? true : null,
            HasSources = query.HasSources,
            MinimumWordCount = query.MinimumWordCount,
            MaximumWordCount = query.MaximumWordCount,
            CreatedFromUtc = query.CreatedFromUtc,
            CreatedToUtc = query.CreatedToUtc,
            UpdatedFromUtc = query.UpdatedFromUtc,
            UpdatedToUtc = query.UpdatedToUtc,
            IsArchived = query.IsArchived,
            IsSample = query.IsSample,
            SortField = query.SortField switch
            {
                ArticleSortField.Relevance => string.IsNullOrWhiteSpace(query.SearchText)
                    ? CoreQuerySortField.Title
                    : CoreQuerySortField.Relevance,
                ArticleSortField.Title => CoreQuerySortField.Title,
                ArticleSortField.Language => CoreQuerySortField.Language,
                ArticleSortField.WordCount => CoreQuerySortField.WordCount,
                ArticleSortField.Status => CoreQuerySortField.Status,
                ArticleSortField.Updated => CoreQuerySortField.Updated,
                _ => string.IsNullOrWhiteSpace(query.SearchText)
                    ? CoreQuerySortField.Title
                    : CoreQuerySortField.Relevance
            },
            SortDirection = query.SortDirection == SortDirection.Ascending
                    ? CoreQuerySortDirection.Ascending
                    : CoreQuerySortDirection.Descending,
            Page = query.PageNumber,
            PageSize = query.PageSize
        };
        var page = await _queries.QueryAsync(coreQuery, cancellationToken);
        return new PageResult<ArticleListData>(
            page.Items.Select(article => new ArticleListData(
                article.Id,
                article.Title,
                article.LanguageCode,
                article.WordCount,
                article.Status,
                article.UpdatedAtUtc,
                article.IsFavorite,
                article.DeletedAtUtc is not null,
                CleanSnippet(article.Snippet))).ToArray(),
            page.TotalCount,
            page.Page,
            page.PageSize);
    }

    public Task<ArticleDocumentData?> GetArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => GetArticleCoreAsync(articleId, cancellationToken), cancellationToken);

    private async Task<ArticleDocumentData?> GetArticleCoreAsync(long articleId, CancellationToken cancellationToken)
    {
        var article = await _articles.GetAsync(articleId, cancellationToken);
        return article is null ? null : MapArticle(article);
    }

    public Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => GetStatisticsCoreAsync(cancellationToken), cancellationToken);

    private async Task<LibraryStatistics> GetStatisticsCoreAsync(CancellationToken cancellationToken)
    {
        var statistics = await _articles.GetStatisticsAsync(cancellationToken);
        var database = await _databaseInformation.GetAsync(cancellationToken);
        return new LibraryStatistics(
            statistics.ActiveArticleCount,
            statistics.ActiveTopicCount,
            statistics.SourceCount,
            database.LastCompletedImportAtUtc,
            _strings.Get(database.IsSearchIndexReady ? "SearchIndexReadyText" : "SearchIndexNeedsRebuildingText"),
            database.SchemaVersion,
            database.DatabaseSizeBytes,
            database.DatabasePath);
    }

    public Task<long> SaveArticleAsync(EditableArticle article, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(article);
        var draft = MapDraft(article);
        var articleId = article.Id;
        return RunOffUiThreadAsync(() => SaveArticleCoreAsync(articleId, draft, cancellationToken), cancellationToken);
    }

    public Task ReplaceArticleTopicsAsync(
        long articleId,
        IReadOnlyList<ArticleTopicData> assignments,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(
            () => _articles.ReplaceTopicAssignmentsAsync(
                articleId,
                assignments.Select(topic => new CoreArticleTopicDraft(topic.TopicId, topic.IsPrimary)).ToArray(),
                cancellationToken),
            cancellationToken);

    private async Task<long> SaveArticleCoreAsync(long articleId, CoreArticleDraft draft, CancellationToken cancellationToken)
    {
        if (articleId == 0)
        {
            return await _articles.CreateAsync(draft, cancellationToken);
        }

        await _articles.UpdateAsync(articleId, draft, cancellationToken);
        return articleId;
    }

    public Task<long> DuplicateArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.DuplicateAsync(articleId, cancellationToken), cancellationToken);

    public Task SetFavoriteAsync(long articleId, bool isFavorite, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.SetFavoriteAsync(articleId, isFavorite, cancellationToken), cancellationToken);

    public Task AddTopicsToArticlesAsync(
        IReadOnlyList<long> articleIds,
        IReadOnlyList<long> topicIds,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.AddTopicsToArticlesAsync(articleIds, topicIds, cancellationToken), cancellationToken);

    public Task RemoveTopicFromArticlesAsync(
        IReadOnlyList<long> articleIds,
        long topicId,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.RemoveTopicFromArticlesAsync(articleIds, topicId, cancellationToken), cancellationToken);

    public Task SetStatusForArticlesAsync(
        IReadOnlyList<long> articleIds,
        string status,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.SetStatusForArticlesAsync(articleIds, status, cancellationToken), cancellationToken);

    public Task MoveArticlesToTrashAsync(
        IReadOnlyList<long> articleIds,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.MoveArticlesToTrashAsync(articleIds, cancellationToken), cancellationToken);

    public Task MoveArticleToTrashAsync(long articleId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.MoveToTrashAsync(articleId, cancellationToken), cancellationToken);

    public Task RestoreArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.RestoreAsync(articleId, cancellationToken), cancellationToken);

    public Task PermanentlyDeleteArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _articles.DeletePermanentlyAsync(articleId, cancellationToken), cancellationToken);

    public Task EmptyTrashAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(async () => _ = await _articles.EmptyTrashAsync(cancellationToken), cancellationToken);

    public Task<long> CreateTopicAsync(
        string name,
        string? description,
        long? parentId,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _topics.CreateAsync(name, parentId, description, false, cancellationToken), cancellationToken);

    public Task RenameTopicAsync(
        long topicId,
        string name,
        string? description,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => RenameTopicCoreAsync(topicId, name, description, cancellationToken), cancellationToken);

    private async Task RenameTopicCoreAsync(
        long topicId,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        await _topics.RenameAsync(topicId, name, cancellationToken);
        await _topics.UpdateDescriptionAsync(topicId, description, cancellationToken);
    }

    public Task MoveTopicAsync(long topicId, long? destinationParentId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _topics.MoveAsync(topicId, destinationParentId, null, cancellationToken), cancellationToken);

    public Task ReorderTopicAsync(long topicId, int newSortOrder, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => _topics.ReorderAsync(topicId, newSortOrder, cancellationToken), cancellationToken);

    public Task DeleteTopicAsync(long topicId, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(async () => _ = await _topics.DeleteAsync(topicId, cancellationToken), cancellationToken);

    public Task<ImportPreviewResult> PreviewImportAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => PreviewImportCoreAsync(filePaths, cancellationToken), cancellationToken);

    private async Task<ImportPreviewResult> PreviewImportCoreAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        var importRepository = new PediaImportRepository(_articles, _connections);
        var previews = new List<ImportPreviewItem>(filePaths.Count);
        var batchTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var preview = await _importPreview.PreviewAsync(path, cancellationToken);
                var existing = await importRepository.FindByTitleAsync(preview.Document.Title, cancellationToken);
                var hasConflict = existing is not null || !batchTitles.Add(preview.Document.Title);
                previews.Add(new ImportPreviewItem(
                    path,
                    preview.Source.FileName,
                    preview.Document.Title,
                    _strings.Get(preview.Source.Format == ImportFileFormat.Markdown ? "MarkdownFormatText" : "PlainTextFormatText"),
                    hasConflict,
                    true,
                    null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Could not preview import file {ImportPath}", path);
                previews.Add(new ImportPreviewItem(
                    path,
                    Path.GetFileName(path),
                    Path.GetFileNameWithoutExtension(path),
                    _strings.Get(Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ? "MarkdownFormatText" : "PlainTextFormatText"),
                    false,
                    false,
                    _strings.Get("ImportFileReadFailedText")));
            }
        }

        return new ImportPreviewResult(previews);
    }

    public Task<ImportOperationResult> ImportAsync(
        ImportRequest request,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => ImportCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ImportOperationResult> ImportCoreAsync(
        ImportRequest request,
        CancellationToken cancellationToken)
    {
        var importRepository = new PediaImportRepository(
            _articles,
            _connections,
            request.LanguageCode,
            request.Status,
            request.DestinationTopicId);
        var importer = new FileImportService(
            importRepository,
            _importPreview,
            logger: new ImportLoggerAdapter(_logger));
        var duplicateMode = request.DuplicateHandling switch
        {
            ImportDuplicateHandling.CreateCopy => DuplicateMode.CreateCopy,
            ImportDuplicateHandling.Replace => DuplicateMode.Replace,
            _ => DuplicateMode.Skip
        };
        var result = await importer.ImportAsync(request.FilePaths, duplicateMode, cancellationToken);

        return new ImportOperationResult(
            result.Files.Count(file => file.Outcome is ImportFileOutcome.Imported or ImportFileOutcome.Replaced),
            result.Files.Count(file => file.Outcome == ImportFileOutcome.Skipped),
            result.Files.Count(file => file.Outcome == ImportFileOutcome.Failed),
            result.Files
                .Where(file => file.Error is not null)
                .Select(file => $"{Path.GetFileName(file.SourcePath)}: {_strings.OperationFailed}")
                .ToArray());
    }

    [SuppressMessage(
        "Maintainability",
        "S6672",
        Justification = "This adapter intentionally forwards import events through the enclosing service logger.")]
    private sealed class ImportLoggerAdapter(ILogger<CorePediaDataService> logger) : ILogger<FileImportService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logger.Log(logLevel, eventId, state, exception, formatter);
    }

    public Task ExportAsync(
        IReadOnlyList<long> articleIds,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => ExportCoreAsync(articleIds, format, destinationPath, cancellationToken), cancellationToken);

    private async Task ExportCoreAsync(
        IReadOnlyList<long> articleIds,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return;
        }

        var articles = new List<Pedia.Core.Models.ArticleDetails>(articleIds.Count);
        foreach (var articleId in articleIds)
        {
            var article = await _articles.GetAsync(articleId, cancellationToken)
                ?? throw new KeyNotFoundException(_strings.Format("ArticleNotFoundFormat", articleId));
            articles.Add(article);
        }

        var coreFormat = format switch
        {
            ExportFormat.Markdown => DocumentExportFormat.Markdown,
            ExportFormat.PediaJson => DocumentExportFormat.PediaJson,
            _ => DocumentExportFormat.PlainText
        };

        if (articleIds.Count == 1 && Path.HasExtension(destinationPath))
        {
            var content = coreFormat switch
            {
                DocumentExportFormat.Markdown => DocumentExportService.SerializeMarkdown(articles[0]),
                DocumentExportFormat.PediaJson => _exports.SerializePediaJson(articles[0]),
                _ => DocumentExportService.SerializePlainText(articles[0])
            };
            await File.WriteAllTextAsync(destinationPath, content, new UTF8Encoding(false), cancellationToken);
            return;
        }

        foreach (var article in articles)
        {
            await _exports.ExportAsync(article, destinationPath, coreFormat, cancellationToken);
        }
    }

    public Task CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(async () => _ = await _backups.CreateAsync(destinationPath, cancellationToken), cancellationToken);

    public Task<Models.BackupValidationResult> ValidateBackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => ValidateBackupCoreAsync(sourcePath, cancellationToken), cancellationToken);

    private async Task<Models.BackupValidationResult> ValidateBackupCoreAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var result = await _backups.ValidateAsync(sourcePath, cancellationToken);
        return new Models.BackupValidationResult(result.IsValid, result.Error);
    }

    public Task RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(() => RestoreBackupCoreAsync(sourcePath, cancellationToken), cancellationToken);

    private async Task RestoreBackupCoreAsync(string sourcePath, CancellationToken cancellationToken)
    {
        using (await _connections.WriteGate.EnterAsync(cancellationToken))
        {
            _ = await _backups.RestoreAsync(sourcePath, cancellationToken);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _initialization = await _initializer.InitializeAsync(seedSamples: false, cancellationToken);
    }

    public Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(async () => _ = await _queries.RebuildIndexAsync(cancellationToken), cancellationToken);

    public Task DeleteSampleContentAsync(CancellationToken cancellationToken = default) =>
        RunOffUiThreadAsync(async () => _ = await _articles.DeleteSampleContentAsync(cancellationToken), cancellationToken);

    private static ArticleDocumentData MapArticle(Pedia.Core.Models.ArticleDetails article) => new(
        article.Id,
        article.Title,
        article.Subtitle,
        article.Summary,
        article.LanguageCode,
        article.ArticleType,
        article.Status,
        article.Notes,
        article.IsFavorite,
        article.WordCount,
        article.IsSample,
        article.CreatedAtUtc,
        article.UpdatedAtUtc,
        article.DeletedAtUtc,
        article.Sections.Select(section => new ArticleSectionData(
            section.Id, section.Heading, section.HeadingLevel, section.Body, section.SortOrder)).ToArray(),
        article.Sources.Select(source => new ArticleSourceData(
            source.Id,
            source.SourceType,
            source.Title,
            source.Url,
            source.ExternalPageId,
            source.ExternalRevisionId,
            source.LicenseName,
            source.AttributionText,
            source.RetrievedAtUtc,
            source.LastCheckedAtUtc,
            source.Notes,
            source.SortOrder)).ToArray(),
        article.TopicAssignments.Select(topic => new ArticleTopicData(
            topic.TopicId, topic.TopicPath, topic.IsPrimary)).ToArray());

    private static CoreArticleDraft MapDraft(EditableArticle article) => new()
    {
        Title = article.Title,
        Subtitle = NullIfWhiteSpace(article.Subtitle),
        Summary = NullIfWhiteSpace(article.Summary),
        LanguageCode = string.IsNullOrWhiteSpace(article.LanguageCode) ? "en" : article.LanguageCode.Trim(),
        ArticleType = article.ArticleType,
        Status = article.Status,
        Notes = NullIfWhiteSpace(article.Notes),
        IsFavorite = article.IsFavorite,
        IsSample = false,
        Sections = article.Sections.Select(section => new CoreArticleSectionDraft(
            NullIfWhiteSpace(section.Heading),
            Math.Clamp(section.HeadingLevel, 1, 3),
            section.Body)).ToArray(),
        Sources = article.Sources.Select(source => new CoreArticleSourceDraft
        {
            SourceType = source.SourceType,
            Title = NullIfWhiteSpace(source.Title),
            Url = NullIfWhiteSpace(source.Url),
            ExternalPageId = NullIfWhiteSpace(source.ExternalPageId),
            ExternalRevisionId = NullIfWhiteSpace(source.ExternalRevisionId),
            LicenseName = NullIfWhiteSpace(source.LicenseName),
            AttributionText = NullIfWhiteSpace(source.AttributionText),
            RetrievedAtUtc = source.RetrievedAtUtc,
            LastCheckedAtUtc = source.LastCheckedAtUtc,
            Notes = NullIfWhiteSpace(source.Notes)
        }).ToArray(),
        TopicAssignments = article.Topics.Select(topic => new CoreArticleTopicDraft(topic.TopicId, topic.IsPrimary)).ToArray()
    };

    private static string? CleanSnippet(string? snippet) => snippet?
        .Replace("[", string.Empty, StringComparison.Ordinal)
        .Replace("]", string.Empty, StringComparison.Ordinal);

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Task RunOffUiThreadAsync(Func<Task> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);

    private static Task<TResult> RunOffUiThreadAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);
}
