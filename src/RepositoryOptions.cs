namespace Ipfs.Engine;

/// <summary>
///   Configuration options for the repository.
/// </summary>
/// <seealso cref="IpfsEngineOptions"/>
public class RepositoryOptions
{
    /// <summary>
    ///   Creates a new instance of the <see cref="RepositoryOptions"/> class
    ///   with the default values.
    /// </summary>
    public RepositoryOptions()
    {
        string? path = Environment.GetEnvironmentVariable("IPFS_PATH");
        if (path is not null)
        {
            Folder = path;
        }
        else
        {
            Folder = Path.Combine(
                Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetEnvironmentVariable("HOMEPATH")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".csipfs");
        }
    }

    /// <summary>
    ///   The directory of the repository.
    /// </summary>
    /// <value>
    ///   The default value is <c>$IPFS_PATH</c> or <c>$HOME/.csipfs</c> or
    ///   <c>$HOMEPATH/.csipfs</c>.
    /// </value>
    public string Folder { get; set; }

    /// <summary>
    ///   Get the existing directory of the repository.
    /// </summary>
    /// <returns>
    ///   An existing directory.
    /// </returns>
    /// <remarks>
    ///   Creates the <see cref="Folder"/> if it does not exist.
    /// </remarks>
    public string ExistingFolder()
    {
        string path = Folder;
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }
}
