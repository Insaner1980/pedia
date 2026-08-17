using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Pedia.Core.Data;
using Pedia.Core.Models;

namespace Pedia.Core.Search;

public interface IArticleQueryService
{
    Task<ArticlePage> QueryAsync(ArticleQuery query, CancellationToken cancellationToken = default);
    Task<int> RebuildIndexAsync(CancellationToken cancellationToken = default);
    Task<bool> VerifyFts5Async(CancellationToken cancellationToken = default);
}

public sealed class ArticleQueryService : IArticleQueryService
{
    private readonly SqliteConnectionFactory _connections;

    public ArticleQueryService(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    [SuppressMessage(
        "Security",
        "S2077",
        Justification = "The SQL fragments are selected from internal constant whitelists; all external values are parameters.")]
    public async Task<ArticlePage> QueryAsync(
        ArticleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page numbers start at 1.");
        }

        if (query.PageSize is < 1 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page size must be between 1 and 250.");
        }

        var sql = BuildSql(query);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        int totalCount;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"{sql.WithClause} SELECT COUNT(*) {sql.FromClause} WHERE {sql.WhereClause};";
            AddParameters(count, sql.Parameters);
            totalCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {sql.WithClause}
            SELECT a.Id, a.Title, a.Subtitle, a.Summary, a.LanguageCode, a.ArticleType,
                   a.Status, a.IsFavorite, a.WordCount, a.IsSample,
                   a.CreatedAtUtc, a.UpdatedAtUtc, a.DeletedAtUtc,
                   (SELECT COUNT(*) FROM ArticleTopics topic WHERE topic.ArticleId = a.Id),
                   (SELECT COUNT(*) FROM ArticleSources source WHERE source.ArticleId = a.Id),
                   {sql.SnippetExpression},
                   {sql.RankExpression}
            {sql.FromClause}
            WHERE {sql.WhereClause}
            ORDER BY {sql.OrderByClause}
            LIMIT $pageSize OFFSET $offset;
            """;
        AddParameters(command, sql.Parameters);
        command.Parameters.AddWithValue("$pageSize", query.PageSize);
        command.Parameters.AddWithValue("$offset", (query.Page - 1) * query.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<ArticleSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ArticleSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                DatabaseValue.ReadDate(reader.GetString(10)),
                DatabaseValue.ReadDate(reader.GetString(11)),
                reader.IsDBNull(12) ? null : DatabaseValue.ReadDate(reader.GetString(12)),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetDouble(16)));
        }

        return new ArticlePage(items, totalCount, query.Page, query.PageSize);
    }

    public async Task<int> RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM SearchDocumentsFts; DELETE FROM SearchDocuments;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var articleIds = new List<long>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT Id FROM Articles WHERE DeletedAtUtc IS NULL ORDER BY Id;";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                articleIds.Add(reader.GetInt64(0));
            }
        }

        foreach (var articleId in articleIds)
        {
            await SearchDocumentStore.ReindexArticleAsync(
                    connection,
                    transaction,
                    articleId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return articleIds.Count;
    }

    public async Task<bool> VerifyFts5Async(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM pragma_module_list WHERE name = 'fts5');";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static QuerySql BuildSql(ArticleQuery query)
    {
        var parameters = new List<QueryParameter>();
        var conditions = new List<string>();
        var searchText = query.SearchText?.Trim();
        var ftsQuery = FtsQueryBuilder.Build(searchText, query.SearchScope);
        var useFts = query.View != ArticleSmartView.Trash &&
                     !string.IsNullOrEmpty(searchText) &&
                     !FtsQueryBuilder.ShouldUseTitleFallback(searchText) &&
                     ftsQuery is not null;

        var withClause = string.Empty;
        if (query.TopicId is not null && query.IncludeDescendantTopics)
        {
            withClause = """
                WITH RECURSIVE TopicScope(Id) AS (
                    SELECT $topicId
                    UNION ALL
                    SELECT topic.Id
                    FROM Topics topic
                    JOIN TopicScope parent ON topic.ParentId = parent.Id
                    WHERE topic.DeletedAtUtc IS NULL
                )
                """;
        }

        var fromClause = useFts
            ? "FROM SearchDocumentsFts JOIN Articles a ON a.Id = SearchDocumentsFts.rowid"
            : "FROM Articles a";

        AddSearchFilter(query, conditions, parameters, searchText, ftsQuery, useFts);
        AddViewFilter(query, conditions);
        AddTopicFilter(query, conditions, parameters);

        AddLanguageFilter(conditions, parameters, query.LanguageCodes);
        AddInFilter(conditions, parameters, "a.ArticleType", "type", query.ArticleTypes);
        AddInFilter(conditions, parameters, "a.Status", "status", query.Statuses);

        AddOptionalFilters(query, conditions, parameters);

        var rankExpression = useFts
            ? "bm25(SearchDocumentsFts, 0.0, 12.0, 5.0, 3.0, 1.0, 0.8, 0.5)"
            : "NULL";
        var snippetExpression = useFts
            ? "snippet(SearchDocumentsFts, -1, '[', ']', ' … ', 24)"
            : "NULL";
        var orderBy = CreateOrderBy(query, useFts, rankExpression);
        return new QuerySql(
            withClause,
            fromClause,
            string.Join(" AND ", conditions),
            snippetExpression,
            rankExpression,
            orderBy,
            parameters);
    }

    private static void AddSearchFilter(
        ArticleQuery query,
        List<string> conditions,
        List<QueryParameter> parameters,
        string? searchText,
        string? ftsQuery,
        bool useFts)
    {
        if (useFts)
        {
            conditions.Add("SearchDocumentsFts MATCH $ftsQuery");
            parameters.Add(new("$ftsQuery", ftsQuery!));
            return;
        }

        if (string.IsNullOrEmpty(searchText))
        {
            return;
        }

        var allText = query.SearchScope == ArticleSearchScope.AllText;
        var fallbackParameter = allText ? "$allTextFallback" : "$titleFallback";
        conditions.Add(allText
            ? """
              (
                  a.Title LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                  OR a.Subtitle LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                  OR a.Summary LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                  OR a.Notes LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                  OR EXISTS(
                      SELECT 1
                      FROM ArticleSections sectionFallback
                      WHERE sectionFallback.ArticleId = a.Id
                        AND (
                            sectionFallback.Heading LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                            OR sectionFallback.Body LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                        )
                  )
                  OR EXISTS(
                      SELECT 1
                      FROM ArticleSources sourceFallback
                      WHERE sourceFallback.ArticleId = a.Id
                        AND (
                            sourceFallback.Title LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                            OR sourceFallback.AttributionText LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                            OR sourceFallback.Notes LIKE $allTextFallback ESCAPE '\' COLLATE NOCASE
                        )
                  )
              )
              """
            : "a.Title LIKE $titleFallback ESCAPE '\\' COLLATE NOCASE");
        parameters.Add(new(fallbackParameter, "%" + EscapeLike(searchText) + "%"));
    }

    private static void AddViewFilter(ArticleQuery query, List<string> conditions)
    {
        if (query.View == ArticleSmartView.Trash)
        {
            conditions.Add("a.DeletedAtUtc IS NOT NULL");
            return;
        }

        conditions.Add("a.DeletedAtUtc IS NULL");
        if (query.View == ArticleSmartView.Favorites)
        {
            conditions.Add("a.IsFavorite = 1");
        }
        else if (query.View == ArticleSmartView.Uncategorized)
        {
            conditions.Add("NOT EXISTS(SELECT 1 FROM ArticleTopics uncategorized WHERE uncategorized.ArticleId = a.Id)");
        }
    }

    private static void AddTopicFilter(
        ArticleQuery query,
        List<string> conditions,
        List<QueryParameter> parameters)
    {
        if (query.TopicId is null)
        {
            return;
        }

        conditions.Add(query.IncludeDescendantTopics
            ? "EXISTS(SELECT 1 FROM ArticleTopics assignment WHERE assignment.ArticleId = a.Id AND assignment.TopicId IN (SELECT Id FROM TopicScope))"
            : "EXISTS(SELECT 1 FROM ArticleTopics assignment WHERE assignment.ArticleId = a.Id AND assignment.TopicId = $topicId)");
        parameters.Add(new("$topicId", query.TopicId.Value));
    }

    private static void AddOptionalFilters(
        ArticleQuery query,
        List<string> conditions,
        List<QueryParameter> parameters)
    {
        if (query.IsFavorite is not null)
        {
            conditions.Add("a.IsFavorite = $isFavorite");
            parameters.Add(new("$isFavorite", query.IsFavorite.Value));
        }

        if (query.HasSources is not null)
        {
            conditions.Add(query.HasSources.Value
                ? "EXISTS(SELECT 1 FROM ArticleSources sourceFilter WHERE sourceFilter.ArticleId = a.Id)"
                : "NOT EXISTS(SELECT 1 FROM ArticleSources sourceFilter WHERE sourceFilter.ArticleId = a.Id)");
        }

        AddRangeFilter(conditions, parameters, "a.WordCount", "$minimumWordCount", ">=", query.MinimumWordCount);
        AddRangeFilter(conditions, parameters, "a.WordCount", "$maximumWordCount", "<=", query.MaximumWordCount);
        AddDateFilter(conditions, parameters, "a.CreatedAtUtc", "$createdFromUtc", ">=", query.CreatedFromUtc);
        AddDateFilter(conditions, parameters, "a.CreatedAtUtc", "$createdToUtc", "<=", query.CreatedToUtc);
        AddDateFilter(conditions, parameters, "a.UpdatedAtUtc", "$updatedFromUtc", ">=", query.UpdatedFromUtc);
        AddDateFilter(conditions, parameters, "a.UpdatedAtUtc", "$updatedToUtc", "<=", query.UpdatedToUtc);

        if (query.IsArchived is not null)
        {
            conditions.Add(query.IsArchived.Value ? "a.Status = $archived" : "a.Status <> $archived");
            parameters.Add(new("$archived", ArticleStatuses.Archived));
        }

        if (query.IsSample is not null)
        {
            conditions.Add("a.IsSample = $isSample");
            parameters.Add(new("$isSample", query.IsSample.Value));
        }
    }

    private static string CreateOrderBy(ArticleQuery query, bool useFts, string rankExpression)
    {
        if (query.SortField == ArticleSortField.Relevance && useFts)
        {
            return $"{rankExpression} ASC, a.UpdatedAtUtc DESC, a.Id DESC";
        }

        var column = query.SortField switch
        {
            ArticleSortField.Title => "a.Title COLLATE NOCASE",
            ArticleSortField.Language => "a.LanguageCode COLLATE NOCASE",
            ArticleSortField.WordCount => "a.WordCount",
            ArticleSortField.Status => "a.Status COLLATE NOCASE",
            ArticleSortField.Created => "a.CreatedAtUtc",
            ArticleSortField.Updated or ArticleSortField.Relevance => "a.UpdatedAtUtc",
            _ => throw new ArgumentOutOfRangeException(nameof(query), "Unsupported article sort field.")
        };
        var direction = query.SortDirection == SortDirection.Descending ? "DESC" : "ASC";
        if (query.SortField == ArticleSortField.Relevance)
        {
            direction = "DESC";
        }

        return $"{column} {direction}, a.Id {direction}";
    }

    private static void AddInFilter(
        List<string> conditions,
        List<QueryParameter> parameters,
        string column,
        string prefix,
        IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        var names = new List<string>();
        var index = 0;
        foreach (var value in values.Distinct(StringComparer.Ordinal))
        {
            var name = $"${prefix}{index++}";
            names.Add(name);
            parameters.Add(new(name, value));
        }

        conditions.Add($"{column} IN ({string.Join(", ", names)})");
    }

    private static void AddLanguageFilter(
        List<string> conditions,
        List<QueryParameter> parameters,
        IReadOnlyCollection<string>? languageCodes)
    {
        if (languageCodes is null || languageCodes.Count == 0)
        {
            return;
        }

        var matches = new List<string>();
        var index = 0;
        foreach (var languageCode in languageCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exactName = $"$language{index}";
            parameters.Add(new(exactName, languageCode));
            if (languageCode.Contains('-', StringComparison.Ordinal))
            {
                matches.Add($"a.LanguageCode = {exactName} COLLATE NOCASE");
            }
            else
            {
                var familyName = $"$languageFamily{index}";
                parameters.Add(new(familyName, EscapeLike(languageCode) + "-%"));
                matches.Add($"(a.LanguageCode = {exactName} COLLATE NOCASE OR a.LanguageCode LIKE {familyName} ESCAPE '\\' COLLATE NOCASE)");
            }

            index++;
        }

        conditions.Add($"({string.Join(" OR ", matches)})");
    }

    private static void AddRangeFilter<T>(
        List<string> conditions,
        List<QueryParameter> parameters,
        string column,
        string name,
        string comparison,
        T? value) where T : struct
    {
        if (value is null)
        {
            return;
        }

        conditions.Add($"{column} {comparison} {name}");
        parameters.Add(new(name, value.Value));
    }

    private static void AddDateFilter(
        List<string> conditions,
        List<QueryParameter> parameters,
        string column,
        string name,
        string comparison,
        DateTimeOffset? value)
    {
        if (value is null)
        {
            return;
        }

        conditions.Add($"{column} {comparison} {name}");
        parameters.Add(new(name, DatabaseValue.Date(value.Value)));
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<QueryParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record QuerySql(
        string WithClause,
        string FromClause,
        string WhereClause,
        string SnippetExpression,
        string RankExpression,
        string OrderByClause,
        IReadOnlyList<QueryParameter> Parameters);

    private sealed record QueryParameter(string Name, object Value);
}
