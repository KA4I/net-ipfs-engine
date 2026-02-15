using Ipfs.Engine.UnixFileSystem;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Internal helper for DAG node operations (formerly IObjectApi, which was removed from IpfsCore).
/// </summary>
internal class ObjectHelper(IpfsEngine ipfs)
{
    internal static DagNode EmptyNode;
    internal static DagNode EmptyDirectory;

    static ObjectHelper()
    {
        EmptyNode = new DagNode([]);
        _ = EmptyNode.Id;

        DataMessage dm = new() { Type = DataType.Directory };
        using MemoryStream pb = new();
        ProtoBuf.Serializer.Serialize(pb, dm);
        EmptyDirectory = new DagNode(pb.ToArray());
        _ = EmptyDirectory.Id;
    }

    public async Task<DagNode> GetAsync(Cid id, CancellationToken cancel = default)
    {
        byte[] data = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
        return new DagNode(new MemoryStream(data, false));
    }

    public async Task<IEnumerable<IMerkleLink>> LinksAsync(Cid id, CancellationToken cancel = default)
    {
        if (id.ContentType != "dag-pb")
        {
            return Enumerable.Empty<IMerkleLink>();
        }

        byte[] data = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
        var node = new DagNode(new MemoryStream(data, false));
        return node.Links;
    }
}
