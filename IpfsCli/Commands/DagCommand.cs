using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "dag", Description = "Interact with IPLD DAG objects")]
[Subcommand(typeof(DagGetCommand))]
[Subcommand(typeof(DagResolveCommand))]
[Subcommand(typeof(DagStatCommand))]
[Subcommand(typeof(DagExportCommand))]
[Subcommand(typeof(DagImportCommand))]
internal class DagCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "get", Description = "Get a DAG node")]
internal class DagGetCommand : CommandBase
{
    [Argument(0, "cid", "CID or IPLD path")]
    [Required]
    public string Cid { get; set; }

    private DagCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var result = await Parent.Parent.CoreApi.Dag.GetAsync(Cid);
        app.Out.WriteLine(result?.ToString());
        return 0;
    }
}

[Command(Name = "resolve", Description = "Resolve an IPLD path")]
internal class DagResolveCommand : CommandBase
{
    [Argument(0, "path", "IPLD path to resolve")]
    [Required]
    public string Path { get; set; }

    private DagCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var result = await Parent.Parent.CoreApi.Dag.ResolveAsync(Path);
        app.Out.WriteLine($"Cid: {result.Cid}");
        if (!string.IsNullOrEmpty(result.RemPath))
            app.Out.WriteLine($"RemPath: {result.RemPath}");
        return 0;
    }
}

[Command(Name = "stat", Description = "Get DAG statistics")]
internal class DagStatCommand : CommandBase
{
    [Argument(0, "cid", "CID to stat")]
    [Required]
    public string Cid { get; set; }

    private DagCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var result = await Parent.Parent.CoreApi.Dag.StatAsync(Cid);
        return Parent.Parent.Output(app, result, null!);
    }
}

[Command(Name = "export", Description = "Export a DAG as a CAR archive")]
internal class DagExportCommand : CommandBase
{
    [Argument(0, "cid", "CID to export")]
    [Required]
    public string Cid { get; set; }

    [Option("-o|--output", Description = "Output file path")]
    public string OutputPath { get; set; }

    private DagCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        using var stream = await Parent.Parent.CoreApi.Dag.ExportAsync(Cid);
        if (OutputPath != null)
        {
            using var file = File.Create(OutputPath);
            await stream.CopyToAsync(file);
        }
        else
        {
            await stream.CopyToAsync(Console.OpenStandardOutput());
        }
        return 0;
    }
}

[Command(Name = "import", Description = "Import a CAR archive")]
internal class DagImportCommand : CommandBase
{
    [Argument(0, "file", "Path to CAR file")]
    [Required]
    public string FilePath { get; set; }

    [Option("--pin-roots", Description = "Pin the root CIDs")]
    public bool PinRoots { get; set; } = true;

    private DagCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        using var stream = File.OpenRead(FilePath);
        var result = await Parent.Parent.CoreApi.Dag.ImportAsync(stream, PinRoots);
        app.Out.WriteLine($"Imported {result.Root?.Cid}");
        return 0;
    }
}
