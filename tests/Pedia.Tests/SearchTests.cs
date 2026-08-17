using Pedia.Core.Models;
using Pedia.Core.Repositories;
using Pedia.Core.Search;

namespace Pedia.Tests;

public sealed class SearchTests
{
    [Fact]
    public async Task SearchesTitlesBodiesQuotedPhrasesAndUnicodeText()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var titleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Shanghai river port",
            Sections = [new ArticleSectionDraft(null, 1, "A concise city overview.")]
        }, TestContext.Current.CancellationToken);
        var bodyId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Commercial change",
            Sections = [new ArticleSectionDraft(null, 1, "The river port expanded beside new warehouses.")]
        }, TestContext.Current.CancellationToken);
        var unicodeId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "上海历史",
            Sections = [new ArticleSectionDraft(null, 1, "黄浦江连接城市与长江口。")]
        }, TestContext.Current.CancellationToken);

        var phrase = await queries.QueryAsync(new ArticleQuery { SearchText = "\"river port\"" }, TestContext.Current.CancellationToken);
        var unicode = await queries.QueryAsync(new ArticleQuery { SearchText = "黄浦江" }, TestContext.Current.CancellationToken);
        var titleOnly = await queries.QueryAsync(new ArticleQuery
        {
            SearchText = "Shanghai",
            SearchScope = ArticleSearchScope.TitleOnly
        }, TestContext.Current.CancellationToken);

        Assert.Equal([titleId, bodyId], phrase.Items.Select(item => item.Id));
        Assert.Contains("[river port]", phrase.Items[1].Snippet!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(unicodeId, Assert.Single(unicode.Items).Id);
        Assert.Equal(titleId, Assert.Single(titleOnly.Items).Id);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData(":() - +")]
    [InlineData("***")]
    public async Task MalformedPunctuationNeverProducesAnFtsSyntaxError(string input)
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        await articles.CreateAsync(new ArticleDraft { Title = "Shanghai" }, TestContext.Current.CancellationToken);

        var result = await queries.QueryAsync(new ArticleQuery { SearchText = input }, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Items);
    }

    [Fact]
    public async Task UsesPrefixSearchAndTitleFallbackForVeryShortText()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var shanghaiId = await articles.CreateAsync(new ArticleDraft { Title = "Shanghai" }, TestContext.Current.CancellationToken);
        await articles.CreateAsync(new ArticleDraft { Title = "Nanjing" }, TestContext.Current.CancellationToken);

        var prefix = await queries.QueryAsync(new ArticleQuery { SearchText = "Shang" }, TestContext.Current.CancellationToken);
        var shortQuery = await queries.QueryAsync(new ArticleQuery { SearchText = "S" }, TestContext.Current.CancellationToken);

        Assert.Equal(shanghaiId, Assert.Single(prefix.Items).Id);
        Assert.Equal(shanghaiId, Assert.Single(shortQuery.Items).Id);
    }

    [Fact]
    public async Task TitleMatchRanksAheadOfBodyOnlyMatch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var titleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Treaty ports",
            Sections = [new ArticleSectionDraft(null, 1, "A short introduction.")]
        }, TestContext.Current.CancellationToken);
        var bodyId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Nineteenth-century commerce",
            Sections = [new ArticleSectionDraft(null, 1, "Several treaty ports connected coastal trade.")]
        }, TestContext.Current.CancellationToken);

        var result = await queries.QueryAsync(new ArticleQuery { SearchText = "treaty ports" }, TestContext.Current.CancellationToken);

        Assert.Equal([titleId, bodyId], result.Items.Select(item => item.Id));
        Assert.True(result.Items[0].Rank < result.Items[1].Rank);
    }

    [Fact]
    public async Task CombinesTopicFiltersAllowlistedSortingAndPaginationInSql()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var china = await topics.CreateAsync("China", cancellationToken: TestContext.Current.CancellationToken);
        var shanghai = await topics.CreateAsync("Shanghai", china, cancellationToken: TestContext.Current.CancellationToken);
        await articles.CreateAsync(CreateFilteredDraft("Alpha", china, words: "one two three four"), TestContext.Current.CancellationToken);
        await articles.CreateAsync(CreateFilteredDraft("Bravo", shanghai, words: "one two three four five"), TestContext.Current.CancellationToken);
        await articles.CreateAsync(CreateFilteredDraft("Charlie", shanghai, words: "one two three four six"), TestContext.Current.CancellationToken);
        await articles.CreateAsync(new ArticleDraft
        {
            Title = "Excluded language",
            LanguageCode = "fi",
            TopicAssignments = [new ArticleTopicDraft(shanghai, true)]
        }, TestContext.Current.CancellationToken);

        var result = await queries.QueryAsync(new ArticleQuery
        {
            TopicId = china,
            IncludeDescendantTopics = true,
            LanguageCodes = ["en"],
            Statuses = [ArticleStatuses.Ready],
            HasSources = true,
            MinimumWordCount = 4,
            SortField = ArticleSortField.Title,
            SortDirection = SortDirection.Descending,
            Page = 2,
            PageSize = 2
        }, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal("Alpha", Assert.Single(result.Items).Title);

        var injectionAttempt = await queries.QueryAsync(new ArticleQuery
        {
            LanguageCodes = ["en') OR 1=1 --"]
        }, TestContext.Current.CancellationToken);
        Assert.Empty(injectionAttempt.Items);
    }

    [Fact]
    public async Task NeutralLanguageFilterIncludesMatchingBcp47Variants()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var neutralId = await articles.CreateAsync(new ArticleDraft { Title = "Neutral English", LanguageCode = "en" }, TestContext.Current.CancellationToken);
        var regionalId = await articles.CreateAsync(new ArticleDraft { Title = "Regional English", LanguageCode = "en-US" }, TestContext.Current.CancellationToken);
        await articles.CreateAsync(new ArticleDraft { Title = "Finnish", LanguageCode = "fi-FI" }, TestContext.Current.CancellationToken);

        var result = await queries.QueryAsync(new ArticleQuery
        {
            LanguageCodes = ["en"],
            SortField = ArticleSortField.Title
        }, TestContext.Current.CancellationToken);

        Assert.Equal([neutralId, regionalId], result.Items.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task SupportsSmartViewsAndExcludesTrashFromNormalSearch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var topicId = await topics.CreateAsync("History", cancellationToken: TestContext.Current.CancellationToken);
        var favoriteId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Favorite Shanghai article",
            IsFavorite = true,
            TopicAssignments = [new ArticleTopicDraft(topicId, true)]
        }, TestContext.Current.CancellationToken);
        var uncategorizedId = await articles.CreateAsync(new ArticleDraft { Title = "Loose note" }, TestContext.Current.CancellationToken);
        var trashId = await articles.CreateAsync(new ArticleDraft { Title = "Shanghai in Trash" }, TestContext.Current.CancellationToken);
        await articles.MoveToTrashAsync(trashId, TestContext.Current.CancellationToken);

        var favorites = await queries.QueryAsync(new ArticleQuery { View = ArticleSmartView.Favorites }, TestContext.Current.CancellationToken);
        var uncategorized = await queries.QueryAsync(new ArticleQuery { View = ArticleSmartView.Uncategorized }, TestContext.Current.CancellationToken);
        var normalSearch = await queries.QueryAsync(new ArticleQuery { SearchText = "Shanghai" }, TestContext.Current.CancellationToken);
        var trash = await queries.QueryAsync(new ArticleQuery { View = ArticleSmartView.Trash }, TestContext.Current.CancellationToken);

        Assert.Equal(favoriteId, Assert.Single(favorites.Items).Id);
        Assert.Equal(uncategorizedId, Assert.Single(uncategorized.Items).Id);
        Assert.DoesNotContain(normalSearch.Items, item => item.Id == trashId);
        Assert.Equal(trashId, Assert.Single(trash.Items).Id);
    }

    [Fact]
    public async Task TrashAllTextSearchFindsDeletedArticleBySectionBody()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var trashId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Maritime archive",
            Sections = [new ArticleSectionDraft(null, 1, "The quayside_100% ledger survived.")]
        }, TestContext.Current.CancellationToken);
        await articles.MoveToTrashAsync(trashId, TestContext.Current.CancellationToken);

        var allText = await queries.QueryAsync(new ArticleQuery
        {
            View = ArticleSmartView.Trash,
            SearchScope = ArticleSearchScope.AllText,
            SearchText = "quayside_100%"
        }, TestContext.Current.CancellationToken);
        var titleOnly = await queries.QueryAsync(new ArticleQuery
        {
            View = ArticleSmartView.Trash,
            SearchScope = ArticleSearchScope.TitleOnly,
            SearchText = "quayside_100%"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(trashId, Assert.Single(allText.Items).Id);
        Assert.Empty(titleOnly.Items);
    }

    [Fact]
    public async Task RebuildRestoresTheAggregatedIndexForAllActiveArticles()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var queries = new ArticleQueryService(database.Connections);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Municipal government",
            Sections = [new ArticleSectionDraft(null, 1, "Council administration in Shanghai")]
        }, TestContext.Current.CancellationToken);
        await using (var connection = await database.Connections.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await using var clear = connection.CreateCommand();
            clear.CommandText = "DELETE FROM SearchDocumentsFts; DELETE FROM SearchDocuments;";
            await clear.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Assert.Empty((await queries.QueryAsync(new ArticleQuery { SearchText = "Council" }, TestContext.Current.CancellationToken)).Items);

        var indexedCount = await queries.RebuildIndexAsync(TestContext.Current.CancellationToken);
        var restored = await queries.QueryAsync(new ArticleQuery { SearchText = "Council" }, TestContext.Current.CancellationToken);

        Assert.Equal(1, indexedCount);
        Assert.Equal(articleId, Assert.Single(restored.Items).Id);
        Assert.True(await queries.VerifyFts5Async(TestContext.Current.CancellationToken));
    }

    private static ArticleDraft CreateFilteredDraft(string title, long topicId, string words) => new()
    {
        Title = title,
        LanguageCode = "en",
        Status = ArticleStatuses.Ready,
        Sections = [new ArticleSectionDraft(null, 1, words)],
        Sources = [new ArticleSourceDraft { SourceType = SourceTypes.Manual, Title = "Notes" }],
        TopicAssignments = [new ArticleTopicDraft(topicId, true)]
    };
}
