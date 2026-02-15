using Microsoft.Extensions.Logging;
using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

internal class BlockApi(IpfsEngine ipfs) : IBlockApi
{
    private static readonly DataBlock emptyDirectory = new()
    {
        DataBytes = ObjectHelper.EmptyDirectory.ToArray(),
        Id = ObjectHelper.EmptyDirectory.Id,
        Size = ObjectHelper.EmptyDirectory.ToArray().Length
    };

    private static readonly DataBlock emptyNode = new()
    {
        DataBytes = ObjectHelper.EmptyNode.ToArray(),
        Id = ObjectHelper.EmptyNode.Id,
        Size = ObjectHelper.EmptyNode.ToArray().Length
    };

    private readonly ILogger<BlockApi> _logger = IpfsEngine.LoggerFactory.CreateLogger<BlockApi>();
    private FileStore<Cid, DataBlock>? store;

    public FileStore<Cid, DataBlock> Store
    {
        get
        {
            if (store is null)
            {
                string folder = Path.Combine(ipfs.Options.Repository.Folder, "blocks");
                if (!Directory.Exists(folder))
                {
                    _ = Directory.CreateDirectory(folder);
                }

                store = new FileStore<Cid, DataBlock>
                {
                    Folder = folder,
                    NameToKey = (cid) => cid.Hash.ToBase32(),
                    KeyToName = (key) => new MultiHash(key.FromBase32()),
                    Serialize = async (stream, cid, block, cancel) =>
                    {
                        await stream.WriteAsync(block.DataBytes.AsMemory(0, block.DataBytes.Length), cancel).ConfigureAwait(false);
                    },
                    Deserialize = async (stream, cid, cancel) =>
                    {
                        int size = (int)stream.Length;
                        DataBlock block = new()
                        {
                            Id = cid,
                            Size = size
                        };
                        block.DataBytes = new byte[size];
                        for (int i = 0, n; i < size; i += n)
                        {
                            n = await stream.ReadAsync(block.DataBytes.AsMemory(i, size - i), cancel).ConfigureAwait(false);
                        }

                        return block;
                    }
                };
            }
            return store;
        }
    }

    public async Task<byte[]> GetAsync(Cid id, CancellationToken cancel = default)
    {
        DataBlock block = await GetBlockInternalAsync(id, cancel).ConfigureAwait(false);
        return block.DataBytes;
    }

    /// <summary>
    /// Internal method that returns the full DataBlock (used by Bitswap and other internal consumers).
    /// </summary>
    internal async Task<DataBlock> GetBlockInternalAsync(Cid id, CancellationToken cancel = default)
    {
        // Hack for empty object and empty directory object
        if (id == emptyDirectory.Id)
        {
            return emptyDirectory;
        }

        if (id == emptyNode.Id)
        {
            return emptyNode;
        }

        // If identity hash, then CID has the content.
        if (id.Hash.IsIdentityHash)
        {
            return new DataBlock
            {
                DataBytes = id.Hash.Digest,
                Id = id,
                Size = id.Hash.Digest.Length
            };
        }

        // Check the local filesystem for the block.
        DataBlock? block = await Store.TryGetAsync(id, cancel).ConfigureAwait(false);
        if (block != null)
        {
            return block;
        }

        // Query the network, via DHT, for peers that can provide the content.
        using CancellationTokenSource queryCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        BlockExchange.Bitswap bitswap = await ipfs.BitswapService;
        Peer localPeer = await ipfs.LocalPeer;
        System.Runtime.CompilerServices.ConfiguredTaskAwaitable<IDataBlock> bitswapGet = bitswap.WantAsync(id, localPeer.Id, queryCancel.Token).ConfigureAwait(false);
        PeerTalk.Routing.Dht1 dht = await ipfs.DhtService;
        Task<IEnumerable<Peer>> _ = dht.FindProvidersAsync(
            id: id,
            limit: 20,
            cancel: queryCancel.Token,
            action: (peer) => { System.Runtime.CompilerServices.ConfiguredTaskAwaitable __ = ProviderFoundAsync(peer, queryCancel.Token).ConfigureAwait(false); }
        );

        IDataBlock got = await bitswapGet;
        _logger.LogDebug("Bitswap got block {Cid}", id);

        await queryCancel.CancelAsync();

        // The Bitswap result is an IDataBlock; cast to DataBlock if possible.
        if (got is DataBlock dataBlock)
        {
            return dataBlock;
        }

        // Fallback: re-fetch from store (it should be there after bitswap)
        DataBlock? stored = await Store.TryGetAsync(id, cancel).ConfigureAwait(false);
        return stored ?? throw new KeyNotFoundException($"Block '{id.Encode()}' not found after bitswap.");
    }

