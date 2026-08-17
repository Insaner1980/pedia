using Microsoft.Data.Sqlite;

namespace Pedia.Core.Data;

public sealed class MigrationRunner
{
    public const int CurrentSchemaVersion = 1;

    private static readonly IReadOnlyList<Migration> Migrations =
    [
        new(1, """
            CREATE TABLE SchemaInfo (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                SchemaVersion INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE Topics (
                Id INTEGER NOT NULL PRIMARY KEY,
                ParentId INTEGER NULL REFERENCES Topics(Id) ON DELETE RESTRICT,
                Name TEXT NOT NULL,
                NameKey TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsSample INTEGER NOT NULL DEFAULT 0 CHECK (IsSample IN (0, 1)),
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                DeletedAtUtc TEXT NULL
            );

            CREATE TABLE Articles (
                Id INTEGER NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Subtitle TEXT NULL,
                Summary TEXT NULL,
                LanguageCode TEXT NOT NULL,
                ArticleType TEXT NOT NULL,
                Status TEXT NOT NULL,
                Notes TEXT NULL,
                IsFavorite INTEGER NOT NULL DEFAULT 0 CHECK (IsFavorite IN (0, 1)),
                WordCount INTEGER NOT NULL DEFAULT 0,
                IsSample INTEGER NOT NULL DEFAULT 0 CHECK (IsSample IN (0, 1)),
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                DeletedAtUtc TEXT NULL
            );

            CREATE TABLE ArticleSections (
                Id INTEGER NOT NULL PRIMARY KEY,
                ArticleId INTEGER NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                Heading TEXT NULL,
                HeadingLevel INTEGER NOT NULL CHECK (HeadingLevel BETWEEN 1 AND 3),
                Body TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE ArticleTopics (
                ArticleId INTEGER NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                TopicId INTEGER NOT NULL REFERENCES Topics(Id) ON DELETE RESTRICT,
                IsPrimary INTEGER NOT NULL DEFAULT 0 CHECK (IsPrimary IN (0, 1)),
                CreatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (ArticleId, TopicId)
            );

            CREATE TABLE ArticleSources (
                Id INTEGER NOT NULL PRIMARY KEY,
                ArticleId INTEGER NOT NULL REFERENCES Articles(Id) ON DELETE CASCADE,
                SourceType TEXT NOT NULL,
                Title TEXT NULL,
                Url TEXT NULL,
                ExternalPageId TEXT NULL,
                ExternalRevisionId TEXT NULL,
                LicenseName TEXT NULL,
                AttributionText TEXT NULL,
                RetrievedAtUtc TEXT NULL,
                LastCheckedAtUtc TEXT NULL,
                Notes TEXT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE SearchDocuments (
                ArticleId INTEGER NOT NULL PRIMARY KEY REFERENCES Articles(Id) ON DELETE CASCADE,
                Title TEXT NOT NULL,
                Subtitle TEXT NOT NULL,
                Summary TEXT NOT NULL,
                SectionText TEXT NOT NULL,
                SourceText TEXT NOT NULL,
                Notes TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE SearchDocumentsFts USING fts5(
                ArticleId UNINDEXED,
                Title,
                Subtitle,
                Summary,
                SectionText,
                SourceText,
                Notes,
                tokenize='unicode61 remove_diacritics 2'
            );

            CREATE TABLE ImportRuns (
                Id INTEGER NOT NULL PRIMARY KEY,
                ImportKind TEXT NOT NULL,
                SourceDescription TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL,
                Status TEXT NOT NULL,
                ImportedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0,
                ErrorCount INTEGER NOT NULL DEFAULT 0,
                ErrorSummary TEXT NULL
            );

            CREATE UNIQUE INDEX UX_Topics_ActiveSiblingName
                ON Topics(IFNULL(ParentId, 0), NameKey)
                WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IX_Topics_ParentId ON Topics(ParentId, SortOrder, Name);
            CREATE INDEX IX_Articles_Title ON Articles(Title COLLATE NOCASE);
            CREATE INDEX IX_Articles_LanguageCode ON Articles(LanguageCode);
            CREATE INDEX IX_Articles_ArticleType ON Articles(ArticleType);
            CREATE INDEX IX_Articles_Status ON Articles(Status);
            CREATE INDEX IX_Articles_UpdatedAtUtc ON Articles(UpdatedAtUtc DESC);
            CREATE INDEX IX_Articles_IsFavorite ON Articles(IsFavorite) WHERE DeletedAtUtc IS NULL;
            CREATE INDEX IX_Articles_DeletedAtUtc ON Articles(DeletedAtUtc);
            CREATE INDEX IX_ArticleSections_ArticleId ON ArticleSections(ArticleId, SortOrder);
            CREATE INDEX IX_ArticleTopics_TopicId ON ArticleTopics(TopicId, ArticleId);
            CREATE INDEX IX_ArticleTopics_ArticleId ON ArticleTopics(ArticleId, TopicId);
            CREATE UNIQUE INDEX UX_ArticleTopics_OnePrimary
                ON ArticleTopics(ArticleId)
                WHERE IsPrimary = 1;
            CREATE INDEX IX_ArticleSources_ArticleId ON ArticleSources(ArticleId, SortOrder);

            INSERT INTO SchemaInfo(Id, SchemaVersion, CreatedAtUtc, UpdatedAtUtc)
            VALUES (1, 0, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """)
    ];

    private readonly SqliteConnectionFactory _connections;

    public MigrationRunner(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        using var writeLease = await _connections.WriteGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var isNewDatabase = !await HasUserTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        var version = await ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The database schema version {version} is newer than the supported version {CurrentSchemaVersion}.");
        }

        if (!isNewDatabase && version < CurrentSchemaVersion)
        {
            await CreateSafetyBackupAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        foreach (var migration in Migrations.Where(item => item.Version > version).OrderBy(item => item.Version))
        {
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText = """
                UPDATE SchemaInfo
                SET SchemaVersion = $version,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = 1;
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$version", migration.Version);
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            version = migration.Version;
        }

        return new MigrationResult(isNewDatabase, version, IsFts5Available: true);
    }

    private static async Task<bool> HasUserTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
            );
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<int> ReadVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaInfo');";
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SchemaVersion FROM SchemaInfo WHERE Id = 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task CreateSafetyBackupAsync(
        SqliteConnection source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backupPath = $"{_connections.Options.DatabasePath}.pre-migration-{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak";
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        await using var destination = new SqliteConnection(connectionString);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }

    private sealed record Migration(int Version, string Sql);
}

public sealed record MigrationResult(bool IsNewDatabase, int SchemaVersion, bool IsFts5Available);
