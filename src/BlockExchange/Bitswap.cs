#nullable disable
using Microsoft.Extensions.Logging;
using Ipfs.CoreApi;
using PeerTalk;
using System.Collections.Concurrent;
using System.Linq;

namespace Ipfs.Engine.BlockExchange;

/// <summary>
/// Exchange blocks with other peers.
/// </summary>
public class Bitswap : IService
{
    private readonly ILogger<Bitswap> _logger = IpfsEngine.LoggerFactory.CreateLogger<Bitswap>();

        private readonly ConcurrentDictionary<Cid, WantedBlock> wants = new();
        private readonly ConcurrentDictionary<Peer, BitswapLedger> peerLedgers = new();

        /// <summary>
        /// The supported bitswap protocols.
        /// </summary>
        /// <value>Defaults to <see cref="Bitswap11"/> and <see cref="Bitswap1"/>.</value>
        public IBitswapProtocol[] Protocols;

        /// <summary>
        /// The number of blocks sent by other peers.
        /// </summary>
        private ulong BlocksReceived;

        /// <summary>
        /// The number of bytes sent by other peers.
        /// </summary>
        private ulong DataReceived;

        /// <summary>
        /// The number of blocks sent to other peers.
        /// </summary>
        private ulong BlocksSent;

        /// <summary>
        /// The number of bytes sent to other peers.
        /// </summary>
        private ulong DataSent;

        /// <summary>
        /// The number of duplicate blocks sent by other peers.
        /// </summary>
        /// <remarks>A duplicate block is a block that is already stored in the local repository.</remarks>
        private ulong DupBlksReceived;

        /// <summary>
        /// The number of duplicate bytes sent by other peers.
        /// </summary>
        /// <remarks>A duplicate block is a block that is already stored in the local repository.</remarks>
        private ulong DupDataReceived;

        /// <summary>
        /// Creates a new instance of the <see cref="Bitswap"/> class.
        /// </summary>
        public Bitswap()
        {
            Protocols =
                [
                    new Bitswap12 { Bitswap = this },
                    new Bitswap11 { Bitswap = this },
                    new Bitswap1 { Bitswap = this }
                ];
        }

        /// <summary>
        /// Provides access to other peers.
        /// </summary>
        public Swarm Swarm { get; set; }

        /// <summary>
        /// Provides access to blocks of data.
        /// </summary>
        public IBlockApi BlockService { get; set; }

        /// <summary>
        /// Statistics on the bitswap component.
        /// </summary>
        /// <seealso cref="IStatsApi"/>
        public BitswapData Statistics => new()
        {
            BlocksReceived = BlocksReceived,
            BlocksSent = BlocksSent,
            DataReceived = DataReceived,
            DataSent = DataSent,
            DupBlksReceived = DupBlksReceived,
            DupDataReceived = DupDataReceived,
            ProvideBufLen = 0, // TODO: Unknown meaning
            Peers = Swarm.KnownPeers.Select(p => p.Id),
            Wantlist = wants.Keys
        };

        /// <summary>
        /// Gets the bitswap ledger for the specified peer.
        /// </summary>
        /// <param name="peer">
        /// The peer to get information on. If the peer is unknown, then a ledger with zeros is returned.
        /// </param>
        /// <returns>Statistics on the bitswap blocks exchanged with the peer.</returns>
        /// <seealso cref="IBitswapApi.LedgerAsync(Peer, CancellationToken)"/>
        public BitswapLedger PeerLedger(Peer peer)
        {
            return peerLedgers.TryGetValue(peer, out BitswapLedger ledger) ? ledger : new BitswapLedger { Peer = peer };
        }

        /// <summary>
        /// Raised when a blocked is needed.
        /// </summary>
        /// <remarks>Only raised when a block is first requested.</remarks>
        public event EventHandler<CidEventArgs> BlockNeeded;

        /// <inheritdoc/>
        public Task StartAsync()
        {
            _logger.LogDebug("Starting");

            foreach (IBitswapProtocol protocol in Protocols)
            {
                Swarm.AddProtocol(protocol);
            }

            Swarm.ConnectionEstablished += Swarm_ConnectionEstablished;

            // TODO: clear the stats.
            peerLedgers.Clear();

            return Task.CompletedTask;
        }

