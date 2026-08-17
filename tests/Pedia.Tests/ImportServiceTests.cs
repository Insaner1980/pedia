using Pedia.Core.Importing;
using Pedia.Core.Utilities;

namespace Pedia.Tests;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task Preview_reads_local_metadata_and_parses_without_remote_requests()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "birds.md");
        await File.WriteAllTextAsync(path, "# Birds\n\nLocal text ![](https://example.invalid/image.png)", TestContext.Current.CancellationToken);
        var service = new ImportPreviewService();

        var preview = await service.PreviewAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("Birds", preview.Document.Title);
        Assert.Equal(Path.GetFullPath(path), preview.Source.FullPath);
        Assert.Equal("birds.md", preview.Source.FileName);
        Assert.Equal(ImportFileFormat.Markdown, preview.Source.Format);
        Assert.True(preview.Source.ByteLength > 0);
        Assert.Matches("^[0-9a-f]{64}$", preview.Source.Sha256);
        Assert.DoesNotContain("https://", preview.Document.LeadBlocks[0].Text);
    }

    [Fact]
    public async Task Skip_mode_preserves_input_order_and_records_one_completed_run()
    {
        using var directory = new TemporaryDirectory();
        var first = await WriteAsync(directory.Path, "first.txt", "First body");
        var duplicate = await WriteAsync(directory.Path, "same.md", "# Existing\n\nReplacement");
        var repository = new FakeImportRepository();
        repository.Articles["Existing"] = 42;
        var service = CreateService(repository);

        var result = await service.ImportAsync([first, duplicate], DuplicateMode.Skip, TestContext.Current.CancellationToken);

        Assert.Collection(
            result.Files,
            file => Assert.Equal(("first.txt", ImportFileOutcome.Imported), (Path.GetFileName(file.SourcePath), file.Outcome)),
            file => Assert.Equal(("same.md", ImportFileOutcome.Skipped), (Path.GetFileName(file.SourcePath), file.Outcome)));
        Assert.Single(repository.Created);
        var run = Assert.Single(repository.CompletedRuns);
        Assert.Equal((1, 1, 0, 0), (run.ImportedCount, run.SkippedCount, run.ReplacedCount, run.FailedCount));
        Assert.Equal(ImportRunOutcome.Completed, run.Outcome);
    }

    [Fact]
    public async Task CreateCopy_mode_uses_an_explicit_available_title()
    {
        using var directory = new TemporaryDirectory();
        var path = await WriteAsync(directory.Path, "entry.md", "# Birds\n\nBody");
        var repository = new FakeImportRepository();
        repository.Articles["Birds"] = 10;
        repository.Articles["Birds (2)"] = 11;
        var service = CreateService(repository);

        var result = await service.ImportAsync([path], DuplicateMode.CreateCopy, TestContext.Current.CancellationToken);

        Assert.Equal("Birds (3)", Assert.Single(result.Files).EffectiveTitle);
        Assert.Equal("Birds (3)", Assert.Single(repository.Created).Document.Title);
    }

    [Fact]
    public async Task Replace_mode_updates_the_existing_article_and_preserves_source_metadata()
    {
        using var directory = new TemporaryDirectory();
        var path = await WriteAsync(directory.Path, "entry.md", "# Birds\n\nNew body");
        var repository = new FakeImportRepository();
        repository.Articles["Birds"] = 10;
        var service = CreateService(repository);

        var result = await service.ImportAsync([path], DuplicateMode.Replace, TestContext.Current.CancellationToken);

        Assert.Equal(ImportFileOutcome.Replaced, Assert.Single(result.Files).Outcome);
        var replacement = Assert.Single(repository.Replaced);
        Assert.Equal(10, replacement.ArticleId);
        Assert.Equal(Path.GetFullPath(path), replacement.Draft.Source.FullPath);
    }

    [Fact]
    public async Task Per_file_errors_are_reported_and_do_not_hide_successful_files()
    {
        using var directory = new TemporaryDirectory();
        var unsupported = await WriteAsync(directory.Path, "image.png", "not an image");
        var valid = await WriteAsync(directory.Path, "valid.txt", "Valid body");
        var repository = new FakeImportRepository();
        var service = CreateService(repository);

        var result = await service.ImportAsync([unsupported, valid], DuplicateMode.Skip, TestContext.Current.CancellationToken);

        Assert.Equal(ImportFileOutcome.Failed, result.Files[0].Outcome);
        Assert.Contains("Unsupported", result.Files[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ImportFileOutcome.Imported, result.Files[1].Outcome);
        Assert.Equal(1, Assert.Single(repository.CompletedRuns).FailedCount);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_and_records_the_cancelled_run()
    {
        using var directory = new TemporaryDirectory();
        var path = await WriteAsync(directory.Path, "valid.txt", "Valid body");
        var repository = new FakeImportRepository { BlockCreateUntilCancelled = true };
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ImportAsync([path], DuplicateMode.Skip, cancellation.Token));

        Assert.Equal(ImportRunOutcome.Cancelled, Assert.Single(repository.CompletedRuns).Outcome);
    }

    private static FileImportService CreateService(FakeImportRepository repository) =>
        new(repository, new ImportPreviewService(), new FixedClock(new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero)));

    private static async Task<string> WriteAsync(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private sealed class FakeImportRepository : IImportRepository
    {
        private long _nextArticleId = 100;
        private long _nextRunId = 1;

        public Dictionary<string, long> Articles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ImportedArticleDraft> Created { get; } = [];

        public List<(long ArticleId, ImportedArticleDraft Draft)> Replaced { get; } = [];

        public List<ImportRunCompletion> CompletedRuns { get; } = [];

        public bool BlockCreateUntilCancelled { get; init; }

        public Task<ExistingImportedArticle?> FindByTitleAsync(string title, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Articles.TryGetValue(title, out var id) ? new ExistingImportedArticle(id, title) : null);
        }

        public async Task<long> CreateAsync(ImportedArticleDraft article, CancellationToken cancellationToken)
        {
            if (BlockCreateUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Created.Add(article);
            var id = _nextArticleId++;
            Articles[article.Document.Title] = id;
            return id;
        }

        public Task ReplaceAsync(long articleId, ImportedArticleDraft article, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Replaced.Add((articleId, article));
            Articles[article.Document.Title] = articleId;
            return Task.CompletedTask;
        }

        public Task<long> BeginRunAsync(ImportRunStart run, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_nextRunId++);
        }

        public Task CompleteRunAsync(long runId, ImportRunCompletion completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedRuns.Add(completion);
            return Task.CompletedTask;
        }
    }
}
