using Microsoft.Data.Sqlite;
using Pedia.Core.Data;

namespace Pedia.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string directoryPath, DatabaseOptions options)
    {
        DirectoryPath = directoryPath;
        Options = options;
        Connections = new SqliteConnectionFactory(options);
        Initializer = new DatabaseInitializer(Connections);
    }

    public string DirectoryPath { get; }

    public DatabaseOptions Options { get; }

    public SqliteConnectionFactory Connections { get; }

    public DatabaseInitializer Initializer { get; }

    public static async Task<TestDatabase> CreateAsync(bool seedSamples = false)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Pedia.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);

        var database = new TestDatabase(
            directoryPath,
            new DatabaseOptions(Path.Combine(directoryPath, "pedia.db"), BusyTimeoutMilliseconds: 2_500));

        await database.Initializer.InitializeAsync(seedSamples);
        return database;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
