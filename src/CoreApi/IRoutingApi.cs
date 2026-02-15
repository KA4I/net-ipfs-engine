using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Specifies the interface to the routing layer (Kubo-compatible).
/// </summary>
/// <remarks>
/// This interface replaces the older DHT-only approach with a composable routing
/// interface supporting FindPeer, FindProviders, Provide, Get, and Put operations
/// as defined in Kubo's CoreAPI.
/// </remarks>
public interface IRoutingApi
{
    /// <summary>
    /// Retrieves the best value for a given key from the routing system.
    /// </summary>
    /// <param name="key">The key to find a value for (e.g. "/ipns/peerId").</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The value associated with the key.</returns>
    Task<byte[]> GetAsync(string key, CancellationToken cancel = default);

    /// <summary>
    /// Sets a value for a given key in the routing system.
    /// </summary>
    /// <param name="key">The key to store the value under (e.g. "/ipns/peerId").</param>
    /// <param name="value">The value to store.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task PutAsync(string key, byte[] value, CancellationToken cancel = default);

    /// <summary>
    /// Queries the routing system for all the multiaddresses associated with the given peer.
    /// </summary>
    /// <param name="id">The peer ID to search for.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The peer information including addresses.</returns>
    Task<Peer> FindPeerAsync(MultiHash id, CancellationToken cancel = default);

    /// <summary>
    /// Finds peers in the routing system who can provide a specific value given a CID.
    /// </summary>
    /// <param name="id">The CID to find providers for.</param>
    /// <param name="limit">Maximum number of providers to find. Defaults to 20.</param>
    /// <param name="providerFound">Optional callback invoked when a provider is found.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The sequence of peers that can provide the content.</returns>
    Task<IEnumerable<Peer>> FindProvidersAsync(Cid id, int limit = 20, Action<Peer>? providerFound = null, CancellationToken cancel = default);

    /// <summary>
    /// Announces to the network that you are providing given values.
    /// </summary>
    /// <param name="cid">The CID of the content being provided.</param>
    /// <param name="advertise">Whether to advertise to the network.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task ProvideAsync(Cid cid, bool advertise = true, CancellationToken cancel = default);
}
