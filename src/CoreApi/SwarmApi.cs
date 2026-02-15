using Microsoft.Extensions.Logging;
using Ipfs.CoreApi;
using Newtonsoft.Json.Linq;

namespace Ipfs.Engine.CoreApi;

internal class SwarmApi(IpfsEngine ipfs) : ISwarmApi
{
    private readonly ILogger<SwarmApi> _logger = IpfsEngine.LoggerFactory.CreateLogger<SwarmApi>();

    private static readonly MultiAddress[] defaultFilters = [];

    public async Task<MultiAddress?> AddAddressFilterAsync(MultiAddress address, bool persist = false, CancellationToken cancel = default)
    {
        var addrs = (await ListAddressFiltersAsync(persist, cancel).ConfigureAwait(false)).ToList();
        if (addrs.Any(a => a == address))
            return address;

        addrs.Add(address);
        var strings = addrs.Select(a => a.ToString());
        await ipfs.Config.SetAsync("Swarm.AddrFilters", JToken.FromObject(strings), cancel).ConfigureAwait(false);

        (await ipfs.SwarmService.ConfigureAwait(false)).WhiteList.Add(address);

        return address;
    }

    public async Task<IEnumerable<Peer>> AddressesAsync(CancellationToken cancel = default)
    {
        var swarm = await ipfs.SwarmService.ConfigureAwait(false);
        return swarm.KnownPeers;
    }

    public async Task ConnectAsync(MultiAddress address, CancellationToken cancel = default)
    {
        var swarm = await ipfs.SwarmService.ConfigureAwait(false);
        _logger.LogDebug("Connecting to {Address}", address);
        var conn = await swarm.ConnectAsync(address, cancel).ConfigureAwait(false);
        _logger.LogDebug("Connected to {Address}", conn.RemotePeer.ConnectedAddress);
    }

    public async Task DisconnectAsync(MultiAddress address, CancellationToken cancel = default)
    {
        var swarm = await ipfs.SwarmService.ConfigureAwait(false);
        await swarm.DisconnectAsync(address, cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<MultiAddress>> ListAddressFiltersAsync(bool persist = false, CancellationToken cancel = default)
    {
        try
        {
            var json = await ipfs.Config.GetAsync("Swarm.AddrFilters", cancel).ConfigureAwait(false);
            if (json == null)
                return [];

            return json
                .Select(a => MultiAddress.TryCreate((string)a!))
                .Where(a => a != null)
                .Select(a => a!);
        }
        catch (KeyNotFoundException)
        {
            var strings = defaultFilters.Select(a => a.ToString());
            await ipfs.Config.SetAsync("Swarm.AddrFilters", JToken.FromObject(strings), cancel).ConfigureAwait(false);
            return defaultFilters;
        }
    }

    public async Task<IEnumerable<Peer>> PeersAsync(CancellationToken cancel = default)
    {
        var swarm = await ipfs.SwarmService.ConfigureAwait(false);
        return swarm.KnownPeers.Where(p => p.ConnectedAddress != null);
    }

    public async Task<MultiAddress?> RemoveAddressFilterAsync(MultiAddress address, bool persist = false, CancellationToken cancel = default)
    {
        var addrs = (await ListAddressFiltersAsync(persist, cancel).ConfigureAwait(false)).ToList();
        if (!addrs.Any(a => a == address))
            return null!;

        addrs.Remove(address);
        var strings = addrs.Select(a => a.ToString());
        await ipfs.Config.SetAsync("Swarm.AddrFilters", JToken.FromObject(strings), cancel).ConfigureAwait(false);

        (await ipfs.SwarmService.ConfigureAwait(false)).WhiteList.Remove(address);

        return address;
    }
}
