using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

internal class DhtApi(IpfsEngine ipfs) : IDhtApi
{
    public async Task<Peer> FindPeerAsync(MultiHash id, CancellationToken cancel = default)
    {
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        return await dht.FindPeerAsync(id, cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<Peer>> FindProvidersAsync(Cid id, int limit = 20, Action<Peer>? providerFound = null, CancellationToken cancel = default)
    {
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        return await dht.FindProvidersAsync(id, limit, providerFound, cancel).ConfigureAwait(false);
    }

    public async Task ProvideAsync(Cid cid, bool advertise = true, CancellationToken cancel = default)
    {
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        await dht.ProvideAsync(cid, advertise, cancel).ConfigureAwait(false);
    }

    public async Task<byte[]> GetAsync(byte[] key, CancellationToken cancel = default)
    {
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        return await dht.GetAsync(key, cancel).ConfigureAwait(false);
    }

    public Task PutAsync(byte[] key, out byte[] value, CancellationToken cancel = default)
    {
        value = key;
        return PutInternalAsync(key, key, cancel);
    }

    private async Task PutInternalAsync(byte[] key, byte[] value, CancellationToken cancel)
    {
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        await dht.PutAsync(key, value, cancel).ConfigureAwait(false);
    }

    public Task<bool> TryGetAsync(byte[] key, out byte[] value, CancellationToken cancel = default)
    {
        // The 'out' parameter in IValueStore forces synchronous resolution.
        // Use Task.Run to avoid deadlocks on synchronization contexts.
        byte[]? result = null;
        bool found = false;
        try
        {
#pragma warning disable VSTHRD103 // Interface requires out parameter, cannot be fully async
            var task = Task.Run(async () =>
            {
                var dht = await ipfs.DhtService.ConfigureAwait(false);
                return await dht.GetAsync(key, cancel).ConfigureAwait(false);
            }, cancel);
            result = task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD103
            found = result != null;
        }
        catch
        {
            // Swallow - key not found
        }
        value = result!;
        return Task.FromResult(found);
    }
}
