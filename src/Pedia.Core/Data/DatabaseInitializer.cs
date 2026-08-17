namespace Pedia.Core.Data;

public sealed class DatabaseInitializer
{
    private readonly MigrationRunner _migrations;
    private readonly SampleDataSeeder _samples;

    public DatabaseInitializer(SqliteConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _migrations = new MigrationRunner(connections);
        _samples = new SampleDataSeeder(connections);
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(
        bool seedSamples = true,
        CancellationToken cancellationToken = default)
    {
        var migration = await _migrations.MigrateAsync(cancellationToken).ConfigureAwait(false);
        var samplesSeeded = migration.IsNewDatabase && seedSamples;
        if (samplesSeeded)
        {
            await _samples.SeedAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DatabaseInitializationResult(
            migration.IsNewDatabase,
            migration.SchemaVersion,
            migration.IsFts5Available,
            samplesSeeded);
    }
}

public sealed record DatabaseInitializationResult(
    bool IsNewDatabase,
    int SchemaVersion,
    bool IsFts5Available,
    bool SamplesSeeded);
