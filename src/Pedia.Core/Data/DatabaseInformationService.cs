namespace Pedia.Core.Data;

public sealed class DatabaseInformationService
{
    private readonly SqliteConnectionFactory _connections;

    public DatabaseInformationService(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<DatabaseInformation> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT SchemaVersion FROM SchemaInfo WHERE Id = 1),
                (SELECT MAX(CompletedAtUtc) FROM ImportRuns WHERE Status = 'Completed'),
                (SELECT COUNT(*) FROM SearchDocuments),
                (SELECT COUNT(*) FROM Articles WHERE DeletedAtUtc IS NULL),
                (SELECT COUNT(*) FROM SearchDocumentsFts);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var activeArticleCount = reader.GetInt64(3);
        var indexedDocumentCount = reader.GetInt64(2);
        var ftsDocumentCount = reader.GetInt64(4);

        return new DatabaseInformation(
            _connections.Options.DatabasePath,
            File.Exists(_connections.Options.DatabasePath)
                ? new FileInfo(_connections.Options.DatabasePath).Length
                : 0,
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : DatabaseValue.ReadDate(reader.GetString(1)),
            indexedDocumentCount == activeArticleCount && ftsDocumentCount == activeArticleCount);
    }
}

public sealed record DatabaseInformation(
    string DatabasePath,
    long DatabaseSizeBytes,
    int SchemaVersion,
    DateTimeOffset? LastCompletedImportAtUtc,
    bool IsSearchIndexReady);
