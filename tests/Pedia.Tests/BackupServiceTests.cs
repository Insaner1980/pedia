using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Pedia.Core.Backup;
using Pedia.Core.Data;
using Pedia.Core.Utilities;

namespace Pedia.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task Create_uses_online_SQLite_backup_and_writes_versioned_manifest()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "pedia.db");
        await CreateDatabaseAsync(databasePath, schemaVersion: 7, "Original");
        await using var liveConnection = await OpenAsync(databasePath);
        await ExecuteAsync(liveConnection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(liveConnection, "INSERT INTO Articles(Title) VALUES ('Committed while open');");
        var service = CreateService(databasePath);

        var result = await service.CreateAsync(Path.Combine(directory.Path, "daily.pediabackup"), TestContext.Current.CancellationToken);

        Assert.Equal("daily.pediabackup", Path.GetFileName(result.Path));
        using var archive = ZipFile.OpenRead(result.Path);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("database.sqlite"));
        var validation = await service.ValidateAsync(result.Path, TestContext.Current.CancellationToken);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(7, validation.Manifest?.DatabaseSchemaVersion);
        Assert.Equal("pedia-backup", validation.Manifest?.Format);
        Assert.Equal(2, validation.Manifest?.Version);
        Assert.False(string.IsNullOrWhiteSpace(validation.Manifest?.PediaVersion));
        Assert.Equal(2, validation.Manifest?.ArticleCount);
        Assert.Equal(0, validation.Manifest?.TopicCount);
    }

    [Fact]
    public async Task Restore_creates_safety_backup_and_replaces_database_with_valid_backup()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "pedia.db");
        await CreateDatabaseAsync(databasePath, schemaVersion: 7, "Before backup");
        var service = CreateService(databasePath);
        var backup = await service.CreateAsync(Path.Combine(directory.Path, "restore-point.pediabackup"), TestContext.Current.CancellationToken);
        await SetOnlyTitleAsync(databasePath, "Current value");

        var restored = await service.RestoreAsync(backup.Path, TestContext.Current.CancellationToken);

        Assert.Equal("Before backup", await ReadFirstTitleAsync(databasePath));
        Assert.True(File.Exists(restored.SafetyBackupPath));
        Assert.True((await service.ValidateAsync(restored.SafetyBackupPath, TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal(7, restored.DatabaseSchemaVersion);
    }

    [Fact]
    public async Task Restore_succeeds_while_an_application_connection_is_open()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "pedia.db");
        await CreateDatabaseAsync(databasePath, schemaVersion: 7, "Before backup");
        var service = CreateService(databasePath);
        var backup = await service.CreateAsync(Path.Combine(directory.Path, "restore-point.pediabackup"), TestContext.Current.CancellationToken);
        var factory = new SqliteConnectionFactory(new DatabaseOptions(databasePath));

        try
        {
            await using var activeConnection = await factory.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await ExecuteAsync(activeConnection, "UPDATE Articles SET Title = 'Current value';");

            await service.RestoreAsync(backup.Path, TestContext.Current.CancellationToken);

            await using var command = activeConnection.CreateCommand();
            command.CommandText = "SELECT Title FROM Articles ORDER BY Id LIMIT 1;";
            Assert.Equal("Before backup", await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Invalid_backup_is_rejected_before_current_database_is_changed()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "pedia.db");
        await CreateDatabaseAsync(databasePath, schemaVersion: 7, "Current value");
        var invalidPath = Path.Combine(directory.Path, "invalid.pediabackup");
        await File.WriteAllTextAsync(invalidPath, "not a zip archive", TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken);
        var service = CreateService(databasePath);

        var validation = await service.ValidateAsync(invalidPath, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(invalidPath, TestContext.Current.CancellationToken));

        Assert.False(validation.IsValid);
        Assert.Equal(before, await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken));
        Assert.Equal("Current value", await ReadFirstTitleAsync(databasePath));
    }

    [Fact]
    public async Task Restore_rejects_a_different_schema_and_leaves_current_database_unchanged()
    {
        using var directory = new TemporaryDirectory();
        var currentPath = Path.Combine(directory.Path, "current.db");
        var otherPath = Path.Combine(directory.Path, "other.db");
        await CreateDatabaseAsync(currentPath, schemaVersion: 7, "Current");
        await CreateDatabaseAsync(otherPath, schemaVersion: 8, "Other");
        var otherService = CreateService(otherPath);
        var backup = await otherService.CreateAsync(Path.Combine(directory.Path, "other.pediabackup"), TestContext.Current.CancellationToken);
        var service = CreateService(currentPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreAsync(backup.Path, TestContext.Current.CancellationToken));

        Assert.Equal("Current", await ReadFirstTitleAsync(currentPath));
    }

    [Fact]
    public async Task Pre_cancelled_operations_do_not_create_or_change_files()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "pedia.db");
        await CreateDatabaseAsync(databasePath, schemaVersion: 7, "Current");
        var backupPath = Path.Combine(directory.Path, "cancelled.pediabackup");
        var service = CreateService(databasePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(backupPath, cancellation.Token));

        Assert.False(File.Exists(backupPath));
        Assert.Equal("Current", await ReadFirstTitleAsync(databasePath));
    }

    [Fact]
    public async Task Pedia_schema_version_is_read_from_SchemaInfo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new BackupService(database.Options.DatabasePath, new FixedClock(DateTimeOffset.UnixEpoch));
        var backupPath = Path.Combine(database.DirectoryPath, "schema.pediabackup");

        var result = await service.CreateAsync(backupPath, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Manifest.DatabaseSchemaVersion);
        Assert.True((await service.ValidateAsync(backupPath, TestContext.Current.CancellationToken)).IsValid);
    }

    private static BackupService CreateService(string databasePath) =>
        new(databasePath, new FixedClock(new DateTimeOffset(2026, 8, 12, 12, 30, 0, TimeSpan.Zero)));

    private static async Task CreateDatabaseAsync(string path, int schemaVersion, string title)
    {
        await using var connection = await OpenAsync(path);
        await ExecuteAsync(connection, $"""
            PRAGMA user_version={schemaVersion};
            CREATE TABLE Articles(Id INTEGER PRIMARY KEY, Title TEXT NOT NULL);
            INSERT INTO Articles(Title) VALUES ('{title.Replace("'", "''", StringComparison.Ordinal)}');
            """);
    }

    private static async Task SetOnlyTitleAsync(string path, string title)
    {
        await using var connection = await OpenAsync(path);
        await ExecuteAsync(connection, "DELETE FROM Articles;");
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Articles(Title) VALUES ($title);";
        command.Parameters.AddWithValue("$title", title);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadFirstTitleAsync(string path)
    {
        await using var connection = await OpenAsync(path, readOnly: true);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title FROM Articles ORDER BY Id LIMIT 1;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<SqliteConnection> OpenAsync(string path, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
