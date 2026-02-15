using Ipfs.CoreApi;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "pin", Description = "Manage pinned content")]
[Subcommand(typeof(PinAddCommand))]
[Subcommand(typeof(PinLsCommand))]
[Subcommand(typeof(PinRmCommand))]
internal class PinCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "add", Description = "Pin a CID")]
internal class PinAddCommand : CommandBase
{
    [Argument(0, "cid", "CID to pin")]
    [Required]
    public string Cid { get; set; }

    [Option("-r|--recursive", Description = "Recursively pin")]
    public bool Recursive { get; set; } = true;

    private PinCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var cids = await Parent.Parent.CoreApi.Pin.AddAsync(Cid, new PinAddOptions { Recursive = Recursive });
        foreach (var c in cids)
        {
            app.Out.WriteLine($"pinned {c.Encode()}");
        }
        return 0;
    }
}

[Command(Name = "ls", Description = "List pinned objects")]
internal class PinLsCommand : CommandBase
{
    private PinCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await foreach (var item in Parent.Parent.CoreApi.Pin.ListAsync())
        {
            app.Out.WriteLine(item.Cid.Encode());
        }
        return 0;
    }
}

[Command(Name = "rm", Description = "Unpin a CID")]
internal class PinRmCommand : CommandBase
{
    [Argument(0, "cid", "CID to unpin")]
    [Required]
    public string Cid { get; set; }

    [Option("-r|--recursive", Description = "Recursively unpin")]
    public bool Recursive { get; set; } = true;

    private PinCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var cids = await Parent.Parent.CoreApi.Pin.RemoveAsync(Cid, Recursive);
        foreach (var c in cids)
        {
            app.Out.WriteLine($"unpinned {c.Encode()}");
        }
        return 0;
    }
}