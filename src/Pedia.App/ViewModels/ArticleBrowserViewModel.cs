using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pedia.Models;
using Pedia.Services;

namespace Pedia.ViewModels;

public sealed partial class ArticleBrowserViewModel : ObservableObject
{
    private readonly IPediaDataService _dataService;
    private readonly IStringService _strings;
    private readonly ILogger<ArticleBrowserViewModel> _logger;
    private readonly IReadOnlyList<SearchScopeOption> _allSearchScopes;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _loadCancellation;
    private bool _suppressSelection;
    private bool _suppressReload;
    private bool _synchronizingLanguageFilters;
    private bool _loadingEnabled;
    private long _loadGeneration;
    private long _selectionNotificationVersion;
    private readonly SemaphoreSlim _selectionNotificationGate = new(1, 1);
    private IReadOnlyList<ArticleRowViewModel> _selectedArticles = [];

    public ArticleBrowserViewModel(
        IPediaDataService dataService,
        IStringService strings,
        ILogger<ArticleBrowserViewModel> logger)
    {
        _dataService = dataService;
        _strings = strings;
        _logger = logger;
        _allSearchScopes =
        [
            new(ArticleSearchScopeKind.AllText, _strings.Get("AllTextSearchScope")),
            new(ArticleSearchScopeKind.TitleOnly, _strings.Get("TitleOnlySearchScope")),
            new(ArticleSearchScopeKind.CurrentTopic, _strings.Get("CurrentTopicSearchScope")),
            new(ArticleSearchScopeKind.CurrentTopicAndDescendants, _strings.Get("CurrentTopicDescendantsSearchScope")),
            new(ArticleSearchScopeKind.EntireLibrary, _strings.Get("EntireLibrarySearchScope"))
        ];
        SearchScopes = new ObservableCollection<SearchScopeOption>(_allSearchScopes);
        Languages = [_strings.Get("AllLanguagesOption"), _strings.Get("EnglishOption"), _strings.Get("FinnishOption")];
        ArticleTypes =
        [
            new(null, _strings.Get("AllTypesOption")),
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
            new(null, _strings.Get("AnyStatusOption")),
            new("Draft", _strings.Get("DraftStatus")),
            new("Ready", _strings.Get("ReadyStatus")),
            new("Needs review", _strings.Get("NeedsReviewStatus")),
            new("Archived", _strings.Get("ArchivedStatus"))
        ];
        SourceFilters =
        [
            new(_strings.Get("AnySourceStateOption"), null),
            new(_strings.Get("HasSourceMetadataOption"), true),
            new(_strings.Get("NoSourceMetadataOption"), false)
        ];
        ArchivedFilters =
        [
            new(_strings.Get("AnyArchivedStateOption"), null),
            new(_strings.Get("ArchivedOnlyOption"), true),
            new(_strings.Get("ExcludeArchivedOption"), false)
        ];
        SampleFilters =
        [
            new(_strings.Get("AnyContentOption"), null),
            new(_strings.Get("SampleContentOnlyOption"), true),
            new(_strings.Get("UserContentOnlyOption"), false)
        ];

        _suppressReload = true;
        SelectedSearchScope = SearchScopes[0];
        SelectedLanguage = Languages[0];
        SelectedArticleType = ArticleTypes[0];
        SelectedArticleStatus = ArticleStatuses[0];
        SelectedSourceFilter = SourceFilters[0];
        SelectedArchivedFilter = ArchivedFilters[0];
        SelectedSampleFilter = SampleFilters[0];
        _suppressReload = false;
    }

