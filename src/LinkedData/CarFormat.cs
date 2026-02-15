using Ipfs.Engine.CoreApi;
using PeterO.Cbor;

namespace Ipfs.Engine.LinkedData;

/// <summary>
/// Implements CARv1 (Content Addressed aRchive) format reading and writing.
/// </summary>
/// <remarks>
/// CAR format specification: https://ipld.io/specs/transport/car/carv1/
/// A CARv1 file consists of a header followed by a sequence of blocks.
/// The header is a varint-length-prefixed DAG-CBOR encoded map with "version" and "roots" keys.
/// Each block is a varint(len(cid + data)) followed by the CID bytes and data bytes.
/// </remarks>
internal static class CarFormat
{
    /// <summary>
    /// Export a DAG rooted at the given CID to a CARv1 stream.
    /// </summary>
    public static async Task ExportAsync(
        Cid root,
        Func<Cid, CancellationToken, Task<DataBlock?>> getBlock,
        Func<Cid, CancellationToken, Task<IEnumerable<IMerkleLink>>> getLinks,
        Stream output,
        CancellationToken cancel = default)
    {
        // Write CARv1 header
        CBORObject header = CBORObject.NewMap();
        header["version"] = CBORObject.FromObject(1);
        CBORObject roots = CBORObject.NewArray();
        roots.Add(CBORObject.FromObject(root.ToArray()));
        header["roots"] = roots;
        byte[] headerBytes = header.EncodeToBytes();

        await WriteVarintAsync(output, headerBytes.Length, cancel).ConfigureAwait(false);
        await output.WriteAsync(headerBytes, cancel).ConfigureAwait(false);

        // BFS traversal of the DAG
        HashSet<string> visited = [];
        Queue<Cid> queue = new();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Cid current = queue.Dequeue();
            string cidKey = current.ToString();

            if (!visited.Add(cidKey))
                continue;

            DataBlock? block = await getBlock(current, cancel).ConfigureAwait(false);
            if (block is null)
                continue;

            // Write block: varint(len(cid_bytes + data_bytes)) + cid_bytes + data_bytes
            byte[] cidBytes = current.ToArray();
            byte[] data = block.DataBytes;
            int totalLen = cidBytes.Length + data.Length;

            await WriteVarintAsync(output, totalLen, cancel).ConfigureAwait(false);
            await output.WriteAsync(cidBytes, cancel).ConfigureAwait(false);
            await output.WriteAsync(data, cancel).ConfigureAwait(false);

            // Queue linked blocks
            try
            {
                IEnumerable<IMerkleLink> links = await getLinks(current, cancel).ConfigureAwait(false);
                foreach (IMerkleLink link in links)
                {
                    if (!visited.Contains(link.Id.ToString()))
                        queue.Enqueue(link.Id);
                }
            }
            catch
            {
                // Not all blocks have parseable links (e.g., raw blocks)
            }
        }

