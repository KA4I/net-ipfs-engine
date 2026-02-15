using Ipfs.CoreApi;
using Ipfs.Engine.LinkedData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PeterO.Cbor;

namespace Ipfs.Engine.CoreApi;

internal class DagApi(IpfsEngine ipfs) : IDagApi
{
    public async Task<JObject> GetAsync(
        Cid id,
        string outputCodec = "dag-json",
        CancellationToken cancel = default)
    {
        byte[] data = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
        ILinkedDataFormat format = GetDataFormat(id);
        CBORObject canonical = format.Deserialise(data);
        using MemoryStream ms = new();
        using StreamReader sr = new(ms);
        using JsonTextReader reader = new(sr);
        canonical.WriteJSONTo(ms);
        ms.Position = 0;
        return (JObject)JToken.ReadFrom(reader);
    }

    public async Task<JToken> GetAsync(
        string path,
        string outputCodec = "dag-json",
        CancellationToken cancel = default)
    {
        if (path.StartsWith("/ipfs/"))
        {
            path = path[6..];
        }

        string[] parts = [.. path.Split('/').Where(p => p.Length > 0)];
        if (parts.Length == 0)
        {
            throw new ArgumentException($"Cannot resolve '{path}'.");
        }

        JToken? token = await GetAsync(Cid.Decode(parts[0]), outputCodec, cancel).ConfigureAwait(false);
        foreach (string child in parts.Skip(1))
        {
            token = ((JObject?)token)?[child];
            if (token == null)
            {
                throw new Exception($"Missing component '{child}'.");
            }
        }

        return token;
    }

    public async Task<T> GetAsync<T>(
        Cid id,
        string outputCodec = "dag-json",
        CancellationToken cancel = default)
    {
        byte[] data = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
        ILinkedDataFormat format = GetDataFormat(id);
        CBORObject canonical = format.Deserialise(data);

        return JObject
            .Parse(canonical.ToJSONString())
            .ToObject<T>()!;
    }

    public async Task<Cid> PutAsync(
        JObject data,
        string storeCodec = "dag-cbor",
        string inputCodec = "dag-json",
        bool? pin = null,
        MultiHash? hash = null,
        bool? allowBigBlock = null,
        CancellationToken cancel = default)
    {
        using MemoryStream ms = new();
        using StreamWriter sw = new(ms);
        using JsonTextWriter writer = new(sw);
        await data.WriteToAsync(writer);
        writer.Flush();
        ms.Position = 0;
        ILinkedDataFormat format = GetDataFormat(storeCodec);
        byte[] block = format.Serialize(CBORObject.ReadJSON(ms));
        IBlockStat stat = await ipfs.Block.PutAsync(block, storeCodec, hash, pin, allowBigBlock, cancel).ConfigureAwait(false);
        return stat.Id;
    }

    public async Task<Cid> PutAsync(Stream data,
        string storeCodec = "dag-cbor",
        string inputCodec = "dag-json",
        bool? pin = null,
        MultiHash? hash = null,
        bool? allowBigBlock = null,
        CancellationToken cancel = default)
    {
        ILinkedDataFormat format = GetDataFormat(storeCodec);
        byte[] block = format.Serialize(CBORObject.Read(data));
        IBlockStat stat = await ipfs.Block.PutAsync(block, storeCodec, hash, pin, allowBigBlock, cancel).ConfigureAwait(false);
        return stat.Id;
    }

    public async Task<Cid> PutAsync(object data,
        string storeCodec = "dag-cbor",
        string inputCodec = "dag-json",
        bool? pin = null,
        MultiHash? hash = null,
        bool? allowBigBlock = null,
        CancellationToken cancel = default)
    {
        ILinkedDataFormat format = GetDataFormat(storeCodec);
        byte[] block = format.Serialize(CBORObject.FromObject(data));
        IBlockStat stat = await ipfs.Block.PutAsync(block, storeCodec, hash, pin, allowBigBlock, cancel).ConfigureAwait(false);
        return stat.Id;
    }

    public async Task<DagResolveOutput> ResolveAsync(string path, CancellationToken cancel = default)
    {
        if (path.StartsWith("/ipfs/"))
        {
            path = path[6..];
        }

        string[] parts = [.. path.Split('/').Where(p => p.Length > 0)];
        if (parts.Length == 0)
        {
            throw new ArgumentException($"Cannot resolve '{path}'.");
        }

        Cid cid = Cid.Decode(parts[0]);
        string remPath = parts.Length > 1 ? string.Join("/", parts.Skip(1)) : "";

        // Walk the path as far as possible through IPLD links
        for (int i = 1; i < parts.Length; i++)
        {
            try
            {
                JObject node = await GetAsync(cid, cancel: cancel).ConfigureAwait(false);
                JToken? link = node[parts[i]];
                if (link is JObject linkObj && linkObj["/"] is JToken cidLink)
                {
                    cid = Cid.Decode(cidLink.ToString());
                    remPath = i + 1 < parts.Length ? string.Join("/", parts.Skip(i + 1)) : "";
                }
                else
                {
                    remPath = string.Join("/", parts.Skip(i));
                    break;
                }
            }
            catch
            {
                remPath = string.Join("/", parts.Skip(i));
                break;
            }
        }

        return new DagResolveOutput((DagCid)cid, remPath);
    }

