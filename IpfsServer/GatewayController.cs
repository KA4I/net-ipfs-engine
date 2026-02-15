using Ipfs;
using Ipfs.CoreApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Server
{
    /// <summary>
    ///   Configuration for the IPFS Gateway.
    /// </summary>
    public class GatewayOptions
    {
        /// <summary>
        ///   Whether the Routing V1 API is exposed at /routing/v1.
        /// </summary>
        /// <value>Defaults to <b>true</b> (Kubo 0.40 default).</value>
        public bool ExposeRoutingAPI { get; set; } = true;

        /// <summary>
        ///   Whether codec conversion is allowed for gateway requests.
        /// </summary>
        /// <value>
        ///   Defaults to <b>false</b> per IPIP-524 (Kubo 0.40).
        ///   When false, requests for a format differing from the block's codec return 406.
        /// </value>
        public bool AllowCodecConversion { get; set; } = false;

        /// <summary>
        ///   Maximum total duration for a gateway request.
        /// </summary>
        /// <value>
        ///   Defaults to 1 hour. Returns 504 Gateway Timeout when exceeded.
        /// </value>
        public TimeSpan MaxRequestDuration { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    ///   IPFS Trustless Gateway — implements the IPFS Gateway specification
    ///   for content-addressed retrieval via /ipfs/{cid} and /ipns/{name}.
    /// </summary>
    /// <remarks>
    ///   Supports:
    ///   - Raw block retrieval (application/vnd.ipld.raw)
    ///   - CAR retrieval (application/vnd.ipld.car)
    ///   - DAG-JSON retrieval (application/vnd.ipld.dag-json)
    ///   - DAG-CBOR retrieval (application/vnd.ipld.dag-cbor)
    ///   - TAR retrieval (application/x-tar)
    ///   - UnixFS file/directory retrieval
    ///   - Entity tag caching via If-None-Match
    ///   - Immutable caching for /ipfs/ content
    ///   - IPIP-523: ?format= takes precedence over Accept header
    ///   - IPIP-524: Codec conversion disabled by default
    ///   
    ///   See https://specs.ipfs.tech/http-gateways/trustless-gateway/
    /// </remarks>
    [Route("")]
    public class GatewayController : Controller
    {
        readonly ICoreApi ipfs;
        readonly GatewayOptions options;

        /// <summary>
        ///   Creates a new instance of the gateway controller.
        /// </summary>
        public GatewayController(ICoreApi ipfs)
        {
            this.ipfs = ipfs;
            this.options = new GatewayOptions();
        }

        CancellationToken Cancel => HttpContext.RequestAborted;

        /// <summary>
        ///   Retrieve content by CID from the IPFS network.
        /// </summary>
        /// <param name="cid">The content identifier.</param>
        /// <param name="path">Optional sub-path within a UnixFS directory.</param>
        /// <returns>The content bytes with appropriate content type.</returns>
        [HttpGet("ipfs/{cid}/{**path}")]
        public async Task<IActionResult> GetIpfsContent(string cid, string path)
        {
            Cid id;
            try
            {
                id = Cid.Decode(cid);
            }
            catch
            {
                return BadRequest("Invalid CID");
            }

            // Enforce MaxRequestDuration (Kubo 0.40)
            using var durationCts = new CancellationTokenSource(options.MaxRequestDuration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Cancel, durationCts.Token);
            var cancel = linkedCts.Token;

            var fullPath = string.IsNullOrEmpty(path) ? $"/ipfs/{cid}" : $"/ipfs/{cid}/{path}";

            // Check If-None-Match for caching
            var etag = new EntityTagHeaderValue(new StringSegment($"\"{id}\""), isWeak: false);
            if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
            {
                if (ifNoneMatch.ToString().Contains(id.ToString()))
                    return StatusCode(304);
            }

            // IPIP-523: ?format= takes precedence over Accept header
            var requestedFormat = DetermineFormat();
            var accept = Request.Headers.Accept.ToString();

            try
            {
                // Raw block retrieval
                if (requestedFormat == "raw" || (requestedFormat == null && accept.Contains("application/vnd.ipld.raw")))
                {
                    var block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
                    SetImmutableHeaders(etag);
                    return File(new MemoryStream(block, false), "application/vnd.ipld.raw");
                }

                // CAR retrieval
                if (requestedFormat == "car" || (requestedFormat == null && accept.Contains("application/vnd.ipld.car")))
                {
                    var block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
                    SetImmutableHeaders(etag);

                    var carStream = new MemoryStream();
                    await WriteCarV1Async(carStream, id, block).ConfigureAwait(false);
                    carStream.Position = 0;
                    return File(carStream, "application/vnd.ipld.car");
                }

                // DAG-JSON retrieval
                if (requestedFormat == "dag-json" || (requestedFormat == null && accept.Contains("application/vnd.ipld.dag-json")))
                {
                    if (!options.AllowCodecConversion && id.ContentType != "dag-json" && id.ContentType != "dag-cbor")
                        return StatusCode(406, "Codec conversion disabled (IPIP-524). Fetch raw block and convert client-side.");

                    var block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
                    SetImmutableHeaders(etag);
                    return File(new MemoryStream(block, false), "application/vnd.ipld.dag-json");
                }

                // DAG-CBOR retrieval
                if (requestedFormat == "dag-cbor" || (requestedFormat == null && accept.Contains("application/vnd.ipld.dag-cbor")))
                {
                    if (!options.AllowCodecConversion && id.ContentType != "dag-cbor" && id.ContentType != "dag-json")
                        return StatusCode(406, "Codec conversion disabled (IPIP-524). Fetch raw block and convert client-side.");

                    var block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
                    SetImmutableHeaders(etag);
                    return File(new MemoryStream(block, false), "application/vnd.ipld.dag-cbor");
                }

                // TAR retrieval
                if (requestedFormat == "tar" || (requestedFormat == null && accept.Contains("application/x-tar")))
                {
                    var node = await ipfs.FileSystem.ReadFileAsync(fullPath, cancel).ConfigureAwait(false);
                    SetImmutableHeaders(etag);
                    return File(node, "application/x-tar");
                }

                // Default: UnixFS file retrieval
                var fileNode = await ipfs.FileSystem.ReadFileAsync(fullPath, cancel).ConfigureAwait(false);
                SetImmutableHeaders(etag);

                var contentType = GetContentType(path ?? cid);
                return File(fileNode, contentType);
            }
            catch (OperationCanceledException) when (durationCts.IsCancellationRequested)
            {
                return StatusCode(504, "Gateway Timeout");
            }
            catch (Exception)
            {
                return NotFound($"Content not found: {fullPath}");
            }
        }

        /// <summary>
        ///   IPIP-523: Determine the requested format. ?format= takes precedence over Accept.
        /// </summary>
        string DetermineFormat()
        {
            // ?format= query parameter always wins (IPIP-523)
            if (Request.Query.TryGetValue("format", out var formatValues))
            {
                var format = formatValues.ToString().Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(format))
                    return format;
            }
            return null; // Fall through to Accept header
        }

        void SetImmutableHeaders(EntityTagHeaderValue etag)
        {
            Response.Headers.Append("Cache-Control", new StringValues("public, max-age=31536000, immutable"));
            Response.Headers.Append("ETag", etag.ToString());
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
        }

        /// <summary>
        ///   Resolve an IPNS name and retrieve the content.
        /// </summary>
        /// <param name="name">The IPNS name (peer ID or DNSLink).</param>
        /// <param name="path">Optional sub-path.</param>
        [HttpGet("ipns/{name}/{**path}")]
        public async Task<IActionResult> GetIpnsContent(string name, string path)
        {
            using var durationCts = new CancellationTokenSource(options.MaxRequestDuration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Cancel, durationCts.Token);
            var cancel = linkedCts.Token;

            try
            {
                var resolved = await ipfs.Name.ResolveAsync(name, cancel: cancel).ConfigureAwait(false);
                var fullPath = string.IsNullOrEmpty(path) ? resolved : $"{resolved}/{path}";

                var node = await ipfs.FileSystem.ReadFileAsync(fullPath, cancel).ConfigureAwait(false);

                // IPNS content is mutable — shorter cache
                Response.Headers.Append("Cache-Control", new StringValues("public, max-age=60"));
                Response.Headers.Append("X-Content-Type-Options", "nosniff");

                var contentType = GetContentType(path ?? name);
                return File(node, contentType);
            }
            catch (OperationCanceledException) when (durationCts.IsCancellationRequested)
            {
                return StatusCode(504, "Gateway Timeout");
            }
            catch (Exception)
            {
                return NotFound($"IPNS name not found: {name}");
            }
        }

        /// <summary>
        ///   HEAD request for /ipfs/{cid} — used for existence checks.
        /// </summary>
        [HttpHead("ipfs/{cid}/{**path}")]
        public async Task<IActionResult> HeadIpfsContent(string cid, string path)
        {
            Cid id;
            try
            {
                id = Cid.Decode(cid);
            }
            catch
            {
                return BadRequest("Invalid CID");
            }

            try
            {
                var stat = await ipfs.Block.StatAsync(id, Cancel).ConfigureAwait(false);
                Response.Headers.Append("Cache-Control", new StringValues("public, max-age=31536000, immutable"));
                Response.Headers.Append("ETag", $"\"{id}\"");
                Response.Headers.ContentLength = stat.Size;
                return Ok();
            }
            catch
            {
                return NotFound();
            }
        }

        static string GetContentType(string path)
        {
            if (string.IsNullOrEmpty(path)) return "application/octet-stream";

            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                ".txt" => "text/plain",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".wasm" => "application/wasm",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        ///   Writes a minimal CAR v1 (Content Addressable aRchive) to the stream.
        /// </summary>
        static async Task WriteCarV1Async(Stream output, Cid rootCid, byte[] blockData)
        {
            // CAR v1 header: { roots: [cid], version: 1 }
            using var headerStream = new MemoryStream();
            // CBOR-encode the header manually (simple map)
            // dag-cbor: {"roots": [cid], "version": 1}
            var cidBytes = rootCid.ToArray();

            // Simplified header: encode as dag-cbor map with 2 entries
            // a2 (map of 2) 65 (text 5) "roots" 81 (array 1) d8 2a (tag 42) 58 xx (bytes of cid) 67 (text 7) "version" 01
            headerStream.WriteByte(0xa2); // CBOR map(2)

            // "roots" key
            headerStream.WriteByte(0x65); // text(5)
            var rootsKey = System.Text.Encoding.UTF8.GetBytes("roots");
            headerStream.Write(rootsKey, 0, rootsKey.Length);

            // roots array with 1 CID
            headerStream.WriteByte(0x81); // array(1)
            headerStream.WriteByte(0xd8); // tag
            headerStream.WriteByte(0x2a); // 42 (CID tag)
            WriteCborBytes(headerStream, new byte[] { 0x00 }.Concat(cidBytes).ToArray()); // identity multibase prefix + cid

            // "version" key
            headerStream.WriteByte(0x67); // text(7)
            var versionKey = System.Text.Encoding.UTF8.GetBytes("version");
            headerStream.Write(versionKey, 0, versionKey.Length);
            headerStream.WriteByte(0x01); // unsigned int 1

            var headerBytes = headerStream.ToArray();

            // Write header length as varint, then header
            WriteVarint(output, (ulong)headerBytes.Length);
            await output.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);

            // Write block: varint(cid_len + data_len), cid_bytes, data_bytes
            var blockPayload = cidBytes.Concat(blockData).ToArray();
            WriteVarint(output, (ulong)blockPayload.Length);
            await output.WriteAsync(blockPayload, 0, blockPayload.Length).ConfigureAwait(false);
        }

        static void WriteCborBytes(Stream s, byte[] data)
        {
            if (data.Length < 24)
            {
                s.WriteByte((byte)(0x40 | data.Length));
            }
            else if (data.Length < 256)
            {
                s.WriteByte(0x58);
                s.WriteByte((byte)data.Length);
            }
            else
            {
                s.WriteByte(0x59);
                s.WriteByte((byte)(data.Length >> 8));
                s.WriteByte((byte)(data.Length & 0xFF));
            }
            s.Write(data, 0, data.Length);
        }

        static void WriteVarint(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }
    }
}
