using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Tests;

public class ConfigChangeFeedTests
{
    [Fact]
    public async Task Publish_BroadcastsToEveryConnectedBrowser()
    {
        var feed = new ConfigChangeFeed();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = feed.Listen(ct.Token).GetAsyncEnumerator();
        await using var second = feed.Listen(ct.Token).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        var previous = first.Current;
        Assert.Equal(previous, second.Current);

        feed.Publish();

        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.NotEqual(previous, first.Current);
        Assert.Equal(first.Current, second.Current);
    }

    [Fact]
    public async Task ReconnectingBrowser_ReceivesChangesMadeWhileDisconnected()
    {
        var feed = new ConfigChangeFeed();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string previous;
        await using (var browser = feed.Listen(ct.Token).GetAsyncEnumerator())
        {
            Assert.True(await browser.MoveNextAsync());
            previous = browser.Current;
        }

        feed.Publish();
        await using var reconnected = feed.Listen(ct.Token).GetAsyncEnumerator();
        Assert.True(await reconnected.MoveNextAsync());
        Assert.NotEqual(previous, reconnected.Current);
    }

    [Fact]
    public async Task SlowBrowser_ReceivesLatestRevisionWithoutBlockingOtherBrowsers()
    {
        var feed = new ConfigChangeFeed();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var slow = feed.Listen(ct.Token).GetAsyncEnumerator();
        Assert.True(await slow.MoveNextAsync());
        for (var save = 0; save < 100; save++)
            feed.Publish();

        await using var latest = feed.Listen(ct.Token).GetAsyncEnumerator();
        Assert.True(await latest.MoveNextAsync());
        Assert.True(await slow.MoveNextAsync());
        Assert.Equal(latest.Current, slow.Current);
    }

    [Fact]
    public async Task Disconnect_CancelsWaitingSubscription()
    {
        var feed = new ConfigChangeFeed();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var browser = feed.Listen(ct.Token).GetAsyncEnumerator();
        Assert.True(await browser.MoveNextAsync());
        var pending = browser.MoveNextAsync().AsTask();

        ct.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        feed.Publish();
    }
}