using Microsoft.Data.Sqlite;
using Pedia.Core.Data;
using Pedia.Core.Models;
using Pedia.Core.Search;

namespace Pedia.Core.Repositories;

public sealed class ArticleRepository
{
    private readonly SqliteConnectionFactory _connections;

    public ArticleRepository(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<long> CreateAsync(
        ArticleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var validated = Validate(draft);
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Articles(
                Title, Subtitle, Summary, LanguageCode, ArticleType, Status, Notes,
                IsFavorite, WordCount, IsSample, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                $title, $subtitle, $summary, $languageCode, $articleType, $status, $notes,
                $isFavorite, $wordCount, $isSample, $now, $now);
            SELECT last_insert_rowid();
            """;
        AddArticleParameters(command, validated, now);
        var articleId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        await ReplaceChildrenAsync(connection, transaction, articleId, validated, now, cancellationToken).ConfigureAwait(false);
        await SearchDocumentStore.ReindexArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return articleId;
    }

    public async Task UpdateAsync(
        long articleId,
        ArticleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var validated = Validate(draft);
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Articles
                SET Title = $title,
                    Subtitle = $subtitle,
                    Summary = $summary,
                    LanguageCode = $languageCode,
                    ArticleType = $articleType,
                    Status = $status,
                    Notes = $notes,
                    IsFavorite = $isFavorite,
                    WordCount = $wordCount,
                    IsSample = $isSample,
                    UpdatedAtUtc = $now
                WHERE Id = $articleId;
                """;
            AddArticleParameters(command, validated, now);
            command.Parameters.AddWithValue("$articleId", articleId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new KeyNotFoundException($"Article {articleId} was not found.");
            }
        }

        await ReplaceChildrenAsync(connection, transaction, articleId, validated, now, cancellationToken).ConfigureAwait(false);
        await SearchDocumentStore.ReindexArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArticleDetails?> GetAsync(
        long articleId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        ArticleHeader? header;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Title, Subtitle, Summary, LanguageCode, ArticleType, Status, Notes,
                       IsFavorite, WordCount, IsSample, CreatedAtUtc, UpdatedAtUtc, DeletedAtUtc
                FROM Articles
                WHERE Id = $articleId;
                """;
            command.Parameters.AddWithValue("$articleId", articleId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            header = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new ArticleHeader(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.GetInt32(9),
                    reader.GetBoolean(10),
                    DatabaseValue.ReadDate(reader.GetString(11)),
                    DatabaseValue.ReadDate(reader.GetString(12)),
                    reader.IsDBNull(13) ? null : DatabaseValue.ReadDate(reader.GetString(13)))
                : null;
        }

        if (header is null)
        {
            return null;
        }

        var sections = await ReadSectionsAsync(connection, articleId, cancellationToken).ConfigureAwait(false);
        var sources = await ReadSourcesAsync(connection, articleId, cancellationToken).ConfigureAwait(false);
        var topics = await ReadTopicsAsync(connection, articleId, cancellationToken).ConfigureAwait(false);
        return new ArticleDetails(
            header.Id,
            header.Title,
            header.Subtitle,
            header.Summary,
            header.LanguageCode,
            header.ArticleType,
            header.Status,
            header.Notes,
            header.IsFavorite,
            header.WordCount,
            header.IsSample,
            header.CreatedAtUtc,
            header.UpdatedAtUtc,
            header.DeletedAtUtc,
            sections,
            sources,
            topics);
    }

    public async Task<long> DuplicateAsync(long articleId, CancellationToken cancellationToken = default)
    {
        var original = await GetAsync(articleId, cancellationToken).ConfigureAwait(false)
                       ?? throw new KeyNotFoundException($"Article {articleId} was not found.");
        if (original.DeletedAtUtc is not null)
        {
            throw new InvalidOperationException("An article in Trash cannot be duplicated.");
        }

        return await CreateAsync(
            new ArticleDraft
            {
                Title = original.Title + " Copy",
                Subtitle = original.Subtitle,
                Summary = original.Summary,
                LanguageCode = original.LanguageCode,
                ArticleType = original.ArticleType,
                Status = ArticleStatuses.Draft,
                Notes = original.Notes,
                IsFavorite = original.IsFavorite,
                IsSample = false,
                Sections = original.Sections
                    .Select(section => new ArticleSectionDraft(section.Heading, section.HeadingLevel, section.Body))
                    .ToArray(),
                Sources = original.Sources.Select(source => new ArticleSourceDraft
                {
                    SourceType = source.SourceType,
                    Title = source.Title,
                    Url = source.Url,
                    ExternalPageId = source.ExternalPageId,
                    ExternalRevisionId = source.ExternalRevisionId,
                    LicenseName = source.LicenseName,
                    AttributionText = source.AttributionText,
                    RetrievedAtUtc = source.RetrievedAtUtc,
                    LastCheckedAtUtc = source.LastCheckedAtUtc,
                    Notes = source.Notes
                }).ToArray(),
                TopicAssignments = original.TopicAssignments
                    .Select(topic => new ArticleTopicDraft(topic.TopicId, topic.IsPrimary))
                    .ToArray()
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveTopicAsync(
        long articleId,
        long topicId,
        CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM ArticleTopics WHERE ArticleId = $articleId AND TopicId = $topicId;";
            command.Parameters.AddWithValue("$articleId", articleId);
            command.Parameters.AddWithValue("$topicId", topicId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await TopicRepository.EnsurePrimaryAssignmentAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await TouchArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceTopicAssignmentsAsync(
        long articleId,
        IReadOnlyList<ArticleTopicDraft> assignments,
        CancellationToken cancellationToken = default)
    {
        var normalizedAssignments = NormalizeTopicAssignments(assignments);
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM ArticleTopics WHERE ArticleId = $articleId;";
            command.Parameters.AddWithValue("$articleId", articleId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertTopicsAsync(
            connection,
            transaction,
            articleId,
            normalizedAssignments,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        await TouchArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFavoriteAsync(
        long articleId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Articles
            SET IsFavorite = $isFavorite, UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $articleId;
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        command.Parameters.AddWithValue("$isFavorite", isFavorite);
        command.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new KeyNotFoundException($"Article {articleId} was not found.");
        }
    }

    public async Task AddTopicsToArticlesAsync(
        IReadOnlyList<long> articleIds,
        IReadOnlyList<long> topicIds,
        CancellationToken cancellationToken = default)
    {
        var articles = NormalizeIds(articleIds, nameof(articleIds));
        var topics = NormalizeIds(topicIds, nameof(topicIds));
        if (articles.Count == 0 || topics.Count == 0)
        {
            return;
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var articleId in articles)
        {
            await EnsureActiveArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }
        foreach (var topicId in topics)
        {
            await EnsureTopicAvailableAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var articleId in articles)
        {
            foreach (var topicId in topics)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR IGNORE INTO ArticleTopics(ArticleId, TopicId, IsPrimary, CreatedAtUtc)
                    VALUES ($articleId, $topicId, 0, $createdAtUtc);
                    """;
                insert.Parameters.AddWithValue("$articleId", articleId);
                insert.Parameters.AddWithValue("$topicId", topicId);
                insert.Parameters.AddWithValue("$createdAtUtc", DatabaseValue.Date(now));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await TopicRepository.EnsurePrimaryAssignmentAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
            await TouchArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveTopicFromArticlesAsync(
        IReadOnlyList<long> articleIds,
        long topicId,
        CancellationToken cancellationToken = default)
    {
        var articles = NormalizeIds(articleIds, nameof(articleIds));
        if (articles.Count == 0)
        {
            return;
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var articleId in articles)
        {
            await EnsureActiveArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        foreach (var articleId in articles)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ArticleTopics WHERE ArticleId = $articleId AND TopicId = $topicId;";
            delete.Parameters.AddWithValue("$articleId", articleId);
            delete.Parameters.AddWithValue("$topicId", topicId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0)
            {
                await TopicRepository.EnsurePrimaryAssignmentAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
                await TouchArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetStatusForArticlesAsync(
        IReadOnlyList<long> articleIds,
        string status,
        CancellationToken cancellationToken = default)
    {
        var articles = NormalizeIds(articleIds, nameof(articleIds));
        var normalizedStatus = status?.Trim();
        if (normalizedStatus is not (ArticleStatuses.Draft or ArticleStatuses.Ready or ArticleStatuses.NeedsReview or ArticleStatuses.Archived))
        {
            throw new ArgumentException("The article status is not supported.", nameof(status));
        }
        if (articles.Count == 0)
        {
            return;
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var articleId in articles)
        {
            await EnsureActiveArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        var updatedAt = DatabaseValue.Date(DateTimeOffset.UtcNow);
        foreach (var articleId in articles)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Articles SET Status = $status, UpdatedAtUtc = $updatedAtUtc WHERE Id = $articleId;";
            update.Parameters.AddWithValue("$status", normalizedStatus);
            update.Parameters.AddWithValue("$updatedAtUtc", updatedAt);
            update.Parameters.AddWithValue("$articleId", articleId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveArticlesToTrashAsync(
        IReadOnlyList<long> articleIds,
        CancellationToken cancellationToken = default)
    {
        var articles = NormalizeIds(articleIds, nameof(articleIds));
        if (articles.Count == 0)
        {
            return;
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var articleId in articles)
        {
            await EnsureActiveArticleExistsAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        var deletedAt = DatabaseValue.Date(DateTimeOffset.UtcNow);
        foreach (var articleId in articles)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Articles SET DeletedAtUtc = $deletedAtUtc, UpdatedAtUtc = $deletedAtUtc WHERE Id = $articleId;";
            update.Parameters.AddWithValue("$deletedAtUtc", deletedAt);
            update.Parameters.AddWithValue("$articleId", articleId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await SearchDocumentStore.RemoveArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task MoveToTrashAsync(long articleId, CancellationToken cancellationToken = default) =>
        SetDeletedStateAsync(articleId, restore: false, cancellationToken);

    public Task RestoreAsync(long articleId, CancellationToken cancellationToken = default) =>
        SetDeletedStateAsync(articleId, restore: true, cancellationToken);

    public async Task DeletePermanentlyAsync(long articleId, CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = "SELECT DeletedAtUtc FROM Articles WHERE Id = $articleId;";
            state.Parameters.AddWithValue("$articleId", articleId);
            var deletedAt = await state.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (deletedAt is null)
            {
                throw new KeyNotFoundException($"Article {articleId} was not found.");
            }

            if (deletedAt is DBNull)
            {
                throw new InvalidOperationException("Only an article in Trash can be permanently deleted.");
            }
        }

        await SearchDocumentStore.RemoveArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Articles WHERE Id = $articleId;";
            command.Parameters.AddWithValue("$articleId", articleId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var clearIndex = connection.CreateCommand())
        {
            clearIndex.Transaction = transaction;
            clearIndex.CommandText = "DELETE FROM SearchDocumentsFts WHERE rowid IN (SELECT Id FROM Articles WHERE DeletedAtUtc IS NOT NULL);";
            await clearIndex.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int count;
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Articles WHERE DeletedAtUtc IS NOT NULL;";
            count = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    public async Task<int> CountAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? "SELECT COUNT(*) FROM Articles;"
            : "SELECT COUNT(*) FROM Articles WHERE DeletedAtUtc IS NULL;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM Articles WHERE DeletedAtUtc IS NULL),
                (SELECT COUNT(*) FROM Articles WHERE DeletedAtUtc IS NOT NULL),
                (SELECT COUNT(*) FROM Topics WHERE DeletedAtUtc IS NULL),
                (SELECT COUNT(*) FROM ArticleSources);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new LibraryStatistics(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    public async Task<SampleDeletionResult> DeleteSampleContentAsync(CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int articleCount;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM Articles WHERE IsSample = 1;";
            articleCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        await using (var clearIndex = connection.CreateCommand())
        {
            clearIndex.Transaction = transaction;
            clearIndex.CommandText = "DELETE FROM SearchDocumentsFts WHERE rowid IN (SELECT Id FROM Articles WHERE IsSample = 1);";
            await clearIndex.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteArticles = connection.CreateCommand())
        {
            deleteArticles.Transaction = transaction;
            deleteArticles.CommandText = "DELETE FROM Articles WHERE IsSample = 1;";
            await deleteArticles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var sampleTopicIds = await ReadSampleTopicIdsDeepestFirstAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        foreach (var topicId in sampleTopicIds)
        {
            var parentId = await ReadTopicParentAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);
            var affectedArticleIds = await ReadTopicArticleIdsAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);
            await using (var reparent = connection.CreateCommand())
            {
                reparent.Transaction = transaction;
                reparent.CommandText = "UPDATE Topics SET ParentId = $parentId WHERE ParentId = $topicId;";
                reparent.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
                reparent.Parameters.AddWithValue("$topicId", topicId);
                await reparent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var assignments = connection.CreateCommand())
            {
                assignments.Transaction = transaction;
                assignments.CommandText = "DELETE FROM ArticleTopics WHERE TopicId = $topicId;";
                assignments.Parameters.AddWithValue("$topicId", topicId);
                await assignments.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var articleId in affectedArticleIds)
            {
                await TopicRepository.EnsurePrimaryAssignmentAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
            }

            await using var deleteTopic = connection.CreateCommand();
            deleteTopic.Transaction = transaction;
            deleteTopic.CommandText = "DELETE FROM Topics WHERE Id = $topicId;";
            deleteTopic.Parameters.AddWithValue("$topicId", topicId);
            await deleteTopic.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SampleDeletionResult(articleCount, sampleTopicIds.Count);
    }

    private async Task SetDeletedStateAsync(
        long articleId,
        bool restore,
        CancellationToken cancellationToken)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = restore
                ? """
                    UPDATE Articles
                    SET DeletedAtUtc = NULL, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $articleId AND DeletedAtUtc IS NOT NULL;
                    """
                : """
                    UPDATE Articles
                    SET DeletedAtUtc = $updatedAtUtc, UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $articleId AND DeletedAtUtc IS NULL;
                    """;
            update.Parameters.AddWithValue("$articleId", articleId);
            update.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException(restore
                    ? "Only an article in Trash can be restored."
                    : "Only an active article can be moved to Trash.");
            }
        }

        if (restore)
        {
            await SearchDocumentStore.ReindexArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SearchDocumentStore.RemoveArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ValidatedArticle Validate(ArticleDraft draft)
    {
        var title = draft.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new ArgumentException("An article title is required.", nameof(draft));
        }

        var languageCode = draft.LanguageCode?.Trim();
        if (string.IsNullOrEmpty(languageCode))
        {
            throw new ArgumentException("An article language code is required.", nameof(draft));
        }

        var articleType = draft.ArticleType?.Trim();
        var status = draft.Status?.Trim();
        if (string.IsNullOrEmpty(articleType) || string.IsNullOrEmpty(status))
        {
            throw new ArgumentException("Article type and status are required.", nameof(draft));
        }

        var sections = (draft.Sections ?? [])
            .Select(section =>
            {
                if (section.HeadingLevel is < 1 or > 3)
                {
                    throw new ArgumentOutOfRangeException(nameof(draft), "Heading levels must be between 1 and 3.");
                }

                return section with
                {
                    Heading = DatabaseValue.OptionalText(section.Heading),
                    Body = section.Body ?? string.Empty
                };
            })
            .ToArray();
        var sources = (draft.Sources ?? []).ToArray();
        var topics = NormalizeTopicAssignments(draft.TopicAssignments ?? []);

        return new ValidatedArticle(
            title,
            DatabaseValue.OptionalText(draft.Subtitle),
            DatabaseValue.OptionalText(draft.Summary),
            languageCode,
            articleType,
            status,
            DatabaseValue.OptionalText(draft.Notes),
            draft.IsFavorite,
            WordCounter.Count(sections.Select(section => section.Body)),
            draft.IsSample,
            sections,
            sources,
            topics);
    }

    private static IReadOnlyList<ArticleTopicDraft> NormalizeTopicAssignments(
        IReadOnlyList<ArticleTopicDraft> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var topics = assignments
            .GroupBy(topic => topic.TopicId)
            .Select(group => group.First())
            .ToArray();
        var selectedPrimary = topics.FirstOrDefault(topic => topic.IsPrimary)?.TopicId ?? topics.FirstOrDefault()?.TopicId;
        return topics.Select(topic => topic with { IsPrimary = topic.TopicId == selectedPrimary }).ToArray();
    }

    private static void AddArticleParameters(SqliteCommand command, ValidatedArticle article, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$title", article.Title);
        command.Parameters.AddWithValue("$subtitle", (object?)article.Subtitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)article.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$languageCode", article.LanguageCode);
        command.Parameters.AddWithValue("$articleType", article.ArticleType);
        command.Parameters.AddWithValue("$status", article.Status);
        command.Parameters.AddWithValue("$notes", (object?)article.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$isFavorite", article.IsFavorite);
        command.Parameters.AddWithValue("$wordCount", article.WordCount);
        command.Parameters.AddWithValue("$isSample", article.IsSample);
        command.Parameters.AddWithValue("$now", DatabaseValue.Date(now));
    }

    private static async Task ReplaceChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        ValidatedArticle article,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM ArticleSections WHERE ArticleId = $articleId;
                DELETE FROM ArticleSources WHERE ArticleId = $articleId;
                DELETE FROM ArticleTopics WHERE ArticleId = $articleId;
                """;
            delete.Parameters.AddWithValue("$articleId", articleId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertSectionsAsync(connection, transaction, articleId, article.Sections, cancellationToken).ConfigureAwait(false);
        await InsertSourcesAsync(connection, transaction, articleId, article.Sources, cancellationToken).ConfigureAwait(false);
        await InsertTopicsAsync(connection, transaction, articleId, article.Topics, now, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertSectionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        IReadOnlyList<ArticleSectionDraft> sections,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ArticleSections(ArticleId, Heading, HeadingLevel, Body, SortOrder)
            VALUES ($articleId, $heading, $headingLevel, $body, $sortOrder);
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        var heading = command.Parameters.Add("$heading", SqliteType.Text);
        var level = command.Parameters.Add("$headingLevel", SqliteType.Integer);
        var body = command.Parameters.Add("$body", SqliteType.Text);
        var order = command.Parameters.Add("$sortOrder", SqliteType.Integer);
        for (var index = 0; index < sections.Count; index++)
        {
            heading.Value = (object?)sections[index].Heading ?? DBNull.Value;
            level.Value = sections[index].HeadingLevel;
            body.Value = sections[index].Body;
            order.Value = index;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertSourcesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        IReadOnlyList<ArticleSourceDraft> sources,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (string.IsNullOrWhiteSpace(source.SourceType))
            {
                throw new ArgumentException("A source type is required.", nameof(sources));
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ArticleSources(
                    ArticleId, SourceType, Title, Url, ExternalPageId, ExternalRevisionId,
                    LicenseName, AttributionText, RetrievedAtUtc, LastCheckedAtUtc, Notes, SortOrder)
                VALUES (
                    $articleId, $sourceType, $title, $url, $externalPageId, $externalRevisionId,
                    $licenseName, $attributionText, $retrievedAtUtc, $lastCheckedAtUtc, $notes, $sortOrder);
                """;
            command.Parameters.AddWithValue("$articleId", articleId);
            command.Parameters.AddWithValue("$sourceType", source.SourceType.Trim());
            command.Parameters.AddWithValue("$title", (object?)DatabaseValue.OptionalText(source.Title) ?? DBNull.Value);
            command.Parameters.AddWithValue("$url", (object?)DatabaseValue.OptionalText(source.Url) ?? DBNull.Value);
            command.Parameters.AddWithValue("$externalPageId", (object?)DatabaseValue.OptionalText(source.ExternalPageId) ?? DBNull.Value);
            command.Parameters.AddWithValue("$externalRevisionId", (object?)DatabaseValue.OptionalText(source.ExternalRevisionId) ?? DBNull.Value);
            command.Parameters.AddWithValue("$licenseName", (object?)DatabaseValue.OptionalText(source.LicenseName) ?? DBNull.Value);
            command.Parameters.AddWithValue("$attributionText", (object?)DatabaseValue.OptionalText(source.AttributionText) ?? DBNull.Value);
            command.Parameters.AddWithValue("$retrievedAtUtc", DatabaseValue.NullableDate(source.RetrievedAtUtc));
            command.Parameters.AddWithValue("$lastCheckedAtUtc", DatabaseValue.NullableDate(source.LastCheckedAtUtc));
            command.Parameters.AddWithValue("$notes", (object?)DatabaseValue.OptionalText(source.Notes) ?? DBNull.Value);
            command.Parameters.AddWithValue("$sortOrder", index);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertTopicsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        IReadOnlyList<ArticleTopicDraft> topics,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var topic in topics)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ArticleTopics(ArticleId, TopicId, IsPrimary, CreatedAtUtc)
                SELECT $articleId, Id, $isPrimary, $createdAtUtc
                FROM Topics
                WHERE Id = $topicId AND DeletedAtUtc IS NULL;
                """;
            command.Parameters.AddWithValue("$articleId", articleId);
            command.Parameters.AddWithValue("$topicId", topic.TopicId);
            command.Parameters.AddWithValue("$isPrimary", topic.IsPrimary);
            command.Parameters.AddWithValue("$createdAtUtc", DatabaseValue.Date(now));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException($"Topic {topic.TopicId} is not available.");
            }
        }
    }

    private static async Task<IReadOnlyList<ArticleSection>> ReadSectionsAsync(
        SqliteConnection connection,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Heading, HeadingLevel, Body, SortOrder
            FROM ArticleSections
            WHERE ArticleId = $articleId
            ORDER BY SortOrder, Id;
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sections = new List<ArticleSection>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sections.Add(new ArticleSection(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return sections;
    }

    private static async Task<IReadOnlyList<ArticleSource>> ReadSourcesAsync(
        SqliteConnection connection,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceType, Title, Url, ExternalPageId, ExternalRevisionId,
                   LicenseName, AttributionText, RetrievedAtUtc, LastCheckedAtUtc, Notes, SortOrder
            FROM ArticleSources
            WHERE ArticleId = $articleId
            ORDER BY SortOrder, Id;
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sources = new List<ArticleSource>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sources.Add(new ArticleSource(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DatabaseValue.ReadDate(reader.GetString(8)),
                reader.IsDBNull(9) ? null : DatabaseValue.ReadDate(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetInt32(11)));
        }

        return sources;
    }

    private static async Task<IReadOnlyList<ArticleTopicAssignment>> ReadTopicsAsync(
        SqliteConnection connection,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE TopicPaths(Id, Path) AS (
                SELECT Id, Name FROM Topics WHERE ParentId IS NULL
                UNION ALL
                SELECT child.Id, parent.Path || ' / ' || child.Name
                FROM Topics child
                JOIN TopicPaths parent ON child.ParentId = parent.Id
            )
            SELECT topic.Id, topic.Name, path.Path, assignment.IsPrimary, assignment.CreatedAtUtc
            FROM ArticleTopics assignment
            JOIN Topics topic ON topic.Id = assignment.TopicId
            JOIN TopicPaths path ON path.Id = topic.Id
            WHERE assignment.ArticleId = $articleId
              AND topic.DeletedAtUtc IS NULL
            ORDER BY assignment.IsPrimary DESC, path.Path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var topics = new List<ArticleTopicAssignment>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            topics.Add(new ArticleTopicAssignment(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                DatabaseValue.ReadDate(reader.GetString(4))));
        }

        return topics;
    }

    private static async Task EnsureArticleExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Articles WHERE Id = $articleId);";
        command.Parameters.AddWithValue("$articleId", articleId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
        {
            throw new KeyNotFoundException($"Article {articleId} was not found.");
        }
    }

    private static async Task EnsureActiveArticleExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Articles WHERE Id = $articleId AND DeletedAtUtc IS NULL);";
        command.Parameters.AddWithValue("$articleId", articleId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
        {
            throw new KeyNotFoundException($"Active article {articleId} was not found.");
        }
    }

    private static async Task EnsureTopicAvailableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long topicId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Topics WHERE Id = $topicId AND DeletedAtUtc IS NULL);";
        command.Parameters.AddWithValue("$topicId", topicId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
        {
            throw new KeyNotFoundException($"Topic {topicId} was not found.");
        }
    }

    private static IReadOnlyList<long> NormalizeIds(IReadOnlyList<long> ids, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(ids, parameterName);
        if (ids.Any(id => id <= 0))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Identifiers must be positive.");
        }

        return ids.Distinct().ToArray();
    }

    private static async Task TouchArticleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Articles SET UpdatedAtUtc = $updatedAtUtc WHERE Id = $articleId;";
        command.Parameters.AddWithValue("$articleId", articleId);
        command.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<long>> ReadSampleTopicIdsDeepestFirstAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE TopicTree(Id, Depth) AS (
                SELECT Id, 0 FROM Topics WHERE ParentId IS NULL
                UNION ALL
                SELECT child.Id, parent.Depth + 1
                FROM Topics child
                JOIN TopicTree parent ON child.ParentId = parent.Id
            )
            SELECT topic.Id
            FROM Topics topic
            JOIN TopicTree tree ON tree.Id = topic.Id
            WHERE topic.IsSample = 1
            ORDER BY tree.Depth DESC, topic.Id DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<long>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static async Task<long?> ReadTopicParentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long topicId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ParentId FROM Topics WHERE Id = $topicId;";
        command.Parameters.AddWithValue("$topicId", topicId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<IReadOnlyList<long>> ReadTopicArticleIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long topicId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ArticleId FROM ArticleTopics WHERE TopicId = $topicId;";
        command.Parameters.AddWithValue("$topicId", topicId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<long>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private sealed record ValidatedArticle(
        string Title,
        string? Subtitle,
        string? Summary,
        string LanguageCode,
        string ArticleType,
        string Status,
        string? Notes,
        bool IsFavorite,
        int WordCount,
        bool IsSample,
        IReadOnlyList<ArticleSectionDraft> Sections,
        IReadOnlyList<ArticleSourceDraft> Sources,
        IReadOnlyList<ArticleTopicDraft> Topics);

    private sealed record ArticleHeader(
        long Id,
        string Title,
        string? Subtitle,
        string? Summary,
        string LanguageCode,
        string ArticleType,
        string Status,
        string? Notes,
        bool IsFavorite,
        int WordCount,
        bool IsSample,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? DeletedAtUtc);
}
