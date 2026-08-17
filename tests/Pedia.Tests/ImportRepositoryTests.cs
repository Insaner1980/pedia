using Pedia.Core.Importing;
using Pedia.Core.Models;
using Pedia.Core.Repositories;

namespace Pedia.Tests;

public sealed class ImportRepositoryTests
{
    [Fact]
    public async Task Adapter_persists_structured_document_and_local_source_metadata()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var repository = new PediaImportRepository(articles, database.Connections, languageCode: "fi");
        var source = Source("C:\\notes\\birds.md", ImportFileFormat.Markdown);
        var draft = new ImportedArticleDraft(
            new ParsedDocument(
                "Birds",
                [ContentBlock.Paragraph("A field guide.")],
                [new DocumentSection("Forest", 2, [ContentBlock.UnorderedList(["Tit", "Owl"])])]),
            source,
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

        var articleId = await repository.CreateAsync(draft, CancellationToken.None);
        var saved = (await articles.GetAsync(articleId, TestContext.Current.CancellationToken))!;

        Assert.Equal("fi", saved.LanguageCode);
        Assert.Collection(
            saved.Sections,
            section =>
            {
                Assert.Null(section.Heading);
                Assert.Equal("A field guide.", section.Body);
            },
            section =>
            {
                Assert.Equal("Forest", section.Heading);
                Assert.Equal("- Tit\n- Owl", section.Body);
            });
        var savedSource = Assert.Single(saved.Sources);
        Assert.Equal("Local Markdown file", savedSource.SourceType);
        Assert.Equal("birds.md", savedSource.Title);
        Assert.Equal(source.FullPath, savedSource.ExternalPageId);
        Assert.Equal(source.Sha256, savedSource.ExternalRevisionId);
        Assert.Equal(source.LastModifiedUtc, savedSource.LastCheckedAtUtc);
        Assert.Contains("128 bytes", savedSource.Notes);
    }

    [Fact]
    public async Task Adapter_persists_selected_language_status_and_topic_in_the_create_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var topicId = await new TopicRepository(database.Connections).CreateAsync("Imported", cancellationToken: TestContext.Current.CancellationToken);
        var repository = new PediaImportRepository(
            articles,
            database.Connections,
            languageCode: "fi",
            status: ArticleStatuses.Ready,
            destinationTopicId: topicId);

        var articleId = await repository.CreateAsync(Draft("Imported article", "Local body"), CancellationToken.None);
        var saved = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));

        Assert.Equal("fi", saved.LanguageCode);
        Assert.Equal(ArticleStatuses.Ready, saved.Status);
        var assignment = Assert.Single(saved.TopicAssignments);
        Assert.Equal(topicId, assignment.TopicId);
        Assert.True(assignment.IsPrimary);
    }

    [Fact]
    public async Task Adapter_finds_and_replaces_active_title_case_insensitively()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var repository = new PediaImportRepository(articles, database.Connections);
        var originalId = await repository.CreateAsync(
            Draft("Birds", "Old body"),
            CancellationToken.None);

        var found = await repository.FindByTitleAsync(" birds ", CancellationToken.None);
        await repository.ReplaceAsync(originalId, Draft("Birds", "New body"), CancellationToken.None);

        Assert.Equal(originalId, found?.ArticleId);
        Assert.Equal("New body", (await articles.GetAsync(originalId, TestContext.Current.CancellationToken))!.Sections[0].Body);
    }

    [Fact]
    public async Task Adapter_records_import_run_completion_in_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new PediaImportRepository(
            new ArticleRepository(database.Connections),
            database.Connections);
        var startedAt = new DateTimeOffset(2026, 8, 12, 11, 0, 0, TimeSpan.Zero);
        var runId = await repository.BeginRunAsync(
            new ImportRunStart(startedAt, DuplicateMode.Replace, 4),
            CancellationToken.None);
        var completion = new ImportRunCompletion(
            startedAt.AddSeconds(3),
            ImportRunOutcome.Completed,
            ImportedCount: 1,
            SkippedCount: 1,
            ReplacedCount: 1,
            FailedCount: 1,
            [new ImportFileResult("bad.txt", ImportFileOutcome.Failed, null, null, "Unreadable file")]);

        await repository.CompleteRunAsync(runId, completion, CancellationToken.None);

        await using var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ImportKind, SourceDescription, Status, ImportedCount, SkippedCount, ErrorCount, ErrorSummary
            FROM ImportRuns WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", runId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("LocalFiles/Replace", reader.GetString(0));
        Assert.Equal("4 local files", reader.GetString(1));
        Assert.Equal("Completed", reader.GetString(2));
        Assert.Equal(2, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
        Assert.Contains("Unreadable file", reader.GetString(6));
    }

    private static ImportedArticleDraft Draft(string title, string body) => new(
        new ParsedDocument(title, [ContentBlock.Paragraph(body)], []),
        Source("C:\\notes\\entry.txt", ImportFileFormat.PlainText),
        DateTimeOffset.UnixEpoch);

    private static LocalFileSourceMetadata Source(string path, ImportFileFormat format) => new(
        path,
        Path.GetFileName(path),
        format,
        128,
        new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero),
        new string('a', 64));
}
