using Ipfs.CoreApi;
using System.Runtime.CompilerServices;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Implements the Filestore API.
/// </summary>
/// <remarks>
/// The filestore tracks blocks that reference files on disk (added with --nocopy).
/// Since the .NET engine doesn't currently support --nocopy, the filestore is empty
/// but the API is implemented for completeness and Kubo parity.
/// </remarks>
#pragma warning disable CS9113 // Parameter is unread
internal class FilestoreApi(IpfsEngine ipfs) : IFilestoreApi
#pragma warning restore CS9113
{
    public async IAsyncEnumerable<FilestoreDuplicate> DupsAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        // The filestore has no entries since --nocopy is not supported.
        // Return empty enumerable.
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<FilestoreItem> ListAsync(string? cid = null, bool? fileOrder = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        // No filestore entries exist.
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<FilestoreItem> VerifyObjectsAsync(string? cid = null, bool? fileOrder = null, [EnumeratorCancellation] CancellationToken token = default)
    {
        // No filestore entries to verify.
        await Task.CompletedTask;
        yield break;
    }
}
