using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

/// <summary>
/// Mutable File System (MFS) API - matches Kubo's 'ipfs files' commands.
/// </summary>
/// <remarks>
/// MFS acts as a single, dynamic filesystem mount. It has a root CID that is
/// transparently updated when a change happens. All files and folders within MFS
/// are protected from garbage collection.
/// </remarks>
public interface IFilesApi
{
    /// <summary>
    /// Copy files into MFS or within MFS (lazy copy).
    /// </summary>
    /// <param name="source">Source IPFS path (/ipfs/CID) or MFS path.</param>
    /// <param name="destination">Destination path within MFS.</param>
    /// <param name="parents">Create parent directories as needed.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task CpAsync(string source, string destination, bool parents = false, CancellationToken cancel = default);

    /// <summary>
    /// List directory contents in MFS.
    /// </summary>
    /// <param name="path">MFS directory path (default "/").</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>A sequence of MFS entries.</returns>
    Task<IEnumerable<MfsEntry>> LsAsync(string path = "/", CancellationToken cancel = default);

    /// <summary>
    /// Create a directory in MFS.
    /// </summary>
    /// <param name="path">Path to the directory to create.</param>
    /// <param name="parents">Create parent directories as needed.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task MkdirAsync(string path, bool parents = false, CancellationToken cancel = default);

    /// <summary>
    /// Move/rename a file or directory within MFS.
    /// </summary>
    /// <param name="source">Source path.</param>
    /// <param name="destination">Destination path.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task MvAsync(string source, string destination, CancellationToken cancel = default);

    /// <summary>
    /// Read a file from MFS.
    /// </summary>
    /// <param name="path">MFS file path.</param>
    /// <param name="offset">Byte offset to start reading.</param>
    /// <param name="count">Maximum number of bytes to read (0 for entire file).</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>A stream of file content.</returns>
    Task<Stream> ReadAsync(string path, long offset = 0, long count = 0, CancellationToken cancel = default);

    /// <summary>
    /// Remove a file or directory from MFS.
    /// </summary>
    /// <param name="path">MFS path to remove.</param>
    /// <param name="recursive">Recursively remove directories.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task RmAsync(string path, bool recursive = false, CancellationToken cancel = default);

    /// <summary>
    /// Get the status of a file or directory in MFS.
    /// </summary>
    /// <param name="path">MFS path (default "/").</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The status of the path.</returns>
    Task<MfsStat> StatAsync(string path = "/", CancellationToken cancel = default);

    /// <summary>
    /// Write data to a file in MFS.
    /// </summary>
    /// <param name="path">MFS file path.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="options">Write options.</param>
    /// <param name="cancel">Cancellation token.</param>
    Task WriteAsync(string path, Stream data, MfsWriteOptions? options = null, CancellationToken cancel = default);

    /// <summary>
    /// Flush MFS path to ensure all changes are persisted.
    /// </summary>
    /// <param name="path">MFS path to flush (default "/").</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>The CID of the flushed path.</returns>
    Task<Cid> FlushAsync(string path = "/", CancellationToken cancel = default);
}

/// <summary>
/// An entry in an MFS directory listing.
/// </summary>
public class MfsEntry
{
    /// <summary>
    /// The name of the entry.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The type of entry (0 = file, 1 = directory).
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// The size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The content identifier.
    /// </summary>
    public Cid? Hash { get; set; }
}

/// <summary>
/// Status information for an MFS path.
/// </summary>
public class MfsStat
{
    /// <summary>
    /// The CID of the node.
    /// </summary>
    public required Cid Hash { get; set; }

    /// <summary>
    /// The cumulative size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The cumulative size including all child blocks.
    /// </summary>
    public long CumulativeSize { get; set; }

    /// <summary>
    /// The number of child blocks.
    /// </summary>
    public int Blocks { get; set; }

    /// <summary>
    /// The type (file or directory).
    /// </summary>
    public required string Type { get; set; }
}

/// <summary>
/// Options for MFS write operations.
/// </summary>
public class MfsWriteOptions
{
    /// <summary>
    /// Create the file if it doesn't exist.
    /// </summary>
    public bool Create { get; set; }

    /// <summary>
    /// Create parent directories as needed.
    /// </summary>
    public bool Parents { get; set; }

    /// <summary>
    /// Truncate the file before writing.
    /// </summary>
    public bool Truncate { get; set; }

    /// <summary>
    /// Byte offset to start writing at.
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// Number of bytes to write (0 for entire stream).
    /// </summary>
    public long Count { get; set; }
}
