using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Pedia.Models;
using Pedia.Services;

namespace Pedia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IPediaDataService _dataService;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly IStringService _strings;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _initialized;
    private bool _lastIncludeSubtopicsDefault;
    private bool? _pendingIncludeSubtopicsDefault;
    private CancellationTokenSource? _importCancellation;

    public Action<bool>? DensityChanged { get; set; }

    [SuppressMessage(
        "Maintainability",
        "S107",
        Justification = "The dependency-injection constructor explicitly declares the window coordinator's required collaborators.")]
    public MainWindowViewModel(
        IPediaDataService dataService,
        ISettingsService settingsService,
        IFilePickerService filePicker,
        IDialogService dialogs,
        IStringService strings,
        TopicPaneViewModel topics,
        ArticleBrowserViewModel browser,
        ArticleDetailViewModel detail,
        SettingsViewModel settings,
        ILogger<MainWindowViewModel> logger)
    {
        _dataService = dataService;
        _settingsService = settingsService;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _strings = strings;
        _logger = logger;
        Topics = topics;
        Browser = browser;
        Detail = detail;
        Settings = settings;

        Topics.ScopeSelected = OnScopeSelectedAsync;
        Topics.TopicMutationStarting = Detail.TryLeaveEditorAsync;
        Topics.TopicsChanged = RefreshDataAsync;
        Browser.ArticleSelected = OnArticleSelectedAsync;
        Browser.ArticleActionRequested = OnArticleActionRequestedAsync;
        Browser.ArticlesExportRequested = ExportArticlesAsync;
        Browser.ArticlesBulkActionRequested = ApplyBulkActionAsync;
        Browser.EmptyTrashRequested = EmptyTrashAsync;
        Browser.ResultsChanged += OnBrowserResultsChanged;
        Browser.LoadFailed += OnBrowserLoadFailed;
        Detail.ArticleChanged = RefreshDataAsync;
        Detail.TopicProvider = () => Topics.UserTopics;
        Detail.NotificationRequested = ShowNotification;
        Settings.BackRequested = CloseSettingsAsync;
        Settings.DataChanged = RefreshDataAsync;
        Settings.SettingsApplied = ApplySettingsAsync;
        Settings.NotificationRequested = ShowNotification;
        Settings.BusyStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUseTitleBarCommands));
            NewArticleCommand.NotifyCanExecuteChanged();
            ImportCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            OpenSettingsCommand.NotifyCanExecuteChanged();
        };
    }

    public TopicPaneViewModel Topics { get; }
    public ArticleBrowserViewModel Browser { get; }
    public ArticleDetailViewModel Detail { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceVisible))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool IsSettingsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseTitleBarCommands))]
    [NotifyCanExecuteChangedFor(nameof(NewArticleCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    public partial bool IsInitializing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseTitleBarCommands))]
    [NotifyCanExecuteChangedFor(nameof(NewArticleCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial string InitializationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInfoBarOpen { get; set; }

    [ObservableProperty]
    public partial string InfoBarMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity InfoBarSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial LibraryStatistics? Statistics { get; set; }

    public bool IsWorkspaceVisible => !IsSettingsVisible;
    public bool CanUseTitleBarCommands => !Settings.IsBusy && !IsImporting && !IsInitializing;
    public bool IsOperationBlockingClose => Settings.IsBusy || IsImporting || IsInitializing;
    public string ArticleCountText => _strings.Format("ArticlesCountFormat", Statistics?.ArticleCount ?? 0);
    public string TopicCountText => _strings.Format("TopicsCountFormat", Statistics?.TopicCount ?? 0);
    public string ResultCountText => _strings.Format("ResultsCountFormat", Browser.TotalCount);
    public string LastImportText => Statistics?.LastImportAtUtc is { } imported
        ? _strings.Format("LastImportFormat", imported.ToLocalTime())
        : _strings.Get("NoImportsText");
    [ObservableProperty]
    public partial string DatabaseStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchIndexStateText { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (_initialized || IsInitializing)
        {
            return;
        }

        IsInitializing = true;
        InitializationMessage = _strings.Get("LoadingText");
        DatabaseStateText = _strings.Get("OpeningDatabaseText");
        SearchIndexStateText = _strings.Get("PreparingSearchIndexText");
        try
        {
            await _settingsService.LoadAsync(CancellationToken.None);
            _lastIncludeSubtopicsDefault = _settingsService.Current.IncludeSubtopicsByDefault;
            DensityChanged?.Invoke(_settingsService.Current.CompactDensity);
            await _dataService.InitializeAsync(CancellationToken.None);
            var state = _settingsService.Current.Window;
            Browser.RestoreFilterState(state);
            await Topics.LoadAsync(
                _dataService.IsNewDatabase ? null : state.SelectedTopicId,
                CancellationToken.None);
            var scope = _dataService.IsNewDatabase
                ? Topics.UserTopics.FirstOrDefault(topic => topic.Name.Equals("History of Shanghai", StringComparison.OrdinalIgnoreCase))
                    ?? Topics.SelectedNode
                    ?? Topics.RootNodes[0]
                : Topics.SelectedNode ?? Topics.RootNodes[0];
            Topics.SelectedNode = scope;
            await Browser.InitializeAsync(
                scope,
                state.IncludeSubtopics,
                _settingsService.Current.PageSize,
                state.SearchQuery,
                !_dataService.IsNewDatabase && _settingsService.Current.RestoreLastArticle ? state.SelectedArticleId : null,
                !_dataService.IsNewDatabase && _settingsService.Current.RestoreLastArticle ? state.PageNumber : 1);
            if (_dataService.IsNewDatabase
                && Browser.Articles.FirstOrDefault(article => article.Title.Equals("History of Shanghai", StringComparison.OrdinalIgnoreCase)) is { } firstArticle)
            {
                Browser.SelectArticleById(firstArticle.Id);
                await Detail.LoadArticleAsync(firstArticle.Id, CancellationToken.None);
            }
            await RefreshStatisticsAsync();
            DatabaseStateText = _strings.Get("DatabaseReadyText");
            _initialized = true;
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Pedia initialization failed");
            InfoBarSeverity = InfoBarSeverity.Error;
            InfoBarMessage = _strings.OperationFailed;
            IsInfoBarOpen = true;
            DatabaseStateText = _strings.Get("DatabaseUnavailableText");
            SearchIndexStateText = _strings.Get("SearchIndexUnavailableText");
        }
        finally
        {
            IsInitializing = false;
        }
    }

    public async Task SaveSessionAsync()
    {
        var state = _settingsService.Current.Window;
        state.SelectedTopicId = Browser.Scope?.Id;
        state.SelectedArticleId = Detail.Article?.Id;
        state.SearchQuery = Browser.SearchText;
        state.IncludeSubtopics = _pendingIncludeSubtopicsDefault ?? Browser.IncludeSubtopics;
        Browser.SaveFilterState(state);
        await _settingsService.SaveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanUseTitleBarCommands))]
    private async Task NewArticleAsync()
    {
        if (!CanUseTitleBarCommands)
        {
            return;
        }

        if (!await Detail.TryLeaveEditorAsync())
        {
            return;
        }

        IsSettingsVisible = false;
        Browser.ClearSelection();
        Detail.CreateNew(Browser.Scope is { IsSmart: false } topic ? topic : null);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (!CanImport())
        {
            return;
        }

        if (!await Detail.TryLeaveEditorAsync())
        {
            return;
        }

        try
        {
            var files = await _filePicker.PickImportFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            _importCancellation = new CancellationTokenSource();
            IsImporting = true;
            var preview = await _dataService.PreviewImportAsync(files, _importCancellation.Token);
            var result = await _dialogs.ShowImportPreviewAsync(
                preview,
                Topics.UserTopics,
                Browser.Scope is { IsSmart: false } topic ? topic.Id : null);
            if (result is null)
            {
                return;
            }

            var importResult = await _dataService.ImportAsync(new ImportRequest(
                files,
                result.DestinationTopicId,
                result.LanguageCode,
                result.Status,
                result.DuplicateHandling), _importCancellation.Token);
            ShowNotification(_strings.Format(
                "ImportCompleteFormat",
                importResult.ImportedCount,
                importResult.SkippedCount,
                importResult.ErrorCount));
            await RefreshDataAsync();
            if (importResult.Errors.Count > 0)
            {
                await _dialogs.ShowMessageAsync(
                    _strings.Get("ImportErrorsTitle"),
                    string.Join(Environment.NewLine, importResult.Errors));
            }
        }
        catch (OperationCanceledException)
        {
            ShowNotification(_strings.Get("ImportCancelledText"));
            await RefreshDataAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Local file import failed");
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
        finally
        {
            IsImporting = false;
            _importCancellation?.Dispose();
            _importCancellation = null;
        }
    }

    private bool CanImport() => !IsSettingsVisible && CanUseTitleBarCommands;

    [RelayCommand(CanExecute = nameof(CanCancelImport))]
    private void CancelImport() => _importCancellation?.Cancel();

    private bool CanCancelImport() => IsImporting;

    [RelayCommand(CanExecute = nameof(CanUseTitleBarCommands))]
    private async Task RefreshAsync()
    {
        if (CanUseTitleBarCommands)
        {
            await RefreshDataAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseTitleBarCommands))]
    private async Task OpenSettingsAsync()
    {
        if (!CanUseTitleBarCommands)
        {
            return;
        }

        if (!await Detail.TryLeaveEditorAsync())
        {
            return;
        }

        if (Statistics is null)
        {
            await RefreshStatisticsAsync();
        }
        Settings.Load(Statistics!);
        IsSettingsVisible = true;
    }

    public async Task CloseSettingsAsync()
    {
        IsSettingsVisible = false;
        await Task.CompletedTask;
    }

    public void ShowNotification(string message)
    {
        InfoBarSeverity = InfoBarSeverity.Success;
        InfoBarMessage = message;
        IsInfoBarOpen = true;
    }

    private async Task<bool> OnScopeSelectedAsync(TopicNodeViewModel scope)
    {
        if (!await Detail.TryLeaveEditorAsync())
        {
            return false;
        }

        var includeSubtopics = scope.IsSmart
            ? Browser.IncludeSubtopics
            : _pendingIncludeSubtopicsDefault ?? Browser.IncludeSubtopics;
        if (!scope.IsSmart)
        {
            _pendingIncludeSubtopicsDefault = null;
        }
        await Browser.SetScopeAsync(scope, includeSubtopics);
        return true;
    }

    private async Task OnArticleSelectedAsync(ArticleRowViewModel? article)
    {
        var previousArticleId = Detail.Article?.Id;
        if (!await Detail.TryLeaveEditorAsync())
        {
            Browser.SelectArticleById(previousArticleId);
            return;
        }

        if (article is null)
        {
            Detail.ClearArticle();
            return;
        }

        if (Browser.SelectedArticle?.Id != article.Id)
        {
            return;
        }

        await Detail.LoadArticleAsync(article.Id, CancellationToken.None);
    }

    private async Task OnArticleActionRequestedAsync(ArticleRowViewModel article, string action)
    {
        if (!await Detail.TryLeaveEditorAsync())
        {
            Browser.SelectArticleById(Detail.Article?.Id);
            return;
        }

        if (Detail.Article?.Id != article.Id)
        {
            await Detail.LoadArticleAsync(article.Id, CancellationToken.None);
            if (Detail.Article?.Id != article.Id)
            {
                return;
            }
        }

        switch (action)
        {
            case "edit":
                ExecuteIfAvailable(Detail.EditCommand);
                break;
            case "duplicate":
                await ExecuteIfAvailableAsync(Detail.DuplicateCommand);
                break;
            case "add-topics":
                await ExecuteIfAvailableAsync(Detail.ManageTopicsCommand);
                break;
            case "remove-topic" when Browser.Scope is { IsSmart: false } topic:
                await Detail.RemoveFromTopicAsync(topic.Id);
                break;
            case "favorite":
                await ExecuteIfAvailableAsync(Detail.ToggleFavoriteCommand);
                break;
            case "export":
                await ExecuteIfAvailableAsync(Detail.ExportCommand);
                break;
            case "trash":
                await ExecuteIfAvailableAsync(Detail.MoveToTrashCommand);
                break;
            case "restore":
                await ExecuteIfAvailableAsync(Detail.RestoreCommand);
                break;
            case "delete":
                await ExecuteIfAvailableAsync(Detail.DeletePermanentlyCommand);
                break;
        }
    }

    private static void ExecuteIfAvailable(IRelayCommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static Task ExecuteIfAvailableAsync(IAsyncRelayCommand command) =>
        command.CanExecute(null) ? command.ExecuteAsync(null) : Task.CompletedTask;

    private async Task ExportArticlesAsync(IReadOnlyList<long> articleIds)
    {
        if (articleIds.Count == 0 || !await Detail.TryLeaveEditorAsync())
        {
            return;
        }

        var format = await _dialogs.ChooseExportFormatAsync();
        if (format is null)
        {
            return;
        }

        var destination = await _filePicker.PickExportDestinationAsync(format.Value, articleIds.Count > 1);
        if (destination is null)
        {
            return;
        }

        try
        {
            await _dataService.ExportAsync(articleIds, format.Value, destination, CancellationToken.None);
            ShowNotification(articleIds.Count == 1
                ? _strings.Get("ArticleExportedText")
                : _strings.Format("ArticlesExportedFormat", articleIds.Count));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not export selected articles");
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private async Task ApplyBulkActionAsync(
        IReadOnlyList<long> articleIds,
        ArticleBulkActionKind action)
    {
        if (articleIds.Count == 0 || !await Detail.TryLeaveEditorAsync())
        {
            return;
        }

        string? notification = null;
        try
        {
            switch (action)
            {
                case ArticleBulkActionKind.AddToTopics:
                    {
                        var topics = await _dialogs.ChooseTopicsAsync(Topics.UserTopics, new HashSet<long>());
                        if (topics is null || topics.Count == 0)
                        {
                            return;
                        }

                        await _dataService.AddTopicsToArticlesAsync(
                            articleIds,
                            topics.Select(topic => topic.Id).ToArray(),
                            CancellationToken.None);
                        notification = _strings.Format("SelectedArticlesAddedToTopicsFormat", articleIds.Count);
                        break;
                    }
                case ArticleBulkActionKind.RemoveFromCurrentTopic:
                    if (Browser.Scope is not { IsSmart: false, Id: > 0 } topic || Browser.IncludeSubtopics)
                    {
                        return;
                    }

                    await _dataService.RemoveTopicFromArticlesAsync(articleIds, topic.Id, CancellationToken.None);
                    notification = _strings.Format("SelectedArticlesRemovedFromTopicFormat", articleIds.Count);
                    break;
                case ArticleBulkActionKind.ChangeStatus:
                    {
                        var status = await _dialogs.ChooseArticleStatusAsync();
                        if (status is null)
                        {
                            return;
                        }

                        await _dataService.SetStatusForArticlesAsync(articleIds, status, CancellationToken.None);
                        notification = _strings.Format("SelectedArticlesStatusChangedFormat", articleIds.Count);
                        break;
                    }
                case ArticleBulkActionKind.MoveToTrash:
                    if (_settingsService.Current.ConfirmBeforeTrash
                        && !await _dialogs.ConfirmAsync(
                            _strings.Get("MoveArticlesToTrashTitle"),
                            _strings.Format("MoveArticlesToTrashMessageFormat", articleIds.Count),
                            _strings.Get("MoveToTrashButtonText")))
                    {
                        return;
                    }

                    await _dataService.MoveArticlesToTrashAsync(articleIds, CancellationToken.None);
                    notification = _strings.Format("SelectedArticlesMovedToTrashFormat", articleIds.Count);
                    break;
                default:
                    return;
            }

            ShowNotification(notification);
            await RefreshDataAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not apply {BulkAction} to {ArticleCount} selected articles", action, articleIds.Count);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            var currentScope = Browser.Scope;
            var selectedTopicId = currentScope?.Id;
            var selectedArticleId = Detail.Article?.Id;
            await Topics.LoadAsync(selectedTopicId, CancellationToken.None);
            var scope = Topics.SelectedNode
                ?? (currentScope is { IsSmart: false } topic && Topics.ContainsTopic(topic.Id) ? topic : null)
                ?? Topics.RootNodes[0];
            await Browser.SetScopeAsync(scope, Browser.IncludeSubtopics, selectedArticleId);
            await RefreshStatisticsAsync();
            NotifyResultCount();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not refresh Pedia data");
            InfoBarSeverity = InfoBarSeverity.Error;
            InfoBarMessage = _strings.OperationFailed;
            IsInfoBarOpen = true;
        }
    }

    private async Task RefreshStatisticsAsync()
    {
        Statistics = await _dataService.GetStatisticsAsync(CancellationToken.None);
        OnPropertyChanged(nameof(ArticleCountText));
        OnPropertyChanged(nameof(TopicCountText));
        OnPropertyChanged(nameof(LastImportText));
        SearchIndexStateText = Statistics.SearchIndexState;
        if (IsSettingsVisible)
        {
            Settings.Load(Statistics);
        }
    }

    private void NotifyResultCount() => OnPropertyChanged(nameof(ResultCountText));

    private void OnBrowserResultsChanged(object? sender, EventArgs args) => NotifyResultCount();

    private void OnBrowserLoadFailed(object? sender, string message)
    {
        InfoBarSeverity = InfoBarSeverity.Error;
        InfoBarMessage = message;
        IsInfoBarOpen = true;
    }

    private async Task ApplySettingsAsync()
    {
        var includeSubtopicsDefault = _settingsService.Current.IncludeSubtopicsByDefault;
        var defaultChanged = includeSubtopicsDefault != _lastIncludeSubtopicsDefault;
        var applyIncludeSubtopics = defaultChanged && Browser.IsTopicScope
            ? includeSubtopicsDefault
            : (bool?)null;
        _pendingIncludeSubtopicsDefault = defaultChanged && !Browser.IsTopicScope
            ? includeSubtopicsDefault
            : null;
        _lastIncludeSubtopicsDefault = includeSubtopicsDefault;
        await Browser.ApplyLiveSettingsAsync(_settingsService.Current.PageSize, applyIncludeSubtopics);
        Detail.NotifyReadingSettingsChanged();
        DensityChanged?.Invoke(_settingsService.Current.CompactDensity);
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        if (Browser.Scope?.Scope != LibraryScopeKind.Trash
            || !await _dialogs.ConfirmAsync(
                _strings.Get("EmptyTrashTitle"),
                _strings.Get("EmptyTrashMessage"),
                _strings.Get("DeleteButtonText")))
        {
            return;
        }

        try
        {
            await _dataService.EmptyTrashAsync(CancellationToken.None);
            ShowNotification(_strings.Get("TrashEmptiedText"));
            Detail.ClearArticle();
            await RefreshDataAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not empty Trash");
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }
}
