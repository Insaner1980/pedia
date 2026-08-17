using System.Net;
using System.Text.RegularExpressions;

namespace Pedia.Core.Importing;

public static partial class MarkdownDocumentParser
{
    public static ParsedDocument Parse(string markdown, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var sanitized = SanitizeMarkdown(markdown);
        var lines = NormalizeLines(sanitized);
        var title = NormalizeTitle(fallbackTitle);
        var lead = new List<ContentBlock>();
        var sections = new List<MutableSection>();
        List<ContentBlock> currentBlocks = lead;
        var paragraph = new List<string>();
        var index = 0;
        var firstH1Seen = false;

        while (index < lines.Length)
        {
            var line = lines[index];
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                ApplyHeading(heading, paragraph, sections, ref currentBlocks, ref title, ref firstH1Seen);
                index++;
                continue;
            }

            if (TryReadList(lines, ref index, out var list))
            {
                FlushParagraph(paragraph, currentBlocks);
                currentBlocks.Add(list);
                continue;
            }

            var cleaned = CleanInlineMarkdown(line);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                FlushParagraph(paragraph, currentBlocks);
            }
            else
            {
                paragraph.Add(cleaned);
            }

            index++;
        }

        FlushParagraph(paragraph, currentBlocks);
        return new ParsedDocument(
            title,
            lead,
            sections.Select(section => new DocumentSection(section.Heading, section.Level, section.Blocks)).ToArray());
    }

    internal static string NormalizeTitle(string? title)
    {
        var normalized = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? "Untitled" : normalized;
    }

    internal static string[] NormalizeLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    internal static void FlushParagraph(List<string> paragraph, List<ContentBlock> destination)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        destination.Add(ContentBlock.Paragraph(string.Join(' ', paragraph)));
        paragraph.Clear();
    }

    internal static bool TryReadList(string[] lines, ref int index, out ContentBlock block)
    {
        var first = ListItemPattern().Match(lines[index]);
        if (!first.Success)
        {
            block = null!;
            return false;
        }

        var ordered = first.Groups[1].Value.Length > 0;
        var items = new List<string>();
        while (index < lines.Length)
        {
            var match = ListItemPattern().Match(lines[index]);
            if (!match.Success || (match.Groups[1].Value.Length > 0) != ordered)
            {
                break;
            }

            var item = CleanInlineMarkdown(match.Groups[2].Value);
            if (!string.IsNullOrWhiteSpace(item))
            {
                items.Add(item);
            }

            index++;
        }

        block = ordered ? ContentBlock.OrderedList(items) : ContentBlock.UnorderedList(items);
        return true;
    }

    internal static string CleanInlineMarkdown(string value)
    {
        var withoutImages = MarkdownImagePattern().Replace(value, string.Empty);
        var withoutLinks = MarkdownLinkPattern().Replace(withoutImages, "${label}");
        var withoutAutoLinks = AutoLinkPattern().Replace(withoutLinks, string.Empty);
        var withoutTags = HtmlTagPattern().Replace(withoutAutoLinks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static void ApplyHeading(
        Match heading,
        List<string> paragraph,
        List<MutableSection> sections,
        ref List<ContentBlock> currentBlocks,
        ref string title,
        ref bool firstH1Seen)
    {
        FlushParagraph(paragraph, currentBlocks);
        var level = heading.Groups[1].Value.Length;
        var headingText = CleanInlineMarkdown(heading.Groups[2].Value);
        if (level == 1 && !firstH1Seen)
        {
            firstH1Seen = true;
            if (!string.IsNullOrWhiteSpace(headingText))
            {
                title = headingText;
            }

            return;
        }

        if (level is 2 or 3 && !string.IsNullOrWhiteSpace(headingText))
        {
            var section = new MutableSection(headingText, level);
            sections.Add(section);
            currentBlocks = section.Blocks;
        }
    }

    private static string SanitizeMarkdown(string value)
    {
        var withoutDangerousElements = DangerousHtmlElementPattern().Replace(value, string.Empty);
        return HtmlCommentPattern().Replace(withoutDangerousElements, string.Empty);
    }

    private sealed record MutableSection(string Heading, int Level)
    {
        public List<ContentBlock> Blocks { get; } = [];
    }

    [GeneratedRegex(@"^\s*(#{1,3})\s+(.+?)\s*#*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*(?:(\d+)[.)]|[-+*])\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"!\[[^\]]*\]\([^\r\n)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImagePattern();

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\([^\r\n)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"<https?://[^>]+>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutoLinkPattern();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"<\s*(?:script|style|iframe|object|embed|svg|math)\b[^>]*>.*?<\s*/\s*(?:script|style|iframe|object|embed|svg|math)\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousHtmlElementPattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlCommentPattern();
}

public static class TextDocumentParser
{
    public static ParsedDocument Parse(string text, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = MarkdownDocumentParser.NormalizeLines(text);
        var blocks = new List<ContentBlock>();
        var paragraph = new List<string>();
        var firstContentIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var title = MarkdownDocumentParser.NormalizeTitle(fallbackTitle);
        var index = Math.Max(firstContentIndex, 0);
        if (firstContentIndex >= 0 && LooksLikeTitle(lines, firstContentIndex))
        {
            title = MarkdownDocumentParser.NormalizeTitle(lines[firstContentIndex]);
            index = firstContentIndex + 1;
        }

        while (index < lines.Length)
        {
            if (MarkdownDocumentParser.TryReadList(lines, ref index, out var list))
            {
                MarkdownDocumentParser.FlushParagraph(paragraph, blocks);
                blocks.Add(list);
                continue;
            }

            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                MarkdownDocumentParser.FlushParagraph(paragraph, blocks);
            }
            else
            {
                paragraph.Add(line);
            }

            index++;
        }

        MarkdownDocumentParser.FlushParagraph(paragraph, blocks);
        return new ParsedDocument(title, blocks, Array.Empty<DocumentSection>());
    }

    private static bool LooksLikeTitle(string[] lines, int index)
    {
        var candidate = lines[index].Trim();
        if (candidate.Length is 0 or > 120
            || candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 16
            || candidate[^1] is '.' or '!' or '?' or ';'
            || candidate.StartsWith("- ", StringComparison.Ordinal)
            || candidate.StartsWith("* ", StringComparison.Ordinal)
            || candidate.StartsWith("+ ", StringComparison.Ordinal))
        {
            return false;
        }

        return index == lines.Length - 1 || string.IsNullOrWhiteSpace(lines[index + 1]);
    }
}
