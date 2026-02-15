using Microsoft.Extensions.Logging;
using ICSharpCode.SharpZipLib.Tar;
using Ipfs.CoreApi;
using Ipfs.Engine.UnixFileSystem;
using ProtoBuf;

namespace Ipfs.Engine.CoreApi;

internal class FileSystemApi(IpfsEngine ipfs) : IFileSystemApi
{
    private static readonly int DefaultLinksPerBlock = 174;
    private readonly ILogger<FileSystemApi> _logger = IpfsEngine.LoggerFactory.CreateLogger<FileSystemApi>();

    public async Task<IFileSystemNode> AddAsync(
        Stream stream,
        string name = "",
        AddFileOptions? options = default,
        CancellationToken cancel = default)
    {
        options ??= new AddFileOptions();

        IBlockApi blockService = GetBlockService(options);
        Cryptography.KeyChain keyChain = await ipfs.KeyChainAsync(cancel).ConfigureAwait(false);

        SizeChunker chunker = new();
        List<FileSystemNode> nodes = await chunker.ChunkAsync(stream, name, options, blockService, keyChain, cancel).ConfigureAwait(false);

        // Multiple nodes for the file?
        FileSystemNode node;
        if (options.Trickle == true)
        {
            node = await BuildTrickleTreeAsync(nodes, options, cancel);
        }
        else
        {
            node = await BuildTreeAsync(nodes, options, cancel);
        }

        // Wrap in directory?
        if (options.Wrap == true)
        {
            IFileSystemLink link = node.ToLink(name);
            IFileSystemLink[] wlinks = [link];
            node = await CreateDirectoryAsync(wlinks, options, cancel).ConfigureAwait(false);
        }
        else
        {
            node.Name = name;
        }

        // Advertise the root node.
        if (options.Pin == true && ipfs.IsStarted)
        {
            await ipfs.Dht.ProvideAsync(node.Id, advertise: true, cancel: cancel).ConfigureAwait(false);
        }

        // Return the file system node.
        return node;
    }

    public async IAsyncEnumerable<IFileSystemNode> AddAsync(
        FilePart[] fileParts,
        FolderPart[] folderParts,
        AddFileOptions? options = default,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel = default)
    {
        options ??= new AddFileOptions();
        options.Wrap = false;

        // Build the directory tree structure. Key = normalized folder path, Value = list of child links.
        Dictionary<string, List<IFileSystemLink>> dirLinks = new(StringComparer.Ordinal);

        // Ensure all declared folders exist in the tree (including root "").
        dirLinks[""] = [];
        foreach (FolderPart folder in folderParts)
        {
            string normalized = folder.Name.Replace('\\', '/').Trim('/');
            if (normalized.Length > 0)
            {
                EnsureDirectoryEntry(dirLinks, normalized);
            }
        }

        // Add each file and place its link in the appropriate parent directory.
        foreach (FilePart filePart in fileParts)
        {
            string normalized = filePart.Name.Replace('\\', '/').Trim('/');
            string parentDir = "";
            string fileName = normalized;
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                parentDir = normalized[..lastSlash];
                fileName = normalized[(lastSlash + 1)..];
                EnsureDirectoryEntry(dirLinks, parentDir);
            }

            IFileSystemNode fileNode;
            if (filePart.Data is not null)
            {
                fileNode = await AddAsync(filePart.Data, fileName, options, cancel).ConfigureAwait(false);
            }
            else
            {
                // Empty file
                using MemoryStream empty = new([]);
                fileNode = await AddAsync(empty, fileName, options, cancel).ConfigureAwait(false);
            }

            yield return fileNode;
            dirLinks[parentDir].Add(fileNode.ToLink(fileName));
        }

        // Create directory nodes bottom-up (deepest first).
        foreach (string dir in dirLinks.Keys.OrderByDescending(k => k.Length))
        {
            if (dir.Length == 0)
            {
                continue; // root handled last
            }

            FileSystemNode dirNode = await CreateDirectoryAsync(dirLinks[dir], options, cancel).ConfigureAwait(false);
            string dirName = dir;
            int lastSlash = dir.LastIndexOf('/');
            string parentDir = "";
            if (lastSlash >= 0)
            {
                parentDir = dir[..lastSlash];
                dirName = dir[(lastSlash + 1)..];
            }
            dirNode.Name = dirName;

            yield return dirNode;
            dirLinks[parentDir].Add(dirNode.ToLink(dirName));
        }

