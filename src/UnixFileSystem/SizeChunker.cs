using Ipfs.CoreApi;
using Ipfs.Engine.Cryptography;
using Ipfs.Engine.CoreApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.UnixFileSystem
{
    /// <summary>
    ///   Chunks a data stream into data blocks based upon a size.
    /// </summary>
    public class SizeChunker
    {
        private const int DefaultChunkSize = 256 * 1024;

        /// <summary>
        ///   Performs the chunking.
        /// </summary>
        /// <param name="stream">
        ///   The data source.
        /// </param>
        /// <param name="name">
        ///   A name for the data.
        /// </param>
        /// <param name="options">
        ///   The options when adding data to the IPFS file system.
        /// </param>
        /// <param name="blockService">
        ///   The destination for the chunked data block(s).
        /// </param>
        /// <param name="keyChain">
        ///   Used to protect the chunked data blocks(s).
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///    A task that represents the asynchronous operation. The task's value is
        ///    the sequence of file system nodes of the added data blocks.
        /// </returns>
        public async Task<List<FileSystemNode>> ChunkAsync(
            Stream stream, 
            string name,
            AddFileOptions options, 
            IBlockApi blockService,
            KeyChain keyChain,
            CancellationToken cancel)
        {
            var nodes = new List<FileSystemNode>();
            var chunkSize = ParseChunkSize(options.Chunker);
            var chunk = new byte[chunkSize];
            var chunking = true;
            var totalBytes = 0UL;

            while (chunking)
            {
                // Get an entire chunk.
                int length = 0;
                while (length < chunkSize)
                {
                    var n = await stream.ReadAsync(chunk, length, chunkSize - length, cancel).ConfigureAwait(false);
                    if (n < 1)
                    {
                        chunking = false;
                        break;
                    }
                    length += n;
                    totalBytes += (uint)n;
                }

                //  Only generate empty block, when the stream is empty.
                if (length == 0 && nodes.Count > 0)
                {
                    chunking = false;
                    break;
                }

                if (options.Progress != null)
                {
                    options.Progress.Report(new TransferProgress
                    {
                        Name = name,
                        Bytes = totalBytes
                    });
                }

                // CMS encryption (ProtectionKey)
                if (!string.IsNullOrWhiteSpace(options.ProtectionKey))
                {
                    var plain = new byte[length];
                    Array.Copy(chunk, plain, length);
                    var cipher = await keyChain.CreateProtectedDataAsync(options.ProtectionKey, plain, cancel).ConfigureAwait(false);
                    var stat = await blockService.PutAsync(
                        data: cipher,
                        cidCodec: "cms",
                        hash: !string.IsNullOrEmpty(options.Hash) ? MultiHash.ComputeHash(cipher, options.Hash) : null,
                        pin: options.Pin,
                        cancel: cancel).ConfigureAwait(false);
                    nodes.Add(new FileSystemNode
                    {
                        Id = stat.Id,
                        Size = (ulong)length,
                        DagSize = cipher.Length,
                        Links = FileSystemLink.None
                    });
                }
                else if (options.RawLeaves == true)
                {
                    // TODO: Inefficent to copy chunk, use ArraySegment in DataMessage.Data
                    var data = new byte[length];
                    Array.Copy(chunk, data, length);
                    var stat = await blockService.PutAsync(
                        data: data,
                        cidCodec: "raw",
                        hash: !string.IsNullOrEmpty(options.Hash) ? MultiHash.ComputeHash(data, options.Hash) : null,
                        pin: options.Pin,
                        cancel: cancel).ConfigureAwait(false);
                    nodes.Add(new FileSystemNode
                    {
                        Id = stat.Id,
                        Size = (ulong)length,
                        DagSize = length,
                        Links = FileSystemLink.None
                    });
                }
                else
                {
                    // Build the DAG.
                    var dm = new DataMessage
                    {
                        Type = DataType.File,
                        FileSize = (ulong)length,
                    };
                    if (length > 0)
                    {
                        // TODO: Inefficent to copy chunk, use ArraySegment in DataMessage.Data
                        var data = new byte[length];
                        Array.Copy(chunk, data, length);
                        dm.Data = data;
                    }
                    var pb = new MemoryStream();
                    ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
                    var dag = new DagNode(pb.ToArray(), null, options.Hash);

                    // Save it.
                    var stat = await blockService.PutAsync(
                        data: dag.ToArray(),
                        cidCodec: "dag-pb",
                        hash: !string.IsNullOrEmpty(options.Hash) ? MultiHash.ComputeHash(dag.ToArray(), options.Hash) : null,
                        pin: options.Pin,
                        cancel: cancel).ConfigureAwait(false);
                    dag.Id = stat.Id;

                    var node = new FileSystemNode
                    {
                        Id = dag.Id,
                        Size = (ulong)length,
                        DagSize = (long)dag.Size,
                        Links = FileSystemLink.None
                    };
                    nodes.Add(node);
                }
            }

            return nodes;
        }

        private static int ParseChunkSize(string? chunker)
        {
            if (string.IsNullOrEmpty(chunker))
                return DefaultChunkSize;

            // Parse "size-262144" format
            if (chunker.StartsWith("size-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(chunker.AsSpan(5), out int size)
                && size > 0)
            {
                return size;
            }

            return DefaultChunkSize;
        }
    }
}