    public async Task<IBlockStat> PutAsync(
        byte[] data,
        string cidCodec = "raw",
        MultiHash? hash = null,
        bool? pin = null,
        bool? allowBigBlock = null,
        CancellationToken cancel = default)
    {
        if (allowBigBlock != true && data.Length > ipfs.Options.Block.MaxBlockSize)
        {
            throw new ArgumentOutOfRangeException("data.Length", $"Block length can not exceed {ipfs.Options.Block.MaxBlockSize}.");
        }

        // Small enough for an inline CID?
        if (ipfs.Options.Block.AllowInlineCid && data.Length <= ipfs.Options.Block.InlineCidLimit)
        {
            Cid inlineCid = new()
            {
                ContentType = cidCodec,
                Hash = MultiHash.ComputeHash(data, "identity")
            };
            return new DataBlock { Id = inlineCid, Size = data.Length };
        }

        string hashAlgorithm = hash?.Algorithm?.Name ?? MultiHash.DefaultAlgorithmName;

        Cid cid = new()
        {
            ContentType = cidCodec,
            Hash = hash ?? MultiHash.ComputeHash(data, hashAlgorithm)
        };
        DataBlock block = new()
        {
            DataBytes = data,
            Id = cid,
            Size = data.Length
        };
        if (await Store.ExistsAsync(cid, cancel).ConfigureAwait(false))
        {
            _logger.LogDebug("Block '{Cid}' already present", cid);
        }
        else
        {
            await Store.PutAsync(cid, block, cancel).ConfigureAwait(false);
            if (ipfs.IsStarted)
            {
                await ipfs.Dht.ProvideAsync(cid, advertise: false, cancel: cancel).ConfigureAwait(false);
            }
            _logger.LogDebug("Added block '{Cid}'", cid);
        }

        // Inform the Bitswap service.
        _ = (await ipfs.BitswapService.ConfigureAwait(false)).Found(block);

        // Pin if requested.
        if (pin == true)
        {
            _ = await ipfs.Pin.AddAsync(cid.Encode(), new PinAddOptions { Recursive = false }, cancel).ConfigureAwait(false);
        }

        return block;
    }

    public async Task<IBlockStat> PutAsync(
        Stream data,
        string cidCodec = "raw",
        MultiHash? hash = null,
        bool? pin = null,
        bool? allowBigBlock = null,
        CancellationToken cancel = default)
    {
        using MemoryStream ms = new();
        await data.CopyToAsync(ms, cancel).ConfigureAwait(false);
        return await PutAsync(ms.ToArray(), cidCodec, hash, pin, allowBigBlock, cancel).ConfigureAwait(false);
    }

    public async Task<Cid> RemoveAsync(Cid id, bool ignoreNonexistent = false, CancellationToken cancel = default)
    {
        if (id.Hash.IsIdentityHash)
        {
            return id;
        }
        if (await Store.ExistsAsync(id, cancel).ConfigureAwait(false))
        {
            await Store.RemoveAsync(id, cancel).ConfigureAwait(false);
            _ = await ipfs.Pin.RemoveAsync(id, recursive: false, cancel: cancel).ConfigureAwait(false);
            return id;
        }
        return ignoreNonexistent ? null! : throw new KeyNotFoundException($"Block '{id.Encode()}' does not exist.");
    }

    public async Task<IBlockStat> StatAsync(Cid id, CancellationToken cancel = default)
    {
        if (id.Hash.IsIdentityHash)
        {
            return new DataBlock
            {
                Id = id,
                Size = id.Hash.Digest.Length
            };
        }

        long? length = await Store.LengthAsync(id, cancel).ConfigureAwait(false);
        if (length.HasValue)
        {
            return new DataBlock
            {
                Id = id,
                Size = (int)length.Value
            };
        }

        return null!;
    }

    private async Task ProviderFoundAsync(Peer peer, CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
        {
            return;
        }

        _logger.LogDebug("Connecting to provider {PeerId}", peer.Id);
        PeerTalk.Swarm swarm = await ipfs.SwarmService.ConfigureAwait(false);
        try
        {
            _ = await swarm.ConnectAsync(peer, cancel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Connection to provider {PeerId} failed: {Message}", peer.Id, e.Message);
        }
    }
}