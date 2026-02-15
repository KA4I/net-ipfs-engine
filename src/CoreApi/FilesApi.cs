using Ipfs.CoreApi;
using Ipfs.Engine.UnixFileSystem;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Implements the Mutable File System (MFS) API.
/// </summary>
/// <remarks>
/// MFS is backed by a JSON tree stored in the repository. When flushed,
/// a proper IPLD DAG is built. This matches Kubo's lazy-copy semantics.
/// </remarks>
internal class FilesApi(IpfsEngine ipfs) : IFilesApi
{
    private readonly ILogger<FilesApi> _logger = IpfsEngine.LoggerFactory.CreateLogger<FilesApi>();
    private MfsTree? _tree;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private async Task<MfsTree> GetTreeAsync(CancellationToken cancel)
    {
        if (_tree is not null)
            return _tree;

        await _lock.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (_tree is not null)
                return _tree;

            string mfsPath = Path.Combine(ipfs.Options.Repository.Folder, "mfs.json");
            if (File.Exists(mfsPath))
            {
                string json = await File.ReadAllTextAsync(mfsPath, cancel).ConfigureAwait(false);
                _tree = JsonConvert.DeserializeObject<MfsTree>(json) ?? new MfsTree();
            }
            else
            {
                _tree = new MfsTree();
            }
            return _tree;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveTreeAsync(CancellationToken cancel)
    {
        if (_tree is null) return;
        string mfsPath = Path.Combine(ipfs.Options.Repository.Folder, "mfs.json");
        string json = JsonConvert.SerializeObject(_tree, Formatting.Indented);
        await File.WriteAllTextAsync(mfsPath, json, cancel).ConfigureAwait(false);
    }

    public async Task CpAsync(string source, string destination, bool parents = false, CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);

        // Resolve source CID
        Cid sourceCid;
        if (source.StartsWith("/ipfs/"))
        {
            sourceCid = Cid.Decode(source[6..]);
        }
        else
        {
            // Copy within MFS
            MfsTreeNode? sourceNode = tree.Resolve(source);
            sourceCid = sourceNode?.Cid ?? throw new FileNotFoundException($"MFS path not found: {source}");
        }

        // Create parent directories if needed
        if (parents)
        {
            string? parentPath = Path.GetDirectoryName(destination)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentPath))
            {
                tree.MkdirP(parentPath);
            }
        }

        // Get the block stat to determine type
        var block = await ipfs.Block.StatAsync(sourceCid, cancel).ConfigureAwait(false);

        string name = destination.Split('/').Last(s => s.Length > 0);
        string dirPath = destination[..^name.Length].TrimEnd('/');
        if (string.IsNullOrEmpty(dirPath)) dirPath = "/";

        MfsTreeNode? dir = tree.Resolve(dirPath);
        if (dir is null || dir.Type != MfsNodeType.Directory)
            throw new DirectoryNotFoundException($"Parent directory not found: {dirPath}");

        dir.Children ??= [];
        dir.Children[name] = new MfsTreeNode
        {
            Cid = sourceCid,
            Type = MfsNodeType.File,
            Size = (ulong)block.Size
        };

        _logger.LogDebug("Copied {Source} to {Dest}", source, destination);
        await SaveTreeAsync(cancel).ConfigureAwait(false);
    }

    public async Task<IEnumerable<MfsEntry>> LsAsync(string path = "/", CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);
        MfsTreeNode? node = tree.Resolve(path);

        if (node is null)
            throw new DirectoryNotFoundException($"MFS path not found: {path}");

        if (node.Type != MfsNodeType.Directory || node.Children is null)
            return [];

        return node.Children.Select(kvp => new MfsEntry
        {
            Name = kvp.Key,
            Type = kvp.Value.Type == MfsNodeType.Directory ? 1 : 0,
            Size = (long)kvp.Value.Size,
            Hash = kvp.Value.Cid
        });
    }

    public async Task MkdirAsync(string path, bool parents = false, CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);

        if (parents)
        {
            tree.MkdirP(path);
        }
        else
        {
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            MfsTreeNode current = tree.Root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current.Children ??= [];
                if (!current.Children.TryGetValue(parts[i], out MfsTreeNode? child))
                    throw new DirectoryNotFoundException($"Parent directory not found: /{string.Join('/', parts.Take(i + 1))}");
                current = child;
            }

            current.Children ??= [];
            string dirName = parts[^1];
            if (current.Children.ContainsKey(dirName))
                throw new IOException($"Directory already exists: {path}");

            current.Children[dirName] = new MfsTreeNode
            {
                Type = MfsNodeType.Directory,
                Children = []
            };
        }

        _logger.LogDebug("Created directory {Path}", path);
        await SaveTreeAsync(cancel).ConfigureAwait(false);
    }

    public async Task MvAsync(string source, string destination, CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);

        // Find and remove source
        string[] srcParts = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MfsTreeNode srcParent = tree.Root;
        for (int i = 0; i < srcParts.Length - 1; i++)
        {
            srcParent.Children ??= [];
            if (!srcParent.Children.TryGetValue(srcParts[i], out MfsTreeNode? child))
                throw new FileNotFoundException($"Source not found: {source}");
            srcParent = child;
        }

        string srcName = srcParts[^1];
        if (srcParent.Children is null || !srcParent.Children.TryGetValue(srcName, out MfsTreeNode? srcNode))
            throw new FileNotFoundException($"Source not found: {source}");

        srcParent.Children.Remove(srcName);

        // Place at destination
        string[] dstParts = destination.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MfsTreeNode dstParent = tree.Root;
        for (int i = 0; i < dstParts.Length - 1; i++)
        {
            dstParent.Children ??= [];
            if (!dstParent.Children.TryGetValue(dstParts[i], out MfsTreeNode? child))
                throw new DirectoryNotFoundException($"Destination parent not found: {destination}");
            dstParent = child;
        }

        dstParent.Children ??= [];
        dstParent.Children[dstParts[^1]] = srcNode;

        _logger.LogDebug("Moved {Source} to {Dest}", source, destination);
        await SaveTreeAsync(cancel).ConfigureAwait(false);
    }

    public async Task<Stream> ReadAsync(string path, long offset = 0, long count = 0, CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);
        MfsTreeNode? node = tree.Resolve(path);

        if (node?.Cid is null)
            throw new FileNotFoundException($"MFS path not found: {path}");

        string ipfsPath = $"/ipfs/{node.Cid}";
        Stream stream = await ipfs.FileSystem.ReadFileAsync(ipfsPath, cancel).ConfigureAwait(false);

        if (offset > 0)
            stream.Seek(offset, SeekOrigin.Begin);

        if (count > 0)
            return new SlicedStream(stream, offset, count);

        return stream;
    }

    public async Task RmAsync(string path, bool recursive = false, CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new InvalidOperationException("Cannot remove the MFS root.");

        MfsTreeNode parent = tree.Root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            parent.Children ??= [];
            if (!parent.Children.TryGetValue(parts[i], out MfsTreeNode? child))
                throw new FileNotFoundException($"Path not found: {path}");
            parent = child;
        }

        string name = parts[^1];
        if (parent.Children is null || !parent.Children.TryGetValue(name, out MfsTreeNode? target))
            throw new FileNotFoundException($"Path not found: {path}");

        if (target.Type == MfsNodeType.Directory && target.Children?.Count > 0 && !recursive)
            throw new IOException($"Directory is not empty: {path}. Use recursive=true to remove.");

        parent.Children.Remove(name);

        _logger.LogDebug("Removed {Path}", path);
        await SaveTreeAsync(cancel).ConfigureAwait(false);
    }

    public async Task<MfsStat> StatAsync(string path = "/", CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);
        MfsTreeNode? node = tree.Resolve(path);

        if (node is null)
            throw new FileNotFoundException($"MFS path not found: {path}");

        // If node has a CID, get the actual stats
        if (node.Cid is not null)
        {
            var block = await ipfs.Block.StatAsync(node.Cid, cancel).ConfigureAwait(false);
            return new MfsStat
            {
                Hash = node.Cid,
                Size = block.Size,
                CumulativeSize = block.Size,
                Type = node.Type == MfsNodeType.Directory ? "directory" : "file"
            };
        }

        // For directories without a CID yet (unflushed)
        Cid cid = await FlushAsync(path, cancel).ConfigureAwait(false);
        return new MfsStat
        {
            Hash = cid,
            Size = 0,
            CumulativeSize = 0,
            Blocks = node.Children?.Count ?? 0,
            Type = "directory"
        };
    }

    public async Task WriteAsync(string path, Stream data, MfsWriteOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new MfsWriteOptions { Create = true };
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);

        if (options.Parents)
        {
            string? parentPath = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentPath))
                tree.MkdirP(parentPath);
        }

        // Find the parent directory
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MfsTreeNode parent = tree.Root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            parent.Children ??= [];
            if (!parent.Children.TryGetValue(parts[i], out MfsTreeNode? child))
            {
                if (options.Create)
                    throw new DirectoryNotFoundException($"Parent not found: /{string.Join('/', parts.Take(i + 1))}. Use parents=true.");
                throw new DirectoryNotFoundException($"Parent not found: /{string.Join('/', parts.Take(i + 1))}");
            }
            parent = child;
        }

        string fileName = parts[^1];
        parent.Children ??= [];

        if (!parent.Children.ContainsKey(fileName) && !options.Create)
            throw new FileNotFoundException($"File not found: {path}. Use create=true.");

        // Add the data as a block
        var node = await ipfs.FileSystem.AddAsync(data, fileName, new AddFileOptions
        {
            Pin = true
        }, cancel).ConfigureAwait(false);

        parent.Children[fileName] = new MfsTreeNode
        {
            Cid = node.Id,
            Type = MfsNodeType.File,
            Size = node.Size
        };

        _logger.LogDebug("Wrote {Size} bytes to {Path}", node.Size, path);
        await SaveTreeAsync(cancel).ConfigureAwait(false);
    }

    public async Task<Cid> FlushAsync(string path = "/", CancellationToken cancel = default)
    {
        MfsTree tree = await GetTreeAsync(cancel).ConfigureAwait(false);
        MfsTreeNode? node = tree.Resolve(path);

        if (node is null)
            throw new FileNotFoundException($"MFS path not found: {path}");

        if (node.Cid is not null)
            return node.Cid;

        // Build a DAG for the directory
        if (node.Type == MfsNodeType.Directory)
        {
            using MemoryStream ms = new();
            using (StreamWriter sw = new(ms, leaveOpen: true))
            {
                using JsonTextWriter jw = new(sw);
                jw.WriteStartObject();
                if (node.Children is not null)
                {
                    foreach (var child in node.Children)
                    {
                        Cid childCid = await FlushAsync($"{path.TrimEnd('/')}/{child.Key}", cancel).ConfigureAwait(false);
                        jw.WritePropertyName(child.Key);
                        jw.WriteValue(childCid.ToString());
                    }
                }
                jw.WriteEndObject();
            }

            ms.Position = 0;
            var fsNode = await ipfs.FileSystem.AddAsync(ms, "", new Ipfs.CoreApi.AddFileOptions { Pin = true }, cancel).ConfigureAwait(false);
            node.Cid = fsNode.Id;
            await SaveTreeAsync(cancel).ConfigureAwait(false);
            return node.Cid;
        }

        throw new InvalidOperationException($"Cannot flush a file node without a CID: {path}");
    }
}

