using Microsoft.Extensions.Logging;
using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Provides access to the routing layer, replacing the older DHT-only approach.
/// </summary>
/// <remarks>
/// This is the Kubo-compatible Routing API that composes DHT, delegated routing,
/// and other routing subsystems into a unified interface.
/// </remarks>
internal class RoutingApi(IpfsEngine ipfs) : IRoutingApi
{
    private readonly ILogger<RoutingApi> _logger = IpfsEngine.LoggerFactory.CreateLogger<RoutingApi>();

    public async Task<byte[]> GetAsync(string key, CancellationToken cancel = default)
    {
        // Normalize the key to ensure it's a valid routing key.
        ValidateRoutingKey(key);

        _logger.LogDebug("Routing.Get: {Key}", key);

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        return await dht.GetAsync(keyBytes, cancel).ConfigureAwait(false);
    }

    public async Task PutAsync(string key, byte[] value, CancellationToken cancel = default)
    {
        ValidateRoutingKey(key);

        _logger.LogDebug("Routing.Put: {Key} ({Length} bytes)", key, value.Length);

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var dht = await ipfs.DhtService.ConfigureAwait(false);
        await dht.PutAsync(keyBytes, value, cancel).ConfigureAwait(false);
    }

    public async Task<Peer> FindPeerAsync(MultiHash id, CancellationToken cancel = default)
    {
        _logger.LogDebug("Routing.FindPeer: {PeerId}", id);
        return await ipfs.Dht.FindPeerAsync(id, cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<Peer>> FindProvidersAsync(Cid id, int limit = 20, Action<Peer>? providerFound = null, CancellationToken cancel = default)
    {
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), "Number of providers must be greater than 0.");

        _logger.LogDebug("Routing.FindProviders: {Cid} (limit={Limit})", id, limit);
        return await ipfs.Dht.FindProvidersAsync(id, limit, providerFound, cancel).ConfigureAwait(false);
    }

    public async Task ProvideAsync(Cid cid, bool advertise = true, CancellationToken cancel = default)
    {
        _logger.LogDebug("Routing.Provide: {Cid} (advertise={Advertise})", cid, advertise);
        await ipfs.Dht.ProvideAsync(cid, advertise, cancel).ConfigureAwait(false);
    }

    private static void ValidateRoutingKey(string key)
    {
        var parts = key.Split('/');
        if (parts.Length < 3 || parts[0] != "" || (parts[1] != "ipns" && parts[1] != "pk"))
        {
            throw new ArgumentException($"Invalid routing key '{key}'. Must be in format '/ipns/<peerId>' or '/pk/<peerId>'.", nameof(key));
        }
    }
}
