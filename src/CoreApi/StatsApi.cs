using Ipfs.CoreApi;
using PeerTalk;

namespace Ipfs.Engine.CoreApi;

internal class StatsApi(IpfsEngine ipfs) : IStatsApi
{
    public Task<BandwidthData> BandwidthAsync(CancellationToken cancel = default)
    {
        return Task.FromResult(StatsStream.AllBandwidth);
    }

    public async Task<BitswapData> BitswapAsync(CancellationToken cancel = default)
    {
        var bitswap = await ipfs.BitswapService.ConfigureAwait(false);
        return bitswap.Statistics;
    }

    public Task<RepositoryData> RepositoryAsync(CancellationToken cancel = default)
    {
        return ipfs.BlockRepository.StatisticsAsync(cancel);
    }
}
