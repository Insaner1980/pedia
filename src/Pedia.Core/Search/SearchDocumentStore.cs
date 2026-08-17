using Microsoft.Data.Sqlite;

namespace Pedia.Core.Search;

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

        var title = reader.GetString(0);
        var subtitle = reader.GetString(1);
        var summary = reader.GetString(2);
        var sectionText = reader.GetString(3);
        var sourceText = reader.GetString(4);
        var notes = reader.GetString(5);
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
            AddDocumentParameters(document, articleId, title, subtitle, summary, sectionText, sourceText, notes);
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
            AddDocumentParameters(index, articleId, title, subtitle, summary, sectionText, sourceText, notes);
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

    private static void AddDocumentParameters(
        SqliteCommand command,
        long articleId,
        string title,
        string subtitle,
        string summary,
        string sectionText,
        string sourceText,
        string notes)
    {
        command.Parameters.AddWithValue("$articleId", articleId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$subtitle", subtitle);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$sectionText", sectionText);
        command.Parameters.AddWithValue("$sourceText", sourceText);
        command.Parameters.AddWithValue("$notes", notes);
    }
}
