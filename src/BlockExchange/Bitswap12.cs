#nullable disable
using Common.Logging;
using PeerTalk;
using ProtoBuf;
using Semver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable 0649

namespace Ipfs.Engine.BlockExchange
{
    /// <summary>
    /// Bitswap Protocol version 1.2.0
    /// </summary>
    /// <remarks>
    /// Adds HAVE/DONT_HAVE block presence messages and WantType (Block vs Have).
    /// See https://github.com/ipfs/specs/blob/main/BITSWAP.md
    /// </remarks>
    public class Bitswap12 : IBitswapProtocol
    {
        private static readonly ILog log = LogManager.GetLogger<Bitswap12>();

        /// <inheritdoc/>
        public string Name { get; } = "ipfs/bitswap";

        /// <inheritdoc/>
        public SemVersion Version { get; } = new SemVersion(1, 2);

        /// <inheritdoc/>
        public override string ToString() => $"/{Name}/{Version}";

        /// <summary>
        /// The <see cref="Bitswap"/> service.
        /// </summary>
        public Bitswap Bitswap { get; set; }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "<Pending>")]
        public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
        {
            _ = await connection.IdentityEstablished.Task.ConfigureAwait(false);

            while (true)
            {
                Message request = await ProtoBufHelper.ReadMessageAsync<Message>(stream, cancel).ConfigureAwait(false);

                // Process want list
                if (request.wantlist?.entries is not null)
                {
                    foreach (Entry entry in request.wantlist.entries)
                    {
                        Cid cid = Cid.Read(entry.block);
                        if (entry.cancel)
                        {
                            Bitswap.Unwant(cid);
                        }
                        else if (entry.wantType == WantType.Have)
                        {
                            // Peer wants to know if we HAVE the block
                            _ = SendBlockPresenceAsync(cid, connection.RemotePeer, entry.sendDontHave, cancel);
                        }
                        else
                        {
                            _ = GetBlockAsync(cid, connection.RemotePeer, entry.sendDontHave, CancellationToken.None);
                        }
                    }
                }

                // Forward sent blocks
                if (request.payload is not null)
                {
                    log.Debug($"got block(s) from {connection.RemotePeer}");
                    foreach (Block sentBlock in request.payload)
                    {
                        using MemoryStream ms = new(sentBlock.prefix);
                        int version = ms.ReadVarint32();
                        string contentType = ms.ReadMultiCodec().Name;
                        string multiHash = MultiHash.GetHashAlgorithmName(ms.ReadVarint32());
                        await Bitswap.OnBlockReceivedAsync(connection.RemotePeer, sentBlock.data, contentType, multiHash);
                    }
                }

                // Process block presences (HAVE/DONT_HAVE from remote)
                if (request.blockPresences is not null)
                {
                    foreach (var bp in request.blockPresences)
                    {
                        var cid = Cid.Read(bp.cid);
                        if (bp.type == BlockPresenceType.DontHave)
                        {
                            log.Debug($"{connection.RemotePeer} DONT_HAVE {cid}");
                        }
                        // HAVE responses — the peer has the block, we can request it
                    }
                }
            }
        }

        private async Task SendBlockPresenceAsync(Cid cid, Peer remotePeer, bool sendDontHave, CancellationToken cancel)
        {
            try
            {
                var stat = await Bitswap.BlockService.StatAsync(cid, cancel).ConfigureAwait(false);
                bool have = stat is not null;

                if (!have && !sendDontHave)
                    return;

                using Stream stream = await Bitswap.Swarm.DialAsync(remotePeer, ToString(), cancel).ConfigureAwait(false);
                var msg = new Message
                {
                    blockPresences =
                    [
                        new BlockPresence
                        {
                            cid = cid.ToArray(),
                            type = have ? BlockPresenceType.Have : BlockPresenceType.DontHave
                        }
                    ]
                };
                Serializer.SerializeWithLengthPrefix(stream, msg, PrefixStyle.Base128);
                await stream.FlushAsync(cancel).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log.Warn($"Failed to send block presence for {cid}", e);
            }
        }

        private async Task GetBlockAsync(Cid cid, Peer remotePeer, bool sendDontHave, CancellationToken cancel)
        {
            try
            {
                IDataBlock block;
                if ((await Bitswap.BlockService.StatAsync(cid, cancel).ConfigureAwait(false)) is not null)
                {
                    byte[] data = await Bitswap.BlockService.GetAsync(cid, cancel).ConfigureAwait(false);
                    block = new CoreApi.DataBlock { Id = cid, DataBytes = data, Size = data.Length };
                }
                else
                {
                    if (sendDontHave)
                    {
                        try
                        {
                            // Send DONT_HAVE
                            using Stream dontHaveStream = await Bitswap.Swarm.DialAsync(remotePeer, ToString(), cancel).ConfigureAwait(false);
                            var dontHaveMsg = new Message
                            {
                                blockPresences =
                                [
                                    new BlockPresence
                                    {
                                        cid = cid.ToArray(),
                                        type = BlockPresenceType.DontHave
                                    }
                                ]
                            };
                            Serializer.SerializeWithLengthPrefix(dontHaveStream, dontHaveMsg, PrefixStyle.Base128);
                            await dontHaveStream.FlushAsync(cancel).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            log.Warn($"Failed to send DONT_HAVE for {cid} to {remotePeer}", e);
                        }
                    }

                    block = await Bitswap.WantAsync(cid, remotePeer.Id, cancel).ConfigureAwait(false);
                }

                using Stream stream = await Bitswap.Swarm.DialAsync(remotePeer, ToString(), cancel).ConfigureAwait(false);
                await SendAsync(stream, block, cancel).ConfigureAwait(false);
                await Bitswap.OnBlockSentAsync(remotePeer, block).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception e)
            {
                log.Warn("getting block for remote failed", e);
            }
        }

        /// <inheritdoc/>
        public async Task SendWantsAsync(
            Stream stream,
            IEnumerable<WantedBlock> wants,
            bool full = true,
            CancellationToken cancel = default)
        {
            Message message = new()
            {
                wantlist = new Wantlist
                {
                    full = full,
                    entries = [.. wants
                        .Select(w => new Entry
                        {
                            block = w.Id.ToArray(),
                            wantType = WantType.Block,
                            sendDontHave = true
                        })]
                },
                payload = []
            };

            Serializer.SerializeWithLengthPrefix(stream, message, PrefixStyle.Base128);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
        }

        internal async Task SendAsync(Stream stream, IDataBlock block, CancellationToken cancel = default)
        {
            log.Debug($"Sending block {block.Id}");
            byte[] dataBytes = block is CoreApi.DataBlock db ? db.DataBytes : throw new InvalidOperationException("Block must be a DataBlock to send.");
            Message message = new()
            {
                payload =
                [
                    new Block
                    {
                        prefix = GetCidPrefix(block.Id),
                        data = dataBytes
                    }
                ]
            };

            Serializer.SerializeWithLengthPrefix(stream, message, PrefixStyle.Base128);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
        }

        private static byte[] GetCidPrefix(Cid id)
        {
            using MemoryStream ms = new();
            ms.WriteVarint(id.Version);
            ms.WriteMultiCodec(id.ContentType);
            ms.WriteVarint(id.Hash.Algorithm.Code);
            ms.WriteVarint(id.Hash.Digest.Length);
            return ms.ToArray();
        }

        // --- Protobuf ---

        enum WantType
        {
            Block = 0,
            Have = 1,
        }

        enum BlockPresenceType
        {
            Have = 0,
            DontHave = 1,
        }

        [ProtoContract]
        private class Entry
        {
            [ProtoMember(1)]
            public byte[] block;

            [ProtoMember(2)]
            public int priority = 1;

            [ProtoMember(3)]
            public bool cancel;

            [ProtoMember(4)]
            public WantType wantType;

            [ProtoMember(5)]
            public bool sendDontHave;
        }

        [ProtoContract]
        private class Wantlist
        {
            [ProtoMember(1)]
            public Entry[] entries;

            [ProtoMember(2)]
            public bool full;
        }

        [ProtoContract]
        private class Block
        {
            [ProtoMember(1)]
            public byte[] prefix;

            [ProtoMember(2)]
            public byte[] data;
        }

        [ProtoContract]
        private class BlockPresence
        {
            [ProtoMember(1)]
            public byte[] cid;

            [ProtoMember(2)]
            public BlockPresenceType type;
        }

        [ProtoContract]
        private class Message
        {
            [ProtoMember(1)]
            public Wantlist wantlist;

            [ProtoMember(2)]
            public byte[][] blocks;

            [ProtoMember(3)]
            public List<Block> payload;

            [ProtoMember(4)]
            public List<BlockPresence> blockPresences;

            [ProtoMember(5)]
            public int pendingBytes;
        }
    }
}
