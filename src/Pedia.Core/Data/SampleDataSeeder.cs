using Microsoft.Data.Sqlite;
using Pedia.Core.Models;
using Pedia.Core.Repositories;
using Pedia.Core.Search;

namespace Pedia.Core.Data;

internal sealed class SampleDataSeeder
{
    private readonly SqliteConnectionFactory _connections;

    public SampleDataSeeder(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (await HasContentAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var history = await InsertTopicAsync(connection, transaction, "History", null, 0, cancellationToken).ConfigureAwait(false);
        var worldHistory = await InsertTopicAsync(connection, transaction, "World history", history, 0, cancellationToken).ConfigureAwait(false);
        var asia = await InsertTopicAsync(connection, transaction, "Asia", worldHistory, 0, cancellationToken).ConfigureAwait(false);
        var china = await InsertTopicAsync(connection, transaction, "China", asia, 0, cancellationToken).ConfigureAwait(false);
        var imperialChina = await InsertTopicAsync(connection, transaction, "Imperial China", china, 0, cancellationToken).ConfigureAwait(false);
        var modernChina = await InsertTopicAsync(connection, transaction, "Modern China", china, 1, cancellationToken).ConfigureAwait(false);
        var historyOfShanghai = await InsertTopicAsync(connection, transaction, "History of Shanghai", china, 2, cancellationToken).ConfigureAwait(false);
        var geography = await InsertTopicAsync(connection, transaction, "Geography", null, 1, cancellationToken).ConfigureAwait(false);
        var science = await InsertTopicAsync(connection, transaction, "Science", null, 2, cancellationToken).ConfigureAwait(false);
        var technology = await InsertTopicAsync(connection, transaction, "Technology", null, 3, cancellationToken).ConfigureAwait(false);
        var philosophy = await InsertTopicAsync(connection, transaction, "Philosophy", null, 4, cancellationToken).ConfigureAwait(false);
        var culture = await InsertTopicAsync(connection, transaction, "Culture", null, 5, cancellationToken).ConfigureAwait(false);

        var samples = new[]
        {
            new SampleArticle(
                "History of Shanghai",
                "A port city shaped by waterways, trade, conflict, and rapid urban change.",
                ArticleTypes.Timeline,
                ArticleStatuses.Ready,
                [
                    new(null, 1, "Shanghai developed where the Huangpu River meets the broad Yangtze delta. Fishing villages, market towns, and river traffic linked the area long before it became an international port."),
                    new("Treaty-port era", 2, "Foreign concessions expanded after the Opium War. Chinese merchants, migrant workers, and overseas firms built a dense commercial city whose institutions often overlapped."),
                    new("Twentieth century", 2, "War, revolution, and reconstruction repeatedly changed Shanghai. Since the 1990s, development in Pudong has added a new skyline while older neighborhoods preserve traces of earlier periods.")
                ],
                [historyOfShanghai, china],
                new SampleSource("Book", "Shanghai: a local history reading list")),
            new SampleArticle(
                "Shanghai",
                "A major Chinese municipality centered on the Huangpu River and the Yangtze delta.",
                ArticleTypes.Place,
                ArticleStatuses.Ready,
                [
                    new(null, 1, "Shanghai is a coastal municipality in eastern China. Its urban core grew on both banks of the Huangpu River, with the historic center to the west and Pudong to the east."),
                    new("Urban character", 2, "The city combines port infrastructure, financial districts, residential lanes, industrial heritage, universities, parks, and extensive public transport.")
                ],
                [geography, historyOfShanghai]),
            new SampleArticle(
                "Ming dynasty",
                "The dynasty that governed China from 1368 to 1644.",
                ArticleTypes.General,
                ArticleStatuses.Ready,
                [new(null, 1, "During the Ming period, the lower Yangtze region supported intensive agriculture, textile production, publishing, and far-reaching commerce. Shanghai's county town participated in these regional networks." )],
                [imperialChina]),
            new SampleArticle(
                "Qing dynasty",
                "The final imperial dynasty of China, ruling from 1644 to 1912.",
                ArticleTypes.General,
                ArticleStatuses.NeedsReview,
                [new(null, 1, "Qing rule encompassed major demographic and economic growth as well as severe nineteenth-century crises. Shanghai changed from a regional port into an international commercial center during this era." )],
                [imperialChina, historyOfShanghai]),
            new SampleArticle(
                "Opium War",
                "A nineteenth-century conflict that altered China's foreign relations and port system.",
                ArticleTypes.Event,
                ArticleStatuses.Ready,
                [new(null, 1, "The First Opium War ended in 1842. The resulting treaty arrangements opened Shanghai and several other ports to expanding foreign trade and residence." )],
                [imperialChina, historyOfShanghai]),
            new SampleArticle(
                "Taiping Rebellion",
                "A vast civil war that transformed mid-nineteenth-century China.",
                ArticleTypes.Event,
                ArticleStatuses.NeedsReview,
                [new(null, 1, "Fighting and displacement during the Taiping Rebellion sent many people toward Shanghai. The city's population and economy grew even as conflict approached the surrounding region." )],
                [imperialChina, historyOfShanghai]),
            new SampleArticle(
                "Shanghai International Settlement",
                "A foreign-administered urban district that existed until the Second World War.",
                ArticleTypes.Place,
                ArticleStatuses.Ready,
                [
                    new(null, 1, "The International Settlement combined earlier British and American areas. It was governed through institutions that served foreign ratepayers while most residents were Chinese."),
                    new("Legacy", 2, "Street plans, public buildings, housing, and commercial districts from the settlement period remain part of Shanghai's urban fabric.")
                ],
                [historyOfShanghai],
                new SampleSource("Manual", "Pedia sample research notes")),
            new SampleArticle(
                "Treaty ports",
                "Ports opened to foreign residence and commerce under nineteenth-century treaties.",
                ArticleTypes.Concept,
                ArticleStatuses.Draft,
                [new(null, 1, "Treaty ports were places where new legal, commercial, and municipal arrangements emerged. Their histories also include Chinese entrepreneurship, labor, migration, and cultural exchange." )],
                [imperialChina, historyOfShanghai]),
            new SampleArticle(
                "First Sino-Japanese War",
                "The 1894 to 1895 war between Qing China and Japan.",
                ArticleTypes.Event,
                ArticleStatuses.Ready,
                [new(null, 1, "The conflict demonstrated changing power in East Asia. News, finance, shipping, and public debate connected wartime events to Shanghai's growing commercial world." )],
                [imperialChina]),
            new SampleArticle(
                "Republic of China",
                "The republican state established after the 1911 Revolution.",
                ArticleTypes.General,
                ArticleStatuses.NeedsReview,
                [new(null, 1, "Shanghai was a major center of publishing, finance, industry, political organizing, and popular culture during the republican decades. Authority remained divided and often contested." )],
                [modernChina, historyOfShanghai]),
            new SampleArticle(
                "Battle of Shanghai",
                "A major 1937 battle fought during the Second Sino-Japanese War.",
                ArticleTypes.Event,
                ArticleStatuses.Ready,
                [new(null, 1, "Intense fighting in and around Shanghai lasted for months in 1937. The battle caused extensive destruction, military losses, and mass civilian displacement." )],
                [modernChina, historyOfShanghai]),
            new SampleArticle(
                "Shanghai during World War II",
                "Occupation, refuge, and survival in Shanghai during the global conflict.",
                ArticleTypes.Timeline,
                ArticleStatuses.NeedsReview,
                [
                    new(null, 1, "Wartime Shanghai contained occupied districts, foreign-administered areas, crowded refugee neighborhoods, and tightly controlled communities."),
                    new("After 1941", 2, "Following the wider Pacific war, Japanese forces took control of the remaining foreign concessions. Residents faced scarcity, surveillance, and profound uncertainty.")
                ],
                [modernChina, historyOfShanghai]),
            new SampleArticle(
                "Shanghai Municipal Council",
                "The administrative body of the Shanghai International Settlement.",
                ArticleTypes.Organization,
                ArticleStatuses.Draft,
                [new(null, 1, "The council managed roads, policing, public health, utilities, and other municipal services. Its restricted political structure did not represent the settlement's large Chinese majority." )],
                [historyOfShanghai]),
            new SampleArticle(
                "Shanghai Stock Exchange",
                "A securities exchange in Shanghai with roots in the city's long financial history.",
                ArticleTypes.Organization,
                ArticleStatuses.Ready,
                [new(null, 1, "Modern securities trading resumed in Shanghai in 1990. The exchange became an important institution in China's evolving capital markets and financial technology infrastructure." )],
                [modernChina, technology, historyOfShanghai]),
            new SampleArticle(
                "Pudong",
                "The district east of the Huangpu River that became a center of modern development.",
                ArticleTypes.Place,
                ArticleStatuses.Ready,
                [
                    new(null, 1, "Pudong includes riverfront financial towers, residential districts, industrial zones, parks, an international airport, and extensive transport links."),
                    new("Development", 2, "Large-scale development accelerated after 1990. The district's transformation became a prominent symbol of Shanghai's renewed global role.")
                ],
                [geography, modernChina, historyOfShanghai],
                new SampleSource("Manual", "Pedia sample geography notes"))
        };

        foreach (var sample in samples)
        {
            await InsertArticleAsync(connection, transaction, sample, cancellationToken).ConfigureAwait(false);
        }

        _ = science;
        _ = philosophy;
        _ = culture;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasContentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Topics UNION ALL SELECT 1 FROM Articles);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<long> InsertTopicAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        long? parentId,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Topics(
                ParentId, Name, NameKey, Description, SortOrder, IsSample, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($parentId, $name, $nameKey, NULL, $sortOrder, 1, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$parentId", (object?)parentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$nameKey", TopicRepository.CreateNameKey(name));
        command.Parameters.AddWithValue("$sortOrder", sortOrder);
        command.Parameters.AddWithValue("$now", DatabaseValue.Date(DateTimeOffset.UtcNow));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task InsertArticleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SampleArticle sample,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var article = connection.CreateCommand();
        article.Transaction = transaction;
        article.CommandText = """
            INSERT INTO Articles(
                Title, Summary, LanguageCode, ArticleType, Status, IsFavorite,
                WordCount, IsSample, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                $title, $summary, 'en', $articleType, $status, 0,
                $wordCount, 1, $now, $now);
            SELECT last_insert_rowid();
            """;
        article.Parameters.AddWithValue("$title", sample.Title);
        article.Parameters.AddWithValue("$summary", sample.Summary);
        article.Parameters.AddWithValue("$articleType", sample.ArticleType);
        article.Parameters.AddWithValue("$status", sample.Status);
        article.Parameters.AddWithValue("$wordCount", WordCounter.Count(sample.Sections.Select(section => section.Body)));
        article.Parameters.AddWithValue("$now", DatabaseValue.Date(now));
        var articleId = Convert.ToInt64(await article.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        for (var index = 0; index < sample.Sections.Count; index++)
        {
            await using var section = connection.CreateCommand();
            section.Transaction = transaction;
            section.CommandText = """
                INSERT INTO ArticleSections(ArticleId, Heading, HeadingLevel, Body, SortOrder)
                VALUES ($articleId, $heading, $headingLevel, $body, $sortOrder);
                """;
            section.Parameters.AddWithValue("$articleId", articleId);
            section.Parameters.AddWithValue("$heading", (object?)sample.Sections[index].Heading ?? DBNull.Value);
            section.Parameters.AddWithValue("$headingLevel", sample.Sections[index].HeadingLevel);
            section.Parameters.AddWithValue("$body", sample.Sections[index].Body);
            section.Parameters.AddWithValue("$sortOrder", index);
            await section.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < sample.TopicIds.Count; index++)
        {
            await using var assignment = connection.CreateCommand();
            assignment.Transaction = transaction;
            assignment.CommandText = """
                INSERT INTO ArticleTopics(ArticleId, TopicId, IsPrimary, CreatedAtUtc)
                VALUES ($articleId, $topicId, $isPrimary, $createdAtUtc);
                """;
            assignment.Parameters.AddWithValue("$articleId", articleId);
            assignment.Parameters.AddWithValue("$topicId", sample.TopicIds[index]);
            assignment.Parameters.AddWithValue("$isPrimary", index == 0);
            assignment.Parameters.AddWithValue("$createdAtUtc", DatabaseValue.Date(now));
            await assignment.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (sample.Source is not null)
        {
            await using var source = connection.CreateCommand();
            source.Transaction = transaction;
            source.CommandText = """
                INSERT INTO ArticleSources(ArticleId, SourceType, Title, SortOrder)
                VALUES ($articleId, $sourceType, $title, 0);
                """;
            source.Parameters.AddWithValue("$articleId", articleId);
            source.Parameters.AddWithValue("$sourceType", sample.Source.SourceType);
            source.Parameters.AddWithValue("$title", sample.Source.Title);
            await source.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await SearchDocumentStore.ReindexArticleAsync(connection, transaction, articleId, cancellationToken).ConfigureAwait(false);
    }

    private sealed record SampleArticle(
        string Title,
        string Summary,
        string ArticleType,
        string Status,
        IReadOnlyList<ArticleSectionDraft> Sections,
        IReadOnlyList<long> TopicIds,
        SampleSource? Source = null);

    private sealed record SampleSource(string SourceType, string Title);
}
