using Pedia.Models;
using Pedia.ViewModels;

namespace Pedia.Services;

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

public sealed record TopicDialogResult(string Name, string? Description);

public interface IPediaDataService
{
    bool IsNewDatabase { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopicData>> GetTopicsAsync(CancellationToken cancellationToken = default);
    Task<PageResult<ArticleListData>> QueryArticlesAsync(ArticleQuery query, CancellationToken cancellationToken = default);
    Task<ArticleDocumentData?> GetArticleAsync(long articleId, CancellationToken cancellationToken = default);
    Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<long> SaveArticleAsync(EditableArticle article, CancellationToken cancellationToken = default);
    Task ReplaceArticleTopicsAsync(long articleId, IReadOnlyList<ArticleTopicData> assignments, CancellationToken cancellationToken = default);
    Task<long> DuplicateArticleAsync(long articleId, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long articleId, bool isFavorite, CancellationToken cancellationToken = default);
    Task AddTopicsToArticlesAsync(IReadOnlyList<long> articleIds, IReadOnlyList<long> topicIds, CancellationToken cancellationToken = default);
    Task RemoveTopicFromArticlesAsync(IReadOnlyList<long> articleIds, long topicId, CancellationToken cancellationToken = default);
    Task SetStatusForArticlesAsync(IReadOnlyList<long> articleIds, string status, CancellationToken cancellationToken = default);
    Task MoveArticlesToTrashAsync(IReadOnlyList<long> articleIds, CancellationToken cancellationToken = default);
    Task MoveArticleToTrashAsync(long articleId, CancellationToken cancellationToken = default);
    Task RestoreArticleAsync(long articleId, CancellationToken cancellationToken = default);
    Task PermanentlyDeleteArticleAsync(long articleId, CancellationToken cancellationToken = default);
    Task EmptyTrashAsync(CancellationToken cancellationToken = default);
    Task<long> CreateTopicAsync(string name, string? description, long? parentId, CancellationToken cancellationToken = default);
    Task RenameTopicAsync(long topicId, string name, string? description, CancellationToken cancellationToken = default);
    Task MoveTopicAsync(long topicId, long? destinationParentId, CancellationToken cancellationToken = default);
    Task ReorderTopicAsync(long topicId, int newSortOrder, CancellationToken cancellationToken = default);
    Task DeleteTopicAsync(long topicId, CancellationToken cancellationToken = default);
    Task<ImportPreviewResult> PreviewImportAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);
    Task<ImportOperationResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default);
    Task ExportAsync(IReadOnlyList<long> articleIds, ExportFormat format, string destinationPath, CancellationToken cancellationToken = default);
    Task CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<BackupValidationResult> ValidateBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default);
    Task DeleteSampleContentAsync(CancellationToken cancellationToken = default);
}

public interface IStringService
{
    string Get(string key);
    string Format(string key, params object?[] args);
    string OperationFailed => Get("OperationFailedMessage");
}

public interface ISettingsService
{
    string PediaDirectory { get; }
    string SettingsPath { get; }
    PediaSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IDialogService
{
    Task ShowErrorAsync(string message);
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText);
    Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync();
    Task<TopicDialogResult?> ShowTopicEditorAsync(string title, string? name = null, string? description = null);
    Task<TopicNodeViewModel?> ChooseTopicAsync(string title, IReadOnlyList<TopicNodeViewModel> topics, long? selectedTopicId = null);
    Task<IReadOnlyList<TopicNodeViewModel>?> ChooseTopicsAsync(
        IReadOnlyList<TopicNodeViewModel> topics,
        IReadOnlySet<long> selectedTopicIds);
    Task<string?> ChooseArticleStatusAsync();
    Task<ExportFormat?> ChooseExportFormatAsync();
}

public interface IFilePickerService
{
    Task<string?> PickExportDestinationAsync(ExportFormat format, bool multipleArticles);
}
