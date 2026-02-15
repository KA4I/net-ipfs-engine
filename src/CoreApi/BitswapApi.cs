using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

internal class BitswapApi(IpfsEngine ipfs) : IBitswapApi
{
    public async Task<IDataBlock> GetAsync(Cid id, CancellationToken cancel = default)
    {
        var bs = await ipfs.BitswapService.ConfigureAwait(false);
        var peer = await ipfs.LocalPeer.ConfigureAwait(false);
        return await bs.WantAsync(id, peer.Id, cancel).ConfigureAwait(false);
    }

    public async Task<BitswapLedger> LedgerAsync(Peer peer, CancellationToken cancel = default)
    {
        var bs = await ipfs.BitswapService.ConfigureAwait(false);
        return bs.PeerLedger(peer);
    }

    public async Task UnwantAsync(Cid id, CancellationToken cancel = default)
    {
        (await ipfs.BitswapService.ConfigureAwait(false)).Unwant(id);
    }

    public async Task<IEnumerable<Cid>> WantsAsync(MultiHash? peer = null, CancellationToken cancel = default)
    {
        peer ??= (await ipfs.LocalPeer.ConfigureAwait(false)).Id;
        var bs = await ipfs.BitswapService.ConfigureAwait(false);
        return bs.PeerWants(peer);
    }
}
