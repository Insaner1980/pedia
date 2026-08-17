using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pedia.Models;
using Pedia.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Pedia.ViewModels;

public sealed partial class ArticleDetailViewModel : ObservableObject
{
    private readonly IPediaDataService _dataService;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly ISettingsService _settings;
    private readonly IStringService _strings;
    private readonly ILogger<ArticleDetailViewModel> _logger;
    private bool _trackingChanges;
    private long _articleLoadGeneration;

    public ArticleDetailViewModel(
        IPediaDataService dataService,
        IDialogService dialogs,
        IFilePickerService filePicker,
        ISettingsService settings,
        IStringService strings,
        ILogger<ArticleDetailViewModel> logger)
    {
        _dataService = dataService;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _settings = settings;
        _strings = strings;
        _logger = logger;
        ArticleTypes =
        [
            new("General", _strings.Get("GeneralArticleType")),
            new("Person", _strings.Get("PersonArticleType")),
            new("Place", _strings.Get("PlaceArticleType")),
            new("Event", _strings.Get("EventArticleType")),
            new("Concept", _strings.Get("ConceptArticleType")),
            new("Organization", _strings.Get("OrganizationArticleType")),
            new("Timeline", _strings.Get("TimelineArticleType")),
            new("Other", _strings.Get("OtherArticleType"))
        ];
        ArticleStatuses =
        [
            new("Draft", _strings.Get("DraftStatus")),
            new("Ready", _strings.Get("ReadyStatus")),
            new("Needs review", _strings.Get("NeedsReviewStatus")),
            new("Archived", _strings.Get("ArchivedStatus"))
        ];
        SourceTypes =
        [
            new("Manual", _strings.Get("ManualSourceType")),
            new("Local text file", _strings.Get("LocalTextFileSourceType")),
            new("Local Markdown file", _strings.Get("LocalMarkdownFileSourceType")),
            new("Book", _strings.Get("BookSourceType")),
            new("Website", _strings.Get("WebsiteSourceType")),
            new("Encyclopedia", _strings.Get("EncyclopediaSourceType")),
            new("Other", _strings.Get("OtherSourceType"))
        ];
    }

