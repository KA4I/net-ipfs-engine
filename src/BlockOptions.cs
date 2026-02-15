namespace Ipfs.Engine;

/// <summary>
///   Configuration options for a <see cref="Ipfs.CoreApi.IBlockApi">block service</see>.
/// </summary>
/// <seealso cref="IpfsEngineOptions"/>
public class BlockOptions
{
    /// <summary>
    ///   Determines if an inline CID can be created.
    /// </summary>
    /// <value>
    ///   Defaults to <b>false</b>.
    /// </value>
    /// <remarks>
    ///   An "inline CID" places the content in the CID not in a separate block.
    ///   It is used to speed up access to content that is small.
    /// </remarks>
    public bool AllowInlineCid { get; set; }

    /// <summary>
    ///   Used to determine if the content is small enough to be inlined.
    /// </summary>
    /// <value>
    ///   The maximum number of bytes for content that will be inlined.
    ///   Defaults to 64.
    /// </value>
    public int InlineCidLimit { get; set; } = 64;

    /// <summary>
    ///   The maximum length of data block.
    /// </summary>
    /// <value>
    ///   2 MiB (2 * 1024 * 1024), matching the Bitswap spec.
    /// </value>
    /// <remarks>
    ///   Kubo 0.40 raised this from 1 MiB to 2 MiB to match
    ///   the <see href="https://specs.ipfs.tech/bitswap-protocol/#block-sizes">Bitswap specification</see>.
    /// </remarks>
    public int MaxBlockSize { get; } = 2 * 1024 * 1024;
}
