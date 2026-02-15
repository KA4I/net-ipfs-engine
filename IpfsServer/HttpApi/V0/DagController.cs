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
    ///   A link to a CID.
    /// </summary>
    public class LinkedDataDto
    {
        /// <summary>
        ///   The CID.
        /// </summary>
        [JsonProperty(PropertyName = "/")]
        public string Link;
    }

    /// <summary>
    ///   A CID as linked data.
    /// </summary>
    public class LinkedDataCidDto
    {
        /// <summary>
        ///   A link to the CID.
        /// </summary>
        public LinkedDataDto Cid;
    }

    /// <summary>
    ///   Manages the IPLD (linked data) Directed Acrylic Graph.
    /// </summary>
    public class DagController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public DagController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///  Resolve a reference.
        /// </summary>
        [HttpGet, HttpPost, Route("dag/resolve")]
        public async Task<DagResolveOutput> Resolve(string arg)
        {
            return await IpfsCore.Dag.ResolveAsync(arg, Cancel);
        }

        /// <summary>
        ///   Gets the content of some linked data.
        /// </summary>
        /// <param name="arg">
        ///   A path, such as "cid", "/ipfs/cid/" or "cid/a".
        /// </param>
        [HttpGet, HttpPost, Route("dag/get")]
        public async Task<JToken> Get(string arg)
        {
            return await IpfsCore.Dag.GetAsync(arg, cancel: Cancel);
        }

        /// <summary>
        ///   Add some linked data.
        /// </summary>
        /// <param name="file">
        ///   multipart/form-data.
        /// </param>
        /// <param name="cidBase">
        ///   The base encoding algorithm.
        /// </param>
        /// <param name="format">
        ///   The content type.
        /// </param>
        /// <param name="hash">
        ///   The hashing algorithm.
        /// </param>
        /// <param name="pin">
        ///   Pin the linked data.
        /// </param>
        [HttpPost("dag/put")]
        public async Task<LinkedDataCidDto> Put(
            IFormFile file,
            string format = "dag-cbor",
            string hash = MultiHash.DefaultAlgorithmName,
            bool pin = true,
            [ModelBinder(Name = "cid-base")] string cidBase = MultiBase.DefaultAlgorithmName)
        {
            if (file == null)
                throw new ArgumentNullException("file");

            using (var stream = file.OpenReadStream())
            using (var sr = new StreamReader(stream))
            using (var tr = new JsonTextReader(sr))
            {
                var serializer = new JsonSerializer();
                JObject json = (JObject)serializer.Deserialize(tr);
                
                var cid = await IpfsCore.Dag.PutAsync(
                    json,
                    storeCodec: format,
                    pin: false,
                    cancel: Cancel);
                return new LinkedDataCidDto { Cid = new LinkedDataDto { Link = cid } };
            }
        }

        /// <summary>
        ///   Get DAG statistics.
        /// </summary>
        [HttpGet, HttpPost, Route("dag/stat")]
        public async Task<DagStatSummary> Stat(string arg)
        {
            return await IpfsCore.Dag.StatAsync(arg, cancel: Cancel);
        }

        /// <summary>
        ///   Export a DAG as a CAR archive.
        /// </summary>
        [HttpGet, HttpPost, Route("dag/export")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> Export(string arg)
        {
            var stream = await IpfsCore.Dag.ExportAsync(arg, Cancel);
            return File(stream, "application/vnd.ipld.car");
        }

        /// <summary>
        ///   Import a CAR archive.
        /// </summary>
        [HttpPost("dag/import")]
        public async Task<CarImportOutput> Import(IFormFile file, [ModelBinder(Name = "pin-roots")] bool pinRoots = true)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            using var stream = file.OpenReadStream();
            return await IpfsCore.Dag.ImportAsync(stream, pinRoots, cancellationToken: Cancel);
        }

    }
}
