using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pedia.Models;
using Pedia.Services;
using Windows.Storage;
using Windows.System;

namespace Pedia.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public event EventHandler? BusyStateChanged;
    private readonly IPediaDataService _dataService;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly IStringService _strings;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IPediaDataService dataService,
        ISettingsService settingsService,
        IFilePickerService filePicker,
        IDialogService dialogs,
        IStringService strings,
        ILogger<SettingsViewModel> logger)
    {
        _dataService = dataService;
        _settingsService = settingsService;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _strings = strings;
        _logger = logger;
        Statuses =
        [
            new("Draft", _strings.Get("DraftStatus")),
            new("Ready", _strings.Get("ReadyStatus")),
            new("Needs review", _strings.Get("NeedsReviewStatus")),
            new("Archived", _strings.Get("ArchivedStatus"))
        ];
    }

    public Func<Task>? BackRequested { get; set; }
    public Func<Task>? DataChanged { get; set; }
    public Func<Task>? SettingsApplied { get; set; }
    public Action<string>? NotificationRequested { get; set; }

    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];
    public IReadOnlyList<ValueLabelOption> Statuses { get; }

    [ObservableProperty] public partial string DefaultLanguageCode { get; set; } = "en";
    [ObservableProperty] public partial string DefaultArticleStatus { get; set; } = "Draft";
    [ObservableProperty] public partial bool RestoreLastArticle { get; set; } = true;
    [ObservableProperty] public partial bool ConfirmBeforeTrash { get; set; } = true;
    [ObservableProperty] public partial int PageSize { get; set; } = 50;
    [ObservableProperty] public partial bool IncludeSubtopicsByDefault { get; set; }
    [ObservableProperty] public partial double ArticleBodyFontSize { get; set; } = 16;
    [ObservableProperty] public partial double ArticleLineSpacing { get; set; } = 24;
    [ObservableProperty] public partial double MaximumReadingWidth { get; set; } = 860;
    [ObservableProperty] public partial bool RememberScrollPositions { get; set; } = true;
    [ObservableProperty] public partial bool CompactDensity { get; set; } = true;
    [ObservableProperty] public partial LibraryStatistics? Statistics { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDataFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackupNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildSearchIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSampleContentCommand))]
    public partial bool IsBusy { get; set; }

    public bool CanInteract => !IsBusy;

    partial void OnIsBusyChanged(bool value) => BusyStateChanged?.Invoke(this, EventArgs.Empty);

    public string DatabasePath => Statistics?.DatabasePath ?? string.Empty;
    public string DatabaseSize => Statistics is null ? string.Empty : FormatFileSize(Statistics.DatabaseSizeBytes);
    public string SchemaVersion => Statistics?.SchemaVersion.ToString() ?? string.Empty;
    public string ArticleCount => Statistics?.ArticleCount.ToString("N0") ?? string.Empty;
    public string TopicCount => Statistics?.TopicCount.ToString("N0") ?? string.Empty;
    public string SourceCount => Statistics?.SourceCount.ToString("N0") ?? string.Empty;
    public string SearchIndexState => Statistics?.SearchIndexState ?? string.Empty;
    [SuppressMessage(
        "Maintainability",
        "S2325",
        Justification = "WinUI binds this value through the view-model instance.")]
    [SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "WinUI binds this value through the view-model instance.")]
    public string VersionText => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void Load(LibraryStatistics statistics)
    {
        var settings = _settingsService.Current;
        DefaultLanguageCode = settings.DefaultLanguageCode;
        DefaultArticleStatus = settings.DefaultArticleStatus;
        RestoreLastArticle = settings.RestoreLastArticle;
        ConfirmBeforeTrash = settings.ConfirmBeforeTrash;
        PageSize = settings.PageSize;
        IncludeSubtopicsByDefault = settings.IncludeSubtopicsByDefault;
        ArticleBodyFontSize = settings.ArticleBodyFontSize;
        ArticleLineSpacing = settings.ArticleLineSpacing;
        MaximumReadingWidth = settings.MaximumReadingWidth;
        RememberScrollPositions = settings.RememberScrollPositions;
        CompactDensity = settings.CompactDensity;
        Statistics = statistics;
        NotifyStatisticsProperties();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BackAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (BackRequested is not null)
        {
            await BackRequested();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SaveSettingsAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var settings = _settingsService.Current;
            settings.DefaultLanguageCode = string.IsNullOrWhiteSpace(DefaultLanguageCode) ? "en" : DefaultLanguageCode.Trim();
            settings.DefaultArticleStatus = DefaultArticleStatus;
            settings.RestoreLastArticle = RestoreLastArticle;
            settings.ConfirmBeforeTrash = ConfirmBeforeTrash;
            settings.PageSize = PageSize;
            settings.IncludeSubtopicsByDefault = IncludeSubtopicsByDefault;
            ArticleBodyFontSize = NormalizeReadingSetting(ArticleBodyFontSize, 16, 13, 24);
            ArticleLineSpacing = NormalizeReadingSetting(ArticleLineSpacing, 24, 19, 38);
            MaximumReadingWidth = NormalizeReadingSetting(MaximumReadingWidth, 860, 600, 1100);
            settings.ArticleBodyFontSize = ArticleBodyFontSize;
            settings.ArticleLineSpacing = ArticleLineSpacing;
            settings.MaximumReadingWidth = MaximumReadingWidth;
            settings.RememberScrollPositions = RememberScrollPositions;
            settings.CompactDensity = CompactDensity;
            await _settingsService.SaveAsync();
            if (SettingsApplied is not null)
            {
                await SettingsApplied();
            }
            NotificationRequested?.Invoke(_strings.Get("SettingsSavedText"));
            if (DataChanged is not null)
            {
                await DataChanged();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task OpenDataFolderAsync()
    {
        if (Statistics?.DatabasePath is not { Length: > 0 } path || !TryBeginOperation())
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not open the Pedia data folder");
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BackupNowAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var destination = await _filePicker.PickBackupDestinationAsync();
            if (destination is null)
            {
                return;
            }

            await RunDataOperationCoreAsync(
                () => _dataService.CreateBackupAsync(destination),
                _strings.Get("BackupCreatedText"),
                refresh: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task RestoreBackupAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        string? source = null;
        try
        {
            source = await _filePicker.PickBackupSourceAsync();
            if (source is null)
            {
                return;
            }

            var validation = await _dataService.ValidateBackupAsync(source);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Backup validation failed for {BackupPath}: {ValidationError}", source, validation.ErrorMessage);
                await _dialogs.ShowErrorAsync(_strings.Get("InvalidBackupMessage"));
                return;
            }

            if (!await _dialogs.ConfirmAsync(
                    _strings.Get("RestoreBackupTitle"),
                    _strings.Get("RestoreBackupMessage"),
                    _strings.Get("RestoreButtonText")))
            {
                return;
            }

            await _dataService.RestoreBackupAsync(source);
            NotificationRequested?.Invoke(_strings.Get("BackupRestoredText"));
            await NotifyDataChangedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not restore Pedia backup {BackupPath}", source);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task RebuildSearchIndexAsync() => await RunDataOperationAsync(
        () => _dataService.RebuildSearchIndexAsync(),
        _strings.Get("SearchIndexRebuiltText"),
        refresh: true);

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task DeleteSampleContentAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            if (!await _dialogs.ConfirmAsync(
                    _strings.Get("DeleteSamplesTitle"),
                    _strings.Get("DeleteSamplesMessage"),
                    _strings.Get("DeleteButtonText")))
            {
                return;
            }

            await RunDataOperationCoreAsync(
                () => _dataService.DeleteSampleContentAsync(),
                _strings.Get("SampleContentDeletedText"),
                refresh: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunDataOperationAsync(Func<Task> operation, string notification, bool refresh)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            await RunDataOperationCoreAsync(operation, notification, refresh);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunDataOperationCoreAsync(Func<Task> operation, string notification, bool refresh)
    {
        try
        {
            await operation();
            NotificationRequested?.Invoke(notification);
            if (refresh)
            {
                await NotifyDataChangedAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A Pedia data settings operation failed");
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private bool TryBeginOperation()
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        return true;
    }

    private async Task NotifyDataChangedAsync()
    {
        if (DataChanged is not null)
        {
            await DataChanged();
        }
    }

    partial void OnStatisticsChanged(LibraryStatistics? value) => NotifyStatisticsProperties();

    private void NotifyStatisticsProperties()
    {
        OnPropertyChanged(nameof(DatabasePath));
        OnPropertyChanged(nameof(DatabaseSize));
        OnPropertyChanged(nameof(SchemaVersion));
        OnPropertyChanged(nameof(ArticleCount));
        OnPropertyChanged(nameof(TopicCount));
        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(SearchIndexState));
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }

    private static double NormalizeReadingSetting(double value, double defaultValue, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : defaultValue;
}
