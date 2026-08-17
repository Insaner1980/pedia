namespace Pedia.Core.Backup;

public sealed record BackupManifest(
    string Format,
    int Version,
    string PediaVersion,
    DateTimeOffset CreatedAtUtc,
    int DatabaseSchemaVersion,
    long ArticleCount,
    long TopicCount,
    long DatabaseLength,
    string DatabaseSha256,
    string DatabaseSchemaSha256);

public sealed record BackupCreateResult(
    string Path,
    bool CollisionRenamed,
    BackupManifest Manifest);

public sealed record BackupValidationResult(
    bool IsValid,
    string? Error,
    BackupManifest? Manifest);

public sealed record BackupRestoreResult(
    string SafetyBackupPath,
    int DatabaseSchemaVersion);