    public async Task<DagStatSummary> StatAsync(string cid, IProgress<DagStatSummary>? progress = null, CancellationToken cancel = default)
    {
        Cid root = Cid.Decode(cid);
        HashSet<string> visited = [];
        ulong totalSize = 0;
        int uniqueBlocks = 0;
        ulong redundantSize = 0;

        await WalkDagAsync(root, visited, (blockCid, data, isNew) =>
        {
            ulong blockSize = (ulong)data.Length;
            if (isNew)
            {
                totalSize += blockSize;
                uniqueBlocks++;
            }
            else
            {
                redundantSize += blockSize;
            }

            if (progress != null)
            {
                progress.Report(new DagStatSummary
                {
                    UniqueBlocks = uniqueBlocks,
                    TotalSize = totalSize,
                    RedundantSize = redundantSize
                });
            }
        }, cancel).ConfigureAwait(false);

        var dagStat = new DagStat
        {
            Cid = (DagCid)root,
            Size = totalSize,
            NumBlocks = uniqueBlocks
        };

        return new DagStatSummary
        {
            UniqueBlocks = uniqueBlocks,
            TotalSize = totalSize,
            SharedSize = 0,
            Ratio = totalSize > 0 ? 1.0f : 0f,
            RedundantSize = redundantSize,
            DagStatsArray = [dagStat]
        };
    }

    private async Task WalkDagAsync(Cid cid, HashSet<string> visited, Action<Cid, byte[], bool> onBlock, CancellationToken cancel)
    {
        string cidStr = cid.Encode();
        bool isNew = visited.Add(cidStr);

        byte[] data;
        try
        {
            data = await ipfs.Block.GetAsync(cid, cancel).ConfigureAwait(false);
        }
        catch
        {
            return; // Block not available
        }

        onBlock(cid, data, isNew);

        if (!isNew)
            return;

        // Follow links
        try
        {
            var links = await ipfs.ObjectHelper.LinksAsync(cid, cancel).ConfigureAwait(false);
            foreach (var link in links)
            {
                await WalkDagAsync(link.Id, visited, onBlock, cancel).ConfigureAwait(false);
            }
        }
        catch
        {
            // Not a dag-pb node, or no links
        }
    }

    public async Task<Stream> ExportAsync(string cid, CancellationToken cancellationToken = default)
    {
        Cid id = Cid.Decode(cid);
        MemoryStream output = new();
        await CarFormat.ExportAsync(
            id,
            async (c, ct) =>
            {
                try
                {
                    byte[] data = await ipfs.Block.GetAsync(c, ct).ConfigureAwait(false);
                    return new DataBlock { Id = c, DataBytes = data, Size = data.Length };
                }
                catch { return null; }
            },
            async (c, ct) => await ipfs.ObjectHelper.LinksAsync(c, ct).ConfigureAwait(false),
            output,
            cancellationToken).ConfigureAwait(false);
        output.Position = 0;
        return output;
    }

    public async Task<CarImportOutput> ImportAsync(Stream stream, bool? pinRoots = null, bool stats = false, CancellationToken cancellationToken = default)
    {
        IList<Cid> roots = await CarFormat.ImportAsync(
            stream,
            async (cid, data, pin, ct) =>
            {
                await ipfs.Block.PutAsync(
                    data,
                    cidCodec: cid.ContentType ?? "raw",
                    hash: cid.Hash,
                    pin: pin ? true : null,
                    cancel: ct).ConfigureAwait(false);
            },
            pinRoots ?? true,
            cancellationToken).ConfigureAwait(false);

        return new CarImportOutput
        {
            Root = roots.Count > 0
                ? new CarImportOutput.RootMeta { Cid = (DagCid)roots[0] }
                : null
        };
    }

    private static ILinkedDataFormat GetDataFormat(Cid id) => IpldRegistry.Formats.TryGetValue(id.ContentType, out ILinkedDataFormat? format)
            ? format
            : throw new KeyNotFoundException($"Unknown IPLD format '{id.ContentType}'.");

    private static ILinkedDataFormat GetDataFormat(string contentType) => IpldRegistry.Formats.TryGetValue(contentType, out ILinkedDataFormat? format)
            ? format
            : throw new KeyNotFoundException($"Unknown IPLD format '{contentType}'.");
}