using Ipfs.CoreApi;
using PeerTalk; // TODO: need MultiAddress.WithOutPeer (should be in IPFS code)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Ipfs.Server.HttpApi.V0
{
    /// <summary>
    ///   Information from the Distributed Hash Table.
    /// </summary>
    public class DhtPeerDto
    {
        /// <summary>
        ///   The ID of the peer that provided the response.
        /// </summary>
        public string ID;

        /// <summary>
        ///   Unknown.
        /// </summary>
        public int Type; // TODO: what is the type?

        /// <summary>
        ///   The peer that has the information.
        /// </summary>
        public IEnumerable<DhtPeerResponseDto> Responses;

        /// <summary>
        ///   Unknown.
        /// </summary>
        public string Extra = string.Empty;
    }

    /// <summary>
    ///   Information on a peer that has the information.
    /// </summary>
    public class DhtPeerResponseDto
    {
        /// <summary>
        ///   The peer ID.
        /// </summary>
        public string ID;

        /// <summary>
        ///   The listening addresses of the peer.
        /// </summary>
        public IEnumerable<String> Addrs;
    }

    /// <summary>
    ///   Distributed Hash Table.
    /// </summary>
    /// <remarks>
    ///   The DHT is a place to store, not the value, but pointers to peers who have 
    ///   the actual value.
    /// </remarks>
    public class DhtController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public DhtController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///   Query the DHT for all of the multiaddresses associated with a Peer ID.
        /// </summary>
        /// <param name="arg">
        ///   The peer ID to find.
        /// </param>
        /// <returns>
        ///   Information about the peer.
        /// </returns>
        [HttpGet, HttpPost, Route("dht/findpeer")]
        public async Task<DhtPeerDto> FindPeer(string arg)
        {
            var peer = await IpfsCore.Dht.FindPeerAsync(arg, Cancel);
            return new DhtPeerDto
            {
                ID = peer.Id.ToBase58(),
                Responses = new DhtPeerResponseDto[]
                {
                    new DhtPeerResponseDto
                    {
                        ID = peer.Id.ToBase58(),
                        Addrs = peer.Addresses.Select(a => a.WithoutPeerId().ToString())
                    }
                }
            };
        }

        /// <summary>
        ///  Find peers in the DHT that can provide a specific value, given a key.
        /// </summary>
        /// <param name="arg">
        ///   The CID key,
        /// </param>
        /// <param name="limit">
        ///   The maximum number of providers to find.
        /// </param>
        /// <returns>
        ///   Information about the peer providers.
        /// </returns>
        [HttpGet, HttpPost, Route("dht/findprovs")]
        public async Task<IEnumerable<DhtPeerDto>> FindProviders(
            string arg,
            [ModelBinder(Name = "num-providers")] int limit = 20
            )
        {
            var peers = await IpfsCore.Dht.FindProvidersAsync(arg, limit, null, Cancel);
            return peers.Select(peer => new DhtPeerDto
            {
                ID = peer.Id.ToBase58(), // TODO: should be the peer ID that answered the query
                Responses = new DhtPeerResponseDto[]
                {
                    new DhtPeerResponseDto
                    {
                        ID = peer.Id.ToBase58(),
                        Addrs = peer.Addresses.Select(a => a.WithoutPeerId().ToString())
                    }
                }
            });
        }

        /// <summary>
        ///   Get a value from the DHT.
        /// </summary>
        /// <param name="arg">
        ///   The key to get.
        /// </param>
        [HttpGet, HttpPost, Route("dht/get")]
        public async Task<DhtPeerDto> GetValue(string arg)
        {
            var key = Encoding.UTF8.GetBytes(arg);
            var data = await IpfsCore.Dht.GetAsync(key, Cancel);
            return new DhtPeerDto
            {
                ID = "",
                Type = 5, // Value
                Extra = Convert.ToBase64String(data),
                Responses = Array.Empty<DhtPeerResponseDto>()
            };
        }

        /// <summary>
        ///   Put a value into the DHT.
        /// </summary>
        /// <param name="arg">
        ///   The key and value.
        /// </param>
        [HttpGet, HttpPost, Route("dht/put")]
        public async Task<DhtPeerDto> PutValue(string[] arg)
        {
            if (arg == null || arg.Length < 1)
                throw new ArgumentException("At least one argument (key) is required.");
            var key = Encoding.UTF8.GetBytes(arg[0]);
            var task = IpfsCore.Dht.PutAsync(key, out _);
            await task;
            return new DhtPeerDto
            {
                ID = "",
                Type = 5,
                Extra = "",
                Responses = Array.Empty<DhtPeerResponseDto>()
            };
        }

        /// <summary>
        ///   Announce to the network that you are providing the given values.
        /// </summary>
        /// <param name="arg">
        ///   The CID to provide.
        /// </param>
        [HttpGet, HttpPost, Route("dht/provide")]
        public async Task<DhtPeerDto> Provide(string arg)
        {
            await IpfsCore.Dht.ProvideAsync(arg, cancel: Cancel);
            return new DhtPeerDto
            {
                ID = "",
                Type = 4,
                Extra = "",
                Responses = Array.Empty<DhtPeerResponseDto>()
            };
        }

        /// <summary>
        ///   Find the closest peers to a given key.
        /// </summary>
        /// <param name="arg">
        ///   The peer ID to query for.
        /// </param>
        [HttpGet, HttpPost, Route("dht/query")]
        public async Task<IEnumerable<DhtPeerDto>> Query(string arg)
        {
            var peer = await IpfsCore.Dht.FindPeerAsync(arg, Cancel);
            return new[]
            {
                new DhtPeerDto
                {
                    ID = peer.Id.ToBase58(),
                    Responses = new DhtPeerResponseDto[]
                    {
                        new DhtPeerResponseDto
                        {
                            ID = peer.Id.ToBase58(),
                            Addrs = peer.Addresses.Select(a => a.WithoutPeerId().ToString())
                        }
                    }
                }
            };
        }

    }
}
