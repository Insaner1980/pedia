using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pedia.Core.Utilities;

namespace Pedia.Core.Backup;

public sealed class BackupService
{
    private const string FormatName = "pedia-backup";
    private const int CurrentVersion = 2;
    private const string ManifestEntryName = "manifest.json";
    private const string DatabaseEntryName = "database.sqlite";
    private const long MaximumManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly IClock _clock;
    private readonly int? _requiredSchemaVersion;
    private readonly long _maximumDatabaseBytes;

    public BackupService(
        string databasePath,
        IClock? clock = null,
        int? requiredSchemaVersion = null,
        long maximumDatabaseBytes = 8L * 1024 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (requiredSchemaVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredSchemaVersion));
        }

        if (maximumDatabaseBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDatabaseBytes));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _clock = clock ?? SystemClock.Instance;
        _requiredSchemaVersion = requiredSchemaVersion;
        _maximumDatabaseBytes = maximumDatabaseBytes;
    }

    public async Task<BackupCreateResult> CreateAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBackupExtension(backupPath);
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("The Pedia database was not found.", _databasePath);
        }

        var requestedPath = Path.GetFullPath(backupPath);
        var outputDirectory = Path.GetDirectoryName(requestedPath)
            ?? throw new InvalidOperationException("The backup path has no parent directory.");
        Directory.CreateDirectory(outputDirectory);
        using var temporary = TemporaryWorkspace.Create();
        var snapshotPath = Path.Combine(temporary.Path, DatabaseEntryName);
        await CreateOnlineSnapshotAsync(snapshotPath, cancellationToken);
        var snapshot = await InspectDatabaseAsync(snapshotPath, cancellationToken);
        if (_requiredSchemaVersion is { } required && snapshot.SchemaVersion != required)
        {
            throw new InvalidDataException(
                $"Database schema version {snapshot.SchemaVersion} does not match required version {required}.");
        }

        var databaseLength = new FileInfo(snapshotPath).Length;
        if (databaseLength > _maximumDatabaseBytes)
        {
            throw new InvalidDataException($"The database exceeds the {_maximumDatabaseBytes} byte backup limit.");
        }

        var manifest = new BackupManifest(
            FormatName,
            CurrentVersion,
            typeof(BackupService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            _clock.UtcNow,
            snapshot.SchemaVersion,
            snapshot.ArticleCount,
            snapshot.TopicCount,
            databaseLength,
            await ComputeFileHashAsync(snapshotPath, cancellationToken),
            snapshot.SchemaSha256);
        var archivePath = Path.Combine(temporary.Path, "archive.tmp");
        await WriteArchiveAsync(archivePath, snapshotPath, manifest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(requestedPath);
        for (var collisionNumber = 1; ; collisionNumber++)
        {
            var candidate = FileNameUtilities.GetCollisionPath(outputDirectory, fileName, collisionNumber);
            try
            {
                File.Move(archivePath, candidate);
                return new BackupCreateResult(candidate, collisionNumber > 1, manifest);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var prepared = await PrepareBackupAsync(backupPath, cancellationToken);
            return new BackupValidationResult(true, null, prepared.Manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsValidationFailure(exception))
        {
            return new BackupValidationResult(false, exception.Message, null);
        }
    }

    public async Task<BackupRestoreResult> RestoreAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("The current Pedia database was not found.", _databasePath);
        }

        using var prepared = await PrepareBackupAsync(backupPath, cancellationToken);
        var current = await InspectDatabaseAsync(_databasePath, cancellationToken);
        if (prepared.Manifest.DatabaseSchemaVersion != current.SchemaVersion ||
            !string.Equals(prepared.Manifest.DatabaseSchemaSha256, current.SchemaSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The backup schema does not match the current Pedia database schema.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var databaseDirectory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        var databaseStem = Path.GetFileNameWithoutExtension(_databasePath);
        var safetyName = $"{databaseStem}.safety-{_clock.UtcNow:yyyyMMdd-HHmmss}.pediabackup";
        var safety = await CreateAsync(Path.Combine(databaseDirectory, safetyName), cancellationToken);
        using var rollback = await PrepareBackupAsync(safety.Path, CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();
        var databaseRestored = false;
        try
        {
            SqliteConnection.ClearAllPools();
            await RestoreOnlineSnapshotAsync(prepared.DatabasePath, cancellationToken);
            databaseRestored = true;

            var restored = await InspectDatabaseAsync(_databasePath, CancellationToken.None);
            if (restored.SchemaVersion != prepared.Manifest.DatabaseSchemaVersion ||
                !string.Equals(restored.SchemaSha256, prepared.Manifest.DatabaseSchemaSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The restored database failed its post-restore schema validation.");
            }

            SqliteConnection.ClearAllPools();
            return new BackupRestoreResult(safety.Path, restored.SchemaVersion);
        }
        catch (Exception restoreException)
        {
            if (!databaseRestored)
            {
                throw;
            }

            try
            {
                await RestoreOnlineSnapshotAsync(rollback.DatabasePath, CancellationToken.None);
                SqliteConnection.ClearAllPools();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Database restore failed and automatic rollback also failed. Safety backup: {safety.Path}",
                    restoreException,
                    rollbackException);
            }

            throw;
        }
    }

    private async Task CreateOnlineSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(CreateConnectionString(snapshotPath, SqliteOpenMode.ReadWriteCreate));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task WriteArchiveAsync(
        string archivePath,
        string snapshotPath,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var databaseEntry = archive.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
            databaseEntry.LastWriteTime = ClampZipTimestamp(manifest.CreatedAtUtc);
            await using (var destination = databaseEntry.Open())
            await using (var source = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = ClampZipTimestamp(manifest.CreatedAtUtc);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
        }

        await archiveStream.FlushAsync(cancellationToken);
        archiveStream.Flush(flushToDisk: true);
    }

    private async Task<PreparedBackup> PrepareBackupAsync(
        string backupPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBackupExtension(backupPath);
        var fullPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The backup file was not found.", fullPath);
        }

        var temporary = TemporaryWorkspace.Create();
        try
        {
            using var archive = ZipFile.OpenRead(fullPath);
            if (archive.Entries.Count != 2 ||
                archive.Entries.Any(entry =>
                    !string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal) &&
                    !string.Equals(entry.FullName, DatabaseEntryName, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The backup archive has an invalid entry layout.");
            }

            var manifestEntry = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException("The backup manifest is missing.");
            var databaseEntry = archive.GetEntry(DatabaseEntryName)
                ?? throw new InvalidDataException("The backup database is missing.");
            if (manifestEntry.Length is < 1 or > MaximumManifestBytes)
            {
                throw new InvalidDataException("The backup manifest size is invalid.");
            }

            if (databaseEntry.Length < 1 || databaseEntry.Length > _maximumDatabaseBytes)
            {
                throw new InvalidDataException("The backup database size is invalid.");
            }

            BackupManifest manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("The backup manifest is empty.");
            }

            ValidateManifest(manifest, databaseEntry.Length);
            var extractedPath = Path.Combine(temporary.Path, DatabaseEntryName);
            await using (var source = databaseEntry.Open())
            await using (var destination = new FileStream(
                extractedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await CopyWithLimitAsync(source, destination, manifest.DatabaseLength, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var actualHash = await ComputeFileHashAsync(extractedPath, cancellationToken);
            if (!string.Equals(actualHash, manifest.DatabaseSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The backup database checksum does not match its manifest.");
            }

            var snapshot = await InspectDatabaseAsync(extractedPath, cancellationToken);
            if (snapshot.SchemaVersion != manifest.DatabaseSchemaVersion ||
                !string.Equals(snapshot.SchemaSha256, manifest.DatabaseSchemaSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The backup database schema does not match its manifest.");
            }

            if (_requiredSchemaVersion is { } required && snapshot.SchemaVersion != required)
            {
                throw new InvalidDataException(
                    $"Backup schema version {snapshot.SchemaVersion} does not match required version {required}.");
            }

            return new PreparedBackup(temporary, extractedPath, manifest);
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }

    private static void ValidateManifest(BackupManifest manifest, long entryLength)
    {
        if (!string.Equals(manifest.Format, FormatName, StringComparison.Ordinal) || manifest.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported Pedia backup format or version {manifest.Version}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.PediaVersion)
            || manifest.CreatedAtUtc.Offset != TimeSpan.Zero
            || manifest.DatabaseSchemaVersion < 0
            || manifest.ArticleCount < 0
            || manifest.TopicCount < 0)
        {
            throw new InvalidDataException("The backup manifest contains invalid metadata.");
        }

        if (manifest.DatabaseLength != entryLength ||
            !IsSha256(manifest.DatabaseSha256) ||
            !IsSha256(manifest.DatabaseSchemaSha256))
        {
            throw new InvalidDataException("The backup manifest checksum or length is invalid.");
        }
    }

    private static async Task<DatabaseInspection> InspectDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(CreateConnectionString(databasePath, SqliteOpenMode.ReadOnly));
        await connection.OpenAsync(cancellationToken);

        await using (var quickCheck = connection.CreateCommand())
        {
            quickCheck.CommandText = "PRAGMA quick_check;";
            await using var reader = await quickCheck.ExecuteReaderAsync(cancellationToken);
            var sawResult = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                sawResult = true;
                if (!string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The backup database failed SQLite quick_check.");
                }
            }

            if (!sawResult)
            {
                throw new InvalidDataException("The backup database returned no SQLite quick_check result.");
            }
        }

        await using (var foreignKeyCheck = connection.CreateCommand())
        {
            foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException("The backup database contains foreign key violations.");
            }
        }

        bool hasSchemaInfo;
        await using (var schemaInfoCommand = connection.CreateCommand())
        {
            schemaInfoCommand.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM sqlite_schema
                    WHERE type = 'table' AND name = 'SchemaInfo');
                """;
            hasSchemaInfo = Convert.ToInt64(
                await schemaInfoCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) != 0;
        }

        int schemaVersion;
        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = hasSchemaInfo
                ? "SELECT COALESCE((SELECT SchemaVersion FROM SchemaInfo WHERE Id = 1), 0);"
                : "PRAGMA user_version;";
            schemaVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        var schemaText = new StringBuilder();
        await using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.CommandText = """
                SELECT type, name, tbl_name, COALESCE(sql, '')
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
                ORDER BY type, name;
                """;
            await using var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                for (var column = 0; column < 4; column++)
                {
                    var value = reader.GetString(column);
                    schemaText.Append(value.Length).Append(':').Append(value);
                }

                schemaText.AppendLine();
            }
        }

        var schemaHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemaText.ToString())))
            .ToLowerInvariant();
        var articleCount = await CountArticlesAsync(connection, cancellationToken);
        var topicCount = await CountTopicsAsync(connection, cancellationToken);
        return new DatabaseInspection(schemaVersion, schemaHash, articleCount, topicCount);
    }

    private static async Task<long> CountArticlesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        return await CountKnownTableAsync(
            connection,
            "Articles",
            "SELECT EXISTS(SELECT 1 FROM pragma_table_info('Articles') WHERE name = 'DeletedAtUtc');",
            "SELECT COUNT(*) FROM Articles WHERE DeletedAtUtc IS NULL;",
            "SELECT COUNT(*) FROM Articles;",
            cancellationToken);
    }

    private static async Task<long> CountTopicsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        return await CountKnownTableAsync(
            connection,
            "Topics",
            "SELECT EXISTS(SELECT 1 FROM pragma_table_info('Topics') WHERE name = 'DeletedAtUtc');",
            "SELECT COUNT(*) FROM Topics WHERE DeletedAtUtc IS NULL;",
            "SELECT COUNT(*) FROM Topics;",
            cancellationToken);
    }

    private static async Task<long> CountKnownTableAsync(
        SqliteConnection connection,
        string tableName,
        string deletedColumnSql,
        string activeCountSql,
        string totalCountSql,
        CancellationToken cancellationToken)
    {
        await using var table = connection.CreateCommand();
        table.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = $name);";
        table.Parameters.AddWithValue("$name", tableName);
        if (Convert.ToInt64(await table.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
        {
            return 0;
        }

        await using var deletedColumn = connection.CreateCommand();
        deletedColumn.CommandText = deletedColumnSql; // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- Private callers pass SQL literals.
        var hasDeletedAt = Convert.ToInt64(await deletedColumn.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0;

        await using var count = connection.CreateCommand();
        count.CommandText = hasDeletedAt ? activeCountSql : totalCountSql; // nosemgrep: csharp.lang.security.sqli.csharp-sqli.csharp-sqli -- Private callers pass SQL literals.
        return Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied += read;
            if (copied > expectedLength)
            {
                throw new InvalidDataException("The backup database expands beyond its declared length.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (copied != expectedLength)
        {
            throw new InvalidDataException("The backup database length does not match its manifest.");
        }
    }

    private async Task RestoreOnlineSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(CreateConnectionString(snapshotPath, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(CreateConnectionString(_databasePath, SqliteOpenMode.ReadWrite));
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        source.BackupDatabase(destination);
    }

    private static void EnsureBackupExtension(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".pediabackup", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Pedia backups must use the .pediabackup extension.");
        }
    }

    private static string CreateConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidationFailure(Exception exception) =>
        exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or SqliteException or NotSupportedException;

    private static DateTimeOffset ClampZipTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var minimum = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maximum = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        return utc < minimum ? minimum : utc > maximum ? maximum : utc;
    }

    private sealed record DatabaseInspection(
        int SchemaVersion,
        string SchemaSha256,
        long ArticleCount,
        long TopicCount);

    private sealed class PreparedBackup(
        TemporaryWorkspace workspace,
        string databasePath,
        BackupManifest manifest) : IDisposable
    {
        public string DatabasePath { get; } = databasePath;

        public BackupManifest Manifest { get; } = manifest;

        public void Dispose() => workspace.Dispose();
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Pedia",
                "Backup",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryWorkspace(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
