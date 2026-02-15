using Ipfs.CoreApi;
using Newtonsoft.Json.Linq;

namespace Ipfs.Engine.CoreApi;

internal class BootstrapApi(IpfsEngine ipfs) : IBootstrapApi
{
    // Updated bootstrap peers from Kubo (modern /p2p/ format with current peer IDs)
    private static readonly MultiAddress[] defaults =
    [
        "/dnsaddr/bootstrap.libp2p.io/p2p/QmNnooDu7bfjPFoTZYxMNLWUQJyrVwtbZg5gBMjTezGAJN",
        "/dnsaddr/bootstrap.libp2p.io/p2p/QmQCU2EcMqAqQPR2i9bChDtGNJchTbq5TbXJJ16u19uLTa",
        "/dnsaddr/bootstrap.libp2p.io/p2p/QmbLHAnMoJPWSCR5Zhtx6BHJX9KiKNN6tpvbUcqanj75Nb",
        "/dnsaddr/bootstrap.libp2p.io/p2p/QmcZf59bWwK5XFi76CZX8cbJ4BhTzzA3gU1ZjYZcYW3dwt",
        "/ip4/104.131.131.82/tcp/4001/p2p/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
    ];

    public async Task<MultiAddress?> AddAsync(MultiAddress address, CancellationToken cancel = default)
    {
        // Throw if missing peer ID
        _ = address.PeerId;

        var addrs = (await ListAsync(cancel).ConfigureAwait(false)).ToList();
        if (addrs.Any(a => a == address))
            return address;

        addrs.Add(address);
        var strings = addrs.Select(a => a.ToString());
        await ipfs.Config.SetAsync("Bootstrap", JToken.FromObject(strings), cancel).ConfigureAwait(false);
        return address;
    }

    public async Task<IEnumerable<MultiAddress>> AddDefaultsAsync(CancellationToken cancel = default)
    {
        foreach (var a in defaults)
        {
            await AddAsync(a, cancel).ConfigureAwait(false);
        }

        return defaults;
    }

    public async Task<IEnumerable<MultiAddress>> ListAsync(CancellationToken cancel = default)
    {
        if (ipfs.Options.Discovery.BootstrapPeers != null)
        {
            return ipfs.Options.Discovery.BootstrapPeers;
        }

        try
        {
            var json = await ipfs.Config.GetAsync("Bootstrap", cancel).ConfigureAwait(false);
            if (json == null)
                return [];

            return json
                .Select(a => MultiAddress.TryCreate((string)a!))
                .Where(a => a != null)
                .Select(a => a!);
        }
        catch (KeyNotFoundException)
        {
            var strings = defaults.Select(a => a.ToString());
            await ipfs.Config.SetAsync("Bootstrap", JToken.FromObject(strings), cancel).ConfigureAwait(false);
            return defaults;
        }
    }

    public async Task RemoveAllAsync(CancellationToken cancel = default)
    {
        await ipfs.Config.SetAsync("Bootstrap", JToken.FromObject(Array.Empty<string>()), cancel).ConfigureAwait(false);
    }

    public async Task<MultiAddress?> RemoveAsync(MultiAddress address, CancellationToken cancel = default)
    {
        var addrs = (await ListAsync(cancel).ConfigureAwait(false)).ToList();
        if (!addrs.Any(a => a == address))
            return address;

        addrs.Remove(address);
        var strings = addrs.Select(a => a.ToString());
        await ipfs.Config.SetAsync("Bootstrap", JToken.FromObject(strings), cancel).ConfigureAwait(false);
        return address;
    }
}
