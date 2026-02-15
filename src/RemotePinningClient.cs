#nullable disable
using Common.Logging;
using Ipfs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    /// <summary>
    ///   Client for the IPFS Pinning Service API specification.
    /// </summary>
    /// <remarks>
    ///   Implements https://ipfs.github.io/pinning-services-api-spec/
    ///   <para>
    ///   This allows pinning CIDs to remote pinning services like Pinata,
    ///   web3.storage, Filebase, etc.
    ///   </para>
    /// </remarks>
    public class RemotePinningClient
    {
        static readonly ILog log = LogManager.GetLogger(typeof(RemotePinningClient));
        readonly HttpClient httpClient;
        readonly Uri baseUrl;

        /// <summary>
        ///   Creates a new remote pinning service client.
        /// </summary>
        /// <param name="endpoint">The pinning service API endpoint URL.</param>
        /// <param name="accessToken">The authentication bearer token.</param>
        public RemotePinningClient(string endpoint, string accessToken)
        {
            baseUrl = new Uri(endpoint.TrimEnd('/'));
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        ///   List pin objects matching the given criteria.
        /// </summary>
        public async Task<PinResults> ListPinsAsync(
            IEnumerable<Cid> cids = null,
            string name = null,
            RemotePinStatus? status = null,
            int? limit = null,
            CancellationToken cancel = default)
        {
            var query = new List<string>();
            if (cids != null)
                query.Add($"cid={string.Join(",", cids)}");
            if (!string.IsNullOrEmpty(name))
                query.Add($"name={Uri.EscapeDataString(name)}");
            if (status.HasValue)
                query.Add($"status={status.Value.ToString().ToLowerInvariant()}");
            if (limit.HasValue)
                query.Add($"limit={limit.Value}");

            var queryString = query.Count > 0 ? "?" + string.Join("&", query) : "";
            var url = new Uri(baseUrl, $"/pins{queryString}");

            using var response = await httpClient.GetAsync(url, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PinResults>(json, JsonOpts);
        }

        /// <summary>
        ///   Get a specific pin object by request ID.
        /// </summary>
        public async Task<PinStatusResult> GetPinAsync(string requestId, CancellationToken cancel = default)
        {
            var url = new Uri(baseUrl, $"/pins/{Uri.EscapeDataString(requestId)}");
            using var response = await httpClient.GetAsync(url, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PinStatusResult>(json, JsonOpts);
        }

        /// <summary>
        ///   Add a new pin object (request pinning of a CID).
        /// </summary>
        public async Task<PinStatusResult> AddPinAsync(
            Cid cid,
            string name = null,
            IEnumerable<string> origins = null,
            IDictionary<string, string> meta = null,
            CancellationToken cancel = default)
        {
            var url = new Uri(baseUrl, "/pins");
            var pin = new PinRequest
            {
                Cid = cid.ToString(),
                Name = name,
                Origins = origins != null ? new List<string>(origins) : null,
                Meta = meta != null ? new Dictionary<string, string>(meta) : null
            };

            var content = new StringContent(
                JsonSerializer.Serialize(pin, JsonOpts),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.PostAsync(url, content, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PinStatusResult>(json, JsonOpts);
        }

        /// <summary>
        ///   Replace an existing pin with a new one.
        /// </summary>
        public async Task<PinStatusResult> ReplacePinAsync(
            string requestId,
            Cid cid,
            string name = null,
            IEnumerable<string> origins = null,
            IDictionary<string, string> meta = null,
            CancellationToken cancel = default)
        {
            var url = new Uri(baseUrl, $"/pins/{Uri.EscapeDataString(requestId)}");
            var pin = new PinRequest
            {
                Cid = cid.ToString(),
                Name = name,
                Origins = origins != null ? new List<string>(origins) : null,
                Meta = meta != null ? new Dictionary<string, string>(meta) : null
            };

            var content = new StringContent(
                JsonSerializer.Serialize(pin, JsonOpts),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            // Some services use POST for replace, others use PUT
            request.Method = HttpMethod.Post;

            using var response = await httpClient.SendAsync(request, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PinStatusResult>(json, JsonOpts);
        }

        /// <summary>
        ///   Remove a pin object (unpin a CID from the remote service).
        /// </summary>
        public async Task RemovePinAsync(string requestId, CancellationToken cancel = default)
        {
            var url = new Uri(baseUrl, $"/pins/{Uri.EscapeDataString(requestId)}");
            using var response = await httpClient.DeleteAsync(url, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        // --- API DTOs ---

        /// <summary>
        ///   Request body for creating or replacing a pin.
        /// </summary>
        public class PinRequest
        {
            public string Cid { get; set; }
            public string Name { get; set; }
            public List<string> Origins { get; set; }
            public Dictionary<string, string> Meta { get; set; }
        }

        /// <summary>
        ///   Status of a pin request.
        /// </summary>
        public class PinStatusResult
        {
            public string RequestId { get; set; }
            public string Status { get; set; }
            public DateTime Created { get; set; }
            public PinInfo Pin { get; set; }
            public List<string> Delegates { get; set; }
            public Dictionary<string, string> Info { get; set; }
        }

        /// <summary>
        ///   Information about a pinned CID.
        /// </summary>
        public class PinInfo
        {
            public string Cid { get; set; }
            public string Name { get; set; }
            public List<string> Origins { get; set; }
            public Dictionary<string, string> Meta { get; set; }
        }

        /// <summary>
        ///   Paginated list of pin results.
        /// </summary>
        public class PinResults
        {
            public int Count { get; set; }
            public List<PinStatusResult> Results { get; set; }
        }
    }

    /// <summary>
    ///   Pin status values per the Pinning Service API spec.
    /// </summary>
    public enum RemotePinStatus
    {
        Queued,
        Pinning,
        Pinned,
        Failed
    }
}
