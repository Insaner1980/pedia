using System.Text;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Pedia.Core.Data;
using Pedia.Core.Models;

namespace Pedia.Core.Repositories;

[SuppressMessage(
    "Maintainability",
    "S1192",
    Justification = "SQLite parameter names intentionally match the placeholders in their SQL statements.")]
public sealed class TopicRepository
{
    private readonly SqliteConnectionFactory _connections;

    public TopicRepository(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<long> CreateAsync(
        string name,
        long? parentId = null,
        string? description = null,
        bool isSample = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (parentId is not null)
        {
            await EnsureActiveTopicAsync(connection, null, parentId.Value, cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Topics(
                ParentId, Name, NameKey, Description, SortOrder, IsSample, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                $parentId, $name, $nameKey, $description,
                COALESCE((SELECT MAX(SortOrder) + 1 FROM Topics
                          WHERE ParentId IS $parentId AND DeletedAtUtc IS NULL), 0),
                $isSample, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", normalizedName);
        command.Parameters.AddWithValue("$nameKey", CreateNameKey(normalizedName));
        command.Parameters.AddWithValue("$description", (object?)DatabaseValue.OptionalText(description) ?? DBNull.Value);
        command.Parameters.AddWithValue("$isSample", isSample);
        command.Parameters.AddWithValue("$now", DatabaseValue.Date(DateTimeOffset.UtcNow));

        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"A topic named '{normalizedName}' already exists at that level.",
                exception);
        }
    }

    public async Task RenameAsync(
        long topicId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Topics
            SET Name = $name,
                NameKey = $nameKey,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $topicId
              AND DeletedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        command.Parameters.AddWithValue("$name", normalizedName);
        command.Parameters.AddWithValue("$nameKey", CreateNameKey(normalizedName));
        command.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));

        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new KeyNotFoundException($"Topic {topicId} was not found.");
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"A topic named '{normalizedName}' already exists at that level.",
                exception);
        }
    }

    public async Task UpdateDescriptionAsync(
        long topicId,
        string? description,
        CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Topics
            SET Description = $description,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $topicId AND DeletedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        command.Parameters.AddWithValue("$description", (object?)DatabaseValue.OptionalText(description) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new KeyNotFoundException($"Topic {topicId} was not found.");
        }
    }

    public async Task MoveAsync(
        long topicId,
        long? newParentId,
        int? newSortOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (topicId == newParentId)
        {
            throw new InvalidOperationException("A topic cannot be moved into itself.");
        }

        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var oldParentId = await ReadParentIdAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);

        if (newParentId is not null)
        {
            await EnsureActiveTopicAsync(connection, transaction, newParentId.Value, cancellationToken).ConfigureAwait(false);
            if (await IsDescendantAsync(connection, transaction, topicId, newParentId.Value, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("A topic cannot be moved into one of its descendants.");
            }
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Topics
                SET ParentId = $parentId,
                    SortOrder = 2147483647,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $topicId AND DeletedAtUtc IS NULL;
                """;
            update.Parameters.AddWithValue("$parentId", (object?)newParentId ?? DBNull.Value);
            update.Parameters.AddWithValue("$topicId", topicId);
            update.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
            try
            {
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("A topic with the same name already exists at the destination.", exception);
            }
        }

        await NormalizeSiblingsAsync(connection, transaction, oldParentId, null, null, cancellationToken).ConfigureAwait(false);
        await NormalizeSiblingsAsync(
            connection,
            transaction,
            newParentId,
            topicId,
            newSortOrder,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderAsync(
        long topicId,
        int newSortOrder,
        CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var parentId = await ReadParentIdAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);
        await NormalizeSiblingsAsync(
            connection,
            transaction,
            parentId,
            topicId,
            newSortOrder,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TopicSummary>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = TopicSelectSql + " ORDER BY t.ParentId, t.SortOrder, t.Name COLLATE NOCASE;";
        return await ReadTopicsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TopicSummary>> GetChildrenAsync(
        long? parentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = TopicSelectSql +
                              " AND t.ParentId IS $parentId ORDER BY t.SortOrder, t.Name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        return await ReadTopicsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TopicSummary>> GetDescendantsAsync(
        long topicId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE Descendants(Id, Depth, Trail) AS (
                SELECT Id, 1, printf('%010d', SortOrder)
                FROM Topics
                WHERE ParentId = $topicId AND DeletedAtUtc IS NULL
                UNION ALL
                SELECT child.Id, parent.Depth + 1,
                       parent.Trail || '/' || printf('%010d', child.SortOrder)
                FROM Topics child
                JOIN Descendants parent ON child.ParentId = parent.Id
                WHERE child.DeletedAtUtc IS NULL
            ),
            Hierarchy(AncestorId, DescendantId) AS (
                SELECT Id, Id FROM Topics WHERE DeletedAtUtc IS NULL
                UNION ALL
                SELECT hierarchy.AncestorId, child.Id
                FROM Hierarchy hierarchy
                JOIN Topics child ON child.ParentId = hierarchy.DescendantId
                WHERE child.DeletedAtUtc IS NULL
            )
            SELECT t.Id, t.ParentId, t.Name, t.Description, t.SortOrder, t.IsSample,
                   t.CreatedAtUtc, t.UpdatedAtUtc,
                   (SELECT COUNT(DISTINCT direct.ArticleId)
                    FROM ArticleTopics direct
                    JOIN Articles article ON article.Id = direct.ArticleId AND article.DeletedAtUtc IS NULL
                    WHERE direct.TopicId = t.Id),
                   (SELECT COUNT(DISTINCT subtree.ArticleId)
                    FROM Hierarchy scope
                    JOIN ArticleTopics subtree ON subtree.TopicId = scope.DescendantId
                    JOIN Articles article ON article.Id = subtree.ArticleId AND article.DeletedAtUtc IS NULL
                    WHERE scope.AncestorId = t.Id)
            FROM Descendants descendants
            JOIN Topics t ON t.Id = descendants.Id
            ORDER BY descendants.Trail, t.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        return await ReadTopicsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetPathAsync(long topicId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE Ancestors(Id, ParentId, Path) AS (
                SELECT Id, ParentId, Name
                FROM Topics
                WHERE Id = $topicId AND DeletedAtUtc IS NULL
                UNION ALL
                SELECT parent.Id, parent.ParentId, parent.Name || ' / ' || child.Path
                FROM Topics parent
                JOIN Ancestors child ON child.ParentId = parent.Id
                WHERE parent.DeletedAtUtc IS NULL
            )
            SELECT Path FROM Ancestors WHERE ParentId IS NULL;
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))
               ?? throw new KeyNotFoundException($"Topic {topicId} was not found.");
    }

    public async Task<TopicDeleteResult> DeleteAsync(
        long topicId,
        CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var parentId = await ReadParentIdAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);
        var affectedArticleIds = await ReadAssignedArticleIdsAsync(connection, transaction, topicId, cancellationToken).ConfigureAwait(false);

        int childCount;
        await using (var reparent = connection.CreateCommand())
        {
            reparent.Transaction = transaction;
            reparent.CommandText = """
                UPDATE Topics
                SET ParentId = $parentId,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE ParentId = $topicId AND DeletedAtUtc IS NULL;
                """;
            reparent.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
            reparent.Parameters.AddWithValue("$topicId", topicId);
            reparent.Parameters.AddWithValue("$updatedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
            try
            {
                childCount = await reparent.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException(
                    "A child topic has the same name as a topic at the destination level.",
                    exception);
            }
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
            await EnsurePrimaryAssignmentAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                UPDATE Topics
                SET DeletedAtUtc = $deletedAtUtc,
                    UpdatedAtUtc = $deletedAtUtc
                WHERE Id = $topicId AND DeletedAtUtc IS NULL;
                """;
            delete.Parameters.AddWithValue("$topicId", topicId);
            delete.Parameters.AddWithValue("$deletedAtUtc", DatabaseValue.Date(DateTimeOffset.UtcNow));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new KeyNotFoundException($"Topic {topicId} was not found.");
            }
        }

        await NormalizeSiblingsAsync(connection, transaction, parentId, null, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new TopicDeleteResult(childCount, affectedArticleIds.Count);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Topics WHERE DeletedAtUtc IS NULL;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private const string TopicSelectSql = """
        WITH DirectArticleCounts(TopicId, ArticleCount) AS (
            SELECT assignment.TopicId, COUNT(*)
            FROM ArticleTopics assignment
            JOIN Articles article ON article.Id = assignment.ArticleId
            WHERE article.DeletedAtUtc IS NULL
            GROUP BY assignment.TopicId
        )
        SELECT t.Id, t.ParentId, t.Name, t.Description, t.SortOrder, t.IsSample,
               t.CreatedAtUtc, t.UpdatedAtUtc,
               COALESCE(direct.ArticleCount, 0),
               COALESCE(direct.ArticleCount, 0)
        FROM Topics t
        LEFT JOIN DirectArticleCounts direct ON direct.TopicId = t.Id
        WHERE t.DeletedAtUtc IS NULL
        """;

    private static async Task<IReadOnlyList<TopicSummary>> ReadTopicsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var topics = new List<TopicSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            topics.Add(new TopicSummary(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                DatabaseValue.ReadDate(reader.GetString(6)),
                DatabaseValue.ReadDate(reader.GetString(7)),
                reader.GetInt32(8),
                reader.GetInt32(9)));
        }

        return topics;
    }

    private static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A topic name is required.", nameof(name));
        }

        return normalized;
    }

    internal static string CreateNameKey(string name) =>
        name.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static async Task EnsureActiveTopicAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
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

    private static async Task<long?> ReadParentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long topicId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ParentId FROM Topics WHERE Id = $topicId AND DeletedAtUtc IS NULL;";
        command.Parameters.AddWithValue("$topicId", topicId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            throw new KeyNotFoundException($"Topic {topicId} was not found.");
        }

        return value is DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<bool> IsDescendantAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long topicId,
        long possibleDescendantId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE Descendants(Id) AS (
                SELECT Id FROM Topics WHERE ParentId = $topicId AND DeletedAtUtc IS NULL
                UNION ALL
                SELECT child.Id
                FROM Topics child
                JOIN Descendants parent ON child.ParentId = parent.Id
                WHERE child.DeletedAtUtc IS NULL
            )
            SELECT EXISTS(SELECT 1 FROM Descendants WHERE Id = $possibleDescendantId);
            """;
        command.Parameters.AddWithValue("$topicId", topicId);
        command.Parameters.AddWithValue("$possibleDescendantId", possibleDescendantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task NormalizeSiblingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long? parentId,
        long? movedTopicId,
        int? requestedIndex,
        CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT Id
            FROM Topics
            WHERE ParentId IS $parentId
              AND DeletedAtUtc IS NULL
              AND ($movedTopicId IS NULL OR Id <> $movedTopicId)
            ORDER BY SortOrder, Name COLLATE NOCASE, Id;
            """;
        read.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        read.Parameters.AddWithValue("$movedTopicId", (object?)movedTopicId ?? DBNull.Value);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var topicIds = new List<long>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            topicIds.Add(reader.GetInt64(0));
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        if (movedTopicId is not null)
        {
            topicIds.Insert(Math.Clamp(requestedIndex ?? topicIds.Count, 0, topicIds.Count), movedTopicId.Value);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Topics SET SortOrder = $sortOrder WHERE Id = $topicId;";
        var idParameter = update.Parameters.Add("$topicId", SqliteType.Integer);
        var orderParameter = update.Parameters.Add("$sortOrder", SqliteType.Integer);
        for (var index = 0; index < topicIds.Count; index++)
        {
            idParameter.Value = topicIds[index];
            orderParameter.Value = index;
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<long>> ReadAssignedArticleIdsAsync(
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
        var result = new List<long>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    internal static async Task EnsurePrimaryAssignmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ArticleTopics
            SET IsPrimary = 1
            WHERE ArticleId = $articleId
              AND TopicId = (
                  SELECT TopicId FROM ArticleTopics
                  WHERE ArticleId = $articleId
                  ORDER BY CreatedAtUtc, TopicId
                  LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM ArticleTopics
                  WHERE ArticleId = $articleId AND IsPrimary = 1);
            """;
        command.Parameters.AddWithValue("$articleId", articleId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
