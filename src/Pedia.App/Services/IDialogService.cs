using Microsoft.UI.Xaml;
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
public sealed record ImportDialogResult(
    long? DestinationTopicId,
    string LanguageCode,
    string Status,
    ImportDuplicateHandling DuplicateHandling);

public interface IDialogService
{
    void AttachXamlRoot(XamlRoot xamlRoot);
    Task ShowErrorAsync(string message);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText);
    Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync();
    Task<TopicDialogResult?> ShowTopicEditorAsync(string title, string? name = null, string? description = null);
    Task<TopicNodeViewModel?> ChooseTopicAsync(string title, IReadOnlyList<TopicNodeViewModel> topics, long? selectedTopicId = null);
    Task<IReadOnlyList<TopicNodeViewModel>?> ChooseTopicsAsync(IReadOnlyList<TopicNodeViewModel> topics, IReadOnlySet<long> selectedTopicIds);
    Task<string?> ChooseArticleStatusAsync();
    Task<ImportDialogResult?> ShowImportPreviewAsync(ImportPreviewResult preview, IReadOnlyList<TopicNodeViewModel> topics, long? defaultTopicId);
    Task<ExportFormat?> ChooseExportFormatAsync();
}
