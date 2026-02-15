using Ipfs.CoreApi;
using System.Runtime.CompilerServices;

namespace Ipfs.Engine.CoreApi;

internal class Pin
{
    public required Cid Id { get; set; }
}

internal class PinApi(IpfsEngine ipfs) : IPinApi
{
    private FileStore<Cid, Pin>? store;

    private FileStore<Cid, Pin> Store
    {
        get
        {
            if (store is null)
            {
                string folder = Path.Combine(ipfs.Options.Repository.Folder, "pins");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                store = new FileStore<Cid, Pin>
                {
                    Folder = folder,
                    NameToKey = (cid) => cid.Hash.ToBase32(),
                    KeyToName = (key) => new MultiHash(key.FromBase32())
                };
            }
            return store;
        }
    }

    public async Task<IEnumerable<Cid>> AddAsync(string path, PinAddOptions options, CancellationToken cancel = default)
    {
        var id = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
        var todos = new Stack<Cid>();
        todos.Push(id);
        var dones = new List<Cid>();

        // The pin is added before the content is fetched, so that
        // garbage collection will not delete the newly pinned content.
        while (todos.Count > 0)
        {
            var current = todos.Pop();

            await Store.PutAsync(current, new Pin { Id = current }, cancel).ConfigureAwait(false);
            _ = await ipfs.Block.GetAsync(current, cancel).ConfigureAwait(false);

            // Recursively pin the links?
            if (options.Recursive && current.ContentType == "dag-pb")
            {
                var links = await ipfs.ObjectHelper.LinksAsync(current, cancel).ConfigureAwait(false);
                foreach (var link in links)
                {
                    todos.Push(link.Id);
                }
            }

            dones.Add(current);
        }

        return dones;
    }

    public async Task<IEnumerable<Cid>> AddAsync(string path, PinAddOptions options, IProgress<BlocksPinnedProgress> progress, CancellationToken cancel = default)
    {
        var result = await AddAsync(path, options, cancel).ConfigureAwait(false);
        var list = result.ToList();
        progress.Report(new BlocksPinnedProgress { BlocksPinned = list.Count });
        return list;
    }

    public async IAsyncEnumerable<PinListItem> ListAsync([EnumeratorCancellation] CancellationToken cancel = default)
    {
        foreach (var pin in Store.Values)
        {
            yield return new PinListItem { Cid = pin.Id, Type = PinType.Recursive };
        }
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<PinListItem> ListAsync(PinType type, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        await foreach (var item in ListAsync(cancel))
        {
            if (type == PinType.All || item.Type == type)
                yield return item;
        }
    }

    public async IAsyncEnumerable<PinListItem> ListAsync(PinListOptions options, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        await foreach (var item in ListAsync(options.Type, cancel))
        {
            yield return item;
        }
    }

    public async Task<IEnumerable<Cid>> RemoveAsync(Cid id, bool recursive = true, CancellationToken cancel = default)
    {
        var todos = new Stack<Cid>();
        todos.Push(id);
        var dones = new List<Cid>();

        while (todos.Count > 0)
        {
            var current = todos.Pop();
            await Store.RemoveAsync(current, cancel).ConfigureAwait(false);
            if (recursive)
            {
                if (null != await ipfs.Block.StatAsync(current, cancel).ConfigureAwait(false))
                {
                    try
                    {
                        var links = await ipfs.ObjectHelper.LinksAsync(current, cancel).ConfigureAwait(false);
                        foreach (var link in links)
                        {
                            todos.Push(link.Id);
                        }
                    }
                    catch (Exception)
                    {
                        // ignore if current is not an object.
                    }
                }
            }
            dones.Add(current);
        }

        return dones;
    }

    public async Task<bool> IsPinnedAsync(Cid id, CancellationToken cancel = default)
    {
        return await Store.ExistsAsync(id, cancel).ConfigureAwait(false);
    }
}
