using Microsoft.Data.Sqlite;
using Pedia.Core.Data;

namespace Pedia.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task OpenConnectionEnablesRequiredSQLiteBehavior()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Pedia.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);

        try
        {
            var options = new DatabaseOptions(
                Path.Combine(directoryPath, "settings.db"),
                BusyTimeoutMilliseconds: 2_500);
            var factory = new SqliteConnectionFactory(options);

            await using var connection = await factory.OpenConnectionAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1L, await ScalarInt64Async(connection, "PRAGMA foreign_keys;"));
            Assert.Equal("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
            Assert.Equal(2_500L, await ScalarInt64Async(connection, "PRAGMA busy_timeout;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAppliesExplicitCurrentSchemaAndProvidesFts5()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            MigrationRunner.CurrentSchemaVersion,
            await ScalarInt64Async(connection, "SELECT SchemaVersion FROM SchemaInfo WHERE Id = 1;"));

        var expectedTables = new[]
        {
            "ArticleSections",
            "ArticleSources",
            "ArticleTopics",
            "Articles",
            "ImportRuns",
            "SchemaInfo",
            "SearchDocuments",
            "SearchDocumentsFts",
            "Topics"
        };

        await using var tablesCommand = connection.CreateCommand();
        tablesCommand.CommandText =
            "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        await using var reader = await tablesCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var actualTables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var name = reader.GetString(0);
            if (!name.StartsWith("SearchDocumentsFts_", StringComparison.Ordinal))
            {
                actualTables.Add(name);
            }
        }

        Assert.Equal(expectedTables, actualTables);

        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO SearchDocumentsFts(rowid, ArticleId, Title, Subtitle, Summary, SectionText, SourceText, Notes) " +
            "VALUES (9001, 9001, 'Shanghai', '', '', 'River port history', '', '');";
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var search = connection.CreateCommand();
        search.CommandText = "SELECT ArticleId FROM SearchDocumentsFts WHERE SearchDocumentsFts MATCH 'river';";
        Assert.Equal(9001L, await search.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeIsIdempotentAndDoesNotReseedAnExistingSchema()
    {
        await using var database = await TestDatabase.CreateAsync(seedSamples: true);
        await using (var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM Articles;";
            await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var secondResult = await database.Initializer.InitializeAsync(seedSamples: true, cancellationToken: TestContext.Current.CancellationToken);

        await using var verification = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken);
        Assert.False(secondResult.IsNewDatabase);
        Assert.Equal(0L, await ScalarInt64Async(verification, "SELECT COUNT(*) FROM Articles;"));
    }

    [Fact]
    public async Task DatabaseInformationReportsLiveSchemaIndexAndLastCompletedImport()
    {
        await using var database = await TestDatabase.CreateAsync();
        var completedAt = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        await using (var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ImportRuns(
                    ImportKind, SourceDescription, StartedAtUtc, CompletedAtUtc, Status,
                    ImportedCount, SkippedCount, ErrorCount)
                VALUES ('LocalFiles/Skip', '1 local file', $startedAt, $completedAt, 'Completed', 1, 0, 0);
                """;
            command.Parameters.AddWithValue("$startedAt", completedAt.AddSeconds(-2).ToString("O"));
            command.Parameters.AddWithValue("$completedAt", completedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var information = await new DatabaseInformationService(database.Connections).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(database.Options.DatabasePath, information.DatabasePath);
        Assert.True(information.DatabaseSizeBytes > 0);
        Assert.Equal(MigrationRunner.CurrentSchemaVersion, information.SchemaVersion);
        Assert.Equal(completedAt, information.LastCompletedImportAtUtc);
        Assert.True(information.IsSearchIndexReady);
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
