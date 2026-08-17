using System.Text;
using System.Text.Json;
using Pedia.Core.Importing;
using Pedia.Core.Models;
using Pedia.Core.Utilities;

namespace Pedia.Core.Exporting;

public enum DocumentExportFormat
{
    PlainText,
    Markdown,
    PediaJson
}

public sealed record ExportResult(string Path, bool CollisionRenamed);

public sealed class DocumentExportService
{
    private const string FormatName = "pedia-document";
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly IClock _clock;

    public DocumentExportService(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public static string SerializePlainText(ParsedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        builder.AppendLine(document.Title);
        AppendBlocks(builder, document.LeadBlocks, markdown: false);
        foreach (var section in document.Sections)
        {
            AppendBlankLine(builder);
            builder.AppendLine(section.Heading);
            AppendBlocks(builder, section.Blocks, markdown: false);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string SerializePlainText(ArticleDetails article)
    {
        ArgumentNullException.ThrowIfNull(article);
        var builder = new StringBuilder();
        builder.AppendLine(article.Title);
        if (!string.IsNullOrWhiteSpace(article.Subtitle))
        {
            builder.AppendLine(article.Subtitle);
        }

        AppendBlankLine(builder);
        if (!string.IsNullOrWhiteSpace(article.Summary))
        {
            builder.Append("Summary: ").AppendLine(article.Summary);
        }
        AppendArticleMetadata(builder, article, markdown: false);
        AppendArticleSections(builder, article, markdown: false);
        AppendArticleSources(builder, article, markdown: false);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string SerializeMarkdown(ParsedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title);
        AppendBlocks(builder, document.LeadBlocks, markdown: true);
        foreach (var section in document.Sections)
        {
            AppendBlankLine(builder);
            builder.Append(new string('#', Math.Clamp(section.Level, 2, 3))).Append(' ').AppendLine(section.Heading);
            AppendBlocks(builder, section.Blocks, markdown: true);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string SerializeMarkdown(ArticleDetails article)
    {
        ArgumentNullException.ThrowIfNull(article);
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(article.Title);
        if (!string.IsNullOrWhiteSpace(article.Subtitle))
        {
            AppendBlankLine(builder);
            builder.Append('*').Append(article.Subtitle).AppendLine("*");
        }

        if (!string.IsNullOrWhiteSpace(article.Summary))
        {
            AppendBlankLine(builder);
            builder.AppendLine(article.Summary);
        }
        AppendBlankLine(builder);
        AppendArticleMetadata(builder, article, markdown: true);
        AppendArticleSections(builder, article, markdown: true);
        AppendArticleSources(builder, article, markdown: true);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    public string SerializePediaJson(ParsedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var envelope = new PediaDocumentEnvelope(FormatName, CurrentVersion, "document", _clock.UtcNow, document);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public string SerializePediaJson(ArticleDetails article)
    {
        ArgumentNullException.ThrowIfNull(article);
        var envelope = new PediaArticleEnvelope(FormatName, CurrentVersion, "article", _clock.UtcNow, article);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static ParsedDocument DeserializePediaJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var envelope = JsonSerializer.Deserialize<PediaDocumentEnvelope>(json, JsonOptions)
                ?? throw new InvalidDataException("The Pedia JSON document is empty.");
            if (!string.Equals(envelope.Format, FormatName, StringComparison.Ordinal) || envelope.Version != CurrentVersion)
            {
                throw new InvalidDataException($"Unsupported Pedia JSON format or version {envelope.Version}.");
            }

            if (envelope.Document is null || string.IsNullOrWhiteSpace(envelope.Document.Title))
            {
                throw new InvalidDataException("The Pedia JSON document payload is invalid.");
            }

            return envelope.Document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Pedia JSON document is malformed.", exception);
        }
    }

    public static ArticleDetails DeserializePediaArticleJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var envelope = JsonSerializer.Deserialize<PediaArticleEnvelope>(json, JsonOptions)
                ?? throw new InvalidDataException("The Pedia article JSON document is empty.");
            if (!string.Equals(envelope.Format, FormatName, StringComparison.Ordinal) ||
                envelope.Version != CurrentVersion ||
                !string.Equals(envelope.Kind, "article", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported Pedia article JSON format or version {envelope.Version}.");
            }

            if (envelope.Article is null || string.IsNullOrWhiteSpace(envelope.Article.Title))
            {
                throw new InvalidDataException("The Pedia article JSON payload is invalid.");
            }

            return envelope.Article;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Pedia article JSON document is malformed.", exception);
        }
    }

    public async Task<ExportResult> ExportAsync(
        ParsedDocument document,
        string directoryPath,
        DocumentExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directoryPath);

        var extension = format switch
        {
            DocumentExportFormat.PlainText => ".txt",
            DocumentExportFormat.Markdown => ".md",
            DocumentExportFormat.PediaJson => ".pedia.json",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var content = format switch
        {
            DocumentExportFormat.PlainText => SerializePlainText(document),
            DocumentExportFormat.Markdown => SerializeMarkdown(document),
            DocumentExportFormat.PediaJson => SerializePediaJson(document),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var baseName = FileNameUtilities.SanitizeFileName(document.Title);
        var fileName = baseName + extension;

        var collisionNumber = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = FileNameUtilities.GetCollisionPath(directoryPath, fileName, collisionNumber);
            try
            {
                await using var stream = new FileStream(
                    candidate,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                return new ExportResult(candidate, collisionNumber > 1);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Try the next explicit collision suffix without overwriting existing content.
                collisionNumber++;
            }
        }
    }

    public async Task<ExportResult> ExportAsync(
        ArticleDetails article,
        string directoryPath,
        DocumentExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(article);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directoryPath);

        var extension = format switch
        {
            DocumentExportFormat.PlainText => ".txt",
            DocumentExportFormat.Markdown => ".md",
            DocumentExportFormat.PediaJson => ".pedia.json",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var content = format switch
        {
            DocumentExportFormat.PlainText => SerializePlainText(article),
            DocumentExportFormat.Markdown => SerializeMarkdown(article),
            DocumentExportFormat.PediaJson => SerializePediaJson(article),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var fileName = FileNameUtilities.SanitizeFileName(article.Title) + extension;

        var collisionNumber = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = FileNameUtilities.GetCollisionPath(directoryPath, fileName, collisionNumber);
            try
            {
                await using var stream = new FileStream(
                    candidate,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                return new ExportResult(candidate, collisionNumber > 1);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Try the next explicit collision suffix without overwriting existing content.
                collisionNumber++;
            }
        }
    }

    private static void AppendArticleMetadata(StringBuilder builder, ArticleDetails article, bool markdown)
    {
        AppendMetadataLine(builder, "Language", article.LanguageCode, markdown);
        AppendMetadataLine(builder, "Type", article.ArticleType, markdown);
        AppendMetadataLine(builder, "Status", article.Status, markdown);
        AppendMetadataLine(builder, "Favorite", article.IsFavorite ? "Yes" : "No", markdown);
        AppendMetadataLine(builder, "Word count", article.WordCount.ToString(System.Globalization.CultureInfo.InvariantCulture), markdown);
        AppendMetadataLine(builder, "Created", article.CreatedAtUtc.ToString("O"), markdown);
        AppendMetadataLine(builder, "Updated", article.UpdatedAtUtc.ToString("O"), markdown);
        if (article.DeletedAtUtc is { } deletedAt)
        {
            AppendMetadataLine(builder, "Deleted", deletedAt.ToString("O"), markdown);
        }
        if (article.TopicAssignments.Count > 0)
        {
            AppendMetadataLine(builder, "Topics", string.Join(", ", article.TopicAssignments.Select(topic => topic.TopicPath)), markdown);
        }
        if (!string.IsNullOrWhiteSpace(article.Notes))
        {
            AppendMetadataLine(builder, "Notes", article.Notes, markdown);
        }
    }

    private static void AppendArticleSections(StringBuilder builder, ArticleDetails article, bool markdown)
    {
        foreach (var section in article.Sections.OrderBy(section => section.SortOrder))
        {
            AppendBlankLine(builder);
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                if (markdown)
                {
                    builder.Append(new string('#', Math.Clamp(section.HeadingLevel, 2, 3))).Append(' ');
                }
                builder.AppendLine(section.Heading);
                AppendBlankLine(builder);
            }
            builder.AppendLine(section.Body.Trim());
        }
    }

    private static void AppendArticleSources(StringBuilder builder, ArticleDetails article, bool markdown)
    {
        if (article.Sources.Count == 0)
        {
            return;
        }

        AppendBlankLine(builder);
        builder.AppendLine(markdown ? "## Sources" : "Sources");
        foreach (var (source, index) in article.Sources.OrderBy(source => source.SortOrder).Select((source, index) => (source, index)))
        {
            AppendBlankLine(builder);
            var label = source.Title ?? source.Url ?? source.SourceType;
            builder.Append(index + 1).Append(markdown ? ". **" : ". ").Append(label);
            if (markdown)
            {
                builder.Append("**");
            }
            builder.Append(" (").Append(source.SourceType).AppendLine(")");
            AppendSourceField(builder, "URL", source.Url, markdown);
            AppendSourceField(builder, "External page ID", source.ExternalPageId, markdown);
            AppendSourceField(builder, "External revision ID", source.ExternalRevisionId, markdown);
            AppendSourceField(builder, "License", source.LicenseName, markdown);
            AppendSourceField(builder, "Attribution", source.AttributionText, markdown);
            AppendSourceField(builder, "Retrieved", source.RetrievedAtUtc?.ToString("O"), markdown);
            AppendSourceField(builder, "Last checked", source.LastCheckedAtUtc?.ToString("O"), markdown);
            AppendSourceField(builder, "Notes", source.Notes, markdown);
        }
    }

    private static void AppendMetadataLine(StringBuilder builder, string name, string value, bool markdown)
    {
        if (markdown)
        {
            builder.Append("- **").Append(name).Append(":** ").AppendLine(value);
        }
        else
        {
            builder.Append(name).Append(": ").AppendLine(value);
        }
    }

    private static void AppendSourceField(StringBuilder builder, string name, string? value, bool markdown)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(markdown ? "   - **" : "   ").Append(name);
        if (markdown)
        {
            builder.Append(":** ");
        }
        else
        {
            builder.Append(": ");
        }
        builder.AppendLine(value);
    }

    private static void AppendBlocks(StringBuilder builder, IReadOnlyList<ContentBlock> blocks, bool markdown)
    {
        foreach (var block in blocks)
        {
            AppendBlankLine(builder);
            switch (block.Kind)
            {
                case ContentBlockKind.Paragraph:
                    builder.AppendLine(block.Text);
                    break;
                case ContentBlockKind.UnorderedList:
                    foreach (var item in block.Items)
                    {
                        builder.Append(markdown ? "- " : "• ").AppendLine(item);
                    }

                    break;
                case ContentBlockKind.OrderedList:
                    for (var index = 0; index < block.Items.Count; index++)
                    {
                        builder.Append(index + 1).Append(". ").AppendLine(block.Items[index]);
                    }

                    break;
                default:
                    throw new InvalidDataException($"Unsupported content block kind {block.Kind}.");
            }
        }
    }

    private static void AppendBlankLine(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        if (builder.Length > 1 && builder[^1] == '\n' && builder[^2] != '\n')
        {
            builder.AppendLine();
        }
    }

    private sealed record PediaDocumentEnvelope(
        string Format,
        int Version,
        string Kind,
        DateTimeOffset ExportedAtUtc,
        ParsedDocument? Document);

    private sealed record PediaArticleEnvelope(
        string Format,
        int Version,
        string Kind,
        DateTimeOffset ExportedAtUtc,
        ArticleDetails? Article);
}