        await output.FlushAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Import blocks from a CARv1 stream.
    /// </summary>
    /// <returns>The root CIDs from the CAR header.</returns>
    public static async Task<IList<Cid>> ImportAsync(
        Stream input,
        Func<Cid, byte[], bool, CancellationToken, Task> putBlock,
        bool pinRoots = true,
        CancellationToken cancel = default)
    {
        // Read header
        int headerLen = await ReadVarintAsync(input, cancel).ConfigureAwait(false);
        byte[] headerBytes = new byte[headerLen];
        await ReadExactAsync(input, headerBytes, cancel).ConfigureAwait(false);

        CBORObject header = CBORObject.DecodeFromBytes(headerBytes);
        int version = header["version"].AsInt32();
        if (version != 1 && version != 2)
            throw new NotSupportedException($"Unsupported CAR version: {version}");

        // If CARv2, skip to the CARv1 data payload
        if (version == 2)
        {
            // CARv2 wraps CARv1: read the v2 header to find the data offset
            // For v2, the initial header is the v2 pragma, then characteristics + data offset/size
            // We've already consumed the v2 pragma header, now read v2 fields
            byte[] v2Header = new byte[40]; // characteristics(16) + dataOffset(8) + dataSize(8) + indexOffset(8)
            await ReadExactAsync(input, v2Header, cancel).ConfigureAwait(false);

            // Read the inner CARv1 header
            headerLen = await ReadVarintAsync(input, cancel).ConfigureAwait(false);
            headerBytes = new byte[headerLen];
            await ReadExactAsync(input, headerBytes, cancel).ConfigureAwait(false);
            header = CBORObject.DecodeFromBytes(headerBytes);
        }

        // Extract roots
        List<Cid> roots = [];
        CBORObject rootsArray = header["roots"];
        if (rootsArray is not null)
        {
            for (int i = 0; i < rootsArray.Count; i++)
            {
                byte[] cidBytes = rootsArray[i].GetByteString();
                roots.Add(new Cid { Hash = new MultiHash(cidBytes) });
            }
        }

        // Read blocks
        while (input.Position < input.Length)
        {
            int blockLen;
            try
            {
                blockLen = await ReadVarintAsync(input, cancel).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (blockLen == 0) break;

            byte[] blockData = new byte[blockLen];
            await ReadExactAsync(input, blockData, cancel).ConfigureAwait(false);

            // Parse CID from the front of blockData
            using MemoryStream cidStream = new(blockData);
            Cid cid = ReadCid(cidStream);
            int cidLen = (int)cidStream.Position;
            byte[] rawData = new byte[blockLen - cidLen];
            Buffer.BlockCopy(blockData, cidLen, rawData, 0, rawData.Length);

            bool shouldPin = pinRoots && roots.Any(r => r.ToString() == cid.ToString());
            await putBlock(cid, rawData, shouldPin, cancel).ConfigureAwait(false);
        }

        return roots;
    }

    private static Cid ReadCid(Stream stream)
    {
        // CID format: version (varint) + codec (varint) + multihash
        int version = ReadVarintSync(stream);

        if (version == 0x12 || version == 0x20)
        {
            // CIDv0: starts with multihash directly (0x12 = sha2-256, 0x20 = sha2-256 length)
            // We need to back up and read the full multihash
            stream.Position -= 1;
            byte[] mhBytes = ReadMultiHashBytes(stream);
            return new Cid { Hash = new MultiHash(mhBytes) };
        }

        // CIDv1: version + codec + multihash
        int codec = ReadVarintSync(stream);
        byte[] multihashBytes = ReadMultiHashBytes(stream);
        MultiHash mh = new(multihashBytes);

        return new Cid
        {
            Version = version,
            ContentType = CodecToContentType(codec),
            Hash = mh
        };
    }

    private static byte[] ReadMultiHashBytes(Stream stream)
    {
        long start = stream.Position;
        int hashCode = ReadVarintSync(stream);
        int digestSize = ReadVarintSync(stream);
        long headerLen = stream.Position - start;

        byte[] result = new byte[headerLen + digestSize];
        stream.Position = start;
        stream.ReadExactly(result, 0, result.Length);
        return result;
    }

    private static string CodecToContentType(int codec) => codec switch
    {
        0x55 => "raw",
        0x70 => "dag-pb",
        0x71 => "dag-cbor",
        0x0129 => "dag-json",
        _ => $"0x{codec:x}"
    };

    private static async Task WriteVarintAsync(Stream stream, int value, CancellationToken cancel)
    {
        while (value >= 0x80)
        {
            byte b = (byte)(value | 0x80);
            stream.WriteByte(b);
            value >>= 7;
        }
        stream.WriteByte((byte)value);
        await Task.CompletedTask;
    }

    private static async Task<int> ReadVarintAsync(Stream stream, CancellationToken cancel)
    {
        int result = 0;
        int shift = 0;
        int b;

        do
        {
            b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException("Unexpected end of CAR stream while reading varint.");

            result |= (b & 0x7F) << shift;
            shift += 7;

            if (shift > 35)
                throw new InvalidDataException("Varint too large in CAR stream.");
        }
        while ((b & 0x80) != 0);

        await Task.CompletedTask;
        return result;
    }

    private static int ReadVarintSync(Stream stream)
    {
        int result = 0;
        int shift = 0;
        int b;

        do
        {
            b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException();

            result |= (b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);

        return result;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancel)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancel).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of CAR stream.");
            offset += read;
        }
    }
}
