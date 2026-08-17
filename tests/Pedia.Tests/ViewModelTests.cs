using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Pedia.Models;
using Pedia.Services;
using Pedia.ViewModels;

namespace Pedia.Tests;

public sealed class ArticleBrowserViewModelTests
{
    [Fact]
    public async Task Search_debounce_queries_only_the_latest_rapidly_entered_text()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        data.Queries.Clear();
        var latestApplied = ResultApplied(browser, "Newest");
        data.QueryHandler = (query, _) => Task.FromResult(Page(Row(2, query.SearchText == "new" ? "Newest" : "Unexpected")));

        browser.SearchText = "n";
        await Task.Delay(50, TestContext.Current.CancellationToken);
        browser.SearchText = "new";

        await latestApplied.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var query = Assert.Single(data.Queries);
        Assert.Equal("new", query.SearchText);
        Assert.Equal("Newest", Assert.Single(browser.Articles).Title);
    }

    [Fact]
    public async Task New_search_cancels_the_in_flight_query_and_only_applies_latest_results()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        data.Queries.Clear();
        var oldStarted = NewSignal();
        var oldCancelled = NewSignal();
        var latestApplied = ResultApplied(browser, "Latest result");
        data.QueryHandler = async (query, cancellationToken) =>
        {
            if (query.SearchText == "old")
            {
                oldStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    oldCancelled.TrySetResult();
                    throw;
                }
            }

            return Page(Row(3, "Latest result"));
        };

        browser.SearchText = "old";
        await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        browser.SearchText = "latest";

        await oldCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await latestApplied.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(["old", "latest"], data.Queries.Select(query => query.SearchText));
        Assert.Equal("Latest result", Assert.Single(browser.Articles).Title);
    }

    [Fact]
    public async Task Active_filters_map_to_the_database_query_without_losing_selection_state()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);
        var createdFrom = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedFrom = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        browser.SelectedSearchScope = browser.SearchScopes.Single(option => option.Kind == ArticleSearchScopeKind.CurrentTopicAndDescendants);
        browser.IncludeEnglish = true;
        browser.IncludeFinnish = true;
        browser.SelectedArticleType = browser.ArticleTypes.Single(option => option.Value == "Place");
        browser.SelectedArticleStatus = browser.ArticleStatuses.Single(option => option.Value == "Ready");
        browser.FavoritesOnly = true;
        browser.SelectedSourceFilter = browser.SourceFilters.Single(option => option.Value == true);
        browser.MinimumWordCount = 12;
        browser.MaximumWordCount = 456;
        browser.CreatedFrom = createdFrom;
        browser.UpdatedFrom = updatedFrom;
        browser.SelectedArchivedFilter = browser.ArchivedFilters.Single(option => option.Value == false);
        browser.SelectedSampleFilter = browser.SampleFilters.Single(option => option.Value == true);
        browser.SortField = ArticleSortField.Updated;
        browser.SortDirection = SortDirection.Descending;

        await browser.InitializeAsync(CreateTopic(42), includeSubtopics: false, pageSize: 25, searchText: "  harbor  ");

        var query = Assert.Single(data.Queries);
        Assert.Equal(LibraryScopeKind.Topic, query.Scope);
        Assert.Equal(42, query.TopicId);
        Assert.True(query.IncludeDescendants);
        Assert.Equal("harbor", query.SearchText);
        Assert.Equal(ArticleSearchScopeKind.CurrentTopicAndDescendants, query.SearchScope);
        Assert.Equal(["en", "fi"], query.LanguageCodes);
        Assert.Equal("Place", query.ArticleType);
        Assert.Equal("Ready", query.Status);
        Assert.True(query.FavoritesOnly);
        Assert.True(query.HasSources);
        Assert.Equal(12, query.MinimumWordCount);
        Assert.Equal(456, query.MaximumWordCount);
        Assert.Equal(createdFrom, query.CreatedFromUtc);
        Assert.Equal(updatedFrom, query.UpdatedFromUtc);
        Assert.False(query.IsArchived);
        Assert.True(query.IsSample);
        Assert.Equal(ArticleSortField.Updated, query.SortField);
        Assert.Equal(SortDirection.Descending, query.SortDirection);
        Assert.Equal(1, query.PageNumber);
        Assert.Equal(25, query.PageSize);
        Assert.True(browser.HasActiveFilters);
        Assert.Equal("11 active filters", browser.ActiveFilterSummary);
    }

    [Fact]
    public async Task Active_filters_round_trip_through_the_persisted_window_state()
    {
        var source = CreateBrowser(new FakePediaDataService());
        source.SelectedSearchScope = source.SearchScopes.Single(option => option.Kind == ArticleSearchScopeKind.TitleOnly);
        source.IncludeFinnish = true;
        source.SelectedArticleType = source.ArticleTypes.Single(option => option.Value == "Concept");
        source.SelectedArticleStatus = source.ArticleStatuses.Single(option => option.Value == "Needs review");
        source.FavoritesOnly = true;
        source.SelectedSourceFilter = source.SourceFilters.Single(option => option.Value == false);
        source.MinimumWordCount = 25;
        source.MaximumWordCount = 250;
        source.CreatedFrom = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
        source.UpdatedTo = new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);
        source.SelectedArchivedFilter = source.ArchivedFilters.Single(option => option.Value == false);
        source.SelectedSampleFilter = source.SampleFilters.Single(option => option.Value == true);
        source.SortField = ArticleSortField.WordCount;
        source.SortDirection = SortDirection.Descending;
        var state = new WindowLayoutState();

        source.SaveFilterState(state);
        var restoredData = new FakePediaDataService();
        var restored = CreateBrowser(restoredData);
        restored.RestoreFilterState(state);
        await restored.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);

        var query = Assert.Single(restoredData.Queries);
        Assert.Equal(ArticleSearchScopeKind.TitleOnly, query.SearchScope);
        Assert.Equal(["fi"], query.LanguageCodes);
        Assert.Equal("Concept", query.ArticleType);
        Assert.Equal("Needs review", query.Status);
        Assert.True(query.FavoritesOnly);
        Assert.False(query.HasSources);
        Assert.Equal(25, query.MinimumWordCount);
        Assert.Equal(250, query.MaximumWordCount);
        Assert.Equal(state.CreatedFrom, query.CreatedFromUtc);
        Assert.Equal(state.UpdatedTo!.Value.Date.AddDays(1).AddTicks(-1), query.UpdatedToUtc);
        Assert.False(query.IsArchived);
        Assert.True(query.IsSample);
        Assert.Equal(ArticleSortField.WordCount, query.SortField);
        Assert.Equal(SortDirection.Descending, query.SortDirection);
    }

    [Fact]
    public async Task Language_quick_filter_and_multi_select_remain_visually_and_logically_consistent()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);

        browser.SelectedLanguage = browser.Languages[1];
        browser.IncludeFinnish = true;

        Assert.Equal(browser.Languages[0], browser.SelectedLanguage);
        Assert.True(browser.IncludeEnglish);
        Assert.True(browser.IncludeFinnish);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        Assert.Equal(["en", "fi"], data.Queries.Last().LanguageCodes);

        browser.SelectedLanguage = browser.Languages[1];
        Assert.False(browser.IncludeEnglish);
        Assert.False(browser.IncludeFinnish);
    }

    [Fact]
    public async Task Smart_scopes_hide_topic_only_search_modes_and_topic_scopes_restore_them()
    {
        var browser = CreateBrowser(new FakePediaDataService());
        var smartScope = new TopicNodeViewModel(
            -1, null, "All articles", null, -1, 0, LibraryScopeKind.AllArticles, true, string.Empty, "All articles");

        await browser.InitializeAsync(smartScope, includeSubtopics: false, pageSize: 50, searchText: string.Empty);

        Assert.DoesNotContain(browser.SearchScopes, option => option.Kind == ArticleSearchScopeKind.CurrentTopic);
        Assert.DoesNotContain(browser.SearchScopes, option => option.Kind == ArticleSearchScopeKind.CurrentTopicAndDescendants);

        await browser.SetScopeAsync(CreateTopic(), includeSubtopics: false);
        Assert.Contains(browser.SearchScopes, option => option.Kind == ArticleSearchScopeKind.CurrentTopic);
        Assert.Contains(browser.SearchScopes, option => option.Kind == ArticleSearchScopeKind.CurrentTopicAndDescendants);
    }

    [Fact]
    public async Task Applying_changed_defaults_updates_the_current_topic_query_once()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        data.Queries.Clear();

        await browser.ApplyLiveSettingsAsync(pageSize: 25, includeSubtopics: true);

        var query = Assert.Single(data.Queries);
        Assert.Equal(25, query.PageSize);
        Assert.True(query.IncludeDescendants);
    }

    [Fact]
    public async Task Preferred_selection_updates_the_callback_and_paging_command_enablement()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (_, _) => Task.FromResult(new PageResult<ArticleListData>(
                [Row(1, "First"), Row(2, "Second")], 3, 1, 2))
        };
        var browser = CreateBrowser(data);
        long? selectedId = null;
        browser.ArticleSelected = article =>
        {
            selectedId = article?.Id;
            return Task.CompletedTask;
        };

        await browser.InitializeAsync(
            CreateTopic(),
            includeSubtopics: false,
            pageSize: 2,
            searchText: string.Empty,
            preferredArticleId: 2);

        Assert.Equal(2, browser.SelectedArticle?.Id);
        Assert.Equal(2, selectedId);
        Assert.True(browser.NextPageCommand.CanExecute(null));
        Assert.False(browser.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Multiple_selected_articles_are_exported_as_one_operation()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (_, _) => Task.FromResult(Page(Row(1, "First"), Row(2, "Second")))
        };
        var browser = CreateBrowser(data);
        IReadOnlyList<long>? exportedIds = null;
        browser.ArticlesExportRequested = ids =>
        {
            exportedIds = ids;
            return Task.CompletedTask;
        };
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);

        browser.SetSelectedArticles(browser.Articles);
        await browser.ExportSelectedCommand.ExecuteAsync(null);

        Assert.Equal([1, 2], exportedIds);
    }

    [Theory]
    [InlineData(ArticleBulkActionKind.AddToTopics)]
    [InlineData(ArticleBulkActionKind.ChangeStatus)]
    [InlineData(ArticleBulkActionKind.MoveToTrash)]
    public async Task Active_multi_selection_routes_each_bulk_action_once(ArticleBulkActionKind action)
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (_, _) => Task.FromResult(Page(Row(1, "First"), Row(2, "Second")))
        };
        var browser = CreateBrowser(data);
        IReadOnlyList<long>? requestedIds = null;
        ArticleBulkActionKind? requestedAction = null;
        browser.ArticlesBulkActionRequested = (ids, requested) =>
        {
            requestedIds = ids;
            requestedAction = requested;
            return Task.CompletedTask;
        };
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        browser.SetSelectedArticles(browser.Articles);

        var command = action switch
        {
            ArticleBulkActionKind.AddToTopics => browser.AddSelectedToTopicsCommand,
            ArticleBulkActionKind.ChangeStatus => browser.ChangeSelectedStatusCommand,
            _ => browser.MoveSelectedToTrashCommand
        };
        await command.ExecuteAsync(null);

        Assert.Equal([1, 2], requestedIds);
        Assert.Equal(action, requestedAction);
    }

    [Fact]
    public async Task Remove_selected_from_topic_is_available_only_for_a_direct_topic_scope()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (_, _) => Task.FromResult(Page(Row(1, "First"), Row(2, "Second")))
        };
        var browser = CreateBrowser(data);
        ArticleBulkActionKind? requestedAction = null;
        browser.ArticlesBulkActionRequested = (_, action) =>
        {
            requestedAction = action;
            return Task.CompletedTask;
        };
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        browser.SetSelectedArticles(browser.Articles);

        Assert.True(browser.RemoveSelectedFromTopicCommand.CanExecute(null));
        await browser.RemoveSelectedFromTopicCommand.ExecuteAsync(null);
        Assert.Equal(ArticleBulkActionKind.RemoveFromCurrentTopic, requestedAction);

        browser.IncludeSubtopics = true;
        Assert.False(browser.RemoveSelectedFromTopicCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refreshing_the_same_scope_preserves_the_current_page_and_preferred_selection()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (query, _) => Task.FromResult(query.PageNumber == 2
                ? new PageResult<ArticleListData>([Row(51, "Page two")], 51, 2, 50)
                : new PageResult<ArticleListData>([Row(1, "Page one")], 51, 1, 50))
        };
        var browser = CreateBrowser(data);
        var scope = CreateTopic();

        await browser.InitializeAsync(
            scope,
            includeSubtopics: false,
            pageSize: 50,
            searchText: string.Empty,
            preferredArticleId: 51,
            initialPageNumber: 2);
        await browser.SetScopeAsync(CreateTopic(scope.Id), includeSubtopics: false, preferredArticleId: 51);

        Assert.Equal(2, data.Queries.Last().PageNumber);
        Assert.Equal(51, browser.SelectedArticle?.Id);
    }

    [Fact]
    public async Task Persisted_filter_state_round_trips_the_current_page()
    {
        var source = CreateBrowser(new FakePediaDataService());
        source.PageNumber = 3;
        var state = new WindowLayoutState();

        source.SaveFilterState(state);
        var restoredData = new FakePediaDataService
        {
            QueryHandler = (query, _) => Task.FromResult(new PageResult<ArticleListData>([], 101, query.PageNumber, 50))
        };
        var restored = CreateBrowser(restoredData);
        restored.RestoreFilterState(state);
        await restored.InitializeAsync(
            CreateTopic(),
            includeSubtopics: false,
            pageSize: 50,
            searchText: string.Empty,
            initialPageNumber: state.PageNumber);

        Assert.Equal(3, Assert.Single(restoredData.Queries).PageNumber);
    }

    [Fact]
    public async Task Current_page_clamps_after_a_mutation_reduces_the_page_count()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (query, _) => Task.FromResult(query.PageNumber == 3
                ? new PageResult<ArticleListData>([], 50, 3, 25)
                : new PageResult<ArticleListData>([Row(50, "Last remaining")], 50, 2, 25))
        };
        var browser = CreateBrowser(data);

        await browser.InitializeAsync(
            CreateTopic(),
            includeSubtopics: false,
            pageSize: 25,
            searchText: string.Empty,
            initialPageNumber: 3);

        Assert.Equal([3, 2], data.Queries.Select(query => query.PageNumber));
        Assert.Equal(2, browser.PageNumber);
        Assert.Equal(50, browser.SelectedArticle?.Id);
    }

    [Fact]
    public async Task Empty_results_explicitly_clear_the_reader_selection()
    {
        var browser = CreateBrowser(new FakePediaDataService());
        ArticleRowViewModel? notifiedArticle = new ArticleRowViewModel(Row(99, "Stale"), new FakeStringService());
        browser.ArticleSelected = article =>
        {
            notifiedArticle = article;
            return Task.CompletedTask;
        };

        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);

        Assert.Null(notifiedArticle);
    }

    [Fact]
    public async Task Programmatic_selection_restore_invalidates_a_queued_obsolete_notification()
    {
        var data = new FakePediaDataService
        {
            QueryHandler = (_, _) => Task.FromResult(Page(Row(1, "First"), Row(2, "Second"), Row(3, "Third")))
        };
        var browser = CreateBrowser(data);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var notifiedIds = new List<long?>();
        browser.ArticleSelected = async article =>
        {
            notifiedIds.Add(article?.Id);
            if (article?.Id == 2)
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
        };

        browser.SelectedArticle = browser.Articles.Single(article => article.Id == 2);
        await firstStarted.Task;
        browser.SelectedArticle = browser.Articles.Single(article => article.Id == 3);
        browser.SelectArticleById(1);
        releaseFirst.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal([2], notifiedIds);
        Assert.Equal(1, browser.SelectedArticle?.Id);
    }

    [Fact]
    public async Task Text_search_defaults_to_relevance_but_explicit_title_sort_is_preserved()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);

        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: "shanghai");
        Assert.Equal(ArticleSortField.Relevance, data.Queries.Last().SortField);

        await browser.SortCommand.ExecuteAsync(ArticleSortField.Title);
        Assert.Equal(ArticleSortField.Title, data.Queries.Last().SortField);
    }

    [Fact]
    public async Task Recently_edited_scope_starts_with_updated_descending_sort()
    {
        var data = new FakePediaDataService();
        var browser = CreateBrowser(data);
        await browser.InitializeAsync(CreateTopic(), includeSubtopics: false, pageSize: 50, searchText: string.Empty);

        await browser.SetScopeAsync(
            new TopicNodeViewModel(
                -3,
                null,
                "Recently edited",
                null,
                -1,
                0,
                LibraryScopeKind.RecentlyEdited,
                true,
                string.Empty,
                "Recently edited"),
            includeSubtopics: false);

        var query = data.Queries.Last();
        Assert.Equal(LibraryScopeKind.RecentlyEdited, query.Scope);
        Assert.Equal(ArticleSortField.Updated, query.SortField);
        Assert.Equal(SortDirection.Descending, query.SortDirection);
    }

    private static ArticleBrowserViewModel CreateBrowser(FakePediaDataService data) =>
        new(data, new FakeStringService(), NullLogger<ArticleBrowserViewModel>.Instance);

    private static TopicNodeViewModel CreateTopic(long id = 7) =>
        new(id, null, "History", null, 0, 0, LibraryScopeKind.Topic, false, string.Empty, "History");

    private static ArticleListData Row(long id, string title) =>
        new(id, title, "en", 10, "Draft", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), false, false, null);

    private static PageResult<ArticleListData> Page(params ArticleListData[] rows) =>
        new(rows, rows.Length, 1, 50);

    private static Task ResultApplied(ArticleBrowserViewModel browser, string expectedTitle)
    {
        var applied = NewSignal();
        browser.ResultsChanged += (_, _) =>
        {
            if (browser.Articles.SingleOrDefault()?.Title == expectedTitle)
            {
                applied.TrySetResult();
            }
        };
        return applied.Task;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class ArticleDetailViewModelTests
{
    [Fact]
    public async Task Duplicated_article_remains_selected_and_opens_in_edit_mode_after_refresh()
    {
        var data = new FakePediaDataService
        {
            Article = Document(17),
            DuplicateArticleId = 22,
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId))
        };
        var detail = CreateDetail(data, new FakeDialogService());
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);
        detail.ArticleChanged = () => detail.LoadArticleAsync(22);

        await detail.DuplicateCommand.ExecuteAsync(null);

        Assert.Equal(22, detail.Article?.Id);
        Assert.Equal(22, detail.Editor?.Id);
        Assert.True(detail.IsEditing);
        Assert.True(detail.IsDirty is false);
    }

    [Fact]
    public async Task Confirmed_trash_action_is_cancelled_if_the_current_article_changes_during_the_dialog()
    {
        var data = new FakePediaDataService
        {
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId))
        };
        var dialogs = new FakeDialogService();
        var detail = CreateDetail(data, dialogs);
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);
        dialogs.ConfirmHandler = async () =>
        {
            await detail.LoadArticleAsync(18);
            return true;
        };

        await detail.MoveToTrashCommand.ExecuteAsync(null);

        Assert.Empty(data.TrashedArticleIds);
        Assert.Equal(18, detail.Article?.Id);
    }

    [Fact]
    public async Task Slower_obsolete_selection_cannot_replace_the_latest_article()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = new FakePediaDataService
        {
            ArticleHandler = async (articleId, _) =>
            {
                if (articleId == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task;
                }

                return Document(articleId);
            }
        };
        var detail = CreateDetail(data, new FakeDialogService());

        var firstLoad = detail.LoadArticleAsync(1, TestContext.Current.CancellationToken);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await detail.LoadArticleAsync(2, TestContext.Current.CancellationToken);
        releaseFirst.TrySetResult();
        await firstLoad;

        Assert.Equal(2, detail.Article?.Id);
    }

    [Fact]
    public async Task Discarding_a_dirty_new_article_clears_editor_state_and_disables_edit_commands()
    {
        var data = new FakePediaDataService();
        var dialogs = new FakeDialogService { UnsavedChoice = UnsavedChangesChoice.Discard };
        var detail = new ArticleDetailViewModel(
            data,
            dialogs,
            new FakeFilePickerService(),
            new FakeSettingsService(),
            new FakeStringService(),
            NullLogger<ArticleDetailViewModel>.Instance);
        var selectedTopic = new TopicNodeViewModel(
            9, null, "Selected", null, 0, 0, LibraryScopeKind.Topic, false, string.Empty, "Selected");

        detail.CreateNew(selectedTopic);

        Assert.True(detail.IsNewArticle);
        Assert.True(detail.IsEditing);
        Assert.True(detail.IsDirty);
        Assert.Equal("fi", detail.Editor?.LanguageCode);
        Assert.Equal("Ready", detail.Editor?.Status);
        Assert.Equal(9, Assert.Single(detail.Editor!.Topics).TopicId);
        Assert.True(detail.SaveCommand.CanExecute(null));
        Assert.True(detail.CancelEditCommand.CanExecute(null));
        Assert.False(detail.EditCommand.CanExecute(null));

        Assert.True(await detail.TryLeaveEditorAsync());
        Assert.Null(detail.Editor);
        Assert.False(detail.IsNewArticle);
        Assert.False(detail.IsEditing);
        Assert.False(detail.IsDirty);
        Assert.False(detail.SaveCommand.CanExecute(null));
        Assert.False(detail.CancelEditCommand.CanExecute(null));
        Assert.Equal(0, data.SaveArticleCallCount);
    }

    [Fact]
    public async Task Managing_topics_from_reader_preserves_primary_and_saves_selected_assignments()
    {
        var article = Document(
            new ArticleTopicData(1, "History", true),
            new ArticleTopicData(2, "History / Asia", false));
        var data = new FakePediaDataService { Article = article };
        var dialogs = new FakeDialogService
        {
            TopicChoice =
            [
                Topic(1, "History"),
                Topic(3, "Geography")
            ]
        };
        var detail = CreateDetail(data, dialogs);
        detail.TopicProvider = () =>
        [
            Topic(1, "History"),
            Topic(2, "History / Asia"),
            Topic(3, "Geography")
        ];

        await detail.LoadArticleAsync(article.Id, TestContext.Current.CancellationToken);
        await detail.ManageTopicsCommand.ExecuteAsync(null);

        Assert.Equal(1, data.ReplaceArticleTopicsCallCount);
        Assert.Equal(0, data.SaveArticleCallCount);
        Assert.Collection(
            data.SavedTopics!,
            topic => { Assert.Equal(1, topic.TopicId); Assert.True(topic.IsPrimary); },
            topic => { Assert.Equal(3, topic.TopicId); Assert.False(topic.IsPrimary); });
    }

    [Fact]
    public async Task Export_is_cancelled_if_the_selected_article_changes_while_choosing_a_destination()
    {
        var data = new FakePediaDataService
        {
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId))
        };
        var dialogs = new FakeDialogService { ExportFormatChoice = ExportFormat.PediaJson };
        ArticleDetailViewModel? detail = null;
        var picker = new FakeFilePickerService
        {
            PickHandler = async (_, _) =>
            {
                await detail!.LoadArticleAsync(18);
                return "article.pedia.json";
            }
        };
        detail = CreateDetail(data, dialogs, picker);
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);

        await detail.ExportCommand.ExecuteAsync(null);

        Assert.Empty(data.ExportedArticleIds);
        Assert.Equal(18, detail.Article?.Id);
    }

    [Fact]
    public async Task Reader_topic_changes_are_cancelled_if_the_selected_article_changes_during_the_dialog()
    {
        var data = new FakePediaDataService
        {
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId))
        };
        var dialogs = new FakeDialogService();
        ArticleDetailViewModel? detail = null;
        dialogs.ChooseTopicsHandler = async (_, _) =>
        {
            await detail!.LoadArticleAsync(18);
            return [Topic(9, "New topic")];
        };
        detail = CreateDetail(data, dialogs);
        detail.TopicProvider = () => [Topic(9, "New topic")];
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);

        await detail.ManageTopicsCommand.ExecuteAsync(null);

        Assert.Empty(data.ReplacedArticleIds);
        Assert.Equal(18, detail.Article?.Id);
    }

    [Fact]
    public async Task Editor_topic_dialog_does_not_continue_after_the_editor_is_replaced()
    {
        var data = new FakePediaDataService
        {
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId))
        };
        var dialogs = new FakeDialogService();
        ArticleDetailViewModel? detail = null;
        dialogs.ChooseTopicsHandler = async (_, _) =>
        {
            await detail!.LoadArticleAsync(18);
            return [Topic(9, "New topic")];
        };
        detail = CreateDetail(data, dialogs);
        detail.TopicProvider = () => [Topic(9, "New topic")];
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);
        detail.EditCommand.Execute(null);

        await detail.AddTopicsCommand.ExecuteAsync(null);

        Assert.Null(detail.Editor);
        Assert.Equal(18, detail.Article?.Id);
    }

    [Fact]
    public async Task Completed_mutation_does_not_restore_an_article_replaced_while_the_write_was_pending()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = new FakePediaDataService
        {
            ArticleHandler = (articleId, _) => Task.FromResult<ArticleDocumentData?>(Document(articleId)),
            SetFavoriteHandler = async (_, _, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
            }
        };
        var detail = CreateDetail(data, new FakeDialogService());
        await detail.LoadArticleAsync(17, TestContext.Current.CancellationToken);

        var mutation = detail.ToggleFavoriteCommand.ExecuteAsync(null);
        await writeStarted.Task;
        await detail.LoadArticleAsync(18, TestContext.Current.CancellationToken);
        releaseWrite.SetResult();
        await mutation;

        Assert.Equal(18, detail.Article?.Id);
    }

    [Fact]
    public async Task Save_is_single_flight_when_an_editor_leave_request_arrives_during_the_write()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var data = new FakePediaDataService
        {
            Article = Document(17),
            SaveArticleHandler = async (_, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
                return 17;
            }
        };
        var dialogs = new FakeDialogService { UnsavedChoice = UnsavedChangesChoice.Save };
        var detail = CreateDetail(data, dialogs);
        detail.CreateNew(null);
        detail.Editor!.Title = "New article";

        var firstSave = detail.SaveCommand.ExecuteAsync(null);
        await writeStarted.Task;
        var leaveAllowed = await detail.TryLeaveEditorAsync();
        releaseWrite.SetResult();
        await firstSave;

        Assert.False(leaveAllowed);
        Assert.Equal(1, data.SaveArticleCallCount);
    }

    [Fact]
    public async Task Reader_topic_mutation_ignores_a_second_click_while_the_first_write_is_pending()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var article = Document(
            new ArticleTopicData(1, "One", true),
            new ArticleTopicData(2, "Two", false),
            new ArticleTopicData(3, "Three", false));
        var data = new FakePediaDataService
        {
            Article = article,
            ReplaceTopicsHandler = async (_, _, _) =>
            {
                writeStarted.SetResult();
                await releaseWrite.Task;
            }
        };
        var detail = CreateDetail(data, new FakeDialogService());
        await detail.LoadArticleAsync(article.Id, TestContext.Current.CancellationToken);

        var firstMutation = detail.RemoveTopicAssignmentAsync(article.Topics[1]);
        await writeStarted.Task;
        await detail.RemoveTopicAssignmentAsync(article.Topics[2]);
        releaseWrite.SetResult();
        await firstMutation;

        Assert.Equal(1, data.ReplaceArticleTopicsCallCount);
    }

    private static ArticleDetailViewModel CreateDetail(
        FakePediaDataService data,
        FakeDialogService dialogs,
        FakeFilePickerService? picker = null) =>
        new(
            data,
            dialogs,
            picker ?? new FakeFilePickerService(),
            new FakeSettingsService(),
            new FakeStringService(),
            NullLogger<ArticleDetailViewModel>.Instance);

    private static TopicNodeViewModel Topic(long id, string path) =>
        new(id, null, path.Split(" / ").Last(), null, 0, 0, LibraryScopeKind.Topic, false, string.Empty, path);

    private static ArticleDocumentData Document(params ArticleTopicData[] topics) => Document(17, topics);

    private static ArticleDocumentData Document(long id, params ArticleTopicData[] topics) =>
        new(
            id,
            "Article",
            null,
            null,
            "en",
            "General",
            "Ready",
            null,
            false,
            1,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            [new ArticleSectionData(1, null, 2, "Body", 0)],
            [],
            topics);
}