internal enum MfsNodeType
{
    File = 0,
    Directory = 1
}

internal class MfsTreeNode
{
    [JsonProperty("cid")]
    [JsonConverter(typeof(CidJsonConverter))]
    public Cid? Cid { get; set; }

    [JsonProperty("type")]
    public MfsNodeType Type { get; set; }

    [JsonProperty("size")]
    public ulong Size { get; set; }

    [JsonProperty("children")]
    public Dictionary<string, MfsTreeNode>? Children { get; set; }
}

internal class MfsTree
{
    [JsonProperty("root")]
    public MfsTreeNode Root { get; set; } = new()
    {
        Type = MfsNodeType.Directory,
        Children = []
    };

    public MfsTreeNode? Resolve(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MfsTreeNode current = Root;

        foreach (string part in parts)
        {
            if (current.Children is null || !current.Children.TryGetValue(part, out MfsTreeNode? child))
                return null;
            current = child;
        }

        return current;
    }

    public void MkdirP(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        MfsTreeNode current = Root;

        foreach (string part in parts)
        {
            current.Children ??= [];
            if (!current.Children.TryGetValue(part, out MfsTreeNode? child))
            {
                child = new MfsTreeNode
                {
                    Type = MfsNodeType.Directory,
                    Children = []
                };
                current.Children[part] = child;
            }
            current = child;
        }
    }
}

/// <summary>
/// JSON converter for Cid to/from string.
/// </summary>
internal class CidJsonConverter : JsonConverter<Cid?>
{
    public override Cid? ReadJson(JsonReader reader, Type objectType, Cid? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        string? value = reader.Value as string;
        return value is null ? null : Cid.Decode(value);
    }

    public override void WriteJson(JsonWriter writer, Cid? value, JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.ToString());
    }
}
