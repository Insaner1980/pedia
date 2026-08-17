namespace Pedia.Core.Importing;

public enum ContentBlockKind
{
    Paragraph,
    UnorderedList,
    OrderedList
}

public sealed record ContentBlock(ContentBlockKind Kind, string? Text, IReadOnlyList<string> Items)
{
    public static ContentBlock Paragraph(string text) =>
        new(ContentBlockKind.Paragraph, text, Array.Empty<string>());

    public static ContentBlock UnorderedList(IReadOnlyList<string> items) =>
        new(ContentBlockKind.UnorderedList, null, items);

    public static ContentBlock OrderedList(IReadOnlyList<string> items) =>
        new(ContentBlockKind.OrderedList, null, items);
}

public sealed record DocumentSection(string Heading, int Level, IReadOnlyList<ContentBlock> Blocks);

public sealed record ParsedDocument(
    string Title,
    IReadOnlyList<ContentBlock> LeadBlocks,
    IReadOnlyList<DocumentSection> Sections);
