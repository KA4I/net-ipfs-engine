namespace Ipfs.Engine;

/// <summary>
///   Configuration options for discovering other peers.
/// </summary>
/// <seealso cref="IpfsEngineOptions"/>
public class DiscoveryOptions
{
    /// <summary>
    ///   Well known peers used to find other peers in
    ///   the IPFS network.
    /// </summary>
    /// <value>
    ///   The default value is <b>null</b>.
    /// </value>
    /// <remarks>
    ///   If not null, then the sequence is used by
    ///   the block API; otherwise the values in the configuration
    ///   file are used.
    /// </remarks>
    public IEnumerable<MultiAddress>? BootstrapPeers { get; set; }

    /// <summary>
    ///   Disables the multicast DNS discovery of other peers
    ///   and advertising of this peer.
    /// </summary>
    public bool DisableMdns { get; set; }

    /// <summary>
    ///   Disables discovery of other peers by walking the
    ///   DHT.
    /// </summary>
    public bool DisableRandomWalk { get; set; }
}