        // When a connection is established (1) Send the local peer's want list to the remote
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "<Pending>")]
        private async void Swarm_ConnectionEstablished(object sender, PeerConnection connection)
        {
            if (wants.IsEmpty)
            {
                return;
            }
            try
            {
                // There is a race condition between getting the remote identity and the remote
                // sending the first wantlist.
                Peer peer = await connection.IdentityEstablished.Task.ConfigureAwait(false);

                // Fire and forget.
                SendWantListAsync(peer, wants.Values, true).Forget();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Sending want list");
            }
        }

        /// <inheritdoc/>
        public Task StopAsync()
        {
            _logger.LogDebug("Stopping");

            Swarm.ConnectionEstablished -= Swarm_ConnectionEstablished;
            foreach (IBitswapProtocol protocol in Protocols)
            {
                Swarm.RemoveProtocol(protocol);
            }

            foreach (Cid cid in wants.Keys)
            {
                Unwant(cid);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The blocks needed by the peer.
        /// </summary>
        /// <param name="peer">The unique ID of the peer.</param>
        /// <returns>The sequence of CIDs need by the <paramref name="peer"/>.</returns>
        public IEnumerable<Cid> PeerWants(MultiHash peer)
        {
            return wants.Values
                .Where(w => w.Peers.Contains(peer))
                .Select(w => w.Id);
        }

        /// <summary>
        /// Adds a block to the want list.
        /// </summary>
        /// <param name="id">The CID of the block to add to the want list.</param>
        /// <param name="peer">
        /// The unique ID of the peer that wants the block. This is for information purposes only.
        /// </param>
        /// <param name="cancel">
        /// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task's result is the contents of block.
        /// </returns>
        /// <remarks>
        /// Other peers are informed that the block is needed by this peer. Hopefully, someone will
        /// forward it to us.
        /// <para>
        /// Besides using <paramref name="cancel"/> for cancellation, the <see cref="Unwant"/>
        /// method will also cancel the operation.
        /// </para>
        /// </remarks>
        public Task<IDataBlock> WantAsync(Cid id, MultiHash peer, CancellationToken cancel)
        {
            _logger.LogDebug("{Peer} wants {Cid}", peer, id);

            TaskCompletionSource<IDataBlock> tsc = new();
            WantedBlock want = wants.AddOrUpdate(
                id,
                (key) => new WantedBlock
                {
                    Id = id,
                    Consumers = [tsc],
                    Peers = [peer]
                },
                (key, block) =>
                {
                    block.Peers.Add(peer);
                    block.Consumers.Add(tsc);
                    return block;
                }
            );

            // If cancelled, then the block is unwanted.
            _ = cancel.Register(() => Unwant(id));

            // If first time, tell other peers.
            if (want.Consumers.Count == 1)
            {
                _ = SendWantListToAllAsync([want], full: false);
                BlockNeeded?.Invoke(this, new CidEventArgs { Id = want.Id });
            }

            return tsc.Task;
        }

        /// <summary>
        /// Removes the block from the want list.
        /// </summary>
        /// <param name="id">The CID of the block to remove from the want list.</param>
        /// <remarks>
        /// Any tasks waiting for the block are cancelled.
        /// <para>No exception is thrown if the <paramref name="id"/> is not on the want list.</para>
        /// </remarks>
        public void Unwant(Cid id)
        {
            _logger.LogDebug("Unwant {Cid}", id);

            if (wants.TryRemove(id, out WantedBlock block))
            {
                foreach (TaskCompletionSource<IDataBlock> consumer in block.Consumers)
                {
                    consumer.SetCanceled();
                }
            }

            // TODO: Tell the swarm
        }

        /// <summary>
        /// Indicate that a remote peer sent a block.
        /// </summary>
        /// <param name="remote">The peer that sent the block.</param>
        /// <param name="block">The data for the block.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks>
        /// <para>Updates the statistics.</para>
        /// <para>
        /// If the block is acceptable then the <paramref name="block"/> is added to local cache via
        /// the <see cref="BlockService"/>.
        /// </para>
        /// </remarks>
        public Task OnBlockReceivedAsync(Peer remote, byte[] block)
        {
            // Try to determine the content type from the want list by matching the block's hash.
            var hash = MultiHash.ComputeHash(block);
            var wantedEntry = wants.Values.FirstOrDefault(w => w.Id.Hash == hash);
            if (wantedEntry is not null)
            {
                return OnBlockReceivedAsync(remote, block, wantedEntry.Id.ContentType, wantedEntry.Id.Hash.Algorithm.Name);
            }
            return OnBlockReceivedAsync(remote, block, Cid.DefaultContentType, MultiHash.DefaultAlgorithmName);
        }

        /// <summary>
        /// Indicate that a remote peer sent a block.
        /// </summary>
        /// <param name="remote">The peer that sent the block.</param>
        /// <param name="block">The data for the block.</param>
        /// <param name="contentType">The <see cref="Cid.ContentType"/> of the block.</param>
        /// <param name="multiHash">The multihash algorithm name of the block.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks>
        /// <para>Updates the statistics.</para>
        /// <para>
        /// If the block is acceptable then the <paramref name="block"/> is added to local cache via
        /// the <see cref="BlockService"/>.
        /// </para>
        /// </remarks>
        public async Task OnBlockReceivedAsync(Peer remote, byte[] block, string contentType, string multiHash)
        {
            // Update statistics.
            ++BlocksReceived;
            DataReceived += (ulong)block.LongLength;
            _ = peerLedgers.AddOrUpdate(remote,
                (peer) => new BitswapLedger
                {
                    Peer = peer,
                    BlocksExchanged = 1,
                    DataReceived = (ulong)block.LongLength
                },
                (peer, ledger) =>
                {
                    ++ledger.BlocksExchanged;
                    DataReceived += (ulong)block.LongLength;
                    return ledger;
                });

            // TODO: Detect if duplicate and update stats
            bool isDuplicate = false;
            if (isDuplicate)
            {
                ++DupBlksReceived;
                DupDataReceived += (ulong)block.Length;
            }

            // TODO: Determine if we should accept the block from the remote.
            bool acceptable = true;
            if (acceptable)
            {
                _ = await BlockService
                    .PutAsync(
                        data: block,
                        cidCodec: contentType,
                        hash: MultiHash.ComputeHash(block, multiHash),
                        pin: false)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Indicate that the local peer sent a block to a remote peer.
        /// </summary>
        /// <param name="remote">The peer that sent the block.</param>
        /// <param name="block">The data for the block.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task OnBlockSentAsync(Peer remote, IDataBlock block)
        {
            long size = block is IBlockStat stat ? stat.Size : 0;
            ++BlocksSent;
            DataSent += (ulong)size;
            _ = peerLedgers.AddOrUpdate(remote,
                (peer) => new BitswapLedger
                {
                    Peer = peer,
                    BlocksExchanged = 1,
                    DataSent = (ulong)size
                },
                (peer, ledger) =>
                {
                    ++ledger.BlocksExchanged;
                    DataSent += (ulong)size;
                    return ledger;
                });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Indicate that a block is found.
        /// </summary>
        /// <param name="block">The block that was found.</param>
        /// <returns>The number of consumers waiting for the <paramref name="block"/>.</returns>
        /// <remarks>
        /// <b>Found</b> should be called whenever a new block is discovered. It will continue any
        /// Task that is waiting for the block and remove the block from the want list.
        /// </remarks>
        public int Found(IDataBlock block)
        {
            if (wants.TryRemove(block.Id, out WantedBlock want))
            {
                foreach (TaskCompletionSource<IDataBlock> consumer in want.Consumers)
                {
                    consumer.SetResult(block);
                }
                return want.Consumers.Count;
            }

            return 0;
        }

        /// <summary>
        /// Send our want list to the connected peers.
        /// </summary>
        private async Task SendWantListToAllAsync(IEnumerable<WantedBlock> wants, bool full)
        {
            if (Swarm is null)
            {
                return;
            }

            try
            {
                Task[] tasks = [.. Swarm.KnownPeers
                    .Where(p => p.ConnectedAddress is not null)
                    .Select(p => SendWantListAsync(p, wants, full))];
                _logger.LogDebug("Spamming {Count} connected peers", tasks.Length);

                await Task.WhenAll(tasks).ConfigureAwait(false);

                _logger.LogDebug("Spam {Count} connected peers done", tasks.Length);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Sending to all failed");
            }
        }

        private async Task SendWantListAsync(Peer peer, IEnumerable<WantedBlock> wants, bool full)
        {
            // Send the want list to the peer on any bitswap protocol that it supports.
            foreach (IBitswapProtocol protocol in Protocols)
            {
                try
                {
                    using System.IO.Stream stream = await Swarm.DialAsync(peer, protocol.ToString()).ConfigureAwait(false);
                    await protocol.SendWantsAsync(stream, wants, full: full).ConfigureAwait(false);
                    return;
                }
                catch (Exception)
                {
                    _logger.LogDebug("{Peer} refused {Protocol}", peer, protocol);
                }
            }

            _logger.LogWarning("{Peer} does not support any bitswap protocol", peer);
        }
    }