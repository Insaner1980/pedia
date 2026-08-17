using Pedia.Core.Exporting;
using Pedia.Core.Importing;
using Pedia.Core.Utilities;
using Pedia.Core.Models;

namespace Pedia.Tests;

public sealed class ExportServiceTests
{
    [Fact]
    public void Pedia_json_is_versioned_and_round_trips_the_document()
    {
        var document = SampleDocument();
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 12, 9, 10, 11, TimeSpan.Zero));
        var service = new DocumentExportService(clock);

        var json = service.SerializePediaJson(document);
        var restored = DocumentExportService.DeserializePediaJson(json);

        Assert.Contains("\"format\": \"pedia-document\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Equal(document.Title, restored.Title);
        Assert.Equal(document.LeadBlocks[0].Text, restored.LeadBlocks[0].Text);
        Assert.Equal(document.Sections[0].Blocks[1].Items, restored.Sections[0].Blocks[1].Items);
    }

    [Fact]
    public async Task Export_writes_plain_text_markdown_and_non_overwriting_collision_name()
    {
        using var directory = new TemporaryDirectory();
        var service = new DocumentExportService(new FixedClock(DateTimeOffset.UnixEpoch));
        var document = SampleDocument() with { Title = "Ääkköset / birds" };

        var text = await service.ExportAsync(document, directory.Path, DocumentExportFormat.PlainText, TestContext.Current.CancellationToken);
        var markdown = await service.ExportAsync(document, directory.Path, DocumentExportFormat.Markdown, TestContext.Current.CancellationToken);
        var secondMarkdown = await service.ExportAsync(document, directory.Path, DocumentExportFormat.Markdown, TestContext.Current.CancellationToken);

        Assert.Equal("Ääkköset birds.txt", Path.GetFileName(text.Path));
        Assert.Equal("Ääkköset birds.md", Path.GetFileName(markdown.Path));
        Assert.Equal("Ääkköset birds (2).md", Path.GetFileName(secondMarkdown.Path));
        Assert.Contains("Forest", await File.ReadAllTextAsync(text.Path, TestContext.Current.CancellationToken));
        Assert.Contains("## Forest", await File.ReadAllTextAsync(markdown.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Unsupported_Pedia_json_version_is_rejected()
    {
        const string json = """{"format":"pedia-document","version":99,"exportedAtUtc":"2026-08-12T00:00:00Z","document":{"title":"x","leadBlocks":[],"sections":[]}}""";

        Assert.Throws<InvalidDataException>(() => DocumentExportService.DeserializePediaJson(json));
    }

    [Fact]
    public void Full_article_Pedia_json_round_trip_preserves_metadata_sources_and_topic_paths()
    {
        var article = SampleArticle();
        var service = new DocumentExportService(new FixedClock(DateTimeOffset.UnixEpoch));

        var json = service.SerializePediaJson(article);
        var restored = DocumentExportService.DeserializePediaArticleJson(json);

        Assert.Contains("\"kind\": \"article\"", json);
        Assert.Equal(article.Title, restored.Title);
        Assert.Equal(article.LanguageCode, restored.LanguageCode);
        Assert.Equal(article.Notes, restored.Notes);
        Assert.Equal("CC BY", Assert.Single(restored.Sources).LicenseName);
        Assert.Equal("Nature / Birds", Assert.Single(restored.TopicAssignments).TopicPath);
        Assert.Equal("Forest", Assert.Single(restored.Sections).Heading);
    }

    [Fact]
    public void Article_text_and_Markdown_exports_include_metadata_sections_and_sources()
    {
        var article = SampleArticle();

        var text = DocumentExportService.SerializePlainText(article);
        var markdown = DocumentExportService.SerializeMarkdown(article);

        Assert.Contains("A summary", text);
        Assert.Contains("Language: fi", text);
        Assert.Contains("Nature / Birds", text);
        Assert.Contains("Field guide", text);
        Assert.Contains("CC BY", text);
        Assert.Contains("Forest", text);
        Assert.Contains("## Forest", markdown);
        Assert.Contains("## Sources", markdown);
        Assert.Contains("Field guide", markdown);
        Assert.Contains("CC BY", markdown);
    }

    private static ParsedDocument SampleDocument() => new(
        "Birds",
        [ContentBlock.Paragraph("Guide")],
        [new DocumentSection("Forest", 2, [ContentBlock.Paragraph("Species"), ContentBlock.UnorderedList(["Tit", "Owl"])])]);

    private static ArticleDetails SampleArticle() => new(
        7,
        "Birds",
        "Field notes",
        "A summary",
        "fi",
        ArticleTypes.Concept,
        ArticleStatuses.Ready,
        "Private note",
        true,
        12,
        false,
        new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
        null,
        [new ArticleSection(10, "Forest", 2, "Tit and owl", 0)],
        [new ArticleSource(11, SourceTypes.Book, "Field guide", null, "shelf-2", "rev-3", "CC BY", "Author", DateTimeOffset.UnixEpoch, null, "Page 4", 0)],
        [new ArticleTopicAssignment(12, "Birds", "Nature / Birds", true, DateTimeOffset.UnixEpoch)]);
}
