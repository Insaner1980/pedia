using Pedia.Core.Models;
using Pedia.Core.Repositories;

namespace Pedia.Tests;

public sealed class TopicRepositoryTests
{
    [Fact]
    public async Task CreatesNestedTopicsAndReturnsPathDescendantsAndCounts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);

        var historyId = await topics.CreateAsync(" History ", cancellationToken: TestContext.Current.CancellationToken);
        var asiaId = await topics.CreateAsync("Asia", historyId, cancellationToken: TestContext.Current.CancellationToken);
        var chinaId = await topics.CreateAsync("中国", asiaId, cancellationToken: TestContext.Current.CancellationToken);
        await articles.CreateAsync(new ArticleDraft
        {
            Title = "Shanghai",
            TopicAssignments = [new ArticleTopicDraft(chinaId, IsPrimary: true)]
        }, TestContext.Current.CancellationToken);

        var descendants = await topics.GetDescendantsAsync(historyId, TestContext.Current.CancellationToken);
        var tree = await topics.GetTreeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([asiaId, chinaId], descendants.Select(topic => topic.Id));
        Assert.Equal("History / Asia / 中国", await topics.GetPathAsync(chinaId, TestContext.Current.CancellationToken));
        Assert.Equal("History", tree.Single(topic => topic.Id == historyId).Name);
        Assert.Equal(0, tree.Single(topic => topic.Id == historyId).DirectArticleCount);
        Assert.Equal(0, tree.Single(topic => topic.Id == historyId).SubtreeArticleCount);
        Assert.Equal(1, tree.Single(topic => topic.Id == chinaId).DirectArticleCount);
    }

    [Fact]
    public async Task RejectsBlankOrCaseEquivalentSiblingNamesButAllowsNameInAnotherBranch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var firstParent = await topics.CreateAsync("First", cancellationToken: TestContext.Current.CancellationToken);
        var secondParent = await topics.CreateAsync("Second", cancellationToken: TestContext.Current.CancellationToken);
        await topics.CreateAsync("Shanghai", firstParent, cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => topics.CreateAsync("   ", cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => topics.CreateAsync("SHANGHAI", firstParent, cancellationToken: TestContext.Current.CancellationToken));
        await topics.CreateAsync("Shanghai", secondParent, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RenameMoveAndReorderPreserveHierarchyAndPreventCycles()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var historyId = await topics.CreateAsync("History", cancellationToken: TestContext.Current.CancellationToken);
        var geographyId = await topics.CreateAsync("Geography", cancellationToken: TestContext.Current.CancellationToken);
        var asiaId = await topics.CreateAsync("Asia", historyId, cancellationToken: TestContext.Current.CancellationToken);
        var chinaId = await topics.CreateAsync("China", asiaId, cancellationToken: TestContext.Current.CancellationToken);

        await topics.RenameAsync(chinaId, "Modern China", TestContext.Current.CancellationToken);
        await topics.MoveAsync(asiaId, geographyId, cancellationToken: TestContext.Current.CancellationToken);
        await topics.ReorderAsync(geographyId, 0, TestContext.Current.CancellationToken);

        Assert.Equal("Geography / Asia / Modern China", await topics.GetPathAsync(chinaId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => topics.MoveAsync(geographyId, chinaId, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => topics.MoveAsync(asiaId, asiaId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, (await topics.GetTreeAsync(TestContext.Current.CancellationToken)).Single(topic => topic.Id == geographyId).SortOrder);
    }

    [Fact]
    public async Task DeleteReparentsChildrenAndUnassignsArticlesWithoutDeletingThem()
    {
        await using var database = await TestDatabase.CreateAsync();
        var topics = new TopicRepository(database.Connections);
        var articles = new ArticleRepository(database.Connections);
        var rootId = await topics.CreateAsync("History", cancellationToken: TestContext.Current.CancellationToken);
        var parentId = await topics.CreateAsync("China", rootId, cancellationToken: TestContext.Current.CancellationToken);
        var childId = await topics.CreateAsync("Shanghai", parentId, cancellationToken: TestContext.Current.CancellationToken);
        var articleId = await articles.CreateAsync(new ArticleDraft
        {
            Title = "Treaty ports",
            TopicAssignments = [new ArticleTopicDraft(parentId, IsPrimary: true)]
        }, TestContext.Current.CancellationToken);

        var result = await topics.DeleteAsync(parentId, TestContext.Current.CancellationToken);
        var article = await articles.GetAsync(articleId, TestContext.Current.CancellationToken);
        var child = (await topics.GetTreeAsync(TestContext.Current.CancellationToken)).Single(topic => topic.Id == childId);

        Assert.Equal(1, result.ReparentedChildCount);
        Assert.Equal(1, result.RemovedArticleAssignmentCount);
        Assert.Equal(rootId, child.ParentId);
        Assert.NotNull(article);
        Assert.Empty(article.TopicAssignments);
    }
}
