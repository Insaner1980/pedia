using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Pedia.Core.Search;

[SuppressMessage(
    "Maintainability",
    "S1192",
    Justification = "SQLite parameter names intentionally match the placeholders in their SQL statements.")]
internal static class SearchDocumentStore
{
    public static async Task ReindexArticleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await RemoveArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT article.Title,
                   COALESCE(article.Subtitle, ''),
                   COALESCE(article.Summary, ''),
                   COALESCE((
                       SELECT group_concat(section.Text, char(10))
                       FROM (
                           SELECT CASE
                                      WHEN Heading IS NULL OR trim(Heading) = '' THEN Body
                                      ELSE Heading || char(10) || Body
                                  END AS Text
                           FROM ArticleSections
                           WHERE ArticleId = article.Id
                           ORDER BY SortOrder, Id
                       ) section
                   ), ''),
                   COALESCE((
                       SELECT group_concat(source.Text, char(10))
                       FROM (
                           SELECT trim(
                               COALESCE(Title, '') || ' ' ||
                               COALESCE(AttributionText, '') || ' ' ||
                               COALESCE(Notes, '')) AS Text
                           FROM ArticleSources
                           WHERE ArticleId = article.Id
                           ORDER BY SortOrder, Id
                       ) source
                   ), ''),
                   COALESCE(article.Notes, '')
            FROM Articles article
            WHERE article.Id = $articleId
              AND article.DeletedAtUtc IS NULL;
            """;
        read.Parameters.AddWithValue("$articleId", articleId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var content = new SearchDocumentContent(
            articleId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
        await reader.DisposeAsync().ConfigureAwait(false);

        await using (var document = connection.CreateCommand())
        {
            document.Transaction = transaction;
            document.CommandText = """
                INSERT INTO SearchDocuments(
                    ArticleId, Title, Subtitle, Summary, SectionText, SourceText, Notes)
                VALUES (
                    $articleId, $title, $subtitle, $summary, $sectionText, $sourceText, $notes);
                """;
            AddDocumentParameters(document, content);
            await document.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText = """
                INSERT INTO SearchDocumentsFts(
                    rowid, ArticleId, Title, Subtitle, Summary, SectionText, SourceText, Notes)
                VALUES (
                    $articleId, $articleId, $title, $subtitle, $summary, $sectionText, $sourceText, $notes);
                """;
            AddDocumentParameters(index, content);
            await index.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task RemoveArticleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText = "DELETE FROM SearchDocumentsFts WHERE rowid = $articleId;";
            index.Parameters.AddWithValue("$articleId", articleId);
            await index.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var document = connection.CreateCommand())
        {
            document.Transaction = transaction;
            document.CommandText = "DELETE FROM SearchDocuments WHERE ArticleId = $articleId;";
            document.Parameters.AddWithValue("$articleId", articleId);
            await document.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddDocumentParameters(SqliteCommand command, SearchDocumentContent content)
    {
        command.Parameters.AddWithValue("$articleId", content.ArticleId);
        command.Parameters.AddWithValue("$title", content.Title);
        command.Parameters.AddWithValue("$subtitle", content.Subtitle);
        command.Parameters.AddWithValue("$summary", content.Summary);
        command.Parameters.AddWithValue("$sectionText", content.SectionText);
        command.Parameters.AddWithValue("$sourceText", content.SourceText);
        command.Parameters.AddWithValue("$notes", content.Notes);
    }

    private sealed record SearchDocumentContent(
        long ArticleId,
        string Title,
        string Subtitle,
        string Summary,
        string SectionText,
        string SourceText,
        string Notes);
}
