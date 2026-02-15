using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ipfs.Engine
{
    /// <summary>
    ///   Interop tests between our .NET IPFS engine and a real Kubo daemon.
    ///   Requires Kubo to be installed (GO_IPFS_LOCATION env var or on PATH).
    /// </summary>
    [TestClass]
    public class KuboInteropTest
    {
        static readonly List<string> reposToClear = [];
        static string KuboRepo;
        static string kuboBin;
        static bool kuboAvailable;
        static Process kuboDaemon;
        static readonly StringBuilder kuboDaemonStderr = new StringBuilder();

        [ClassInitialize]
        public static void SetUp(TestContext context)
        {
            // Resolve Kubo binary from GO_IPFS_LOCATION env var, or from PATH
            var goIpfsLocation = Environment.GetEnvironmentVariable("GO_IPFS_LOCATION");
            var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ipfs.exe" : "ipfs";

            if (!string.IsNullOrEmpty(goIpfsLocation))
            {
                var candidate = Path.Combine(goIpfsLocation, exe);
                if (File.Exists(candidate))
                {
                    kuboBin = candidate;
                }
                else if (File.Exists(goIpfsLocation) &&
                         Path.GetFileName(goIpfsLocation).Equals(exe, StringComparison.OrdinalIgnoreCase))
                {
                    kuboBin = goIpfsLocation;
                }
            }

            // Fallback: search PATH
            if (kuboBin == null)
            {
                var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
                foreach (var dir in pathDirs)
                {
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate))
                    {
                        kuboBin = candidate;
                        break;
                    }
                }
            }

            kuboAvailable = kuboBin != null;
            if (kuboAvailable)
            {
                Console.WriteLine($"Kubo binary: {kuboBin}");
                var version = RunKubo("version");
                Console.WriteLine($"Kubo version: {version.Trim()}");
            }
            else
            {
                Console.WriteLine("Kubo not found. Set GO_IPFS_LOCATION or add ipfs to PATH.");
            }
        }

        [ClassCleanup]
        public static void TearDown()
        {
            StopKuboDaemon();

            // Clean up all repo directories
            foreach (var repo in reposToClear)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        if (Directory.Exists(repo))
                            Directory.Delete(repo, true);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        static void SkipIfNoKubo()
        {
            if (!kuboAvailable)
                Assert.Inconclusive("Kubo binary not available. Set GO_IPFS_LOCATION env var or add ipfs to PATH.");
        }

        [TestMethod]
        [TestCategory("Interop")]
        public async Task Interop_PeerIdentify()
        {
            SkipIfNoKubo();

            InitKuboRepo();
            StartKuboDaemon();

            try
            {
                using var node = new TempNode();
                node.Options.Discovery.DisableMdns = true;
                node.Options.Discovery.BootstrapPeers = [];
                await node.Config.SetAsync("Bootstrap", JToken.FromObject(Array.Empty<string>())).ConfigureAwait(false);
                await node.StartAsync().ConfigureAwait(false);

                // Get Kubo's peer ID and local tcp address
                var idJson = RunKubo("id");
                Console.WriteLine($"Kubo id output: {idJson}");
                var idObj = JObject.Parse(idJson);
                var kuboPeerId = idObj["ID"]?.ToString();
                Assert.IsFalse(string.IsNullOrEmpty(kuboPeerId), "Kubo peer ID should not be empty");

                var addresses = idObj["Addresses"]?.ToObject<string[]>() ?? [];
                var localAddr = addresses.FirstOrDefault(a =>
                    a.Contains("/ip4/127.0.0.1/tcp/") && !a.Contains("/ws"));

                Assert.IsNotNull(localAddr, "Kubo should have a local TCP address");
                Console.WriteLine($"Connecting to Kubo at: {localAddr}");

                // Connect our .NET node to Kubo
                var ma = new MultiAddress(localAddr);
                await node.Swarm.ConnectAsync(ma, default).ConfigureAwait(false);

                // Give identify protocol time to complete
                await Task.Delay(2000).ConfigureAwait(false);

                // Verify we can see the Kubo peer
                var peers = await node.Swarm.PeersAsync(default).ConfigureAwait(false);
                var peerList = peers.ToList();
                Console.WriteLine($"Connected peers: {peerList.Count}");
                foreach (var p in peerList)
                    Console.WriteLine($"  Peer: {p.Id} agent={p.AgentVersion} addr={p.ConnectedAddress}");

                var kuboPeer = peerList.FirstOrDefault(p => p.Id?.ToString() == kuboPeerId);
                if (kuboPeer == null)
                    kuboPeer = peerList.FirstOrDefault(p => p.Id?.ToBase58() == kuboPeerId);

                Assert.IsNotNull(kuboPeer, $"Should be connected to Kubo peer {kuboPeerId}. " +
                    $"Connected to: [{string.Join(", ", peerList.Select(p => p.Id))}]");

                Console.WriteLine($"Successfully connected to Kubo {kuboPeerId}");
                Console.WriteLine($"  Agent: {kuboPeer.AgentVersion}");
                Console.WriteLine($"  Protocol: {kuboPeer.ProtocolVersion}");
                Console.WriteLine($"  Address: {kuboPeer.ConnectedAddress}");

                await node.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                Console.WriteLine($"=== Kubo daemon stderr ===");
                Console.WriteLine(kuboDaemonStderr.ToString());
                StopKuboDaemon();
            }
        }

        [TestMethod]
        [TestCategory("Interop")]
        public async Task Interop_AddAndRetrieveBlock()
        {
            SkipIfNoKubo();

            InitKuboRepo();
            StartKuboDaemon();

            try
            {
                // Add content to Kubo
                var testContent = "Hello from Kubo interop test " + Guid.NewGuid();
                var addResult = RunKubo("add --quieter -", input: testContent);
                var cidStr = addResult.Trim();
                Assert.IsFalse(string.IsNullOrEmpty(cidStr), "Kubo add returned empty CID");
                Console.WriteLine($"Kubo added content with CID: {cidStr}");

                // Create our .NET node
                using var node = new TempNode();
                node.Options.Discovery.DisableMdns = true;
                node.Options.Discovery.BootstrapPeers = [];
                await node.Config.SetAsync("Bootstrap", JToken.FromObject(Array.Empty<string>())).ConfigureAwait(false);
                await node.StartAsync().ConfigureAwait(false);

                // Connect to Kubo
                var localAddr = GetKuboLocalTcpAddress();
                Assert.IsNotNull(localAddr, "Kubo should have a local TCP address");

                var ma = new MultiAddress(localAddr);
                await node.Swarm.ConnectAsync(ma, default).ConfigureAwait(false);
                await Task.Delay(2000).ConfigureAwait(false);

                // Retrieve the block via our .NET implementation
                var cid = Cid.Decode(cidStr);
                Console.WriteLine($"Retrieving block {cid} from Kubo...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var blockData = await node.Block.GetAsync(cid, cts.Token).ConfigureAwait(false);

                Assert.IsNotNull(blockData, "Block data should not be null");
                Assert.IsTrue(blockData.Length > 0, "Block should have data");
                Console.WriteLine($"Retrieved block: {blockData.Length} bytes");

                await node.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                StopKuboDaemon();
            }
        }

        [TestMethod]
        [TestCategory("Interop")]
        public async Task Interop_DotNetToKubo_AddContent()
        {
            SkipIfNoKubo();

            InitKuboRepo();
            StartKuboDaemon();

            try
            {
                using var node = new TempNode();
                // Disable bootstrap to avoid connecting to external peers
                node.Options.Discovery.DisableMdns = true;
                node.Options.Discovery.BootstrapPeers = [];
                await node.Config.SetAsync("Bootstrap", JToken.FromObject(Array.Empty<string>())).ConfigureAwait(false);
                await node.StartAsync().ConfigureAwait(false);

                // Add content to our .NET node
                var testData = "Hello from .NET IPFS " + Guid.NewGuid();
                var fsNode = await node.FileSystem.AddTextAsync(testData).ConfigureAwait(false);
                Assert.IsNotNull(fsNode?.Id, "Should get a CID for added text");
                Console.WriteLine($".NET added content with CID: {fsNode.Id}");

                // Get our .NET node's local address (must include peer ID for Kubo)
                var localPeer = await node.LocalPeer.ConfigureAwait(false);
                var ourAddrs = localPeer.Addresses?.ToArray() ?? [];
                Console.WriteLine($"Our addresses: {string.Join(", ", ourAddrs.Select(a => a.ToString()))}");
                var ourLocalAddrBase = ourAddrs
                    .Select(a => a.ToString())
                    .FirstOrDefault(a => a.Contains("/ip4/127.0.0.1/tcp/") && !a.Contains("/ws"));

                Assert.IsNotNull(ourLocalAddrBase, "Our node should have a local TCP address");
                // Ensure address has /p2p/PeerID (might already have /ipfs/ or /p2p/ suffix)
                var ourLocalAddr = (ourLocalAddrBase.Contains("/p2p/") || ourLocalAddrBase.Contains("/ipfs/"))
                    ? ourLocalAddrBase
                    : $"{ourLocalAddrBase}/p2p/{localPeer.Id}";
                Console.WriteLine($"Our address: {ourLocalAddr}");

                // Tell Kubo to connect to our .NET node
                var connectResult = RunKubo($"swarm connect {ourLocalAddr}", timeout: 10000);
                Console.WriteLine($"Kubo connect result: {connectResult.Trim()}");
                await Task.Delay(2000).ConfigureAwait(false);

                // Kubo retrieves our content
                var catResult = RunKubo($"cat {fsNode.Id}", timeout: 30000);
                Assert.AreEqual(testData, catResult, "Kubo should retrieve content added by .NET node");
                Console.WriteLine("Kubo successfully retrieved .NET content!");

                await node.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                StopKuboDaemon();
            }
        }

        [TestMethod]
        [TestCategory("Interop")]
        public async Task Interop_Bidirectional_PinVerify()
        {
            SkipIfNoKubo();

            InitKuboRepo();
            StartKuboDaemon();

            try
            {
                using var node = new TempNode();
                node.Options.Discovery.DisableMdns = true;
                node.Options.Discovery.BootstrapPeers = [];
                await node.Config.SetAsync("Bootstrap", JToken.FromObject(Array.Empty<string>())).ConfigureAwait(false);
                await node.StartAsync().ConfigureAwait(false);

                // Connect both ways
                var kuboAddr = GetKuboLocalTcpAddress();
                Assert.IsNotNull(kuboAddr, "Kubo should have a local TCP address");
                await node.Swarm.ConnectAsync(new MultiAddress(kuboAddr), default).ConfigureAwait(false);
                await Task.Delay(2000).ConfigureAwait(false);

                // 1) Kubo adds, .NET retrieves
                var kuboContent = "Kubo content " + Guid.NewGuid();
                var kuboCid = RunKubo("add --quieter -", input: kuboContent).Trim();
                Console.WriteLine($"Kubo added: {kuboCid}");

                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var blockFromKubo = await node.Block.GetAsync(Cid.Decode(kuboCid), cts1.Token).ConfigureAwait(false);
                Assert.IsNotNull(blockFromKubo, "Should retrieve Kubo's block");
                Assert.IsTrue(blockFromKubo.Length > 0, "Block should have data");
                Console.WriteLine($"Retrieved {blockFromKubo.Length} bytes from Kubo");

                // 2) .NET adds, Kubo retrieves
                var dotnetContent = ".NET content " + Guid.NewGuid();
                var dotnetNode = await node.FileSystem.AddTextAsync(dotnetContent).ConfigureAwait(false);
                Console.WriteLine($".NET added: {dotnetNode.Id}");

                var catResult = RunKubo($"cat {dotnetNode.Id}", timeout: 30000);
                Assert.AreEqual(dotnetContent, catResult, "Kubo should retrieve .NET content");
                Console.WriteLine("Bidirectional exchange verified!");

                await node.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                StopKuboDaemon();
            }
        }

        #region Kubo management

        static string GetKuboLocalTcpAddress()
        {
            var idJson = RunKubo("id");
            var idObj = JObject.Parse(idJson);
            var addresses = idObj["Addresses"]?.ToObject<string[]>() ?? [];
            return addresses.FirstOrDefault(a =>
                a.Contains("/ip4/127.0.0.1/tcp/") && !a.Contains("/ws"));
        }

        static void InitKuboRepo()
        {
            // Clean up any previous daemon
            StopKuboDaemon();

            // Create a fresh unique repo directory for each test
            KuboRepo = Path.Combine(Path.GetTempPath(), "kubo-interop-" + Guid.NewGuid().ToString("N")[..8]);
            reposToClear.Add(KuboRepo);

            RunKubo("init --profile=test", timeout: 30000);

            // Configure random ports to avoid conflicts
            RunKubo("config Addresses.Swarm --json \"[\\\"/ip4/127.0.0.1/tcp/0\\\"]\"");
            RunKubo("config Addresses.API /ip4/127.0.0.1/tcp/0");
            RunKubo("config Addresses.Gateway /ip4/127.0.0.1/tcp/0");
        }

        static void StartKuboDaemon()
        {
            kuboDaemonStderr.Clear();
            var psi = new ProcessStartInfo(kuboBin, "daemon")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.Environment["IPFS_PATH"] = KuboRepo;

            kuboDaemon = Process.Start(psi);
            kuboDaemon.ErrorDataReceived += (s, e) => { if (e.Data != null) kuboDaemonStderr.AppendLine(e.Data); };
            kuboDaemon.BeginErrorReadLine();
            kuboDaemon.StandardOutput.ReadToEndAsync(); // drain stdout to avoid blocking

            // Wait until the API is ready by polling "ipfs id"
            var ready = false;
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
                try
                {
                    var output = RunKubo("id", timeout: 5000);
                    if (output.Contains("\"ID\""))
                    {
                        ready = true;
                        Console.WriteLine($"Kubo daemon ready after {i + 1}s");
                        break;
                    }
                }
                catch { }
            }

            if (!ready)
                throw new InvalidOperationException("Kubo daemon failed to start within 30 seconds");
        }

        static void StopKuboDaemon()
        {
            if (kuboDaemon != null && !kuboDaemon.HasExited)
            {
                try { kuboDaemon.Kill(entireProcessTree: true); } catch { }

                // Wait for the process to exit
                try { kuboDaemon.WaitForExit(5000); } catch { }
            }
            kuboDaemon = null;

            // Kill any remaining ipfs processes started by us
            try
            {
                foreach (var p in Process.GetProcessesByName("ipfs"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            Thread.Sleep(2000);
        }

        static string RunKubo(string args, string input = null, int timeout = 15000)
        {
            var psi = new ProcessStartInfo(kuboBin, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                CreateNoWindow = true,
            };
            psi.Environment["IPFS_PATH"] = KuboRepo;

            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException($"Failed to start Kubo: {kuboBin} {args}");

            if (input != null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            // Read stderr asynchronously to avoid deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(timeout))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"Kubo command timed out after {timeout}ms: {args}");
            }

            var stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0 && !args.StartsWith("shutdown"))
                Console.WriteLine($"Kubo stderr ({args}): {stderr}");

            return stdout;
        }

        #endregion
    }
}
