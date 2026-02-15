using Ipfs.Engine.Cryptography;
using Makaretu.Dns;

namespace Ipfs.Engine;

/// <summary>
///   Configuration options for the <see cref="IpfsEngine"/>.
/// </summary>
/// <seealso cref="IpfsEngine.Options"/>
public class IpfsEngineOptions
{
    /// <summary>
    ///   Repository options.
    /// </summary>
    public RepositoryOptions Repository { get; set; } = new RepositoryOptions();

    /// <summary>
    ///   KeyChain options.
    /// </summary>
    public KeyChainOptions KeyChain { get; set; } = new KeyChainOptions();

    /// <summary>
    ///   Provides access to the Domain Name System.
    /// </summary>
    /// <value>
    ///   Defaults to <see cref="DotClient"/>, DNS over TLS.
    /// </value>
    public IDnsClient Dns { get; set; } = new DotClient();

    /// <summary>
    ///   Block options.
    /// </summary>
    public BlockOptions Block { get; set; } = new BlockOptions();

    /// <summary>
    ///    Discovery options.
    /// </summary>
    public DiscoveryOptions Discovery { get; set; } = new DiscoveryOptions();

    /// <summary>
    ///   Swarm (network) options.
    /// </summary>
    public SwarmOptions Swarm { get; set; } = new SwarmOptions();

    /// <summary>
    ///   Import options controlling how files are added to IPFS.
    /// </summary>
    public ImportOptions Import { get; set; } = new ImportOptions();
}

/// <summary>
///   Configuration for file import defaults (IPIP-499 CID Profiles).
/// </summary>
/// <remarks>
///   Kubo 0.40 introduced CID Profiles that pin down how files are split
///   into blocks and organized into directories.
/// </remarks>
public class ImportOptions
{
    /// <summary>
    ///   The CID version to use. 0 for CIDv0 (base58, dag-pb, sha2-256), 1 for CIDv1.
    /// </summary>
    public int CidVersion { get; set; } = 0;

    /// <summary>
    ///   Whether to use raw leaf blocks (vs dag-pb wrapping).
    /// </summary>
    public bool RawLeaves { get; set; } = false;

    /// <summary>
    ///   Default chunking algorithm. "size-262144" for 256 KiB chunks.
    /// </summary>
    public string Chunker { get; set; } = "size-262144";

    /// <summary>
    ///   Default hash algorithm name.
    /// </summary>
    public string HashAlgorithm { get; set; } = MultiHash.DefaultAlgorithmName;

    /// <summary>
    ///   DAG layout: "balanced" or "trickle".
    /// </summary>
    public string UnixFSDAGLayout { get; set; } = "balanced";

    /// <summary>
    ///   HAMT directory sharding threshold in bytes.
    ///   Directories larger than this become HAMT-sharded.
    /// </summary>
    public int UnixFSHAMTShardingSize { get; set; } = 256 * 1024;

    /// <summary>
    ///   Applies a named CID profile preset (IPIP-499).
    /// </summary>
    /// <param name="profileName">
    ///   One of: "unixfs-v1-2025", "unixfs-v0-2015", "legacy-cid-v0".
    /// </param>
    public void ApplyProfile(string profileName)
    {
        switch (profileName.ToLowerInvariant())
        {
            case "unixfs-v1-2025":
                CidVersion = 1;
                RawLeaves = true;
                Chunker = "size-1048576"; // 1 MiB
                HashAlgorithm = "sha2-256";
                UnixFSDAGLayout = "balanced";
                break;

            case "unixfs-v0-2015":
            case "legacy-cid-v0":
                CidVersion = 0;
                RawLeaves = false;
                Chunker = "size-262144"; // 256 KiB
                HashAlgorithm = "sha2-256";
                UnixFSDAGLayout = "balanced";
                break;

            default:
                throw new ArgumentException($"Unknown CID profile: {profileName}", nameof(profileName));
        }
    }
}
