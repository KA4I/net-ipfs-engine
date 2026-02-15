using Ipfs.CoreApi;
using System.Globalization;

namespace Ipfs.Engine.CoreApi;

internal class BlockRepositoryApi(IpfsEngine ipfs) : IBlockRepositoryApi
{
    public async Task RemoveGarbageAsync(CancellationToken cancel = default)
    {
        var blockApi = (BlockApi)ipfs.Block;
        var pinApi = (PinApi)ipfs.Pin;
        foreach (var cid in blockApi.Store.Names)
        {
            if (!await pinApi.IsPinnedAsync(cid, cancel).ConfigureAwait(false))
            {
                await ipfs.Block.RemoveAsync(cid, ignoreNonexistent: true, cancel: cancel).ConfigureAwait(false);
            }
        }
    }

    public async Task<RepositoryData> StatisticsAsync(CancellationToken cancel = default)
    {
        var data = new RepositoryData
        {
            RepoPath = Path.GetFullPath(ipfs.Options.Repository.Folder),
            Version = await VersionAsync(cancel).ConfigureAwait(false),
            StorageMax = 10_000_000_000 // TODO: make configurable
        };

        var blockApi = (BlockApi)ipfs.Block;
        GetDirStats(blockApi.Store.Folder, data, cancel);

        return data;
    }

    public async Task VerifyAsync(CancellationToken cancel = default)
    {
        var blockApi = (BlockApi)ipfs.Block;
        foreach (Cid cid in blockApi.Store.Names)
        {
            cancel.ThrowIfCancellationRequested();
            var block = await blockApi.Store.TryGetAsync(cid, cancel).ConfigureAwait(false);
            if (block is null)
                continue;

            // Verify the block data matches its CID hash
            MultiHash computed = MultiHash.ComputeHash(block.DataBytes, cid.Hash.Algorithm.Name);
            if (computed != cid.Hash)
            {
                throw new InvalidDataException($"Block '{cid}' is corrupt. Expected hash '{cid.Hash}', got '{computed}'.");
            }
        }
    }

    public Task<string> VersionAsync(CancellationToken cancel = default)
    {
        return Task.FromResult(ipfs.MigrationManager
            .CurrentVersion
            .ToString(CultureInfo.InvariantCulture));
    }

    private static void GetDirStats(string path, RepositoryData data, CancellationToken cancel)
    {
        foreach (var file in Directory.EnumerateFiles(path))
        {
            cancel.ThrowIfCancellationRequested();
            ++data.NumObjects;
            data.RepoSize += (ulong)new FileInfo(file).Length;
        }

        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            cancel.ThrowIfCancellationRequested();
            GetDirStats(dir, data, cancel);
        }
    }
}
