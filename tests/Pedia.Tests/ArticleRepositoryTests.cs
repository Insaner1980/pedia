using Pedia.Core.Models;
using Pedia.Core.Repositories;

namespace Pedia.Tests;

public sealed class ArticleRepositoryTests
{
    [Fact]
    public async Task CreatePersistsOrderedContentSourcesAndExactlyOnePrimaryTopic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var chinaId = await topics.CreateAsync("China", cancellationToken: TestContext.Current.CancellationToken);
        var shanghaiId = await topics.CreateAsync("History of Shanghai", chinaId, cancellationToken: TestContext.Current.CancellationToken);

        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = " History of Shanghai ",
            Subtitle = "A local chronology",
            Summary = "A concise introduction.",
            LanguageCode = "en",
            ArticleType = ArticleTypes.Place,
            Status = ArticleStatuses.Ready,
            IsFavorite = true,
            Sections =
            [
                new ArticleSectionDraft(null, 1, "Shanghai grew beside the Huangpu River."),
                new ArticleSectionDraft("Modern era", 2, "Trade reshaped the port after 1842.")
            ],
            Sources =
            [
                new ArticleSourceDraft
                {
                    SourceType = SourceTypes.Book,
                    Title = "A history of the city",
                    Notes = "Local shelf reference"
                }
            ],
            TopicAssignments =
            [
                new ArticleTopicDraft(chinaId),
                new ArticleTopicDraft(shanghaiId)
            ]
        }, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));

        Assert.Equal("History of Shanghai", saved.Title);
        Assert.Equal(12, saved.WordCount);
        Assert.Equal([null, "Modern era"], saved.Sections.Select(section => section.Heading));
        Assert.Equal([0, 1], saved.Sections.Select(section => section.SortOrder));
        Assert.Single(saved.Sources);
        Assert.Equal(2, saved.TopicAssignments.Count);
        Assert.Equal(chinaId, saved.TopicAssignments.Single(topic => topic.IsPrimary).TopicId);
    }

    [Fact]
    public async Task UpdateAtomicallyReplacesChildrenRecomputesWordsAndNormalizesPrimaryTopic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var firstTopic = await topics.CreateAsync("First", cancellationToken: TestContext.Current.CancellationToken);
        var secondTopic = await topics.CreateAsync("Second", cancellationToken: TestContext.Current.CancellationToken);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Draft",
            Sections = [new ArticleSectionDraft(null, 1, "old words")],
            TopicAssignments = [new ArticleTopicDraft(firstTopic, true)]
        }, TestContext.Current.CancellationToken);

        await articles.UpdateAsync(articleId, new ArticleDraft
        {
            Title = "Revised",
            Status = ArticleStatuses.NeedsReview,
            Sections = [new ArticleSectionDraft("Overview", 2, "One two three four")],
            TopicAssignments =
            [
                new ArticleTopicDraft(firstTopic, true),
                new ArticleTopicDraft(secondTopic, true)
            ]
        }, TestContext.Current.CancellationToken);

        var saved = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Equal("Revised", saved.Title);
        Assert.Equal(4, saved.WordCount);
        Assert.Single(saved.Sections);
        Assert.Single(saved.TopicAssignments, topic => topic.IsPrimary);
        Assert.Equal(firstTopic, saved.TopicAssignments.Single(topic => topic.IsPrimary).TopicId);

        await articles.RemoveTopicAsync(articleId, firstTopic, TestContext.Current.CancellationToken);
        saved = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Equal(secondTopic, saved.TopicAssignments.Single(topic => topic.IsPrimary).TopicId);
    }

    [Fact]
    public async Task ReplaceTopicAssignmentsPreservesArticleAndChildRowsAndNormalizesAssignments()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var originalTopicId = await topics.CreateAsync("Original", cancellationToken: TestContext.Current.CancellationToken);
        var firstReplacementId = await topics.CreateAsync("First replacement", cancellationToken: TestContext.Current.CancellationToken);
        var primaryReplacementId = await topics.CreateAsync("Primary replacement", cancellationToken: TestContext.Current.CancellationToken);
        var extraReplacementId = await topics.CreateAsync("Extra replacement", cancellationToken: TestContext.Current.CancellationToken);
        var retrievedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var checkedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Preserved title",
            Subtitle = "Preserved subtitle",
            Summary = "Preserved summary",
            LanguageCode = "fi",
            ArticleType = ArticleTypes.Event,
            Status = ArticleStatuses.NeedsReview,
            Notes = "Preserved notes",
            IsFavorite = true,
            IsSample = true,
            Sections =
            [
                new ArticleSectionDraft("First section", 2, "Preserved first body."),
                new ArticleSectionDraft(null, 1, "Preserved second body.")
            ],
            Sources =
            [
                new ArticleSourceDraft
                {
                    SourceType = SourceTypes.Website,
                    Title = "Preserved source",
                    Url = "https://example.com/source",
                    ExternalPageId = "page-1",
                    ExternalRevisionId = "revision-2",
                    LicenseName = "CC BY 4.0",
                    AttributionText = "Example Author",
                    RetrievedAtUtc = retrievedAt,
                    LastCheckedAtUtc = checkedAt,
                    Notes = "Preserved source notes"
                }
            ],
            TopicAssignments = [new ArticleTopicDraft(originalTopicId, true)]
        }, TestContext.Current.CancellationToken);
        var before = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));

        await Task.Delay(20, TestContext.Current.CancellationToken);
        await articles.ReplaceTopicAssignmentsAsync(
            articleId,
            [
                new ArticleTopicDraft(firstReplacementId),
                new ArticleTopicDraft(firstReplacementId),
                new ArticleTopicDraft(primaryReplacementId, true),
                new ArticleTopicDraft(extraReplacementId, true)
            ],
            CancellationToken.None);

        var after = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Subtitle, after.Subtitle);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.LanguageCode, after.LanguageCode);
        Assert.Equal(before.ArticleType, after.ArticleType);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.IsFavorite, after.IsFavorite);
        Assert.Equal(before.WordCount, after.WordCount);
        Assert.True(after.IsSample);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(before.DeletedAtUtc, after.DeletedAtUtc);
        Assert.True(after.UpdatedAtUtc > before.UpdatedAtUtc);
        Assert.Equal(before.Sections, after.Sections);
        Assert.Equal(before.Sources, after.Sources);
        Assert.Equal(3, after.TopicAssignments.Count);
        Assert.Equal(
            [firstReplacementId, primaryReplacementId, extraReplacementId],
            after.TopicAssignments.Select(topic => topic.TopicId).Order());
        Assert.Equal(primaryReplacementId, after.TopicAssignments.Single(topic => topic.IsPrimary).TopicId);
    }

    [Fact]
    public async Task ReplaceTopicAssignmentsAllowsEmptyAssignments()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var topicId = await topics.CreateAsync("Assigned", cancellationToken: TestContext.Current.CancellationToken);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Uncategorized article",
            TopicAssignments = [new ArticleTopicDraft(topicId, true)]
        }, TestContext.Current.CancellationToken);

        await articles.ReplaceTopicAssignmentsAsync(articleId, [], CancellationToken.None);

        var saved = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Empty(saved.TopicAssignments);
    }

    [Fact]
    public async Task ReplaceTopicAssignmentsRollsBackWhenATopicIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var originalTopicId = await topics.CreateAsync("Original", cancellationToken: TestContext.Current.CancellationToken);
        var validReplacementId = await topics.CreateAsync("Valid replacement", cancellationToken: TestContext.Current.CancellationToken);
        var missingTopicId = long.MaxValue;
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Atomic assignment replacement",
            TopicAssignments = [new ArticleTopicDraft(originalTopicId, true)]
        }, TestContext.Current.CancellationToken);
        var before = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            articles.ReplaceTopicAssignmentsAsync(
                articleId,
                [
                    new ArticleTopicDraft(validReplacementId, true),
                    new ArticleTopicDraft(missingTopicId)
                ],
                CancellationToken.None));

        Assert.Contains(missingTopicId.ToString(), error.Message);
        var after = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(before.TopicAssignments, after.TopicAssignments);
    }

    [Fact]
    public async Task DuplicateCreatesIndependentNonSampleDraftWithCopiedRelationships()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var topicId = await topics.CreateAsync("Shanghai", cancellationToken: TestContext.Current.CancellationToken);
        var originalId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Pudong",
            Status = ArticleStatuses.Ready,
            IsSample = true,
            Sections = [new ArticleSectionDraft("Growth", 2, "A district across the river.")],
            Sources = [new ArticleSourceDraft { SourceType = SourceTypes.Manual, Title = "Notes" }],
            TopicAssignments = [new ArticleTopicDraft(topicId, true)]
        }, TestContext.Current.CancellationToken);

        var copyId = await articles.DuplicateAsync(originalId, TestContext.Current.CancellationToken);
        await articles.UpdateAsync(originalId, new ArticleDraft { Title = "Changed original" }, TestContext.Current.CancellationToken);
        var copy = Assert.IsType<ArticleDetails>(await articles.GetAsync(copyId, TestContext.Current.CancellationToken));

        Assert.Equal("Pudong Copy", copy.Title);
        Assert.Equal(ArticleStatuses.Draft, copy.Status);
        Assert.False(copy.IsSample);
        Assert.Single(copy.Sections);
        Assert.Single(copy.Sources);
        Assert.Single(copy.TopicAssignments);
    }

    [Fact]
    public async Task TrashRestoreAndPermanentDeleteFollowArticleLifecycle()
    {
        await using var database = await TestDatabase.CreateAsync();
        var articles = new ArticleRepository(database.Connections);
        var articleId = await articles.CreateAsync(new ArticleDraft { Title = "Battle of Shanghai" }, TestContext.Current.CancellationToken);

        await articles.MoveToTrashAsync(articleId, TestContext.Current.CancellationToken);
        Assert.NotNull((await articles.GetAsync(articleId, TestContext.Current.CancellationToken))!.DeletedAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => articles.DuplicateAsync(articleId, TestContext.Current.CancellationToken));

        await articles.RestoreAsync(articleId, TestContext.Current.CancellationToken);
        Assert.Null((await articles.GetAsync(articleId, TestContext.Current.CancellationToken))!.DeletedAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => articles.DeletePermanentlyAsync(articleId, TestContext.Current.CancellationToken));

        await articles.MoveToTrashAsync(articleId, TestContext.Current.CancellationToken);
        await articles.DeletePermanentlyAsync(articleId, TestContext.Current.CancellationToken);
        Assert.Null(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BulkActionsUpdateEverySelectedArticleAndPreserveIndependentContent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var originalTopicId = await topics.CreateAsync("Original", cancellationToken: TestContext.Current.CancellationToken);
        var addedTopicId = await topics.CreateAsync("Added", cancellationToken: TestContext.Current.CancellationToken);
        var firstId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "First",
            Sections = [new ArticleSectionDraft(null, 1, "First body")],
            TopicAssignments = [new ArticleTopicDraft(originalTopicId, true)]
        }, TestContext.Current.CancellationToken);
        var secondId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Second",
            Sections = [new ArticleSectionDraft(null, 1, "Second body")],
            TopicAssignments = [new ArticleTopicDraft(originalTopicId, true)]
        }, TestContext.Current.CancellationToken);

        await articles.AddTopicsToArticlesAsync([firstId, secondId], [addedTopicId], TestContext.Current.CancellationToken);
        await articles.RemoveTopicFromArticlesAsync([firstId, secondId], originalTopicId, TestContext.Current.CancellationToken);
        await articles.SetStatusForArticlesAsync([firstId, secondId], ArticleStatuses.Ready, TestContext.Current.CancellationToken);

        var first = Assert.IsType<ArticleDetails>(await articles.GetAsync(firstId, TestContext.Current.CancellationToken));
        var second = Assert.IsType<ArticleDetails>(await articles.GetAsync(secondId, TestContext.Current.CancellationToken));
        Assert.Equal("First body", Assert.Single(first.Sections).Body);
        Assert.Equal("Second body", Assert.Single(second.Sections).Body);
        Assert.Equal(addedTopicId, Assert.Single(first.TopicAssignments).TopicId);
        Assert.Equal(addedTopicId, Assert.Single(second.TopicAssignments).TopicId);
        Assert.All([first, second], article => Assert.Equal(ArticleStatuses.Ready, article.Status));

        await articles.MoveArticlesToTrashAsync([firstId, secondId], TestContext.Current.CancellationToken);
        Assert.NotNull((await articles.GetAsync(firstId, TestContext.Current.CancellationToken))!.DeletedAtUtc);
        Assert.NotNull((await articles.GetAsync(secondId, TestContext.Current.CancellationToken))!.DeletedAtUtc);
    }

    [Fact]
    public async Task BulkActionsRollBackAllChangesWhenAnySelectedRecordIsInvalid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var originalTopicId = await topics.CreateAsync("Original", cancellationToken: TestContext.Current.CancellationToken);
        var addedTopicId = await topics.CreateAsync("Added", cancellationToken: TestContext.Current.CancellationToken);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Preserved",
            TopicAssignments = [new ArticleTopicDraft(originalTopicId, true)]
        }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            articles.AddTopicsToArticlesAsync([articleId, long.MaxValue], [addedTopicId], TestContext.Current.CancellationToken));

        var article = Assert.IsType<ArticleDetails>(await articles.GetAsync(articleId, TestContext.Current.CancellationToken));
        Assert.Equal([originalTopicId], article.TopicAssignments.Select(topic => topic.TopicId));
    }

    [Fact]
    public async Task DeleteSampleContentLeavesUserArticlesAndNeverReseeds()
    {
        await using var database = await TestDatabase.CreateAsync(seedSamples: true);
        var articles = new ArticleRepository(database.Connections);
        var userId = await articles.CreateAsync(new ArticleDraft { Title = "My research" }, TestContext.Current.CancellationToken);

        var result = await articles.DeleteSampleContentAsync(TestContext.Current.CancellationToken);

        Assert.InRange(result.DeletedArticleCount, 15, 25);
        Assert.True(result.DeletedTopicCount >= 11);
        Assert.NotNull(await articles.GetAsync(userId, TestContext.Current.CancellationToken));
        Assert.Equal(1, await articles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));

        var reinitialize = await database.Initializer.InitializeAsync(seedSamples: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(reinitialize.SamplesSeeded);
        Assert.Equal(1, await articles.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
}
