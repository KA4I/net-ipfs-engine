using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Ipfs.Engine.Migration;

/// <summary>
///   Allows migration of the repository.
/// </summary>
public class MigrationManager
{
    private readonly ILogger<MigrationManager> _logger = IpfsEngine.LoggerFactory.CreateLogger<MigrationManager>();

    private readonly IpfsEngine ipfs;

    /// <summary>
    ///   Creates a new instance of the <see cref="MigrationManager"/> class
    ///   for the specified <see cref="IpfsEngine"/>.
    /// </summary>
    public MigrationManager(IpfsEngine ipfs)
    {
        this.ipfs = ipfs;

        Migrations = typeof(MigrationManager).Assembly
            .GetTypes()
            .Where(x => typeof(IMigration).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
            .Select(x => (IMigration)Activator.CreateInstance(x)!)
            .OrderBy(x => x.Version)
            .ToList();
    }

    /// <summary>
    ///   The list of migrations that can be performed.
    /// </summary>
    public List<IMigration> Migrations { get; private set; }

    /// <summary>
    ///   Gets the latest supported version number of a repository.
    /// </summary>
    public int LatestVersion => Migrations.Last().Version;

    /// <summary>
    ///   Gets the current version number of the repository.
    /// </summary>
    public int CurrentVersion
    {
        get
        {
            string path = VersionPath();
            if (File.Exists(path))
            {
                using StreamReader reader = new(path);
                string? s = reader.ReadLine();
                return int.Parse(s!, CultureInfo.InvariantCulture);
            }

            return 0;
        }
        private set
        {
            File.WriteAllText(VersionPath(), value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    ///   Upgrade/downgrade to the specified version.
    /// </summary>
    /// <param name="version">
    ///   The required version of the repository.
    /// </param>
    /// <param name="cancel">
    ///   Is used to stop the task.
    /// </param>
    public async Task MigrateToVersionAsync(int version, CancellationToken cancel = default)
    {
        if (version != 0 && !Migrations.Any(m => m.Version == version))
        {
            throw new ArgumentOutOfRangeException(nameof(version), $"Repository version '{version}' is unknown.");
        }

        int currentVersion = CurrentVersion;
        int increment = CurrentVersion < version ? 1 : -1;
        while (currentVersion != version)
        {
            int nextVersion = currentVersion + increment;
            _logger.LogInformation("Migrating to version {Version}", nextVersion);

            if (increment > 0)
            {
                IMigration? migration = Migrations.FirstOrDefault(m => m.Version == nextVersion);
                if (migration is not null && migration.CanUpgrade)
                {
                    await migration.UpgradeAsync(ipfs, cancel).ConfigureAwait(false);
                }
            }
            else if (increment < 0)
            {
                IMigration? migration = Migrations.FirstOrDefault(m => m.Version == currentVersion);
                if (migration is not null && migration.CanDowngrade)
                {
                    await migration.DowngradeAsync(ipfs, cancel).ConfigureAwait(false);
                }
            }

            CurrentVersion = nextVersion;
            currentVersion = nextVersion;
        }
    }

    private string VersionPath()
    {
        return Path.Combine(ipfs.Options.Repository.ExistingFolder(), "version");
    }
}