    public Func<Task>? ArticleChanged { get; set; }
    public Func<IReadOnlyList<TopicNodeViewModel>>? TopicProvider { get; set; }
    public Action<string>? NotificationRequested { get; set; }
    public Action? EditorFocusRequested { get; set; }
    public IReadOnlyList<ValueLabelOption> ArticleTypes { get; }
    public IReadOnlyList<ValueLabelOption> ArticleStatuses { get; }
    public IReadOnlyList<ValueLabelOption> SourceTypes { get; }
    public IReadOnlyList<int> HeadingLevels { get; } = [1, 2, 3];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArticle))]
    [NotifyPropertyChangedFor(nameof(IsReading))]
    [NotifyPropertyChangedFor(nameof(IsArticleDeleted))]
    [NotifyPropertyChangedFor(nameof(LanguageDisplay))]
    [NotifyPropertyChangedFor(nameof(ArticleTypeDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(FavoriteDisplay))]
    [NotifyPropertyChangedFor(nameof(WordCountDisplay))]
    [NotifyPropertyChangedFor(nameof(UpdatedDisplay))]
    [NotifyPropertyChangedFor(nameof(TopicsCountDisplay))]
    [NotifyPropertyChangedFor(nameof(SourcesCountDisplay))]
    [NotifyPropertyChangedFor(nameof(HasSources))]
    [NotifyPropertyChangedFor(nameof(HasTopics))]
    [NotifyPropertyChangedFor(nameof(CanModifyReaderTopics))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToTrashCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePermanentlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManageTopicsCommand))]
    public partial ArticleDocumentData? Article { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReading))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelEditCommand))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial bool IsNewArticle { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToTrashCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePermanentlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManageTopicsCommand))]
    [NotifyPropertyChangedFor(nameof(CanModifyReaderTopics))]
    [NotifyPropertyChangedFor(nameof(CanEditFields))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial EditableArticle? Editor { get; set; }

    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    public bool HasArticle => Article is not null;
    public bool IsReading => HasArticle && !IsEditing;
    public bool IsArticleDeleted => Article?.DeletedAtUtc is not null;
    public string LanguageDisplay => Article is null ? string.Empty : GetLanguageDisplay(Article.LanguageCode);
    public string ArticleTypeDisplay => GetOptionLabel(ArticleTypes, Article?.ArticleType);
    public string StatusDisplay => GetOptionLabel(ArticleStatuses, Article?.Status);
    public string FavoriteDisplay => _strings.Get(Article?.IsFavorite == true ? "YesText" : "NoText");
    public string WordCountDisplay => Article is null ? string.Empty : _strings.Format("WordsFormat", Article.WordCount);
    public string UpdatedDisplay => Article is null ? string.Empty : _strings.Format("UpdatedFormat", Article.UpdatedAtUtc.ToLocalTime());
    public string TopicsCountDisplay => Article is null ? string.Empty : _strings.Format("TopicsAssignedFormat", Article.Topics.Count);
    public string SourcesCountDisplay => Article is null ? string.Empty : _strings.Format("SourcesAssignedFormat", Article.Sources.Count);
    public bool HasSources => Article?.Sources.Count > 0;
    public bool HasTopics => Article?.Topics.Count > 0;
    public bool CanModifyReaderTopics => CanUseActiveArticle();
    public bool CanEditFields => !IsBusy;
    public double ArticleBodyFontSize => _settings.Current.ArticleBodyFontSize;
    public double ArticleLineSpacing => _settings.Current.ArticleLineSpacing;
    public double MaximumReadingWidth => _settings.Current.MaximumReadingWidth;

    public async Task LoadArticleAsync(long articleId, CancellationToken cancellationToken = default)
    {
        var generation = ++_articleLoadGeneration;
        IsBusy = true;
        try
        {
            var article = await _dataService.GetArticleAsync(articleId, cancellationToken);
            if (generation != _articleLoadGeneration)
            {
                return;
            }

            Article = article;
            IsEditing = false;
            IsNewArticle = false;
            Editor = null;
            IsDirty = false;
            ValidationMessage = null;
        }
        finally
        {
            if (generation == _articleLoadGeneration)
            {
                IsBusy = false;
            }
        }
    }

    public void ClearArticle()
    {
        _articleLoadGeneration++;
        Article = null;
        Editor = null;
        IsEditing = false;
        IsNewArticle = false;
        IsDirty = false;
    }

    public void CreateNew(TopicNodeViewModel? selectedTopic)
    {
        _articleLoadGeneration++;
        DetachEditorTracking();
        Article = null;
        Editor = new EditableArticle
        {
            LanguageCode = _settings.Current.DefaultLanguageCode,
            Status = _settings.Current.DefaultArticleStatus,
            ArticleType = "General"
        };
        Editor.Sections.Add(new EditableSection { HeadingLevel = 2 });
        if (selectedTopic is { IsSmart: false })
        {
            Editor.Topics.Add(new EditableTopicAssignment
            {
                TopicId = selectedTopic.Id,
                Path = selectedTopic.FullPath,
                IsPrimary = true
            });
        }

        IsNewArticle = true;
        IsEditing = true;
        IsDirty = true;
        ValidationMessage = null;
        AttachEditorTracking();
        EditorFocusRequested?.Invoke();
    }

    public async Task<bool> TryLeaveEditorAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (!IsEditing || !IsDirty)
        {
            return true;
        }

        var choice = await _dialogs.ConfirmUnsavedChangesAsync();
        switch (choice)
        {
            case UnsavedChangesChoice.Save:
                return await SaveCoreAsync();
            case UnsavedChangesChoice.Discard:
                DiscardEditor();
                return true;
            default:
                return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (Article is null)
        {
            return;
        }

        DetachEditorTracking();
        Editor = Clone(Article);
        IsNewArticle = false;
        IsEditing = true;
        IsDirty = false;
        ValidationMessage = null;
        AttachEditorTracking();
        EditorFocusRequested?.Invoke();
    }

    private bool CanEdit() => Article is { DeletedAtUtc: null } && !IsEditing && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync() => await SaveCoreAsync();

    private bool CanSave() => IsEditing && Editor is not null && !IsBusy;

    private async Task<bool> SaveCoreAsync()
    {
        if (Editor is null || IsBusy)
        {
            return false;
        }

        Editor.Title = Editor.Title.Trim();
        if (Editor.Title.Length == 0)
        {
            ValidationMessage = _strings.Get("ArticleTitleRequiredText");
            return false;
        }

        var editor = Editor;
        var saveGeneration = ++_articleLoadGeneration;
        IsBusy = true;
        ValidationMessage = null;
        var articleIdForLogging = Editor.Id;
        try
        {
            var articleId = await _dataService.SaveArticleAsync(editor);
            var savedArticle = await _dataService.GetArticleAsync(articleId);
            if (saveGeneration == _articleLoadGeneration && ReferenceEquals(Editor, editor))
            {
                DetachEditorTracking();
                Article = savedArticle;
                Editor = null;
                IsEditing = false;
                IsNewArticle = false;
                IsDirty = false;
            }
            NotificationRequested?.Invoke(_strings.Get("ArticleSavedText"));
            if (ArticleChanged is not null)
            {
                await ArticleChanged();
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save article {ArticleId}", articleIdForLogging);
            ValidationMessage = _strings.OperationFailed;
            return false;
        }
        finally
        {
            if (saveGeneration == _articleLoadGeneration)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelEdit))]
    private async Task CancelEditAsync()
    {
        if (IsDirty)
        {
            var choice = await _dialogs.ConfirmUnsavedChangesAsync();
            if (choice == UnsavedChangesChoice.Save)
            {
                await SaveCoreAsync();
                return;
            }

            if (choice == UnsavedChangesChoice.Cancel)
            {
                return;
            }
        }

        DiscardEditor();
    }

    private bool CanCancelEdit() => IsEditing && !IsBusy;

    [RelayCommand]
    private void AddSection()
    {
        Editor?.Sections.Add(new EditableSection { HeadingLevel = 2 });
        NotifySectionMoveCommands();
    }

    [RelayCommand(CanExecute = nameof(CanMoveSectionUp))]
    private void MoveSectionUp(EditableSection? section)
    {
        if (Editor is null || section is null)
        {
            return;
        }

        var index = Editor.Sections.IndexOf(section);
        if (index > 0)
        {
            Editor.Sections.Move(index, index - 1);
            MoveSectionUpCommand.NotifyCanExecuteChanged();
            MoveSectionDownCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanMoveSectionUp(EditableSection? section) =>
        Editor is not null && section is not null && Editor.Sections.IndexOf(section) > 0;

    public bool CanMoveSectionDown(EditableSection? section)
    {
        if (Editor is null || section is null)
        {
            return false;
        }

        var index = Editor.Sections.IndexOf(section);
        return index >= 0 && index < Editor.Sections.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSectionDown))]
    private void MoveSectionDown(EditableSection? section)
    {
        if (Editor is null || section is null)
        {
            return;
        }

        var index = Editor.Sections.IndexOf(section);
        if (index >= 0 && index < Editor.Sections.Count - 1)
        {
            Editor.Sections.Move(index, index + 1);
            MoveSectionUpCommand.NotifyCanExecuteChanged();
            MoveSectionDownCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void DeleteSection(EditableSection? section)
    {
        if (section is not null)
        {
            Editor?.Sections.Remove(section);
            NotifySectionMoveCommands();
        }
    }

    [RelayCommand]
    private void AddSource()
    {
        Editor?.Sources.Add(new EditableSource());
        NotifySourceMoveCommands();
    }

    [RelayCommand]
    private void DeleteSource(EditableSource? source)
    {
        if (source is not null)
        {
            Editor?.Sources.Remove(source);
            NotifySourceMoveCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveSourceUp))]
    private void MoveSourceUp(EditableSource? source)
    {
        if (Editor is null || source is null)
        {
            return;
        }

        var index = Editor.Sources.IndexOf(source);
        if (index > 0)
        {
            Editor.Sources.Move(index, index - 1);
            MoveSourceUpCommand.NotifyCanExecuteChanged();
            MoveSourceDownCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanMoveSourceUp(EditableSource? source) =>
        Editor is not null && source is not null && Editor.Sources.IndexOf(source) > 0;

    public bool CanMoveSourceDown(EditableSource? source)
    {
        if (Editor is null || source is null)
        {
            return false;
        }

        var index = Editor.Sources.IndexOf(source);
        return index >= 0 && index < Editor.Sources.Count - 1;
    }

    private void NotifySectionMoveCommands()
    {
        MoveSectionUpCommand.NotifyCanExecuteChanged();
        MoveSectionDownCommand.NotifyCanExecuteChanged();
    }

    private void NotifySourceMoveCommands()
    {
        MoveSourceUpCommand.NotifyCanExecuteChanged();
        MoveSourceDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveSourceDown))]
    private void MoveSourceDown(EditableSource? source)
    {
        if (Editor is null || source is null)
        {
            return;
        }

        var index = Editor.Sources.IndexOf(source);
        if (index >= 0 && index < Editor.Sources.Count - 1)
        {
            Editor.Sources.Move(index, index + 1);
            MoveSourceUpCommand.NotifyCanExecuteChanged();
            MoveSourceDownCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task AddTopicsAsync()
    {
        if (Editor is null || TopicProvider is null)
        {
            return;
        }

        var editor = Editor;
        var selectedIds = editor.Topics.Select(topic => topic.TopicId).ToHashSet();
        var result = await _dialogs.ChooseTopicsAsync(TopicProvider(), selectedIds);
        if (result is null || !ReferenceEquals(Editor, editor))
        {
            return;
        }

        var primaryId = editor.Topics.FirstOrDefault(topic => topic.IsPrimary)?.TopicId;
        editor.Topics.Clear();
        foreach (var topic in result)
        {
            editor.Topics.Add(new EditableTopicAssignment
            {
                TopicId = topic.Id,
                Path = topic.FullPath,
                IsPrimary = topic.Id == primaryId || (primaryId is null && editor.Topics.Count == 0)
            });
        }
    }

    [RelayCommand]
    private void RemoveTopicAssignment(EditableTopicAssignment? assignment)
    {
        if (Editor is null || assignment is null)
        {
            return;
        }

        var wasPrimary = assignment.IsPrimary;
        Editor.Topics.Remove(assignment);
        if (wasPrimary && Editor.Topics.Count > 0)
        {
            Editor.Topics[0].IsPrimary = true;
        }
    }

    [RelayCommand]
    private void SetPrimaryTopic(EditableTopicAssignment? assignment)
    {
        if (Editor is null || assignment is null)
        {
            return;
        }

        foreach (var topic in Editor.Topics)
        {
            topic.IsPrimary = ReferenceEquals(topic, assignment);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseActiveArticle))]
    private async Task ManageTopicsAsync()
    {
        if (Article is null || TopicProvider is null)
        {
            return;
        }

        var article = Article;
        var selectedIds = article.Topics.Select(topic => topic.TopicId).ToHashSet();
        var primaryId = article.Topics.FirstOrDefault(topic => topic.IsPrimary)?.TopicId;
        var result = await _dialogs.ChooseTopicsAsync(TopicProvider(), selectedIds);
        if (result is null || Article?.Id != article.Id)
        {
            return;
        }

        var assignments = new List<ArticleTopicData>(result.Count);
        foreach (var topic in result)
        {
            assignments.Add(new ArticleTopicData(
                topic.Id,
                topic.FullPath,
                topic.Id == primaryId || (primaryId is null && assignments.Count == 0)));
        }

        if (assignments.Count > 0 && assignments.All(topic => !topic.IsPrimary))
        {
            assignments[0] = assignments[0] with { IsPrimary = true };
        }

        await SaveReaderTopicChangesAsync(article.Id, assignments, _strings.Get("TopicAssignmentsUpdatedText"));
    }

    public async Task RemoveFromTopicAsync(long topicId)
    {
        if (Article is null || IsBusy)
        {
            return;
        }

        var assignment = Article.Topics.FirstOrDefault(topic => topic.TopicId == topicId);
        if (assignment is null)
        {
            return;
        }

        var wasPrimary = assignment.IsPrimary;
        var assignments = Article.Topics.Where(topic => topic.TopicId != topicId).ToList();
        if (wasPrimary && assignments.Count > 0)
        {
            assignments[0] = assignments[0] with { IsPrimary = true };
        }

        await SaveReaderTopicChangesAsync(Article.Id, assignments, _strings.Get("ArticleRemovedFromTopicText"));
    }

    public async Task RemoveTopicAssignmentAsync(ArticleTopicData? assignment)
    {
        if (!IsBusy && assignment is not null)
        {
            await RemoveFromTopicAsync(assignment.TopicId);
        }
    }

    public async Task SetPrimaryTopicAsync(ArticleTopicData? assignment)
    {
        if (Article is null || assignment is null || assignment.IsPrimary || IsBusy)
        {
            return;
        }

        var assignments = Article.Topics
            .Select(topic => topic with { IsPrimary = topic.TopicId == assignment.TopicId })
            .ToArray();

        await SaveReaderTopicChangesAsync(Article.Id, assignments, _strings.Get("PrimaryTopicUpdatedText"));
    }

    private async Task SaveReaderTopicChangesAsync(long articleId, IReadOnlyList<ArticleTopicData> assignments, string notification)
    {
        await ExecuteMutationAsync(
            () => _dataService.ReplaceArticleTopicsAsync(articleId, assignments),
            reloadArticle: true,
            notification);
    }

    [RelayCommand(CanExecute = nameof(CanUseActiveArticle))]
    private async Task ToggleFavoriteAsync()
    {
        if (Article is null)
        {
            return;
        }

        await ExecuteMutationAsync(
            () => _dataService.SetFavoriteAsync(Article.Id, !Article.IsFavorite),
            reloadArticle: true);
    }

    [RelayCommand(CanExecute = nameof(CanUseActiveArticle))]
    private async Task DuplicateAsync()
    {
        if (Article is null)
        {
            return;
        }

        var sourceArticleId = Article.Id;
        try
        {
            var duplicatedId = await _dataService.DuplicateArticleAsync(sourceArticleId);
            await LoadArticleAsync(duplicatedId);
            if (ArticleChanged is not null)
            {
                await ArticleChanged();
            }

            if (Article?.Id != duplicatedId)
            {
                await LoadArticleAsync(duplicatedId);
            }
            Edit();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not duplicate article {ArticleId}", sourceArticleId);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseActiveArticle))]
    private async Task ExportAsync()
    {
        if (Article is null)
        {
            return;
        }

        var articleId = Article.Id;
        var format = await _dialogs.ChooseExportFormatAsync();
        if (format is null || Article?.Id != articleId)
        {
            return;
        }

        var destination = await _filePicker.PickExportDestinationAsync(format.Value, false);
        if (destination is null || Article?.Id != articleId)
        {
            return;
        }

        try
        {
            await _dataService.ExportAsync([articleId], format.Value, destination);
            NotificationRequested?.Invoke(_strings.Get("ArticleExportedText"));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not export article {ArticleId}", articleId);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveToTrash))]
    private async Task MoveToTrashAsync()
    {
        if (Article is null)
        {
            return;
        }

        var articleId = Article.Id;
        if (_settings.Current.ConfirmBeforeTrash
            && !await _dialogs.ConfirmAsync(
                _strings.Get("MoveToTrashTitle"),
                _strings.Get("MoveToTrashMessage"),
                _strings.Get("MoveToTrashButtonText")))
        {
            return;
        }

        if (Article?.Id != articleId)
        {
            return;
        }

        await ExecuteMutationAsync(
            () => _dataService.MoveArticleToTrashAsync(articleId),
            reloadArticle: false,
            _strings.Get("ArticleTrashedText"),
            clearArticle: true);
    }

    private bool CanMoveToTrash() => Article is { DeletedAtUtc: null } && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (Article is null)
        {
            return;
        }

        await ExecuteMutationAsync(
            () => _dataService.RestoreArticleAsync(Article.Id),
            reloadArticle: true,
            _strings.Get("ArticleRestoredText"));
    }

    private bool CanRestore() => Article?.DeletedAtUtc is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task DeletePermanentlyAsync()
    {
        if (Article is null)
        {
            return;
        }

        var articleId = Article.Id;
        if (!await _dialogs.ConfirmAsync(
                _strings.Get("PermanentDeleteTitle"),
                _strings.Get("PermanentDeleteMessage"),
                _strings.Get("DeleteButtonText")))
        {
            return;
        }

        if (Article?.Id != articleId)
        {
            return;
        }

        await ExecuteMutationAsync(
            () => _dataService.PermanentlyDeleteArticleAsync(articleId),
            reloadArticle: false,
            clearArticle: true);
    }

    [RelayCommand]
    private async Task OpenSourceAsync(ArticleSourceData? source)
    {
        if (source?.Url is not { Length: > 0 } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand]
    private void CopySourceUrl(ArticleSourceData? source)
    {
        if (source?.Url is not { Length: > 0 } url)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);
    }

    private bool CanUseActiveArticle() => Article is { DeletedAtUtc: null } && !IsBusy;

    public void NotifyReadingSettingsChanged()
    {
        OnPropertyChanged(nameof(ArticleBodyFontSize));
        OnPropertyChanged(nameof(ArticleLineSpacing));
        OnPropertyChanged(nameof(MaximumReadingWidth));
    }

    private async Task ExecuteMutationAsync(
        Func<Task> mutation,
        bool reloadArticle,
        string? notification = null,
        bool clearArticle = false)
    {
        if (Article is null)
        {
            return;
        }

        var articleId = Article.Id;
        IsBusy = true;
        try
        {
            await mutation();
            if (clearArticle && Article?.Id == articleId)
            {
                ClearArticle();
            }
            else if (reloadArticle && Article?.Id == articleId)
            {
                var reloadedArticle = await _dataService.GetArticleAsync(articleId);
                if (Article?.Id == articleId)
                {
                    Article = reloadedArticle;
                }
            }

            if (notification is not null)
            {
                NotificationRequested?.Invoke(notification);
            }

            if (ArticleChanged is not null)
            {
                await ArticleChanged();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not update article {ArticleId}", articleId);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AttachEditorTracking()
    {
        if (Editor is null)
        {
            return;
        }

        _trackingChanges = true;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.Sections.CollectionChanged += OnCollectionChanged;
        Editor.Sources.CollectionChanged += OnCollectionChanged;
        Editor.Topics.CollectionChanged += OnCollectionChanged;
        foreach (var section in Editor.Sections)
        {
            section.PropertyChanged += OnEditorPropertyChanged;
        }
        foreach (var source in Editor.Sources)
        {
            source.PropertyChanged += OnEditorPropertyChanged;
        }
        foreach (var topic in Editor.Topics)
        {
            topic.PropertyChanged += OnEditorPropertyChanged;
        }
    }

    private void DetachEditorTracking()
    {
        if (!_trackingChanges || Editor is null)
        {
            return;
        }

        Editor.PropertyChanged -= OnEditorPropertyChanged;
        Editor.Sections.CollectionChanged -= OnCollectionChanged;
        Editor.Sources.CollectionChanged -= OnCollectionChanged;
        Editor.Topics.CollectionChanged -= OnCollectionChanged;
        foreach (var section in Editor.Sections)
        {
            section.PropertyChanged -= OnEditorPropertyChanged;
        }
        foreach (var source in Editor.Sources)
        {
            source.PropertyChanged -= OnEditorPropertyChanged;
        }
        foreach (var topic in Editor.Topics)
        {
            topic.PropertyChanged -= OnEditorPropertyChanged;
        }
        _trackingChanges = false;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs args) => IsDirty = true;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (var item in args.OldItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged -= OnEditorPropertyChanged;
            }
        }
        if (args.NewItems is not null)
        {
            foreach (var item in args.NewItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged += OnEditorPropertyChanged;
            }
        }
        IsDirty = true;
    }

    private void DiscardEditor()
    {
        DetachEditorTracking();
        Editor = null;
        IsEditing = false;
        IsNewArticle = false;
        IsDirty = false;
        ValidationMessage = null;
    }

    private static EditableArticle Clone(ArticleDocumentData article)
    {
        var editor = new EditableArticle
        {
            Id = article.Id,
            Title = article.Title,
            Subtitle = article.Subtitle ?? string.Empty,
            Summary = article.Summary ?? string.Empty,
            LanguageCode = article.LanguageCode,
            ArticleType = article.ArticleType,
            Status = article.Status,
            Notes = article.Notes ?? string.Empty,
            IsFavorite = article.IsFavorite
        };
        foreach (var section in article.Sections.OrderBy(section => section.SortOrder))
        {
            editor.Sections.Add(new EditableSection
            {
                Id = section.Id,
                Heading = section.Heading ?? string.Empty,
                HeadingLevel = section.HeadingLevel,
                Body = section.Body
            });
        }
        foreach (var source in article.Sources.OrderBy(source => source.SortOrder))
        {
            editor.Sources.Add(new EditableSource
            {
                Id = source.Id,
                SourceType = source.SourceType,
                Title = source.Title ?? string.Empty,
                Url = source.Url ?? string.Empty,
                ExternalPageId = source.ExternalPageId ?? string.Empty,
                ExternalRevisionId = source.ExternalRevisionId ?? string.Empty,
                LicenseName = source.LicenseName ?? string.Empty,
                AttributionText = source.AttributionText ?? string.Empty,
                RetrievedAtUtc = source.RetrievedAtUtc,
                LastCheckedAtUtc = source.LastCheckedAtUtc,
                Notes = source.Notes ?? string.Empty
            });
        }
        foreach (var topic in article.Topics)
        {
            editor.Topics.Add(new EditableTopicAssignment
            {
                TopicId = topic.TopicId,
                Path = topic.Path,
                IsPrimary = topic.IsPrimary
            });
        }
        return editor;
    }

    private static string GetLanguageDisplay(string languageCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageCode).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return languageCode;
        }
    }

    private static string GetOptionLabel(IReadOnlyList<ValueLabelOption> options, string? value) =>
        options.FirstOrDefault(option => option.Value == value)?.Label ?? value ?? string.Empty;
}
