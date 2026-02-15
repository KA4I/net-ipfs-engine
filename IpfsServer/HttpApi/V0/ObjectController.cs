using Ipfs.CoreApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text;

namespace Ipfs.Server.HttpApi.V0
{
    /// <summary>
    ///   Stats for an object.
    /// </summary>
    public class ObjectStatDto
    {
        /// <summary>
        ///   The CID of the object.
        /// </summary>
        public string Hash;

        /// <summary>
        ///   Number of links.
        /// </summary>
        public int NumLinks { get; set; }

        /// <summary>
        ///   Size of the links segment.
        /// </summary>
        public long LinksSize { get; set; }

        /// <summary>
        ///   Size of the raw, encoded data.
        /// </summary>
        public long BlockSize { get; set; }

        /// <summary>
        ///   Siz of the data segment.
        /// </summary>
        public long DataSize { get; set; }

        /// <summary>
        ///   Size of object and its references
        /// </summary>
        public long CumulativeSize { get; set; }
    }

    /// <summary>
    ///  A link to a file.
    /// </summary>
    public class ObjectLinkDto
    {
        /// <summary>
        ///   The object name.
        /// </summary>
        public string Name;

        /// <summary>
        ///   The CID of the object.
        /// </summary>
        public string Hash;

        /// <summary>
        ///   The object size.
        /// </summary>
        public long Size;
    }

    /// <summary>
    ///   Link details on an object.
    /// </summary>
    public class ObjectLinkDetailDto
    {
        /// <summary>
        ///   The CID of the object.
        /// </summary>
        public string Hash;

        /// <summary>
        ///   Links to other objects.
        /// </summary>
        public IEnumerable<ObjectLinkDto> Links;
    }

    /// <summary>
    ///   Data and link details on an object.
    /// </summary>
    public class ObjectDataDetailDto : ObjectLinkDetailDto
    {
        /// <summary>
        ///   The object data encoded as UTF-8.
        /// </summary>
        public string Data;
    }

    /// <summary>
    ///   Manages the IPFS Merkle Directed Acrylic Graph.
    /// </summary>
    /// <remarks>
    ///   <note>
    ///   This is being obsoleted by <see cref="IDagApi"/>.
    ///   </note>
    /// </remarks>
    /// <remarks>
    ///   The Object API has been removed from the core API.
    ///   All endpoints now return HTTP 501 Not Implemented.
    /// </remarks>
    public class ObjectController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public ObjectController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///   Create an object from a template.
        /// </summary>
        [HttpGet, HttpPost, Route("object/new")]
        public IActionResult Create(string arg)
            => StatusCode(501, "Object API has been removed.");

        /// <summary>
        ///   Store a MerkleDAG node.
        /// </summary>
        [HttpPost("object/put")]
        public IActionResult Put(IFormFile file, string inputenc = "json", string datafieldenc = "text", bool pin = false)
            => StatusCode(501, "Object API has been removed.");

        /// <summary>
        ///   Get the data and links of an object.
        /// </summary>
        [HttpGet, HttpPost, Route("object/get")]
        public IActionResult Get(string arg, [ModelBinder(Name = "data-encoding")] string dataEncoding)
            => StatusCode(501, "Object API has been removed.");

        /// <summary>
        ///   Get the links of an object.
        /// </summary>
        [HttpGet, HttpPost, Route("object/links")]
        public IActionResult Links(string arg)
            => StatusCode(501, "Object API has been removed.");

        /// <summary>
        ///   Get the object's data.
        /// </summary>
        [HttpGet, HttpPost, Route("object/data")]
        [Produces("text/plain")]
        public IActionResult Data(string arg)
            => StatusCode(501, "Object API has been removed.");

        /// <summary>
        ///   Get the stats of an object.
        /// </summary>
        [HttpGet, HttpPost, Route("object/stat")]
        public IActionResult Stat(string arg)
            => StatusCode(501, "Object API has been removed.");
    }
}