        // Create root directory node.
        FileSystemNode root = await CreateDirectoryAsync(dirLinks[""], options, cancel).ConfigureAwait(false);
        root.Name = "";

        // Advertise the root node.
        if (options.Pin == true && ipfs.IsStarted)
        {
            await ipfs.Dht.ProvideAsync(root.Id, advertise: true, cancel: cancel).ConfigureAwait(false);
        }

        yield return root;
    }

    private static void EnsureDirectoryEntry(Dictionary<string, List<IFileSystemLink>> dirLinks, string path)
    {
        if (dirLinks.ContainsKey(path))
        {
            return;
        }
        dirLinks[path] = [];

        // Ensure parent directories exist too.
        int lastSlash = path.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            EnsureDirectoryEntry(dirLinks, path[..lastSlash]);
        }
    }

    public async Task<IFileSystemNode> AddDirectoryAsync(
        string path,
        bool recursive = true,
        AddFileOptions? options = default,
        CancellationToken cancel = default)
    {
        options ??= new AddFileOptions();
        options.Wrap = false;

        // Add the files and sub-directories.
        path = Path.GetFullPath(path);
        IEnumerable<Task<IFileSystemNode>> files = Directory
            .EnumerateFiles(path)
            .OrderBy(s => s)
            .Select(p => AddFileAsync(p, options, cancel));
        if (recursive)
        {
            IEnumerable<Task<IFileSystemNode>> folders = Directory
                .EnumerateDirectories(path)
                .OrderBy(s => s)
                .Select(dir => AddDirectoryAsync(dir, recursive, options, cancel));
            files = files.Union(folders);
        }
        IFileSystemNode[] nodes = await Task.WhenAll(files).ConfigureAwait(false);

        // Create the DAG with links to the created files and sub-directories
        IFileSystemLink[] links = [.. nodes.Select(node => node.ToLink())];
        FileSystemNode fsn = await CreateDirectoryAsync(links, options, cancel).ConfigureAwait(false);
        fsn.Name = Path.GetFileName(path);
        return fsn;
    }

    public async Task<IFileSystemNode> AddFileAsync(
        string path,
        AddFileOptions? options = default,
        CancellationToken cancel = default)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await AddAsync(stream, Path.GetFileName(path), options, cancel).ConfigureAwait(false);
    }

    public async Task<IFileSystemNode> AddTextAsync(
        string text,
        AddFileOptions? options = default,
        CancellationToken cancel = default)
    {
        using MemoryStream ms = new(System.Text.Encoding.UTF8.GetBytes(text), false);
        return await AddAsync(ms, "", options, cancel).ConfigureAwait(false);
    }

    [Obsolete]
    public async Task<Stream> GetAsync(string path, bool compress = false, CancellationToken cancel = default)
    {
        Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
        MemoryStream ms = new();
        using (TarOutputStream tarStream = new(ms, 1))
        using (TarArchive archive = TarArchive.CreateOutputTarArchive(tarStream))
        {
            archive.IsStreamOwner = false;
            await AddTarNodeAsync(cid, cid.Encode(), tarStream, cancel).ConfigureAwait(false);
        }
        ms.Position = 0;
        return ms;
    }

    public async Task<IFileSystemNode> ListAsync(string path, CancellationToken cancel = default)
    {
        Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
        byte[] blockData = await ipfs.Block.GetAsync(cid, cancel).ConfigureAwait(false);

        // TODO: A content-type registry should be used.
        if (cid.ContentType == "dag-pb")
        {
            // fall thru
        }
        else
        {
            return cid.ContentType == "raw"
                ? new FileSystemNode
                {
                    Id = cid,
                    Size = (ulong)blockData.Length
                }
                : cid.ContentType == "cms"
                                ? (IFileSystemNode)new FileSystemNode
                                {
                                    Id = cid,
                                    Size = (ulong)blockData.Length
                                }
                                : throw new NotSupportedException($"Cannot read content type '{cid.ContentType}'.");
        }

        DagNode dag = new(new MemoryStream(blockData, false));
        DataMessage dm = Serializer.Deserialize<DataMessage>(dag.DataStream);
        FileSystemNode fsn = new()
        {
            Id = cid,
            Links = [.. dag.Links
                .Select(l => new FileSystemLink
                {
                    Id = l.Id,
                    Name = l.Name,
                    Size = l.Size
                })],
            IsDirectory = dm.Type == DataType.Directory,
            Size = dm.FileSize ?? 0
        };

        return fsn;
    }

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancel = default)
    {
        using Stream data = await ReadFileAsync(path, cancel).ConfigureAwait(false);
        using StreamReader text = new(data);
        return await text.ReadToEndAsync(cancel).ConfigureAwait(false);
    }

    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancel = default)
    {
        Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
        Cryptography.KeyChain keyChain = await ipfs.KeyChainAsync(cancel).ConfigureAwait(false);
        return await FileSystem.CreateReadStreamAsync(cid, ipfs.Block, keyChain, cancel).ConfigureAwait(false);
    }

    public async Task<Stream> ReadFileAsync(string path, long offset, long count = 0, CancellationToken cancel = default)
    {
        Stream stream = await ReadFileAsync(path, cancel).ConfigureAwait(false);
        return new SlicedStream(stream, offset, count);
    }

    private async Task AddTarNodeAsync(Cid cid, string name, TarOutputStream tar, CancellationToken cancel)
    {
        byte[] blockData = await ipfs.Block.GetAsync(cid, cancel).ConfigureAwait(false);
        DataMessage dm = new() { Type = DataType.Raw };
        DagNode? dag = null;

        if (cid.ContentType == "dag-pb")
        {
            dag = new DagNode(new MemoryStream(blockData, false));
            dm = Serializer.Deserialize<DataMessage>(dag.DataStream);
        }
        TarEntry entry = new(new TarHeader());
        TarHeader header = entry.TarHeader;
        header.Mode = 0x1ff; // 777 in octal
        header.LinkName = string.Empty;
        header.UserName = string.Empty;
        header.GroupName = string.Empty;
        header.Version = "00";
        header.Name = name;
        header.DevMajor = 0;
        header.DevMinor = 0;
        header.UserId = 0;
        header.GroupId = 0;
        header.ModTime = DateTime.Now;

        if (dm.Type == DataType.Directory)
        {
            header.TypeFlag = TarHeader.LF_DIR;
            header.Size = 0;
            tar.PutNextEntry(entry);
            tar.CloseEntry();
        }
        else // Must be a file
        {
            Stream content = await ReadFileAsync(cid, cancel).ConfigureAwait(false);
            header.TypeFlag = TarHeader.LF_NORMAL;
            header.Size = content.Length;
            tar.PutNextEntry(entry);
            await content.CopyToAsync(tar);
            tar.CloseEntry();
        }

        // Recurse over files and subdirectories
        if (dm.Type == DataType.Directory)
        {
            foreach (IMerkleLink link in dag!.Links)
            {
                await AddTarNodeAsync(link.Id, $"{name}/{link.Name}", tar, cancel).ConfigureAwait(false);
            }
        }
    }

    private async Task<FileSystemNode> BuildTreeAsync(
        IEnumerable<FileSystemNode> nodes,
        AddFileOptions options,
        CancellationToken cancel)
    {
        if (nodes.Count() == 1)
        {
            return nodes.First();
        }

        // Bundle DefaultLinksPerBlock links into a block.
        List<FileSystemNode> tree = [];
        for (int i = 0; true; ++i)
        {
            IEnumerable<FileSystemNode> bundle = nodes
                .Skip(DefaultLinksPerBlock * i)
                .Take(DefaultLinksPerBlock);
            if (bundle.Count() == 0)
            {
                break;
            }
            FileSystemNode node = await BuildTreeNodeAsync(bundle, options, cancel);
            tree.Add(node);
        }
        return await BuildTreeAsync(tree, options, cancel);
    }

    private async Task<FileSystemNode> BuildTreeNodeAsync(
        IEnumerable<FileSystemNode> nodes,
        AddFileOptions options,
        CancellationToken cancel)
    {
        IBlockApi blockService = GetBlockService(options);

        // Build the DAG that contains all the file nodes.
        IFileSystemLink[] links = nodes.Select(n => n.ToLink()).ToArray();
        ulong fileSize = (ulong)nodes.Sum(n => (long)n.Size);
        long dagSize = nodes.Sum(n => n.DagSize);
        DataMessage dm = new()
        {
            Type = DataType.File,
            FileSize = fileSize,
            BlockSizes = nodes.Select(n => n.Size).ToArray()
        };
        MemoryStream pb = new();
        ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
        DagNode dag = new(pb.ToArray(), links, options.Hash);

        // Save it.
        IBlockStat stat = await blockService.PutAsync(
            data: dag.ToArray(),
            cidCodec: "dag-pb",
            hash: !string.IsNullOrEmpty(options.Hash) ? MultiHash.ComputeHash(dag.ToArray(), options.Hash) : null,
            pin: options.Pin,
            cancel: cancel).ConfigureAwait(false);
        dag.Id = stat.Id;

        return new FileSystemNode
        {
            Id = dag.Id,
            Size = dm.FileSize ?? 0,
            DagSize = dagSize + (long)dag.Size,
            Links = links
        };
    }

    private async Task<FileSystemNode> CreateDirectoryAsync(IEnumerable<IFileSystemLink> links, AddFileOptions options, CancellationToken cancel)
    {
        DataMessage dm = new() { Type = DataType.Directory };
        MemoryStream pb = new();
        ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
        DagNode dag = new(pb.ToArray(), links, options.Hash);

        // Save it.
        IBlockStat stat = await GetBlockService(options).PutAsync(
            data: dag.ToArray(),
            cidCodec: "dag-pb",
            hash: !string.IsNullOrEmpty(options.Hash) ? MultiHash.ComputeHash(dag.ToArray(), options.Hash) : null,
            pin: options.Pin,
            cancel: cancel).ConfigureAwait(false);

        return new FileSystemNode
        {
            Id = stat.Id,
            Links = links,
            IsDirectory = true
        };
    }

    private IBlockApi GetBlockService(AddFileOptions options)
    {
        return options.OnlyHash == true
            ? new HashOnlyBlockService()
            : ipfs.Block;
    }

    /// <summary>
    /// Builds a trickle DAG from the given leaf nodes.
    /// </summary>
    /// <remarks>
    /// Trickle layout fills subbranches depth-first before moving to the next.
    /// This is suitable for streaming/sequential access patterns.
    /// It uses a branching factor (max children per node) and builds
    /// depth-first rather than the balanced tree approach.
    /// </remarks>
    private async Task<FileSystemNode> BuildTrickleTreeAsync(
        IEnumerable<FileSystemNode> nodes,
        AddFileOptions options,
        CancellationToken cancel)
    {
        if (nodes.Count() == 1)
        {
            return nodes.First();
        }

        const int maxDirectChildren = 174;
        const int maxDepth = 5;
        FileSystemNode[] leafArray = nodes.ToArray();
        int[] offsetBox = [0];

        return await BuildTrickleNodeAsync(leafArray, offsetBox, maxDirectChildren, maxDepth, 0, options, cancel);
    }

    private async Task<FileSystemNode> BuildTrickleNodeAsync(
        FileSystemNode[] leaves,
        int[] offsetBox,
        int maxChildren,
        int maxDepth,
        int depth,
        AddFileOptions options,
        CancellationToken cancel)
    {
        List<FileSystemNode> children = [];

        // Add leaf nodes and subtree nodes up to maxChildren
        while (children.Count < maxChildren && offsetBox[0] < leaves.Length)
        {
            if (depth < maxDepth && offsetBox[0] + 1 < leaves.Length && children.Count > 0)
            {
                // Create a subtree at depth+1
                var subtree = await BuildTrickleNodeAsync(leaves, offsetBox, maxChildren, maxDepth, depth + 1, options, cancel);
                children.Add(subtree);
            }
            else
            {
                children.Add(leaves[offsetBox[0]]);
                offsetBox[0]++;
            }
        }

        if (children.Count == 1)
            return children[0];

        return await BuildTreeNodeAsync(children, options, cancel);
    }

    /// <summary>
    /// A Block service that only computes the block's hash.
    /// </summary>
    private class HashOnlyBlockService : IBlockApi
    {
        public Task<byte[]> GetAsync(Cid id, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        public Task<IBlockStat> PutAsync(
            byte[] data,
            string cidCodec = "raw",
            MultiHash? hash = null,
            bool? pin = null,
            bool? allowBigBlock = null,
            CancellationToken cancel = default)
        {
            string hashAlgorithm = hash?.Algorithm?.Name ?? MultiHash.DefaultAlgorithmName;
            Cid cid = new()
            {
                ContentType = cidCodec,
                Hash = hash ?? MultiHash.ComputeHash(data, hashAlgorithm),
                Version = (cidCodec == "dag-pb" && hashAlgorithm == "sha2-256") ? 0 : 1
            };
            IBlockStat result = new DataBlock { Id = cid, Size = data.Length };
            return Task.FromResult(result);
        }

        public Task<IBlockStat> PutAsync(
            Stream data,
            string cidCodec = "raw",
            MultiHash? hash = null,
            bool? pin = null,
            bool? allowBigBlock = null,
            CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        public Task<Cid> RemoveAsync(Cid id, bool ignoreNonexistent = false, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        public Task<IBlockStat> StatAsync(Cid id, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }
}