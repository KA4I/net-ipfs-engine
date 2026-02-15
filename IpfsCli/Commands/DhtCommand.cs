using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "dht", Description = "Query the DHT for values or peers")]
[Subcommand(typeof(DhtFindPeerCommand))]
[Subcommand(typeof(DhtFindProvidersCommand))]
[Subcommand(typeof(DhtGetCommand))]
[Subcommand(typeof(DhtPutCommand))]
[Subcommand(typeof(DhtProvideCommand))]
[Subcommand(typeof(DhtQueryCommand))]
internal class DhtCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "findpeer", Description = "Find the multiaddresses associated with the peer ID")]
internal class DhtFindPeerCommand : CommandBase
{
    [Argument(0, "peerid", "The IPFS peer ID")]
    [Required]
    public string PeerId { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        Peer peer = await Program.CoreApi.Dht.FindPeerAsync(new MultiHash(PeerId));
        return Program.Output(app, peer, (data, writer) =>
        {
            foreach (MultiAddress a in peer.Addresses)
            {
                writer.WriteLine(a.ToString());
            }
        });
    }
}

[Command(Name = "findprovs", Description = "Find peers that can provide a specific value, given a key")]
internal class DhtFindProvidersCommand : CommandBase
{
    [Argument(0, "key", "The multihash key or a CID")]
    [Required]
    public string Key { get; set; }

    [Option("-n|--num-providers", Description = "The number of providers to find")]
    public int Limit { get; set; } = 20;

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;

        IEnumerable<Peer> peers = await Program.CoreApi.Dht.FindProvidersAsync(Cid.Decode(Key), Limit);
        return Program.Output(app, peers, (data, writer) =>
        {
            foreach (Peer peer in peers)
            {
                writer.WriteLine(peer.Id.ToString());
            }
        });
    }
}

[Command(Name = "get", Description = "Get a value from the DHT")]
internal class DhtGetCommand : CommandBase
{
    [Argument(0, "key", "The key to look up")]
    [Required]
    public string Key { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        var data = await Program.CoreApi.Dht.GetAsync(System.Text.Encoding.UTF8.GetBytes(Key));
        app.Out.WriteLine(Convert.ToBase64String(data));
        return 0;
    }
}

[Command(Name = "put", Description = "Put a value into the DHT")]
internal class DhtPutCommand : CommandBase
{
    [Argument(0, "key", "The key")]
    [Required]
    public string Key { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        var task = Program.CoreApi.Dht.PutAsync(
            System.Text.Encoding.UTF8.GetBytes(Key),
            out _);
        await task;
        return 0;
    }
}

[Command(Name = "provide", Description = "Announce that you are providing a CID")]
internal class DhtProvideCommand : CommandBase
{
    [Argument(0, "cid", "The CID to provide")]
    [Required]
    public string CidArg { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        await Program.CoreApi.Dht.ProvideAsync(Cid.Decode(CidArg));
        return 0;
    }
}

[Command(Name = "query", Description = "Find the closest peers to a given key")]
internal class DhtQueryCommand : CommandBase
{
    [Argument(0, "peerid", "The peer ID to query for")]
    [Required]
    public string PeerId { get; set; }

    private DhtCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        Program Program = Parent.Parent;
        var peer = await Program.CoreApi.Dht.FindPeerAsync(new MultiHash(PeerId));
        return Program.Output(app, peer, (data, writer) =>
        {
            writer.WriteLine(data.Id.ToString());
            foreach (MultiAddress a in data.Addresses)
            {
                writer.WriteLine($"  {a}");
            }
        });
    }
}