using Ipfs.CoreApi;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ipfs.Engine.CoreApi;

internal class GenericApi(IpfsEngine ipfs) : IGenericApi
{
    public async Task<Peer> IdAsync(MultiHash? peer = null, CancellationToken cancel = default)
    {
        if (peer == null)
        {
            return await ipfs.LocalPeer.ConfigureAwait(false);
        }

        return await ipfs.Dht.FindPeerAsync(peer, cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<PingResult>> PingAsync(MultiHash peer, int count = 10, CancellationToken cancel = default)
    {
        var ping = await ipfs.PingService.ConfigureAwait(false);
        return await ping.PingAsync(peer, count, cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<PingResult>> PingAsync(MultiAddress address, int count = 10, CancellationToken cancel = default)
    {
        var ping = await ipfs.PingService.ConfigureAwait(false);
        return await ping.PingAsync(address, count, cancel).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PingResult> Ping(MultiHash peer, int count = 10, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        IEnumerable<PingResult> results = await PingAsync(peer, count, cancel).ConfigureAwait(false);
        foreach (var r in results)
        {
            yield return r;
        }
    }

    public async IAsyncEnumerable<PingResult> Ping(MultiAddress address, int count = 10, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        IEnumerable<PingResult> results = await PingAsync(address, count, cancel).ConfigureAwait(false);
        foreach (var r in results)
        {
            yield return r;
        }
    }

    public async Task<string> ResolveAsync(string name, bool recursive = true, CancellationToken cancel = default)
    {
        string path = name;
        if (path.StartsWith("/ipns/"))
        {
            path = await ipfs.Name.ResolveAsync(path, recursive, false, cancel).ConfigureAwait(false);
            if (!recursive)
                return path;
        }

        if (path.StartsWith("/ipfs/"))
        {
            path = path[6..];
        }

        string[] parts = [.. path.Split('/').Where(p => p.Length > 0)];
        if (parts.Length == 0)
            throw new ArgumentException($"Cannot resolve '{name}'.");

        Cid id = Cid.Decode(parts[0]);
        foreach (string child in parts.Skip(1))
        {
            DagNode container = await ipfs.ObjectHelper.GetAsync(id, cancel).ConfigureAwait(false);
            IMerkleLink? link = container.Links.FirstOrDefault(l => l.Name == child);
            if (link == null)
                throw new ArgumentException($"Cannot resolve '{child}' in '{name}'.");
            id = link.Id;
        }

        return "/ipfs/" + id.Encode();
    }

    public Task ShutdownAsync()
    {
        return ipfs.StopAsync();
    }

    public async Task<Dictionary<string, string>> VersionAsync(CancellationToken cancel = default)
    {
        Version? version = typeof(GenericApi).GetTypeInfo().Assembly.GetName().Version;
        return new Dictionary<string, string>
        {
            { "Version", $"{version?.Major}.{version?.Minor}.{version?.Revision}" },
            { "Repo", await ipfs.BlockRepository.VersionAsync(cancel).ConfigureAwait(false) }
        };
    }
}
