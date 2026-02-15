using Ipfs.CoreApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace Ipfs.Server.HttpApi.V0
{
    /// <summary>
    ///   Content that has an associated name.
    /// </summary>
    public class NamedContentDto
    {
        /// <summary>
        ///   Path to the name, "/ipns/...".
        /// </summary>
        public string Name;

        /// <summary>
        ///   Path to the content, "/ipfs/...".
        /// </summary>
        public string Value;
    }

    /// <summary>
    ///   Manages the IPNS (Interplanetary Name Space).
    /// </summary>
    /// <remarks>
    ///   IPNS is a PKI namespace, where names are the hashes of public keys, and
    ///   the private key enables publishing new(signed) values. The default name
    ///   is the node's own <see cref="Peer.Id"/>,
    ///   which is the hash of its public key.
    /// </remarks>
    public class NameController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public NameController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///   Resolve a name.
        /// </summary>
        [HttpGet, HttpPost, Route("name/resolve")]
        public async Task<PathDto> Resolve(
            string arg,
            bool recursive = false,
            bool nocache = false)
        {
            var path = await IpfsCore.Name.ResolveAsync(arg, recursive, nocache, Cancel);
            return new PathDto(path);
        }

        /// <summary>
        ///   Publish content.
        /// </summary>
        /// <param name="arg">
        ///   The CID or path to the content to publish.
        /// </param>
        /// <param name="resolve">
        ///   Resolve before publishing.
        /// </param>
        /// <param name="key">
        ///   The local key name used to sign the content.
        /// </param>
        /// <param name="lifetime">
        ///   Duration that the record will be valid for.
        /// </param>
        [HttpGet, HttpPost, Route("name/publish")]
        public async Task<NamedContentDto> Publish(
            string arg,
            bool resolve = true,
            string key = "self",
            string lifetime = "24h")
        {
            if (String.IsNullOrWhiteSpace(arg))
                throw new ArgumentNullException("arg", "The name is required.");
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException("type", "The key name is required.");
            if (String.IsNullOrWhiteSpace(lifetime))
                throw new ArgumentNullException("type", "The lifetime is required.");

            var duration = Duration.Parse(lifetime);
            var content = await IpfsCore.Name.PublishAsync(arg, resolve, key, duration, Cancel);
            return new NamedContentDto
            {
                Name = content.NamePath,
                Value = content.ContentPath
            };
        }

        /// <summary>
        ///   Get a raw IPNS record from the routing system (Kubo 0.40).
        /// </summary>
        /// <param name="arg">
        ///   The IPNS name to retrieve the record for (e.g., /ipns/k51... or a peer ID).
        /// </param>
        [HttpGet, HttpPost, Route("name/get")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> Get(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                throw new ArgumentNullException("arg", "The IPNS name is required.");

            // Strip /ipns/ prefix if present
            var name = arg.StartsWith("/ipns/", StringComparison.OrdinalIgnoreCase)
                ? arg.Substring(6)
                : arg;

            // Retrieve the raw IPNS record via the DHT/value store
            var key = Encoding.UTF8.GetBytes($"/ipns/{name}");
            var record = await IpfsCore.Dht.GetAsync(key, Cancel);

            return File(record, "application/vnd.ipfs.ipns-record");
        }

        /// <summary>
        ///   Put a raw IPNS record into the routing system (Kubo 0.40).
        /// </summary>
        /// <param name="arg">
        ///   The IPNS name/key to publish the record for.
        /// </param>
        /// <param name="file">
        ///   The raw IPNS record bytes.
        /// </param>
        [HttpPost("name/put")]
        public async Task<IActionResult> Put(string arg, IFormFile file)
        {
            if (string.IsNullOrWhiteSpace(arg))
                throw new ArgumentNullException("arg", "The IPNS name is required.");
            if (file == null)
                throw new ArgumentNullException("file", "The IPNS record file is required.");

            var name = arg.StartsWith("/ipns/", StringComparison.OrdinalIgnoreCase)
                ? arg.Substring(6)
                : arg;

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, Cancel);

            var key = Encoding.UTF8.GetBytes($"/ipns/{name}");
            await IpfsCore.Dht.PutAsync(key, out _, Cancel);

            return Ok(new { Name = name, Status = "ok" });
        }
    }
}
