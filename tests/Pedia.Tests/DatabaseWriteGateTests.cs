using Pedia.Core.Data;
using Pedia.Core.Repositories;

namespace Pedia.Tests;

public sealed class DatabaseWriteGateTests
{
    [Fact]
    public async Task Repository_write_waits_for_same_database_gate_while_read_completes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var secondFactory = new SqliteConnectionFactory(database.Options);
        var topics = new TopicRepository(secondFactory);
        var lease = await database.Connections.WriteGate.EnterAsync(TestContext.Current.CancellationToken);
        Task<long>? pendingWrite = null;

        try
        {
            pendingWrite = topics.CreateAsync("Blocked write", cancellationToken: TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            Assert.False(pendingWrite.IsCompleted);
            Assert.Equal(0, await topics.CountAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally
        {
            lease.Dispose();
        }

        Assert.NotNull(pendingWrite);
        var topicId = await pendingWrite.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(topicId > 0);
    }

    [Fact]
    public async Task Write_gate_does_not_block_another_database()
    {
        await using var first = await TestDatabase.CreateAsync();
        await using var second = await TestDatabase.CreateAsync();
        var secondTopics = new TopicRepository(second.Connections);
        var lease = await first.Connections.WriteGate.EnterAsync(TestContext.Current.CancellationToken);

        try
        {
            var topicId = await secondTopics
                .CreateAsync("Independent write", cancellationToken: TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.True(topicId > 0);
        }
        finally
        {
            lease.Dispose();
        }
    }

}
