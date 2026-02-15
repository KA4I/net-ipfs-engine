using Ipfs.CoreApi;
using Microsoft.AspNetCore.Mvc;

namespace Ipfs.Server.HttpApi.V0
{
    /// <summary>
    ///   Filestore DTO.
    /// </summary>
    public class FilestoreDto
    {
        /// <summary>The objects.</summary>
        public IEnumerable<object> Objects = Array.Empty<object>();
    }

    /// <summary>
    ///   Manages the filestore.
    /// </summary>
    public class FilestoreController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public FilestoreController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///   List objects in the filestore.
        /// </summary>
        [HttpGet, HttpPost, Route("filestore/ls")]
        public async Task<FilestoreDto> List(string arg = null!, [ModelBinder(Name = "file-order")] bool fileOrder = false)
        {
            var items = new List<object>();
            await foreach (var item in IpfsCore.Filestore.ListAsync(arg, fileOrder, Cancel))
            {
                items.Add(item);
            }
            return new FilestoreDto { Objects = items };
        }

        /// <summary>
        ///   Verify objects in the filestore.
        /// </summary>
        [HttpGet, HttpPost, Route("filestore/verify")]
        public async Task<FilestoreDto> Verify(string arg = null!, [ModelBinder(Name = "file-order")] bool fileOrder = false)
        {
            var items = new List<object>();
            await foreach (var item in IpfsCore.Filestore.VerifyObjectsAsync(arg, fileOrder, Cancel))
            {
                items.Add(item);
            }
            return new FilestoreDto { Objects = items };
        }

        /// <summary>
        ///   List duplicate blocks.
        /// </summary>
        [HttpGet, HttpPost, Route("filestore/dups")]
        public async Task<FilestoreDto> Dups()
        {
            var items = new List<object>();
            await foreach (var item in IpfsCore.Filestore.DupsAsync(Cancel))
            {
                items.Add(item);
            }
            return new FilestoreDto { Objects = items };
        }
    }
}
