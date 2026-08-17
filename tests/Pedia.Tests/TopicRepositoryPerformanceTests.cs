using System.Diagnostics;
using Pedia.Core.Repositories;

namespace Pedia.Tests;

public sealed class TopicRepositoryPerformanceTests
{
    [Fact]
    public async Task Ten_thousand_topic_tree_with_assignments_loads_within_budget()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                BEGIN;
                WITH RECURSIVE Sequence(Value) AS (
                    VALUES(1)
                    UNION ALL
                    SELECT Value + 1 FROM Sequence WHERE Value < 10000
                )
                INSERT INTO Topics(
                    Id, ParentId, Name, NameKey, Description, SortOrder, IsSample,
                    CreatedAtUtc, UpdatedAtUtc)
                SELECT Value,
                       CASE WHEN Value = 1 THEN NULL ELSE Value - 1 END,
                       'Topic ' || Value,
                       'TOPIC ' || Value,
                       NULL,
                       0,
                       0,
                       '2026-08-12T00:00:00.0000000Z',
                       '2026-08-12T00:00:00.0000000Z'
                FROM Sequence;

                WITH RECURSIVE Sequence(Value) AS (
                    VALUES(1)
                    UNION ALL
                    SELECT Value + 1 FROM Sequence WHERE Value < 10000
                )
                INSERT INTO Articles(
                    Id, Title, LanguageCode, ArticleType, Status, IsFavorite,
                    WordCount, IsSample, CreatedAtUtc, UpdatedAtUtc)
                SELECT Value,
                       'Article ' || Value,
                       'en',
                       'General',
                       'Draft',
                       0,
                       0,
                       0,
                       '2026-08-12T00:00:00.0000000Z',
                       '2026-08-12T00:00:00.0000000Z'
                FROM Sequence;

                WITH RECURSIVE Sequence(Value) AS (
                    VALUES(1)
                    UNION ALL
                    SELECT Value + 1 FROM Sequence WHERE Value < 10000
                )
                INSERT INTO ArticleTopics(ArticleId, TopicId, IsPrimary, CreatedAtUtc)
                SELECT Value, Value, 1, '2026-08-12T00:00:00.0000000Z'
                FROM Sequence;
                COMMIT;
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var topics = new TopicRepository(database.Connections);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopwatch = Stopwatch.StartNew();

        var tree = await topics.GetTreeAsync(cancellation.Token);

        stopwatch.Stop();
        Assert.Equal(10_000, tree.Count);
        Assert.All(tree, topic => Assert.Equal(1, topic.DirectArticleCount));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"The 10,000-topic tree query took {stopwatch.Elapsed}.");
    }
}
