using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Pedia.Core.Data;

public sealed class SqliteConnectionFactory
{
    private readonly DatabaseOptions _options;

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(options));
        }

        if (options.BusyTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The busy timeout cannot be negative.");
        }

        _options = options with { DatabasePath = Path.GetFullPath(options.DatabasePath) };
        WriteGate = new DatabaseWriteGate(_options.DatabasePath);
    }

    public DatabaseOptions Options => _options;

    public DatabaseWriteGate WriteGate { get; }

    [SuppressMessage(
        "Security",
        "S2077",
        Justification = "The interpolated PRAGMA value is a validated integer from DatabaseOptions, not user-provided SQL.")]
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = {_options.BusyTimeoutMilliseconds};
                PRAGMA journal_mode = WAL;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