    public ObservableCollection<ArticleRowViewModel> Articles { get; } = [];
    public ObservableCollection<SearchScopeOption> SearchScopes { get; }
    public IReadOnlyList<string> Languages { get; }
    public IReadOnlyList<ValueLabelOption> ArticleTypes { get; }
    public IReadOnlyList<ValueLabelOption> ArticleStatuses { get; }
    public IReadOnlyList<NullableBooleanFilterOption> SourceFilters { get; }
    public IReadOnlyList<NullableBooleanFilterOption> ArchivedFilters { get; }
    public IReadOnlyList<NullableBooleanFilterOption> SampleFilters { get; }
    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];

    public Func<ArticleRowViewModel?, Task>? ArticleSelected { get; set; }
    public Func<ArticleRowViewModel, string, Task>? ArticleActionRequested { get; set; }
    public Func<IReadOnlyList<long>, Task>? ArticlesExportRequested { get; set; }
    public Func<IReadOnlyList<long>, ArticleBulkActionKind, Task>? ArticlesBulkActionRequested { get; set; }
    public Func<Task>? EmptyTrashRequested { get; set; }
    public event EventHandler? ResultsChanged;
    public event EventHandler<string>? LoadFailed;

    [ObservableProperty] public partial TopicNodeViewModel? Scope { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial SearchScopeOption SelectedSearchScope { get; set; } = null!;
    [ObservableProperty] public partial string SelectedLanguage { get; set; } = string.Empty;
    [ObservableProperty] public partial ValueLabelOption SelectedArticleType { get; set; } = null!;
    [ObservableProperty] public partial ValueLabelOption SelectedArticleStatus { get; set; } = null!;
    [ObservableProperty] public partial bool IncludeEnglish { get; set; }
    [ObservableProperty] public partial bool IncludeFinnish { get; set; }
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    [ObservableProperty] public partial NullableBooleanFilterOption SelectedSourceFilter { get; set; } = null!;
    [ObservableProperty] public partial double MinimumWordCount { get; set; } = double.NaN;
    [ObservableProperty] public partial double MaximumWordCount { get; set; } = double.NaN;
    [ObservableProperty] public partial DateTimeOffset? CreatedFrom { get; set; }
    [ObservableProperty] public partial DateTimeOffset? CreatedTo { get; set; }
    [ObservableProperty] public partial DateTimeOffset? UpdatedFrom { get; set; }
    [ObservableProperty] public partial DateTimeOffset? UpdatedTo { get; set; }
    [ObservableProperty] public partial NullableBooleanFilterOption SelectedArchivedFilter { get; set; } = null!;
    [ObservableProperty] public partial NullableBooleanFilterOption SelectedSampleFilter { get; set; } = null!;
    [ObservableProperty] public partial bool IncludeSubtopics { get; set; }
    [ObservableProperty] public partial int PageNumber { get; set; } = 1;
    [ObservableProperty] public partial int PageSize { get; set; } = 50;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial int TotalPages { get; set; } = 1;
    [ObservableProperty] public partial ArticleSortField SortField { get; set; } = ArticleSortField.Relevance;
    [ObservableProperty] public partial SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial ArticleRowViewModel? SelectedArticle { get; set; }

    public string ScopeTitle => Scope?.Name ?? _strings.Get("AllArticlesNode");
    public string SearchPlaceholder => Scope?.Scope switch
    {
        LibraryScopeKind.Favorites => _strings.Get("SearchFavoritesPlaceholder"),
        LibraryScopeKind.Topic => _strings.Format("SearchInTopicFormat", Scope.Name),
        _ => _strings.Get("SearchAllArticlesPlaceholder")
    };
    public bool IsTopicScope => Scope?.Scope == LibraryScopeKind.Topic;
    public bool CanToggleIncludeSubtopics => IsTopicScope
        && SelectedSearchScope.Kind is ArticleSearchScopeKind.AllText or ArticleSearchScopeKind.TitleOnly;
    public bool CanToggleFavoritesOnly => Scope?.Scope != LibraryScopeKind.Favorites;
    public bool IsTrashScope => Scope?.Scope == LibraryScopeKind.Trash;
    public bool HasActiveSelection => CanUseActiveSelection();
    public int RangeStart => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int RangeEnd => Math.Min(PageNumber * PageSize, TotalCount);
    public string PageRangeText => _strings.Format("PageRangeFormat", RangeStart, RangeEnd, TotalCount);
    public string PageNumberText => $"{PageNumber:N0} / {TotalPages:N0}";
    public bool CanGoPrevious => PageNumber > 1 && !IsLoading;
    public bool CanGoNext => PageNumber < TotalPages && !IsLoading;
    public bool HasResults => Articles.Count > 0;
    public bool HasActiveFilters => ActiveFilterCount > 0;
    public string ActiveFilterSummary => ActiveFilterCount == 0
        ? _strings.Get("NoActiveFiltersText")
        : _strings.Format("ActiveFiltersFormat", ActiveFilterCount);
    public string TitleSortGlyph => GetSortGlyph(ArticleSortField.Title);
    public string LanguageSortGlyph => GetSortGlyph(ArticleSortField.Language);
    public string WordCountSortGlyph => GetSortGlyph(ArticleSortField.WordCount);
    public string StatusSortGlyph => GetSortGlyph(ArticleSortField.Status);
    public string UpdatedSortGlyph => GetSortGlyph(ArticleSortField.Updated);

    private int ActiveFilterCount =>
        (SelectedLanguage != Languages[0] || IncludeEnglish || IncludeFinnish ? 1 : 0)
        + (SelectedArticleType.Value is not null ? 1 : 0)
        + (SelectedArticleStatus.Value is not null ? 1 : 0)
        + (FavoritesOnly ? 1 : 0)
        + (SelectedSourceFilter.Value is not null ? 1 : 0)
        + (!double.IsNaN(MinimumWordCount) ? 1 : 0)
        + (!double.IsNaN(MaximumWordCount) ? 1 : 0)
        + (CreatedFrom is not null || CreatedTo is not null ? 1 : 0)
        + (UpdatedFrom is not null || UpdatedTo is not null ? 1 : 0)
        + (SelectedArchivedFilter.Value is not null ? 1 : 0)
        + (SelectedSampleFilter.Value is not null ? 1 : 0)
        + (IncludeSubtopics ? 1 : 0);

    public async Task InitializeAsync(
        TopicNodeViewModel scope,
        bool includeSubtopics,
        int pageSize,
        string searchText,
        long? preferredArticleId = null,
        int initialPageNumber = 1)
    {
        _suppressReload = true;
        Scope = scope;
        UpdateSearchScopes(scope);
        IncludeSubtopics = includeSubtopics;
        PageSize = pageSize;
        SearchText = searchText;
        PageNumber = Math.Max(1, initialPageNumber);
        _suppressReload = false;
        _loadingEnabled = true;
        NotifyScopeProperties();
        NotifyFilterState();
        await LoadAsync(preferredArticleId);
    }

    public void RestoreFilterState(WindowLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _suppressReload = true;
        SelectedSearchScope = _allSearchScopes.FirstOrDefault(option => option.Kind == state.SearchScope) ?? _allSearchScopes[0];
        _synchronizingLanguageFilters = true;
        SelectedLanguage = state.SelectedLanguageCode switch
        {
            "en" => Languages[1],
            "fi" => Languages[2],
            _ => Languages[0]
        };
        IncludeEnglish = SelectedLanguage == Languages[0] && state.IncludeEnglish;
        IncludeFinnish = SelectedLanguage == Languages[0] && state.IncludeFinnish;
        _synchronizingLanguageFilters = false;
        SelectedArticleType = ArticleTypes.FirstOrDefault(option => option.Value == state.ArticleType) ?? ArticleTypes[0];
        SelectedArticleStatus = ArticleStatuses.FirstOrDefault(option => option.Value == state.ArticleStatus) ?? ArticleStatuses[0];
        FavoritesOnly = state.FavoritesOnly;
        SelectedSourceFilter = SourceFilters.First(option => option.Value == state.HasSources);
        MinimumWordCount = state.MinimumWordCount ?? double.NaN;
        MaximumWordCount = state.MaximumWordCount ?? double.NaN;
        CreatedFrom = state.CreatedFrom;
        CreatedTo = state.CreatedTo;
        UpdatedFrom = state.UpdatedFrom;
        UpdatedTo = state.UpdatedTo;
        SelectedArchivedFilter = ArchivedFilters.First(option => option.Value == state.IsArchived);
        SelectedSampleFilter = SampleFilters.First(option => option.Value == state.IsSample);
        SortField = Enum.IsDefined(state.SortField) ? state.SortField : ArticleSortField.Relevance;
        SortDirection = Enum.IsDefined(state.SortDirection) ? state.SortDirection : SortDirection.Ascending;
        _suppressReload = false;
        NotifyFilterState();
        NotifySortGlyphs();
    }

    public void SaveFilterState(WindowLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.SearchScope = SelectedSearchScope.Kind;
        state.SelectedLanguageCode = SelectedLanguage == Languages[1]
            ? "en"
            : SelectedLanguage == Languages[2] ? "fi" : null;
        state.IncludeEnglish = IncludeEnglish;
        state.IncludeFinnish = IncludeFinnish;
        state.ArticleType = SelectedArticleType.Value;
        state.ArticleStatus = SelectedArticleStatus.Value;
        state.FavoritesOnly = FavoritesOnly;
        state.HasSources = SelectedSourceFilter.Value;
        state.MinimumWordCount = ToNullableCount(MinimumWordCount);
        state.MaximumWordCount = ToNullableCount(MaximumWordCount);
        state.CreatedFrom = CreatedFrom;
        state.CreatedTo = CreatedTo;
        state.UpdatedFrom = UpdatedFrom;
        state.UpdatedTo = UpdatedTo;
        state.IsArchived = SelectedArchivedFilter.Value;
        state.IsSample = SelectedSampleFilter.Value;
        state.SortField = SortField;
        state.SortDirection = SortDirection;
        state.PageNumber = PageNumber;
    }

    public async Task SetScopeAsync(TopicNodeViewModel scope, bool includeSubtopics, long? preferredArticleId = null)
    {
        var scopeChanged = Scope?.Id != scope.Id || Scope?.Scope != scope.Scope;
        _suppressReload = true;
        Scope = scope;
        UpdateSearchScopes(scope);
        IncludeSubtopics = includeSubtopics;
        if (scopeChanged && scope.Scope == LibraryScopeKind.RecentlyEdited)
        {
            SortField = ArticleSortField.Updated;
            SortDirection = SortDirection.Descending;
        }
        if (scopeChanged)
        {
            PageNumber = 1;
        }
        _suppressReload = false;
        NotifyScopeProperties();
        NotifyFilterState();
        if (_loadingEnabled)
        {
            await LoadAsync(preferredArticleId);
        }
    }

    public async Task LoadAsync(long? preferredArticleId = null, CancellationToken cancellationToken = default)
    {
        if (!_loadingEnabled)
        {
            return;
        }

        var generation = ++_loadGeneration;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;
        var query = BuildQuery();
        IsLoading = true;
        UpdatePagingState();
        try
        {
            var result = await _dataService.QueryArticlesAsync(query, token);
            if (generation != _loadGeneration)
            {
                return;
            }

            if (query.PageNumber > result.TotalPages)
            {
                PageNumber = result.TotalPages;
                await LoadAsync(preferredArticleId, cancellationToken);
                return;
            }

            _suppressSelection = true;
            SelectedArticle = null;
            SetSelectedArticles([]);
            Articles.Clear();
            foreach (var article in result.Items)
            {
                Articles.Add(new ArticleRowViewModel(article, _strings));
            }

            TotalCount = result.TotalCount;
            PageNumber = result.PageNumber;
            TotalPages = Math.Max(1, result.TotalPages);
            SelectedArticle = preferredArticleId is not null
                ? Articles.FirstOrDefault(article => article.Id == preferredArticleId) ?? Articles.FirstOrDefault()
                : Articles.FirstOrDefault();
            _suppressSelection = false;
            OnPropertyChanged(nameof(HasResults));
            UpdatePagingState();
            ResultsChanged?.Invoke(this, EventArgs.Empty);

            await NotifyArticleSelectedAsync(SelectedArticle);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not query articles");
            throw;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
                UpdatePagingState();
            }
        }
    }

    public void ClearSelection()
    {
        Interlocked.Increment(ref _selectionNotificationVersion);
        _suppressSelection = true;
        SelectedArticle = null;
        _suppressSelection = false;
    }

    public void SelectArticleById(long? articleId)
    {
        Interlocked.Increment(ref _selectionNotificationVersion);
        _suppressSelection = true;
        SelectedArticle = articleId is null ? null : Articles.FirstOrDefault(article => article.Id == articleId);
        _suppressSelection = false;
    }

    public void SetSelectedArticles(IEnumerable<ArticleRowViewModel> articles)
    {
        _selectedArticles = articles.ToArray();
        ExportSelectedCommand.NotifyCanExecuteChanged();
        AddSelectedToTopicsCommand.NotifyCanExecuteChanged();
        RemoveSelectedFromTopicCommand.NotifyCanExecuteChanged();
        ChangeSelectedStatusCommand.NotifyCanExecuteChanged();
        MoveSelectedToTrashCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasActiveSelection));
    }

    [RelayCommand(CanExecute = nameof(CanExportSelected))]
    private async Task ExportSelectedAsync()
    {
        var articleIds = _selectedArticles
            .Where(article => !article.IsDeleted)
            .Select(article => article.Id)
            .Distinct()
            .ToArray();
        if (articleIds.Length > 0 && ArticlesExportRequested is not null)
        {
            await ArticlesExportRequested(articleIds);
        }
    }

    private bool CanExportSelected() => _selectedArticles.Any(article => !article.IsDeleted);

    [RelayCommand(CanExecute = nameof(CanUseActiveSelection))]
    private Task AddSelectedToTopicsAsync() => RequestBulkActionAsync(ArticleBulkActionKind.AddToTopics);

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedFromTopic))]
    private Task RemoveSelectedFromTopicAsync() => RequestBulkActionAsync(ArticleBulkActionKind.RemoveFromCurrentTopic);

    [RelayCommand(CanExecute = nameof(CanUseActiveSelection))]
    private Task ChangeSelectedStatusAsync() => RequestBulkActionAsync(ArticleBulkActionKind.ChangeStatus);

    [RelayCommand(CanExecute = nameof(CanUseActiveSelection))]
    private Task MoveSelectedToTrashAsync() => RequestBulkActionAsync(ArticleBulkActionKind.MoveToTrash);

    private bool CanUseActiveSelection() => _selectedArticles.Any(article => !article.IsDeleted);

    private bool CanRemoveSelectedFromTopic() => CanUseActiveSelection()
        && Scope is { IsSmart: false, Id: > 0 }
        && !IncludeSubtopics
        && SelectedSearchScope.Kind is not ArticleSearchScopeKind.CurrentTopicAndDescendants
            and not ArticleSearchScopeKind.EntireLibrary;

    private Task RequestBulkActionAsync(ArticleBulkActionKind action)
    {
        var articleIds = _selectedArticles
            .Where(article => !article.IsDeleted)
            .Select(article => article.Id)
            .Distinct()
            .ToArray();
        return articleIds.Length > 0 && ArticlesBulkActionRequested is not null
            ? ArticlesBulkActionRequested(articleIds, action)
            : Task.CompletedTask;
    }

    public async Task OpenSelectedArticleAsync()
    {
        if (SelectedArticle is not null)
        {
            await NotifyArticleSelectedAsync(SelectedArticle);
        }
    }

    public async Task RequestArticleActionAsync(ArticleRowViewModel article, string action)
    {
        _suppressSelection = true;
        try
        {
            SelectedArticle = article;
        }
        finally
        {
            _suppressSelection = false;
        }
        if (ArticleActionRequested is not null)
        {
            await ArticleActionRequested(article, action);
        }
    }

    public async Task ApplyLiveSettingsAsync(int pageSize, bool? includeSubtopics)
    {
        var changed = PageSize != pageSize
            || includeSubtopics is { } value && IncludeSubtopics != value;
        if (!changed)
        {
            return;
        }

        _suppressReload = true;
        PageSize = pageSize;
        if (includeSubtopics is { } include)
        {
            IncludeSubtopics = include;
        }
        PageNumber = 1;
        _suppressReload = false;
        NotifyFilterState();
        await LoadAsync(SelectedArticle?.Id);
    }

    partial void OnSelectedArticleChanged(ArticleRowViewModel? value)
    {
        if (!_suppressSelection)
        {
            _ = NotifyArticleSelectedAsync(value);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressReload || !_loadingEnabled)
        {
            return;
        }
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = DebouncedSearchAsync(_searchCancellation.Token);
    }

    partial void OnSelectedSearchScopeChanged(SearchScopeOption value)
    {
        OnPropertyChanged(nameof(CanToggleIncludeSubtopics));
        RemoveSelectedFromTopicCommand.NotifyCanExecuteChanged();
        NotifyFilterState();
        QueueReload();
    }
    partial void OnSelectedLanguageChanged(string value)
    {
        if (_synchronizingLanguageFilters)
        {
            return;
        }

        if (value != Languages[0])
        {
            _synchronizingLanguageFilters = true;
            IncludeEnglish = false;
            IncludeFinnish = false;
            _synchronizingLanguageFilters = false;
        }
        NotifyFilterState();
        QueueReload();
    }
    partial void OnSelectedArticleTypeChanged(ValueLabelOption value) { NotifyFilterState(); QueueReload(); }
    partial void OnSelectedArticleStatusChanged(ValueLabelOption value) { NotifyFilterState(); QueueReload(); }
    partial void OnIncludeEnglishChanged(bool value) => SynchronizeLanguageCheckbox(value);
    partial void OnIncludeFinnishChanged(bool value) => SynchronizeLanguageCheckbox(value);
    partial void OnFavoritesOnlyChanged(bool value) { NotifyFilterState(); QueueReload(); }
    partial void OnSelectedSourceFilterChanged(NullableBooleanFilterOption value) { NotifyFilterState(); QueueReload(); }
    partial void OnMinimumWordCountChanged(double value) { NotifyFilterState(); QueueReload(); }
    partial void OnMaximumWordCountChanged(double value) { NotifyFilterState(); QueueReload(); }
    partial void OnCreatedFromChanged(DateTimeOffset? value) { NotifyFilterState(); QueueReload(); }
    partial void OnCreatedToChanged(DateTimeOffset? value) { NotifyFilterState(); QueueReload(); }
    partial void OnUpdatedFromChanged(DateTimeOffset? value) { NotifyFilterState(); QueueReload(); }
    partial void OnUpdatedToChanged(DateTimeOffset? value) { NotifyFilterState(); QueueReload(); }
    partial void OnSelectedArchivedFilterChanged(NullableBooleanFilterOption value) { NotifyFilterState(); QueueReload(); }
    partial void OnSelectedSampleFilterChanged(NullableBooleanFilterOption value) { NotifyFilterState(); QueueReload(); }
    partial void OnIncludeSubtopicsChanged(bool value)
    {
        NotifyFilterState();
        RemoveSelectedFromTopicCommand.NotifyCanExecuteChanged();
        QueueReload();
    }
    partial void OnPageSizeChanged(int value) => QueueReload();
    partial void OnSortFieldChanged(ArticleSortField value) => NotifySortGlyphs();
    partial void OnSortDirectionChanged(SortDirection value) => NotifySortGlyphs();

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _suppressReload = true;
        SelectedLanguage = Languages[0];
        SelectedArticleType = ArticleTypes[0];
        SelectedArticleStatus = ArticleStatuses[0];
        IncludeEnglish = false;
        IncludeFinnish = false;
        FavoritesOnly = false;
        SelectedSourceFilter = SourceFilters[0];
        MinimumWordCount = double.NaN;
        MaximumWordCount = double.NaN;
        CreatedFrom = null;
        CreatedTo = null;
        UpdatedFrom = null;
        UpdatedTo = null;
        SelectedArchivedFilter = ArchivedFilters[0];
        SelectedSampleFilter = SampleFilters[0];
        SearchText = string.Empty;
        IncludeSubtopics = false;
        PageNumber = 1;
        _suppressReload = false;
        NotifyFilterState();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        if (EmptyTrashRequested is not null)
        {
            await EmptyTrashRequested();
        }
    }

    [RelayCommand]
    private async Task SortAsync(ArticleSortField field)
    {
        if (SortField == field)
        {
            SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
        }
        else
        {
            SortField = field;
            SortDirection = field == ArticleSortField.Updated ? SortDirection.Descending : SortDirection.Ascending;
        }
        PageNumber = 1;
        await LoadAsync(SelectedArticle?.Id);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task FirstPageAsync() { PageNumber = 1; await LoadAsync(); }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync() { PageNumber--; await LoadAsync(); }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync() { PageNumber++; await LoadAsync(); }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task LastPageAsync() { PageNumber = TotalPages; await LoadAsync(); }

    private async Task DebouncedSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            PageNumber = 1;
            await LoadAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportLoadFailure(exception);
        }
    }

    private void QueueReload()
    {
        if (_suppressReload || !_loadingEnabled)
        {
            return;
        }
        _ = ReloadSafelyAsync();
    }

    private async Task ReloadSafelyAsync()
    {
        try
        {
            PageNumber = 1;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ReportLoadFailure(exception);
        }
    }

    private async Task NotifyArticleSelectedAsync(ArticleRowViewModel? article)
    {
        var requestVersion = Interlocked.Increment(ref _selectionNotificationVersion);
        await _selectionNotificationGate.WaitAsync();
        try
        {
            if (requestVersion == Volatile.Read(ref _selectionNotificationVersion)
                && ArticleSelected is not null)
            {
                await ArticleSelected(article);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not open article {ArticleId}", article?.Id);
            LoadFailed?.Invoke(this, _strings.OperationFailed);
        }
        finally
        {
            _selectionNotificationGate.Release();
        }
    }

    private void ReportLoadFailure(Exception exception)
    {
        _logger.LogError(exception, "Could not update the article list");
        LoadFailed?.Invoke(this, _strings.OperationFailed);
    }

    private ArticleQuery BuildQuery()
    {
        var searchKind = SelectedSearchScope.Kind;
        var effectiveScope = searchKind == ArticleSearchScopeKind.EntireLibrary
            ? LibraryScopeKind.AllArticles
            : Scope?.Scope ?? LibraryScopeKind.AllArticles;
        long? topicId = searchKind == ArticleSearchScopeKind.EntireLibrary
            ? null
            : Scope?.Scope == LibraryScopeKind.Topic ? Scope.Id : null;
        var includeDescendants = searchKind switch
        {
            ArticleSearchScopeKind.CurrentTopic => false,
            ArticleSearchScopeKind.CurrentTopicAndDescendants => true,
            _ => IncludeSubtopics
        };

        return new ArticleQuery(
            effectiveScope,
            topicId,
            includeDescendants,
            SearchText.Trim(),
            searchKind,
            GetLanguageCodes(),
            SelectedArticleType.Value,
            SelectedArticleStatus.Value,
            effectiveScope == LibraryScopeKind.Favorites || FavoritesOnly,
            SelectedSourceFilter.Value,
            ToNullableCount(MinimumWordCount),
            ToNullableCount(MaximumWordCount),
            CreatedFrom?.ToUniversalTime(),
            EndOfDayUtc(CreatedTo),
            UpdatedFrom?.ToUniversalTime(),
            EndOfDayUtc(UpdatedTo),
            SelectedArchivedFilter.Value,
            SelectedSampleFilter.Value,
            SortField,
            SortDirection,
            PageNumber,
            PageSize);
    }

    private IReadOnlyList<string> GetLanguageCodes()
    {
        if (SelectedLanguage == Languages[1]) return ["en"];
        if (SelectedLanguage == Languages[2]) return ["fi"];
        var codes = new List<string>(2);
        if (IncludeEnglish) codes.Add("en");
        if (IncludeFinnish) codes.Add("fi");
        return codes;
    }

    private void SynchronizeLanguageCheckbox(bool isChecked)
    {
        if (_synchronizingLanguageFilters)
        {
            return;
        }

        if (isChecked && SelectedLanguage != Languages[0])
        {
            _synchronizingLanguageFilters = true;
            if (SelectedLanguage == Languages[1]) IncludeEnglish = true;
            if (SelectedLanguage == Languages[2]) IncludeFinnish = true;
            SelectedLanguage = Languages[0];
            _synchronizingLanguageFilters = false;
        }
        NotifyFilterState();
        QueueReload();
    }

    private void UpdateSearchScopes(TopicNodeViewModel scope)
    {
        var selectedKind = SelectedSearchScope?.Kind ?? ArticleSearchScopeKind.AllText;
        var available = scope.Scope == LibraryScopeKind.Topic
            ? _allSearchScopes
            : _allSearchScopes
                .Where(option => option.Kind is not ArticleSearchScopeKind.CurrentTopic
                    and not ArticleSearchScopeKind.CurrentTopicAndDescendants)
                .ToArray();
        SearchScopes.Clear();
        foreach (var option in available)
        {
            SearchScopes.Add(option);
        }
        SelectedSearchScope = SearchScopes.FirstOrDefault(option => option.Kind == selectedKind)
            ?? SearchScopes[0];
    }

    private static int? ToNullableCount(double value) => double.IsNaN(value) ? null : Math.Max(0, (int)value);

    private static DateTimeOffset? EndOfDayUtc(DateTimeOffset? value) => value is null
        ? null
        : value.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime();

    private string GetSortGlyph(ArticleSortField field) => SortField != field
        ? string.Empty
        : SortDirection == SortDirection.Ascending ? "\uE70E" : "\uE70D";

    private void NotifyScopeProperties()
    {
        OnPropertyChanged(nameof(ScopeTitle));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(IsTopicScope));
        OnPropertyChanged(nameof(CanToggleIncludeSubtopics));
        OnPropertyChanged(nameof(CanToggleFavoritesOnly));
        OnPropertyChanged(nameof(IsTrashScope));
        RemoveSelectedFromTopicCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ActiveFilterSummary));
    }

    private void NotifySortGlyphs()
    {
        OnPropertyChanged(nameof(TitleSortGlyph));
        OnPropertyChanged(nameof(LanguageSortGlyph));
        OnPropertyChanged(nameof(WordCountSortGlyph));
        OnPropertyChanged(nameof(StatusSortGlyph));
        OnPropertyChanged(nameof(UpdatedSortGlyph));
    }

    private void UpdatePagingState()
    {
        OnPropertyChanged(nameof(RangeStart));
        OnPropertyChanged(nameof(RangeEnd));
        OnPropertyChanged(nameof(PageRangeText));
        OnPropertyChanged(nameof(PageNumberText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }
}
