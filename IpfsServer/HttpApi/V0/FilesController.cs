using Ipfs.CoreApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ipfs.Server.HttpApi.V0
{
    /// <summary>
    ///   MFS file stat result DTO.
    /// </summary>
    public class MfsStatDto
    {
        /// <summary>CID hash.</summary>
        public string Hash;
        /// <summary>Size of node data.</summary>
        public long Size;
        /// <summary>Size including links.</summary>
        public long CumulativeSize;
        /// <summary>Number of children blocks.</summary>
        public int Blocks;
        /// <summary>"file" or "directory".</summary>
        public string Type;
    }

    /// <summary>
    ///   MFS file listing DTO.
    /// </summary>
    public class MfsLsDto
    {
        /// <summary>The listing entries.</summary>
        public IEnumerable<MfsEntryDto> Entries;
    }

    /// <summary>
    ///   A single MFS listing entry.
    /// </summary>
    public class MfsEntryDto
    {
        /// <summary>Entry name.</summary>
        public string Name;
        /// <summary>Entry type (0=file, 1=directory).</summary>
        public int Type;
        /// <summary>Size in bytes.</summary>
        public long Size;
        /// <summary>CID hash.</summary>
        public string Hash;
    }

    /// <summary>
    ///   MFS flush result DTO.
    /// </summary>
    public class MfsFlushDto
    {
        /// <summary>CID of the flushed path.</summary>
        public string Cid;
    }

    /// <summary>
    ///   Manages the Mutable File System (MFS).
    /// </summary>
    public class FilesController : IpfsController
    {
        /// <summary>
        ///   Creates a new controller.
        /// </summary>
        public FilesController(ICoreApi ipfs) : base(ipfs) { }

        /// <summary>
        ///   Copy files into MFS.
        /// </summary>
        [HttpGet, HttpPost, Route("files/cp")]
        public async Task Copy(string[] arg, bool parents = false)
        {
            if (arg == null || arg.Length < 2)
                throw new ArgumentException("Two arguments required: source and destination.");
            await IpfsCore.Mfs.CopyAsync(arg[0], arg[1], parents, Cancel);
        }

        /// <summary>
        ///   Flush a given path's data to disk.
        /// </summary>
        [HttpGet, HttpPost, Route("files/flush")]
        public async Task<MfsFlushDto> Flush(string arg = "/")
        {
            var cid = await IpfsCore.Mfs.FlushAsync(arg, Cancel);
            return new MfsFlushDto { Cid = cid.Encode() };
        }

        /// <summary>
        ///   List directory contents.
        /// </summary>
        [HttpGet, HttpPost, Route("files/ls")]
        public async Task<MfsLsDto> List(string arg = "/", bool U = false)
        {
            var entries = await IpfsCore.Mfs.ListAsync(arg, U, Cancel);
            return new MfsLsDto
            {
                Entries = entries.Select(e => new MfsEntryDto
                {
                    Name = e.Name,
                    Type = e.IsDirectory ? 1 : 0,
                    Size = (long)e.Size,
                    Hash = e.Id?.Encode() ?? ""
                })
            };
        }

        /// <summary>
        ///   Make directories.
        /// </summary>
        [HttpGet, HttpPost, Route("files/mkdir")]
        public async Task MakeDirectory(
            string arg,
            bool parents = false,
            [ModelBinder(Name = "cid-version")] int? cidVersion = null,
            string hash = null!)
        {
            await IpfsCore.Mfs.MakeDirectoryAsync(arg, parents, cidVersion, hash, Cancel);
        }

        /// <summary>
        ///   Move files.
        /// </summary>
        [HttpGet, HttpPost, Route("files/mv")]
        public async Task Move(string[] arg)
        {
            if (arg == null || arg.Length < 2)
                throw new ArgumentException("Two arguments required: source and destination.");
            await IpfsCore.Mfs.MoveAsync(arg[0], arg[1], Cancel);
        }

        /// <summary>
        ///   Read a file from MFS.
        /// </summary>
        [HttpGet, HttpPost, Route("files/read")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> Read(
            string arg,
            long offset = 0,
            long count = 0)
        {
            var stream = await IpfsCore.Mfs.ReadFileStreamAsync(arg, offset > 0 ? offset : null, count > 0 ? count : null, Cancel);
            return File(stream, "application/octet-stream");
        }

        /// <summary>
        ///   Remove a file or directory.
        /// </summary>
        [HttpGet, HttpPost, Route("files/rm")]
        public async Task Remove(
            string arg,
            bool recursive = false,
            bool force = false)
        {
            await IpfsCore.Mfs.RemoveAsync(arg, recursive, force, Cancel);
        }

        /// <summary>
        ///   Display file status.
        /// </summary>
        [HttpGet, HttpPost, Route("files/stat")]
        public async Task<MfsStatDto> Stat(
            string arg,
            [ModelBinder(Name = "with-local")] bool withLocal = false)
        {
            if (withLocal)
            {
                var stat = await IpfsCore.Mfs.StatAsync(arg, withLocal, Cancel);
                return new MfsStatDto
                {
                    Hash = stat.Hash?.Encode() ?? "",
                    Size = stat.Size,
                    CumulativeSize = stat.CumulativeSize,
                    Blocks = stat.Blocks,
                    Type = stat.IsDirectory ? "directory" : "file"
                };
            }
            else
            {
                var stat = await IpfsCore.Mfs.StatAsync(arg, Cancel);
                return new MfsStatDto
                {
                    Hash = stat.Hash?.Encode() ?? "",
                    Size = stat.Size,
                    CumulativeSize = stat.CumulativeSize,
                    Blocks = stat.Blocks,
                    Type = stat.IsDirectory ? "directory" : "file"
                };
            }
        }

        /// <summary>
        ///   Write to a mutable file in MFS.
        /// </summary>
        [HttpPost("files/write")]
        public async Task Write(
            string arg,
            IFormFile file,
            bool create = false,
            bool parents = false,
            long offset = -1,
            long count = -1,
            bool truncate = false,
            [ModelBinder(Name = "raw-leaves")] bool rawLeaves = false,
            [ModelBinder(Name = "cid-version")] int? cidVersion = null,
            string hash = null!)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            var options = new MfsWriteOptions
            {
                Create = create,
                Parents = parents,
                Offset = offset >= 0 ? offset : null,
                Count = count >= 0 ? count : null,
                Truncate = truncate,
                RawLeaves = rawLeaves,
                CidVersion = cidVersion,
                Hash = hash
            };

            using var stream = file.OpenReadStream();
            await IpfsCore.Mfs.WriteAsync(arg, stream, options, Cancel);
        }
    }
}
