using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace UvA.Workflow.Api.Infrastructure;

/// Broadcasts local baseline reload outcomes to every connected browser.
public sealed class ConfigChangeFeed
{
    private readonly object _gate = new();
    private readonly HashSet<Channel<SseItem<string>>> _clients = [];
    private SseItem<string> _latest = new(Guid.NewGuid().ToString("N"), "config-changed");

    public void Publish(bool reloadSucceeded = true)
    {
        lock (_gate)
        {
            // Keep the installed revision available even if a later save failed.
            _latest = reloadSucceeded
                ? new(Guid.NewGuid().ToString("N"), "config-changed")
                : new(_latest.Data, "config-reload-failed");
            foreach (var client in _clients)
                client.Writer.TryWrite(_latest);
        }
    }

    public async IAsyncEnumerable<SseItem<string>> Listen([EnumeratorCancellation] CancellationToken ct)
    {
        // The latest outcome includes the installed revision, so it covers missed saves too.
        var client = Channel.CreateBounded<SseItem<string>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock (_gate)
        {
            _clients.Add(client);
            // Register and send the latest outcome atomically, including on reconnect.
            client.Writer.TryWrite(_latest);
        }

        try
        {
            await foreach (var item in client.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            lock (_gate)
                _clients.Remove(client);
        }
    }
}