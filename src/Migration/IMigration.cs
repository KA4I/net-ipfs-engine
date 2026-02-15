namespace Ipfs.Engine.Migration;

/// <summary>
///   Provides a migration path to the repository.
/// </summary>
public interface IMigration
{
    /// <summary>
    ///   The repository version that is created.
    /// </summary>
    int Version { get; }

    /// <summary>
    ///   Indicates that an upgrade can be performed.
    /// </summary>
    bool CanUpgrade { get; }

    /// <summary>
    ///   Indicates that a downgrade can be performed.
    /// </summary>
    bool CanDowngrade { get; }

    /// <summary>
    ///   Upgrade the repository.
    /// </summary>
    Task UpgradeAsync(IpfsEngine ipfs, CancellationToken cancel = default);

    /// <summary>
    ///   Downgrade the repository.
    /// </summary>
    Task DowngradeAsync(IpfsEngine ipfs, CancellationToken cancel = default);
}
