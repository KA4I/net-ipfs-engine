using Ipfs.CoreApi;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "files", Description = "Manage the mfs (Mutable File System)")]
[Subcommand(typeof(FilesCpCommand))]
[Subcommand(typeof(FilesFlushCommand))]
[Subcommand(typeof(FilesLsCommand))]
[Subcommand(typeof(FilesMkdirCommand))]
[Subcommand(typeof(FilesMvCommand))]
[Subcommand(typeof(FilesReadCommand))]
[Subcommand(typeof(FilesRmCommand))]
[Subcommand(typeof(FilesStatCommand))]
[Subcommand(typeof(FilesWriteCommand))]
internal class FilesCommand : CommandBase
{
    public Program Parent { get; set; }

    protected override Task<int> OnExecute(CommandLineApplication app)
    {
        app.ShowHelp();
        return Task.FromResult(0);
    }
}

[Command(Name = "cp", Description = "Copy files into MFS")]
internal class FilesCpCommand : CommandBase
{
    [Argument(0, "source", "Source IPFS or MFS path")]
    [Required]
    public string Source { get; set; }

    [Argument(1, "dest", "Destination MFS path")]
    [Required]
    public string Dest { get; set; }

    [Option("-p|--parents", Description = "Make parent directories as needed")]
    public bool Parents { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await Parent.Parent.CoreApi.Mfs.CopyAsync(Source, Dest, Parents);
        return 0;
    }
}

[Command(Name = "flush", Description = "Flush a given path's data to disk")]
internal class FilesFlushCommand : CommandBase
{
    [Argument(0, "path", "Path to flush (default: /)")]
    public string Path { get; set; } = "/";

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var cid = await Parent.Parent.CoreApi.Mfs.FlushAsync(Path);
        app.Out.WriteLine(cid.Encode());
        return 0;
    }
}

[Command(Name = "ls", Description = "List directory contents")]
internal class FilesLsCommand : CommandBase
{
    [Argument(0, "path", "Path to list (default: /)")]
    public string Path { get; set; } = "/";

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var entries = await Parent.Parent.CoreApi.Mfs.ListAsync(Path);
        foreach (var entry in entries)
        {
            app.Out.WriteLine($"{entry.Id}\t{entry.Size}\t{entry.Name}");
        }
        return 0;
    }
}

[Command(Name = "mkdir", Description = "Make directories")]
internal class FilesMkdirCommand : CommandBase
{
    [Argument(0, "path", "Path to directory to create")]
    [Required]
    public string Path { get; set; }

    [Option("-p|--parents", Description = "Make parent directories as needed")]
    public bool Parents { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await Parent.Parent.CoreApi.Mfs.MakeDirectoryAsync(Path, Parents);
        return 0;
    }
}

[Command(Name = "mv", Description = "Move files")]
internal class FilesMvCommand : CommandBase
{
    [Argument(0, "source", "Source MFS path")]
    [Required]
    public string Source { get; set; }

    [Argument(1, "dest", "Destination MFS path")]
    [Required]
    public string Dest { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await Parent.Parent.CoreApi.Mfs.MoveAsync(Source, Dest);
        return 0;
    }
}

[Command(Name = "read", Description = "Read a file from MFS")]
internal class FilesReadCommand : CommandBase
{
    [Argument(0, "path", "Path to file")]
    [Required]
    public string Path { get; set; }

    [Option("-o|--offset", Description = "Byte offset to begin reading")]
    public long Offset { get; set; }

    [Option("-n|--count", Description = "Maximum number of bytes to read")]
    public long Count { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        using var stream = await Parent.Parent.CoreApi.Mfs.ReadFileStreamAsync(Path,
            Offset > 0 ? Offset : null,
            Count > 0 ? Count : null);
        await stream.CopyToAsync(Console.OpenStandardOutput());
        return 0;
    }
}

[Command(Name = "rm", Description = "Remove a file or directory")]
internal class FilesRmCommand : CommandBase
{
    [Argument(0, "path", "Path to remove")]
    [Required]
    public string Path { get; set; }

    [Option("-r|--recursive", Description = "Recursively remove directories")]
    public bool Recursive { get; set; }

    [Option("-f|--force", Description = "Forcibly remove")]
    public bool Force { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        await Parent.Parent.CoreApi.Mfs.RemoveAsync(Path, Recursive, Force);
        return 0;
    }
}

[Command(Name = "stat", Description = "Display file status")]
internal class FilesStatCommand : CommandBase
{
    [Argument(0, "path", "Path to node")]
    [Required]
    public string Path { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        var stat = await Parent.Parent.CoreApi.Mfs.StatAsync(Path);
        return Parent.Parent.Output(app, stat, (data, writer) =>
        {
            writer.WriteLine($"{data.Hash}");
            writer.WriteLine($"Size: {data.Size}");
            writer.WriteLine($"CumulativeSize: {data.CumulativeSize}");
            writer.WriteLine($"ChildBlocks: {data.Blocks}");
            writer.WriteLine($"Type: {(data.IsDirectory ? "directory" : "file")}");
        });
    }
}

[Command(Name = "write", Description = "Write to a mutable file")]
internal class FilesWriteCommand : CommandBase
{
    [Argument(0, "path", "MFS path to write")]
    [Required]
    public string MfsPath { get; set; }

    [Argument(1, "file", "Local file to write")]
    [Required]
    public string FilePath { get; set; }

    [Option("-e|--create", Description = "Create the file if it does not exist")]
    public bool Create { get; set; }

    [Option("-p|--parents", Description = "Make parent directories as needed")]
    public bool Parents { get; set; }

    [Option("-t|--truncate", Description = "Truncate the file before writing")]
    public bool Truncate { get; set; }

    private FilesCommand Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        using var stream = File.OpenRead(FilePath);
        var options = new MfsWriteOptions
        {
            Create = Create,
            Parents = Parents,
            Truncate = Truncate
        };
        await Parent.Parent.CoreApi.Mfs.WriteAsync(MfsPath, stream, options);
        return 0;
    }
}