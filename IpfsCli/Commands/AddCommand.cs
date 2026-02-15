using Ipfs.CoreApi;
using McMaster.Extensions.CommandLineUtils;
using System.ComponentModel.DataAnnotations;

namespace Ipfs.Cli.Commands;

[Command(Name = "add", Description = "Add a file to IPFS")]
internal class AddCommand : CommandBase
{
    [Option("--chunker", Description = "Chunking algorithm, e.g. size-262144")]
    public string Chunker { get; set; }

    [Argument(0, "path", "The path to a file to be added to ipfs")]
    [Required]
    public string FilePath { get; set; }

    [Option("--hash", Description = "The hashing algorithm")]
    public string Hash { get; set; }

    [Option("-n|--only-hash", Description = "Only chunk and hash - do not write to disk")]
    public bool OnlyHash { get; set; }

    [Option("--pin", Description = "Pin when adding")]
    public bool Pin { get; set; } = true;

    [Option("-p|--progress", Description = "")]
    public bool Progress { get; set; } = false;

    [Option("--raw-leaves", Description = "Raw data for leaf nodes")]
    public bool RawLeaves { get; set; }

    [Option("-r|--recursive", Description = "Add directory paths recursively")]
    public bool Recursive { get; set; }

    [Option("-t|--trickle", Description = "Use trickle dag format")]
    public bool Trickle { get; set; }

    [Option("-w|--wrap", Description = "Wrap file in a directory")]
    public bool Wrap { get; set; }

    private Program Parent { get; set; }

    protected override async Task<int> OnExecute(CommandLineApplication app)
    {
        AddFileOptions options = new()
        {
            Chunker = Chunker,
            Hash = Hash,
            OnlyHash = OnlyHash,
            Pin = Pin,
            RawLeaves = RawLeaves,
            Trickle = Trickle,
            Wrap = Wrap,
        };
        if (Progress)
        {
            options.Progress = new Progress<TransferProgress>(t =>
            {
                Console.WriteLine($"{t.Name} {t.Bytes}");
            });
        }
        IFileSystemNode node;
        if (Directory.Exists(FilePath))
        {
            // AddDirectoryAsync has been removed; add files individually
            app.Error.WriteLine("Adding directories is not supported in this version. Please add files individually.");
            return 1;
        }
        else
        {
            node = await Parent.CoreApi.FileSystem.AddFileAsync(FilePath, options);
        }
        return Parent.Output(app, node, (data, writer) =>
        {
            writer.WriteLine($"{data.Id.Encode()} added");
        });
    }
}