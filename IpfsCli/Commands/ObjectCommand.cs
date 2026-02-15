using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "object", Description = "Manage IPFS objects")]
[Subcommand(typeof(ObjectLinksCommand))]
[Subcommand(typeof(ObjectGetCommand))]
[Subcommand(typeof(ObjectDumpCommand))]
[Subcommand(typeof(ObjectStatCommand))]
internal class ObjectCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "dump", Description = "Dump the DAG node")]
internal class ObjectDumpCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.Error.WriteLine("Object API has been removed.");
        return Task.FromResult(0);
    }
}

[Command(Name = "get", Description = "Serialise the DAG node")]
internal class ObjectGetCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.Error.WriteLine("Object API has been removed.");
        return Task.FromResult(0);
    }
}

[Command(Name = "links", Description = "Information on the links pointed to by the IPFS block")]
internal class ObjectLinksCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.Error.WriteLine("Object API has been removed.");
        return Task.FromResult(0);
    }
}

[Command(Name = "stat", Description = "Stats for the DAG node")]
internal class ObjectStatCommand : CommandBase
{
    [Argument(0, "cid", "The content ID of the object")]
    [Required]
    public string Cid { get; set; }

    private ObjectCommand Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.Error.WriteLine("Object API has been removed.");
        return Task.FromResult(0);
    }
}