using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace UvA.Workflow.Api.Infrastructure;

/// Broadcasts successful local baseline installs to every connected browser.
public sealed class ConfigChangeFeed
{
    private readonly object _gate = new();
    private readonly HashSet<Channel<string>> _clients = [];
    private string _revision = Guid.NewGuid().ToString("N");

    public void Publish()
    {
        lock (_gate)
        {
            _revision = Guid.NewGuid().ToString("N");
            foreach (var client in _clients)
                client.Writer.TryWrite(_revision);
        }
    }

    public async IAsyncEnumerable<string> Listen([EnumeratorCancellation] CancellationToken ct)
    {
        // A slow browser only needs the latest revision, not every intermediate save.
        var client = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        lock (_gate)
        {
            _clients.Add(client);
            // Register and send the current revision atomically, including on reconnect.
            client.Writer.TryWrite(_revision);
        }

        try
        {
            await foreach (var revision in client.Reader.ReadAllAsync(ct))
                yield return revision;
        }
        finally
        {
            lock (_gate)
                _clients.Remove(client);
        }
    }
}