public sealed class TopicPaneViewModelTests
{
    [Fact]
    public void Active_topic_filter_disables_reorder_that_would_use_hidden_sibling_indices()
    {
        var viewModel = new TopicPaneViewModel(
            new FakePediaDataService(),
            new FakeDialogService(),
            new FakeStringService(),
            NullLogger<TopicPaneViewModel>.Instance);
        viewModel.TopicFilter = "visible";
        var first = Topic(1, "First");
        var second = Topic(2, "Second");
        viewModel.RootNodes.Add(first);
        viewModel.RootNodes.Add(second);
        viewModel.SelectedNode = second;

        Assert.False(viewModel.MoveTopicUpCommand.CanExecute(null));
        Assert.False(viewModel.MoveTopicDownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelled_article_editor_guard_stops_topic_mutation_before_its_dialog()
    {
        var dialogs = new FakeDialogService();
        var viewModel = new TopicPaneViewModel(
            new FakePediaDataService(),
            dialogs,
            new FakeStringService(),
            NullLogger<TopicPaneViewModel>.Instance)
        {
            SelectedNode = Topic(1, "Current"),
            TopicMutationStarting = () => Task.FromResult(false)
        };

        await viewModel.RenameTopicCommand.ExecuteAsync(null);

        Assert.Equal(0, dialogs.TopicEditorCallCount);
    }

    [Fact]
    public async Task Slower_obsolete_topic_selection_cannot_replace_the_latest_selection()
    {
        var viewModel = new TopicPaneViewModel(
            new FakePediaDataService(),
            new FakeDialogService(),
            new FakeStringService(),
            NullLogger<TopicPaneViewModel>.Instance);
        var initial = Topic(1, "Initial");
        var first = Topic(2, "First");
        var latest = Topic(3, "Latest");
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.SelectedNode = initial;
        viewModel.ScopeSelected = async node =>
        {
            if (node.Id == first.Id)
            {
                await releaseFirst.Task;
            }

            return true;
        };

        var obsoleteSelection = viewModel.SelectNodeAsync(first);
        Assert.True(viewModel.IsSelectionPending);
        Assert.False(viewModel.RenameTopicCommand.CanExecute(null));
        Assert.True(await viewModel.SelectNodeAsync(latest));
        Assert.False(viewModel.IsSelectionPending);
        Assert.True(viewModel.RenameTopicCommand.CanExecute(null));
        releaseFirst.SetResult();

        Assert.False(await obsoleteSelection);
        Assert.Same(latest, viewModel.SelectedNode);
    }

    [Fact]
    public async Task Filtering_during_a_pending_selection_cannot_restore_a_hidden_stale_node()
    {
        var data = new FakePediaDataService
        {
            Topics =
            [
                new TopicData(1, null, "Alpha", null, 0, 0, []),
                new TopicData(2, null, "Beta", null, 1, 0, [])
            ]
        };
        var viewModel = new TopicPaneViewModel(
            data,
            new FakeDialogService(),
            new FakeStringService(),
            NullLogger<TopicPaneViewModel>.Instance);
        await viewModel.LoadAsync(1, TestContext.Current.CancellationToken);
        var pendingNode = viewModel.UserTopics.Single(topic => topic.Id == 2);
        var releaseSelection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ScopeSelected = async _ =>
        {
            await releaseSelection.Task;
            return true;
        };

        var pending = viewModel.SelectNodeAsync(pendingNode);
        viewModel.TopicFilter = "Alpha";
        releaseSelection.SetResult();

        Assert.False(await pending);
        Assert.Equal(1, viewModel.SelectedNode?.Id);
        Assert.Contains(viewModel.SelectedNode!, viewModel.UserTopics);
    }

    private static TopicNodeViewModel Topic(long id, string name) =>
        new(id, null, name, null, 0, 0, LibraryScopeKind.Topic, false, string.Empty, name);
}

internal sealed class FakePediaDataService : IPediaDataService
{
    public ConcurrentQueue<ArticleQuery> Queries { get; } = new();
    public Func<ArticleQuery, CancellationToken, Task<PageResult<ArticleListData>>> QueryHandler { get; set; } =
        (_, _) => Task.FromResult(new PageResult<ArticleListData>([], 0, 1, 50));
    public int SaveArticleCallCount { get; private set; }
    public ArticleDocumentData? Article { get; set; }
    public IReadOnlyList<TopicData> Topics { get; init; } = [];
    public Func<long, CancellationToken, Task<ArticleDocumentData?>>? ArticleHandler { get; init; }
    public Func<long, bool, CancellationToken, Task>? SetFavoriteHandler { get; init; }
    public Func<EditableArticle, CancellationToken, Task<long>>? SaveArticleHandler { get; init; }
    public Func<long, IReadOnlyList<ArticleTopicData>, CancellationToken, Task>? ReplaceTopicsHandler { get; init; }
    public long DuplicateArticleId { get; init; }
    public List<long> TrashedArticleIds { get; } = [];
    public List<long> ReplacedArticleIds { get; } = [];
    public List<long> ExportedArticleIds { get; } = [];
    public EditableArticle? SavedArticle { get; private set; }
    public int ReplaceArticleTopicsCallCount { get; private set; }
    public IReadOnlyList<ArticleTopicData>? SavedTopics { get; private set; }
    public bool IsNewDatabase => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<TopicData>> GetTopicsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Topics);
    public Task<PageResult<ArticleListData>> QueryArticlesAsync(ArticleQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Enqueue(query);
        return QueryHandler(query, cancellationToken);
    }
    public Task<ArticleDocumentData?> GetArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        ArticleHandler?.Invoke(articleId, cancellationToken) ?? Task.FromResult(Article);
    public Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<long> SaveArticleAsync(EditableArticle article, CancellationToken cancellationToken = default)
    {
        SaveArticleCallCount++;
        SavedArticle = article;
        return SaveArticleHandler?.Invoke(article, cancellationToken)
            ?? Task.FromResult(article.Id == 0 ? Article?.Id ?? 1 : article.Id);
    }
    public Task ReplaceArticleTopicsAsync(long articleId, IReadOnlyList<ArticleTopicData> assignments, CancellationToken cancellationToken = default)
    {
        ReplaceArticleTopicsCallCount++;
        ReplacedArticleIds.Add(articleId);
        SavedTopics = assignments;
        if (Article is not null)
        {
            Article = Article with { Topics = assignments };
        }
        return ReplaceTopicsHandler?.Invoke(articleId, assignments, cancellationToken) ?? Task.CompletedTask;
    }
    public Task<long> DuplicateArticleAsync(long articleId, CancellationToken cancellationToken = default) =>
        DuplicateArticleId > 0 ? Task.FromResult(DuplicateArticleId) : throw new NotSupportedException();
    public Task SetFavoriteAsync(long articleId, bool isFavorite, CancellationToken cancellationToken = default) =>
        SetFavoriteHandler?.Invoke(articleId, isFavorite, cancellationToken) ?? throw new NotSupportedException();
    public Task AddTopicsToArticlesAsync(IReadOnlyList<long> articleIds, IReadOnlyList<long> topicIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveTopicFromArticlesAsync(IReadOnlyList<long> articleIds, long topicId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetStatusForArticlesAsync(IReadOnlyList<long> articleIds, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MoveArticlesToTrashAsync(IReadOnlyList<long> articleIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MoveArticleToTrashAsync(long articleId, CancellationToken cancellationToken = default)
    {
        TrashedArticleIds.Add(articleId);
        return Task.CompletedTask;
    }
    public Task RestoreArticleAsync(long articleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task PermanentlyDeleteArticleAsync(long articleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task EmptyTrashAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<long> CreateTopicAsync(string name, string? description, long? parentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameTopicAsync(long topicId, string name, string? description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task MoveTopicAsync(long topicId, long? destinationParentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReorderTopicAsync(long topicId, int newSortOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteTopicAsync(long topicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ImportPreviewResult> PreviewImportAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ImportOperationResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ExportAsync(IReadOnlyList<long> articleIds, ExportFormat format, string destinationPath, CancellationToken cancellationToken = default)
    {
        ExportedArticleIds.AddRange(articleIds);
        return Task.CompletedTask;
    }
    public Task CreateBackupAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BackupValidationResult> ValidateBackupAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RebuildSearchIndexAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteSampleContentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeStringService : IStringService
{
    public string Get(string key) => key switch
    {
        "AllLanguagesOption" => "All languages",
        "EnglishOption" => "English",
        "FinnishOption" => "Finnish",
        "AllTypesOption" => "All types",
        "AnyStatusOption" => "Any status",
        _ => key
    };

    public string Format(string key, params object?[] args) => key switch
    {
        "ActiveFiltersFormat" => string.Format("{0:N0} active filters", args),
        _ => $"{key}: {string.Join(", ", args)}"
    };
}

internal sealed class FakeSettingsService : ISettingsService
{
    public string PediaDirectory => throw new NotSupportedException();
    public string SettingsPath => throw new NotSupportedException();
    public PediaSettings Current { get; } = new()
    {
        DefaultLanguageCode = "fi",
        DefaultArticleStatus = "Ready"
    };
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeDialogService : IDialogService
{
    public UnsavedChangesChoice UnsavedChoice { get; init; }
    public IReadOnlyList<TopicNodeViewModel>? TopicChoice { get; init; }
    public Func<Task<bool>>? ConfirmHandler { get; set; }
    public Func<IReadOnlyList<TopicNodeViewModel>, IReadOnlySet<long>, Task<IReadOnlyList<TopicNodeViewModel>?>>? ChooseTopicsHandler { get; set; }
    public ExportFormat? ExportFormatChoice { get; init; }
    public int TopicEditorCallCount { get; private set; }
    public Task ShowErrorAsync(string message) => Task.CompletedTask;
    public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText) =>
        ConfirmHandler?.Invoke() ?? Task.FromResult(false);
    public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync() => Task.FromResult(UnsavedChoice);
    public Task<TopicDialogResult?> ShowTopicEditorAsync(string title, string? name = null, string? description = null)
    {
        TopicEditorCallCount++;
        return Task.FromResult<TopicDialogResult?>(null);
    }
    public Task<TopicNodeViewModel?> ChooseTopicAsync(string title, IReadOnlyList<TopicNodeViewModel> topics, long? selectedTopicId = null) => Task.FromResult<TopicNodeViewModel?>(null);
    public Task<IReadOnlyList<TopicNodeViewModel>?> ChooseTopicsAsync(IReadOnlyList<TopicNodeViewModel> topics, IReadOnlySet<long> selectedTopicIds) =>
        ChooseTopicsHandler?.Invoke(topics, selectedTopicIds) ?? Task.FromResult(TopicChoice);
    public Task<ExportFormat?> ChooseExportFormatAsync() => Task.FromResult(ExportFormatChoice);
    public Task<string?> ChooseArticleStatusAsync() => Task.FromResult<string?>(null);
}

internal sealed class FakeFilePickerService : IFilePickerService
{
    public Func<ExportFormat, bool, Task<string?>>? PickHandler { get; init; }
    public Task<string?> PickExportDestinationAsync(ExportFormat format, bool multipleArticles) =>
        PickHandler?.Invoke(format, multipleArticles) ?? Task.FromResult<string?>(null);
}
