using Pedia.Core.Importing;

namespace Pedia.Tests;

public sealed class ImportParserTests
{
    [Fact]
    public void Markdown_parser_builds_structured_sections_and_preserves_lists()
    {
        const string markdown = """
            # Finnish Birds

            A compact field guide.

            ## Forest

            Common species:

            - Great tit
            - Eurasian blue tit

            ### Owls

            1. Tawny owl
            2. Ural owl
            """;

        var document = MarkdownDocumentParser.Parse(markdown, "fallback");

        Assert.Equal("Finnish Birds", document.Title);
        Assert.Equal("A compact field guide.", Assert.Single(document.LeadBlocks).Text);
        Assert.Collection(
            document.Sections,
            section =>
            {
                Assert.Equal(("Forest", 2), (section.Heading, section.Level));
                Assert.Equal(ContentBlockKind.Paragraph, section.Blocks[0].Kind);
                Assert.Equal(["Great tit", "Eurasian blue tit"], section.Blocks[1].Items);
            },
            section =>
            {
                Assert.Equal(("Owls", 3), (section.Heading, section.Level));
                Assert.Equal(ContentBlockKind.OrderedList, Assert.Single(section.Blocks).Kind);
            });
    }

    [Fact]
    public void Markdown_parser_ignores_remote_media_and_active_html()
    {
        const string markdown = """
            # Safe

            Before ![tracking pixel](https://example.test/pixel.png) after.
            [Readable label](https://example.test/page)
            <script>throw new Error('must not survive')</script>
            <iframe src="https://example.test"></iframe>
            <b>Kept text</b>
            """;

        var document = MarkdownDocumentParser.Parse(markdown, "fallback");
        var text = string.Join('\n', document.LeadBlocks.Select(block => block.Text));

        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Before  after.", text);
        Assert.Contains("Readable label", text);
        Assert.Contains("Kept text", text);
    }

    [Fact]
    public void Markdown_parser_uses_only_the_first_H1_even_when_it_matches_the_fallback()
    {
        const string markdown = """
            # Birds

            Introduction.

            # Wrong later heading
            """;

        var document = MarkdownDocumentParser.Parse(markdown, "Birds");

        Assert.Equal("Birds", document.Title);
    }

    [Fact]
    public void Text_parser_uses_fallback_title_and_preserves_paragraphs_and_lists()
    {
        const string text = "First line\ncontinues here.\n\n- One\n- Two\n\nLast paragraph.";

        var document = TextDocumentParser.Parse(text, "notes");

        Assert.Equal("notes", document.Title);
        Assert.Collection(
            document.LeadBlocks,
            block => Assert.Equal("First line continues here.", block.Text),
            block => Assert.Equal(["One", "Two"], block.Items),
            block => Assert.Equal("Last paragraph.", block.Text));
    }

    [Fact]
    public void Text_parser_uses_an_isolated_short_first_line_as_the_title()
    {
        const string text = "Bird Notes\n\nFirst paragraph.\n\nSecond paragraph.";

        var document = TextDocumentParser.Parse(text, "notes");

        Assert.Equal("Bird Notes", document.Title);
        Assert.Collection(
            document.LeadBlocks,
            block => Assert.Equal("First paragraph.", block.Text),
            block => Assert.Equal("Second paragraph.", block.Text));
    }
}
