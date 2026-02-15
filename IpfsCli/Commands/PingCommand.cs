using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "ping", Description = "Send echo requests to a peer")]
internal class PingCommand : CommandBase
{
    [Argument(0, "peer", "Peer ID or multiaddress")]
    [Required]
    public string Peer { get; set; }

    [Option("-n|--count", Description = "Number of pings (default: 10)")]
    public int Count { get; set; } = 10;

    public Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        IEnumerable<Ipfs.CoreApi.PingResult> results;
        if (Peer.StartsWith("/"))
        {
            MultiAddress address = Peer;
            results = await Parent.CoreApi.Generic.PingAsync(address, Count);
        }
        else
        {
            MultiHash peerId = Peer;
            results = await Parent.CoreApi.Generic.PingAsync(peerId, Count);
        }

        foreach (var r in results)
        {
            if (!string.IsNullOrEmpty(r.Text))
                app.Out.WriteLine(r.Text);
            else if (r.Success)
                app.Out.WriteLine($"Pong received: time={r.Time.TotalMilliseconds:0.000}ms");
            else
                app.Out.WriteLine("Pong failed");
        }
        return 0;
    }
}
