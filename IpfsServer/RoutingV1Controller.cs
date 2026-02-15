using Ipfs;
using Ipfs.CoreApi;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Server
{
    /// <summary>
    ///   Routing V1 HTTP API — implements the IPIP-337 delegated routing spec.
    /// </summary>
    /// <remarks>
    ///   Kubo 0.40 exposes this at /routing/v1 by default, allowing light clients
    ///   (e.g., browsers) to discover providers and peers without running a full DHT.
    ///   See https://specs.ipfs.tech/routing/http-routing-v1/
    /// </remarks>
    [Route("routing/v1")]
    [ApiController]
    public class RoutingV1Controller : ControllerBase
    {
        readonly ICoreApi ipfs;

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public RoutingV1Controller(ICoreApi ipfs)
        {
            this.ipfs = ipfs;
        }

        CancellationToken Cancel => HttpContext.RequestAborted;

        /// <summary>
        ///   Find providers for a given CID.
        /// </summary>
        /// <param name="cid">The content identifier to find providers for.</param>
        [HttpGet("providers/{cid}")]
        public async Task<IActionResult> FindProviders(string cid)
        {
            Cid id;
            try
            {
                id = Cid.Decode(cid);
            }
            catch
            {
                return BadRequest(new { error = "Invalid CID" });
            }

            try
            {
                var providers = await ipfs.Dht.FindProvidersAsync(id, 20, cancel: Cancel).ConfigureAwait(false);
                var results = providers.Select(p => new ProviderRecord
                {
                    Protocol = "transport-bitswap",
                    Schema = "bitswap",
                    ID = p.Id?.ToBase58(),
                    Addrs = p.Addresses?.Select(a => a.ToString()).ToArray()
                }).ToList();

                return Ok(new { Providers = results });
            }
            catch (Exception)
            {
                return Ok(new { Providers = Array.Empty<object>() });
            }
        }

        /// <summary>
        ///   Find a peer by its ID (IPIP-476: Closest Peers API).
        /// </summary>
        /// <param name="peerId">The peer ID to look up.</param>
        [HttpGet("peers/{peerId}")]
        public async Task<IActionResult> FindPeer(string peerId)
        {
            MultiHash id;
            try
            {
                id = new MultiHash(peerId);
            }
            catch
            {
                return BadRequest(new { error = "Invalid peer ID" });
            }

            try
            {
                var peer = await ipfs.Dht.FindPeerAsync(id, Cancel).ConfigureAwait(false);
                var record = new PeerRecord
                {
                    Schema = "peer",
                    ID = peer.Id?.ToBase58(),
                    Addrs = peer.Addresses?.Select(a => a.ToString()).ToArray(),
                    Protocols = peer.AgentVersion != null ? new[] { peer.AgentVersion } : null
                };

                return Ok(new { Peers = new[] { record } });
            }
            catch (Exception)
            {
                return NotFound(new { error = $"Peer not found: {peerId}" });
            }
        }

        class ProviderRecord
        {
            public string Protocol { get; set; }
            public string Schema { get; set; }
            public string ID { get; set; }
            public string[] Addrs { get; set; }
        }

        class PeerRecord
        {
            public string Schema { get; set; }
            public string ID { get; set; }
            public string[] Addrs { get; set; }
            public string[] Protocols { get; set; }
        }
    }
}
