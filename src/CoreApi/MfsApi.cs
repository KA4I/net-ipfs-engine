using System.Text;
using Ipfs.CoreApi;
using Ipfs.Engine.UnixFileSystem;
using CoreMfsWriteOptions = Ipfs.CoreApi.MfsWriteOptions;

namespace Ipfs.Engine.CoreApi;

#pragma warning disable CS9113 // Parameter is unread
internal class MfsApi(IpfsEngine _ipfs) : IMfsApi
#pragma warning restore CS9113
{
    public async Task CopyAsync(string sourceMfsPathOrCid, string destMfsPath, bool? parents = null, CancellationToken cancel = default)
    {
        await _ipfs.Files.CpAsync(sourceMfsPathOrCid, destMfsPath, parents ?? false, cancel).ConfigureAwait(false);
    }

    public async Task<Cid> FlushAsync(string? path = null, CancellationToken cancel = default)
    {
        return await _ipfs.Files.FlushAsync(path ?? "/", cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<IFileSystemNode>> ListAsync(string path, bool? U = null, CancellationToken cancel = default)
    {
        var entries = await _ipfs.Files.LsAsync(path, cancel).ConfigureAwait(false);
        return entries.Select(e => (IFileSystemNode)new FileSystemNode
        {
            Id = e.Hash ?? Cid.Decode("QmdfTbBqBPQ7VNxZEYEj14VmRuZBkqFbiwReogJgS1zR1n"), // empty dir CID as fallback
            Name = e.Name,
            IsDirectory = e.Type == 1,
            Size = (ulong)e.Size
        });
    }

    public async Task MakeDirectoryAsync(string path, bool? parents = null, int? cidVersion = null, string? multiHash = null, CancellationToken cancel = default)
    {
        await _ipfs.Files.MkdirAsync(path, parents ?? false, cancel).ConfigureAwait(false);
    }

    public async Task MoveAsync(string sourceMfsPath, string destMfsPath, CancellationToken cancel = default)
    {
        await _ipfs.Files.MvAsync(sourceMfsPath, destMfsPath, cancel).ConfigureAwait(false);
    }

    public async Task<string> ReadFileAsync(string path, long? offset = null, long? count = null, CancellationToken cancel = default)
    {
        using var stream = await _ipfs.Files.ReadAsync(path, offset ?? 0, count ?? 0, cancel).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancel).ConfigureAwait(false);
    }

    public async Task<Stream> ReadFileStreamAsync(string path, long? offset = null, long? count = null, CancellationToken cancel = default)
    {
        return await _ipfs.Files.ReadAsync(path, offset ?? 0, count ?? 0, cancel).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string path, bool? recursive = null, bool? force = null, CancellationToken cancel = default)
    {
        await _ipfs.Files.RmAsync(path, recursive ?? force ?? false, cancel).ConfigureAwait(false);
    }

    public async Task<FileStatResult> StatAsync(string path, CancellationToken cancel = default)
    {
        var stat = await _ipfs.Files.StatAsync(path, cancel).ConfigureAwait(false);
        return new FileStatResult
        {
            Hash = stat.Hash,
            Size = stat.Size,
            CumulativeSize = stat.CumulativeSize,
            IsDirectory = stat.Type == "directory",
            Blocks = stat.Blocks
        };
    }

    public async Task<FileStatWithLocalityResult> StatAsync(string path, bool withLocal, CancellationToken cancel = default)
    {
        var stat = await _ipfs.Files.StatAsync(path, cancel).ConfigureAwait(false);
        return new FileStatWithLocalityResult
        {
            Hash = stat.Hash,
            Size = stat.Size,
            CumulativeSize = stat.CumulativeSize,
            IsDirectory = stat.Type == "directory",
            Blocks = stat.Blocks,
            WithLocality = withLocal,
            Local = withLocal, // All MFS data is local
            SizeLocal = withLocal ? stat.CumulativeSize : 0
        };
    }

    public async Task WriteAsync(string path, string text, CoreMfsWriteOptions options, CancellationToken cancel = default)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await WriteInternalAsync(path, stream, options, cancel).ConfigureAwait(false);
    }

    public async Task WriteAsync(string path, byte[] data, CoreMfsWriteOptions options, CancellationToken cancel = default)
    {
        using var stream = new MemoryStream(data);
        await WriteInternalAsync(path, stream, options, cancel).ConfigureAwait(false);
    }

    public async Task WriteAsync(string path, Stream data, CoreMfsWriteOptions options, CancellationToken cancel = default)
    {
        await WriteInternalAsync(path, data, options, cancel).ConfigureAwait(false);
    }

    private async Task WriteInternalAsync(string path, Stream data, CoreMfsWriteOptions options, CancellationToken cancel)
    {
        var engineOptions = new Engine.CoreApi.MfsWriteOptions
        {
            Create = options.Create ?? false,
            Parents = options.Parents ?? false,
            Truncate = options.Truncate ?? false,
            Offset = options.Offset ?? 0,
            Count = options.Count ?? 0
        };
        await _ipfs.Files.WriteAsync(path, data, engineOptions, cancel).ConfigureAwait(false);
    }
}
