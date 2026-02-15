using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ipfs.CoreApi;
using Ipfs.Engine.CoreApi;
using Ipfs.Engine.Cryptography;
using Ipfs.Engine.Migration;
using Makaretu.Dns;
using Nito.AsyncEx;
using PeerTalk;
using PeerTalk.Cryptography;
using PeerTalk.Discovery;
using PeerTalk.SecureCommunication;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security;

namespace Ipfs.Engine;

/// <summary>
/// Implements the <see cref="ICoreApi">Core API</see> which makes it possible to
/// create a decentralised and distributed application without relying on an "IPFS daemon".
/// </summary>
/// <remarks>
/// The engine should be used as a shared object in your program. It is thread safe (re-entrant)
/// and conserves resources when only one instance is used.
/// </remarks>
public partial class IpfsEngine : ICoreApi, IService, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets or sets the logger factory used throughout the IPFS Engine.
    /// </summary>
    /// <remarks>
    /// Set this before creating <see cref="IpfsEngine"/> instances.
    /// Defaults to <see cref="NullLoggerFactory.Instance"/>.
    /// </remarks>
    public static ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    private readonly ILogger<IpfsEngine> _logger = LoggerFactory.CreateLogger<IpfsEngine>();

    private KeyChain? keyChain;
    private readonly SecureString passphrase;
    private ConcurrentBag<Func<Task>> stopTasks = [];

    /// <summary>
    /// Creates a new instance of the <see cref="IpfsEngine"/> class with the IPFS_PASS
    /// environment variable.
    /// </summary>
    /// <remarks>The passphrase must be in the IPFS_PASS environment variable.</remarks>
    public IpfsEngine()
    {
        string s = Environment.GetEnvironmentVariable("IPFS_PASS")
            ?? throw new InvalidOperationException("The IPFS_PASS environment variable is missing.");
        passphrase = new SecureString();
        foreach (char c in s)
        {
            passphrase.AppendChar(c);
        }

        Init();
    }

    /// <summary>
    /// Creates a new instance of the <see cref="IpfsEngine"/> class with the specified passphrase.
    /// </summary>
    /// <param name="passphrase">The password used to access the keychain.</param>
    /// <remarks>
    /// A <b>SecureString</b> copy of the passphrase is made so that the array can be zeroed out
    /// after the call.
    /// </remarks>
    public IpfsEngine(char[] passphrase)
    {
        this.passphrase = new SecureString();
        foreach (char c in passphrase)
        {
            this.passphrase.AppendChar(c);
        }
        Init();
    }

    /// <summary>
    /// Creates a new instance of the <see cref="IpfsEngine"/> class with the specified passphrase.
    /// </summary>
    /// <param name="passphrase">The password used to access the keychain.</param>
    /// <remarks>A copy of the <paramref name="passphrase"/> is made.</remarks>
    public IpfsEngine(SecureString passphrase)
    {
        this.passphrase = passphrase.Copy();
        Init();
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(Bitswap), nameof(Block), nameof(BlockRepository), nameof(Bootstrap),
        nameof(Config), nameof(Dag), nameof(Dht), nameof(Dns), nameof(FileSystem),
        nameof(Files), nameof(Generic), nameof(Key), nameof(Name), nameof(ObjectHelper),
        nameof(Pin), nameof(PubSub), nameof(Routing), nameof(Stats), nameof(Swarm),
        nameof(Mfs), nameof(Filestore),
        nameof(MigrationManager), nameof(LocalPeer), nameof(SwarmService),
        nameof(BitswapService), nameof(DhtService), nameof(PingService), nameof(PubSubService))]
    private void Init()
    {
        // Init the core api interface.
        Bitswap = new BitswapApi(this);
        Block = new BlockApi(this);
        BlockRepository = new BlockRepositoryApi(this);
        Bootstrap = new BootstrapApi(this);
        Config = new ConfigApi(this);
        Dag = new DagApi(this);
        Dht = new DhtApi(this);
        Dns = new DnsApi(this);
        FileSystem = new FileSystemApi(this);
        Files = new FilesApi(this);
        Generic = new GenericApi(this);
        Key = new KeyApi(this);
        Name = new NameApi(this);
        ObjectHelper = new ObjectHelper(this);
        Pin = new PinApi(this);
        PubSub = new PubSubApi(this);
        Routing = new RoutingApi(this);
        Stats = new StatsApi(this);
        Swarm = new SwarmApi(this);
        Mfs = new MfsApi(this);
        Filestore = new FilestoreApi(this);

        MigrationManager = new MigrationManager(this);

        // Async properties
        LocalPeer = new AsyncLazy<Peer>(async () =>
        {
            _logger.LogDebug("Building local peer");
            KeyChain kc = await KeyChainAsync().ConfigureAwait(false);
            _logger.LogDebug("Getting key info about self");
            IKey self = await kc.FindKeyByNameAsync("self").ConfigureAwait(false);
            Peer localPeer = new()
            {
                Id = self.Id.Hash,
                PublicKey = await kc.GetPublicKeyAsync("self").ConfigureAwait(false),
                ProtocolVersion = "ipfs/0.1.0"
            };
            Version? version = typeof(IpfsEngine).GetTypeInfo().Assembly.GetName().Version;
            localPeer.AgentVersion = $"net-ipfs/{version?.Major}.{version?.Minor}.{version?.Revision}";
            _logger.LogDebug("Built local peer");
            return localPeer;
        });
        SwarmService = new AsyncLazy<Swarm>(async () =>
        {
            _logger.LogDebug("Building swarm service");
            if (Options.Swarm.PrivateNetworkKey == null)
            {
                string path = Path.Combine(Options.Repository.Folder, "swarm.key");
                if (File.Exists(path))
                {
                    using StreamReader x = File.OpenText(path);
                    Options.Swarm.PrivateNetworkKey = new PreSharedKey();
                    Options.Swarm.PrivateNetworkKey.Import(x);
                }
            }
            Peer peer = await LocalPeer.ConfigureAwait(false);
            KeyChain kc = await KeyChainAsync().ConfigureAwait(false);
            Org.BouncyCastle.Crypto.AsymmetricKeyParameter self = await kc.GetPrivateKeyAsync("self").ConfigureAwait(false);
            Swarm swarm = new()
            {
                LocalPeer = peer,
                LocalPeerKey = PeerTalk.Cryptography.Key.CreatePrivateKey(self),
                NetworkProtector = Options.Swarm.PrivateNetworkKey == null
                    ? null
                    : new Psk1Protector { Key = Options.Swarm.PrivateNetworkKey }
            };
            if (Options.Swarm.PrivateNetworkKey != null)
            {
                _logger.LogDebug("Private network {Fingerprint}", Options.Swarm.PrivateNetworkKey.Fingerprint().ToHexString());
            }

            _logger.LogDebug("Built swarm service");
            return swarm;
        });
        BitswapService = new AsyncLazy<BlockExchange.Bitswap>(async () =>
        {
            _logger.LogDebug("Building bitswap service");
            BlockExchange.Bitswap bitswap = new()
            {
                Swarm = await SwarmService.ConfigureAwait(false),
                BlockService = Block
            };
            _logger.LogDebug("Built bitswap service");
            return bitswap;
        });
        DhtService = new AsyncLazy<PeerTalk.Routing.Dht1>(async () =>
        {
            _logger.LogDebug("Building DHT service");
            PeerTalk.Routing.Dht1 dht = new()
            {
                Swarm = await SwarmService.ConfigureAwait(false)
            };
            dht.Swarm.Router = dht;
            _logger.LogDebug("Built DHT service");
            return dht;
        });
        PingService = new AsyncLazy<PeerTalk.Protocols.Ping1>(async () =>
        {
            _logger.LogDebug("Building Ping service");
            PeerTalk.Protocols.Ping1 ping = new()
            {
                Swarm = await SwarmService.ConfigureAwait(false)
            };
            _logger.LogDebug("Built Ping service");
            return ping;
        });
        PubSubService = new AsyncLazy<PeerTalk.PubSub.NotificationService>(async () =>
        {
            _logger.LogDebug("Building PubSub service");
            PeerTalk.PubSub.NotificationService pubsub = new()
            {
                LocalPeer = await LocalPeer.ConfigureAwait(false)
            };
            pubsub.Routers.Add(new PeerTalk.PubSub.FloodRouter
            {
                Swarm = await SwarmService.ConfigureAwait(false)
            });
            _logger.LogDebug("Built PubSub service");
            return pubsub;
        });
    }

    /// <summary>
    /// The configuration options.
    /// </summary>
    public IpfsEngineOptions Options { get; set; } = new IpfsEngineOptions();

    /// <summary>
    /// Manages the version of the repository.
    /// </summary>
    public MigrationManager MigrationManager { get; set; }

    /// <inheritdoc/>
    public IBitswapApi Bitswap { get; set; }

    /// <inheritdoc/>
    public IBlockApi Block { get; set; }

    /// <inheritdoc/>
    public IBlockRepositoryApi BlockRepository { get; set; }

    /// <inheritdoc/>
    public IBootstrapApi Bootstrap { get; set; }

    /// <inheritdoc/>
    public IConfigApi Config { get; set; }

    /// <inheritdoc/>
    public IDagApi Dag { get; set; }

    /// <inheritdoc/>
    public IDhtApi Dht { get; set; }

    /// <inheritdoc/>
    public IDnsApi Dns { get; set; }

    /// <inheritdoc/>
    public IFileSystemApi FileSystem { get; set; }

    /// <summary>
    /// Provides access to the Mutable File System (MFS).
    /// </summary>
    /// <remarks>
    /// MFS is a mutable file system backed by IPFS that allows files and directories
    /// to be manipulated like a regular file system. Changes are persisted when flushed.
    /// </remarks>
    public IFilesApi Files { get; set; }

    /// <inheritdoc/>
    public IMfsApi Mfs { get; set; }

    /// <inheritdoc/>
    public IFilestoreApi Filestore { get; set; }

    /// <inheritdoc/>
    public IGenericApi Generic { get; set; }

    /// <inheritdoc/>
    public IKeyApi Key { get; set; }

    /// <inheritdoc/>
    public INameApi Name { get; set; }

    /// <summary>
    /// Internal helper for DAG node operations (formerly IObjectApi).
    /// </summary>
    internal ObjectHelper ObjectHelper { get; set; }

    /// <inheritdoc/>
    public IPinApi Pin { get; set; }

    /// <inheritdoc/>
    public IPubSubApi PubSub { get; set; }

    /// <inheritdoc/>
    public ISwarmApi Swarm { get; set; }

    /// <inheritdoc/>
    public IStatsApi Stats { get; set; }

    /// <summary>
    /// The Routing API provides access to the routing layer (Kubo-compatible).
    /// </summary>
    /// <remarks>
    /// Replaces the older Dht-only approach with a composable routing interface
    /// supporting FindPeer, FindProviders, Provide, Get, and Put operations.
    /// </remarks>
    public IRoutingApi Routing { get; set; }

    /// <summary>
    /// Provides access to the <see cref="KeyChain"/>.
    /// </summary>
    /// <param name="cancel">
    /// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException"/> is raised.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task's result is the <see cref="KeyChain"/>.
    /// </returns>
    public async Task<KeyChain> KeyChainAsync(CancellationToken cancel = default)
    {
        if (keyChain is null)
        {
            lock (this)
            {
                keyChain ??= new KeyChain(this)
                {
                    Options = Options.KeyChain
                };
            }

            await keyChain.SetPassphraseAsync(passphrase, cancel).ConfigureAwait(false);

            // Maybe create "self" key, this is the local peer's id.
            IKey? self = await keyChain.FindKeyByNameAsync("self", cancel).ConfigureAwait(false);
            _ = self ?? await keyChain.CreateAsync("self", null, 0, cancel).ConfigureAwait(false);
        }

        return keyChain;
    }

    /// <summary>
    /// Provides access to the local peer.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task's result is a <see cref="Peer"/>.
    /// </returns>
    public AsyncLazy<Peer> LocalPeer { get; private set; }

    /// <summary>
    /// Resolve an "IPFS path" to a content ID.
    /// </summary>
    /// <param name="path">A IPFS path, such as "Qm...", "Qm.../a/b/c" or "/ipfs/QM..."</param>
    /// <param name="cancel">
    /// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException"/> is raised.
    /// </param>
    /// <returns>The content ID of <paramref name="path"/>.</returns>
    /// <exception cref="ArgumentException">The <paramref name="path"/> cannot be resolved.</exception>
    public async Task<Cid> ResolveIpfsPathToCidAsync(string path, CancellationToken cancel = default)
    {
        string r = await Generic.ResolveAsync(path, true, cancel).ConfigureAwait(false);
        return Cid.Decode(r[6..]);  // strip '/ipfs/'.
    }

    /// <summary>
    /// Determines if the engine has started.
    /// </summary>
    /// <value><b>true</b> if the engine has started; otherwise, <b>false</b>.</value>
    /// <seealso cref="StartAsync"/>
    public bool IsStarted => !stopTasks.IsEmpty;

    /// <summary>
    /// Starts the network services.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// Starts the various IPFS and PeerTalk network services. This should be called after any
    /// configuration changes.
    /// </remarks>
    /// <exception cref="InvalidOperationException">When the engine is already started.</exception>
    public async Task StartAsync()
    {
        if (!stopTasks.IsEmpty)
        {
            throw new InvalidOperationException("IPFS engine is already started.");
        }

        // Repository must be at the correct version.
        await MigrationManager.MigrateToVersionAsync(MigrationManager.LatestVersion)
            .ConfigureAwait(false);

        Peer localPeer = await LocalPeer.ConfigureAwait(false);
        _logger.LogDebug("Starting {PeerId}", localPeer.Id);

        // Everybody needs the swarm.
        Swarm swarm = await SwarmService.ConfigureAwait(false);
        stopTasks.Add(swarm.StopAsync);
        await swarm.StartAsync().ConfigureAwait(false);

        PeerManager peerManager = new() { Swarm = swarm };
        await peerManager.StartAsync().ConfigureAwait(false);
        stopTasks.Add(peerManager.StopAsync);

        // Start the primary services.
        List<Func<Task>> tasks =
        [
            async () =>
            {
                BlockExchange.Bitswap bitswap = await BitswapService.ConfigureAwait(false);
                stopTasks.Add(async () => await bitswap.StopAsync().ConfigureAwait(false));
                await bitswap.StartAsync().ConfigureAwait(false);
            },
            async () =>
            {
                PeerTalk.Routing.Dht1 dht = await DhtService.ConfigureAwait(false);
                stopTasks.Add(async () => await dht.StopAsync().ConfigureAwait(false));
                await dht.StartAsync().ConfigureAwait(false);
            },
            async () =>
            {
                PeerTalk.Protocols.Ping1 ping = await PingService.ConfigureAwait(false);
                stopTasks.Add(async () => await ping.StopAsync().ConfigureAwait(false));
                await ping.StartAsync().ConfigureAwait(false);
            },
            async () =>
            {
                PeerTalk.PubSub.NotificationService pubsub = await PubSubService.ConfigureAwait(false);
                stopTasks.Add(async () => await pubsub.StopAsync().ConfigureAwait(false));
                await pubsub.StartAsync().ConfigureAwait(false);
            },
        ];

        _logger.LogDebug("Waiting for services to start");
        await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);

        // Start listening to the swarm.
        Newtonsoft.Json.Linq.JToken json = await Config.GetAsync("Addresses.Swarm").ConfigureAwait(false);
        int numberListeners = 0;
        foreach (string a in json.Select(v => (string?)v).Where(v => v is not null)!)
        {
            try
            {
                _ = await swarm.StartListeningAsync(a).ConfigureAwait(false);
                ++numberListeners;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Listener failure for '{Address}'", a);
            }
        }

        if (numberListeners == 0)
        {
            _logger.LogError("No listeners were created");
        }

        // Now that the listener addresses are established, the discovery services can begin.
        MulticastService? multicast = null;
        if (!Options.Discovery.DisableMdns)
        {
            multicast = new MulticastService();
            stopTasks.Add(() => Task.Run(multicast.Dispose));
        }

        AutoDialer autodialer = new(swarm)
        {
            MinConnections = Options.Swarm.MinConnections
        };
        stopTasks.Add(() => Task.Run(autodialer.Dispose));

        tasks =
        [
            // Bootstrap discovery
            async () =>
            {
                Bootstrap bootstrap = new() {
                    Addresses = await Bootstrap.ListAsync()
                };
                bootstrap.PeerDiscovered += OnPeerDiscovered;
                stopTasks.Add(async () => await bootstrap.StopAsync().ConfigureAwait(false));
                await bootstrap.StartAsync().ConfigureAwait(false);
            },
            // New multicast DNS discovery
            async () =>
            {
                if (Options.Discovery.DisableMdns) { return; }
                MdnsNext mdns = new() {
                    LocalPeer = localPeer,
                    MulticastService = multicast
                };
                if (Options.Swarm.PrivateNetworkKey != null)
                {
                    mdns.ServiceName = $"_p2p-{Options.Swarm.PrivateNetworkKey.Fingerprint().ToHexString()}._udp";
                }
                mdns.PeerDiscovered += OnPeerDiscovered;
                stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                await mdns.StartAsync().ConfigureAwait(false);
            },
            // Old style JS multicast DNS discovery
            async () =>
            {
                if (Options.Discovery.DisableMdns || Options.Swarm.PrivateNetworkKey != null) { return; }
                MdnsJs mdns = new() {
                    LocalPeer = localPeer,
                    MulticastService = multicast
                };
                mdns.PeerDiscovered += OnPeerDiscovered;
                stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                await mdns.StartAsync().ConfigureAwait(false);
            },
            // Old style GO multicast DNS discovery
            async () =>
            {
                if (Options.Discovery.DisableMdns || Options.Swarm.PrivateNetworkKey != null) { return; }
                MdnsGo mdns = new() {
                    LocalPeer = localPeer,
                    MulticastService = multicast
                };
                mdns.PeerDiscovered += OnPeerDiscovered;
                stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                await mdns.StartAsync().ConfigureAwait(false);
            },
            async () =>
            {
                if (Options.Discovery.DisableRandomWalk) { return; }
                RandomWalk randomWalk = new() { Dht = Dht };
                stopTasks.Add(async () => await randomWalk.StopAsync().ConfigureAwait(false));
                await randomWalk.StartAsync().ConfigureAwait(false);
            }
        ];
        _logger.LogDebug("Waiting for discovery services to start");
        await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);

        multicast?.Start();

        _logger.LogDebug("Started");
    }

    /// <summary>
    /// Stops the running services.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>Multiple calls are okay.</remarks>
    public async Task StopAsync()
    {
        _logger.LogDebug("Stopping");
        try
        {
            Func<Task>[] tasks = [.. stopTasks];
            stopTasks = [];
            await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failure when stopping the engine");
        }

        // Many services use cancellation to stop. A cancellation may not run immediately, so we
        // need to give them some.
        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

        _logger.LogDebug("Stopped");
    }

    /// <summary>
    /// Manages communication with other peers.
    /// </summary>
    public AsyncLazy<Swarm> SwarmService { get; private set; }

    /// <summary>
    /// Manages publishing and subscribing to messages.
    /// </summary>
    public AsyncLazy<PeerTalk.PubSub.NotificationService> PubSubService { get; private set; }

    /// <summary>
    /// Exchange blocks with other peers.
    /// </summary>
    public AsyncLazy<BlockExchange.Bitswap> BitswapService { get; private set; }

    /// <summary>
    /// Finds information with a distributed hash table.
    /// </summary>
    public AsyncLazy<PeerTalk.Routing.Dht1> DhtService { get; private set; }

    /// <summary>
    /// Determines latency to a peer.
    /// </summary>
    public AsyncLazy<PeerTalk.Protocols.Ping1> PingService { get; private set; }

    /// <summary>
    /// Fired when a peer is discovered.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Event handler")]
    private async void OnPeerDiscovered(object? sender, Peer peer)
    {
        try
        {
            Swarm swarm = await SwarmService.ConfigureAwait(false);
            _ = swarm.RegisterPeer(peer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register peer {Peer}", peer);
        }
    }

    private bool _disposed;

    /// <summary>
    /// Releases the unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing">
    /// <b>true</b> to release both managed and unmanaged resources; <b>false</b> to release
    /// only unmanaged resources.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Required for IDisposable contract")]
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (disposing)
        {
            passphrase?.Dispose();
            StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously releases managed resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await StopAsync().ConfigureAwait(false);
            passphrase?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
