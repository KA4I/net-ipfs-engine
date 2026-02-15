using Ipfs.Engine.BlockExchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{

    [TestClass]
    public class BitswapApiTest
    {
        IpfsEngine ipfs = TestFixture.Ipfs;
        IpfsEngine ipfsOther = TestFixture.IpfsOther;

        [TestMethod]
        [Ignore("Bitswap.GetAsync removed")]
        public async Task Wants()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        [Ignore("Bitswap.UnwantAsync removed")]
        public async Task Unwant()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task OnConnect_Sends_WantList()
        {
            ipfs.Options.Discovery.DisableMdns = true;
            ipfs.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfs.StartAsync();

            ipfsOther.Options.Discovery.DisableMdns = true;
            ipfsOther.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfsOther.StartAsync();
            try
            {
                var local = await ipfs.LocalPeer;
                var remote = await ipfsOther.LocalPeer;
                Console.WriteLine($"this at {local.Addresses.First()}");
                Console.WriteLine($"othr at {remote.Addresses.First()}");

                var data = Guid.NewGuid().ToByteArray();
                var cid = new Cid { Hash = MultiHash.ComputeHash(data) };
                var _ = ipfs.Block.GetAsync(cid);
                await Task.Delay(500); // Allow the want to be registered before connecting
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First());

                var endTime = DateTime.Now.AddSeconds(10);
                while (DateTime.Now < endTime)
                {
                    var wants = await ipfsOther.Bitswap.WantsAsync(local.Id);
                    if (wants.Contains(cid))
                        return;
                    await Task.Delay(200);
                }

                Assert.Fail("want list not sent");
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();

                ipfs.Options.Discovery = new DiscoveryOptions();
                ipfsOther.Options.Discovery = new DiscoveryOptions();
            }
        }

        [TestMethod]
        public async Task GetsBlock_OnConnect()
        {
            ipfs.Options.Discovery.DisableMdns = true;
            ipfs.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfs.StartAsync();

            ipfsOther.Options.Discovery.DisableMdns = true;
            ipfsOther.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfsOther.StartAsync();
            try
            {
                var data = Guid.NewGuid().ToByteArray();
                var putResult = await ipfsOther.Block.PutAsync(data);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var getTask = ipfs.Block.GetAsync(putResult.Id, cts.Token);

                var remote = await ipfsOther.LocalPeer;
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First(), cts.Token);
                var block = await getTask;

                Assert.IsFalse(getTask.IsCanceled, "task cancelled");
                Assert.IsFalse(getTask.IsFaulted, "task faulted");
                Assert.IsTrue(getTask.IsCompleted, "task not completed");
                Assert.IsNotNull(block);
                CollectionAssert.AreEqual(data, block);

                var otherPeer = await ipfsOther.LocalPeer;
                var ledger = await ipfs.Bitswap.LedgerAsync(otherPeer);
                Assert.AreEqual(otherPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreNotEqual(0UL, ledger.DataReceived);
                Assert.AreEqual(0UL, ledger.DataSent);
                Assert.IsTrue(ledger.IsInDebt);

                // TODO: Timing issue here.  ipfsOther could have sent the block
                // but not updated the stats yet.
#if false
                var localPeer = await ipfs.LocalPeer;
                ledger = await ipfsOther.Bitswap.LedgerAsync(localPeer);
                Assert.AreEqual(localPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreEqual(0UL, ledger.DataReceived);
                Assert.AreNotEqual(0UL, ledger.DataSent);
                Assert.IsFalse(ledger.IsInDebt);
#endif
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();

                ipfs.Options.Discovery = new DiscoveryOptions();
                ipfsOther.Options.Discovery = new DiscoveryOptions();
            }
        }

        [TestMethod]
        public async Task GetsBlock_OnConnect_Bitswap1()
        {
            var originalProtocols = (await ipfs.BitswapService).Protocols;
            var otherOriginalProtocols = (await ipfsOther.BitswapService).Protocols;

            (await ipfs.BitswapService).Protocols = new IBitswapProtocol[]
            {
                new Bitswap1 { Bitswap = (await ipfs.BitswapService) }
            };
            ipfs.Options.Discovery.DisableMdns = true;
            ipfs.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfs.StartAsync();

            (await ipfsOther.BitswapService).Protocols = new IBitswapProtocol[]
            {
                new Bitswap1 { Bitswap = (await ipfsOther.BitswapService) }
            };
            ipfsOther.Options.Discovery.DisableMdns = true;
            ipfsOther.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfsOther.StartAsync();
            try
            {
                var data = Guid.NewGuid().ToByteArray();
                var putResult = await ipfsOther.Block.PutAsync(data);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var getTask = ipfs.Block.GetAsync(putResult.Id, cts.Token);

                var remote = await ipfsOther.LocalPeer;
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First(), cts.Token);
                var block = await getTask;

                Assert.IsFalse(getTask.IsCanceled, "task cancelled");
                Assert.IsFalse(getTask.IsFaulted, "task faulted");
                Assert.IsTrue(getTask.IsCompleted, "task not completed");
                Assert.IsNotNull(block);
                CollectionAssert.AreEqual(data, block);

                var otherPeer = await ipfsOther.LocalPeer;
                var ledger = await ipfs.Bitswap.LedgerAsync(otherPeer);
                Assert.AreEqual(otherPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreNotEqual(0UL, ledger.DataReceived);
                Assert.AreEqual(0UL, ledger.DataSent);
                Assert.IsTrue(ledger.IsInDebt);

                // TODO: Timing issue here.  ipfsOther could have sent the block
                // but not updated the stats yet.
#if false
                var localPeer = await ipfs.LocalPeer;
                ledger = await ipfsOther.Bitswap.LedgerAsync(localPeer);
                Assert.AreEqual(localPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreEqual(0UL, ledger.DataReceived);
                Assert.AreNotEqual(0UL, ledger.DataSent);
                Assert.IsFalse(ledger.IsInDebt);
#endif
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();

                ipfs.Options.Discovery = new DiscoveryOptions();
                ipfsOther.Options.Discovery = new DiscoveryOptions();

                (await ipfs.BitswapService).Protocols = originalProtocols;
                (await ipfsOther.BitswapService).Protocols = otherOriginalProtocols;
            }
        }

        [TestMethod]
        public async Task GetsBlock_OnConnect_Bitswap11()
        {
            var originalProtocols = (await ipfs.BitswapService).Protocols;
            var otherOriginalProtocols = (await ipfsOther.BitswapService).Protocols;

            (await ipfs.BitswapService).Protocols = new IBitswapProtocol[]
            {
                new Bitswap11 { Bitswap = (await ipfs.BitswapService) }
            };
            ipfs.Options.Discovery.DisableMdns = true;
            ipfs.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfs.StartAsync();

            (await ipfsOther.BitswapService).Protocols = new IBitswapProtocol[]
            {
                new Bitswap11 { Bitswap = (await ipfsOther.BitswapService) }
            };
            ipfsOther.Options.Discovery.DisableMdns = true;
            ipfsOther.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfsOther.StartAsync();
            try
            {
                var data = Guid.NewGuid().ToByteArray();
                var putResult = await ipfsOther.Block.PutAsync(data);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var getTask = ipfs.Block.GetAsync(putResult.Id, cts.Token);

                var remote = await ipfsOther.LocalPeer;
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First(), cts.Token);
                var block = await getTask;

                Assert.IsFalse(getTask.IsCanceled, "task cancelled");
                Assert.IsFalse(getTask.IsFaulted, "task faulted");
                Assert.IsTrue(getTask.IsCompleted, "task not completed");
                Assert.IsNotNull(block);
                CollectionAssert.AreEqual(data, block);

                var otherPeer = await ipfsOther.LocalPeer;
                var ledger = await ipfs.Bitswap.LedgerAsync(otherPeer);
                Assert.AreEqual(otherPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreNotEqual(0UL, ledger.DataReceived);
                Assert.AreEqual(0UL, ledger.DataSent);
                Assert.IsTrue(ledger.IsInDebt);

                // TODO: Timing issue here.  ipfsOther could have sent the block
                // but not updated the stats yet.
#if false
                var localPeer = await ipfs.LocalPeer;
                ledger = await ipfsOther.Bitswap.LedgerAsync(localPeer);
                Assert.AreEqual(localPeer, ledger.Peer);
                Assert.AreNotEqual(0UL, ledger.BlocksExchanged);
                Assert.AreEqual(0UL, ledger.DataReceived);
                Assert.AreNotEqual(0UL, ledger.DataSent);
                Assert.IsFalse(ledger.IsInDebt);
#endif
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();

                ipfs.Options.Discovery = new DiscoveryOptions();
                ipfsOther.Options.Discovery = new DiscoveryOptions();

                (await ipfs.BitswapService).Protocols = originalProtocols;
                (await ipfsOther.BitswapService).Protocols = otherOriginalProtocols;
            }
        }

        [TestMethod]
        public async Task GetsBlock_OnRequest()
        {
            ipfs.Options.Discovery.DisableMdns = true;
            ipfs.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfs.StartAsync();

            ipfsOther.Options.Discovery.DisableMdns = true;
            ipfsOther.Options.Discovery.BootstrapPeers = new MultiAddress[0];
            await ipfsOther.StartAsync();
            try
            {
                var cts = new CancellationTokenSource(10000);
                var data = Guid.NewGuid().ToByteArray();
                var putResult = await ipfsOther.Block.PutAsync(data, cancel:  cts.Token);

                var remote = await ipfsOther.LocalPeer;
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First(), cancel: cts.Token);

                var block = await ipfs.Block.GetAsync(putResult.Id, cancel: cts.Token);
                Assert.IsNotNull(block);
                CollectionAssert.AreEqual(data, block);
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();
                ipfs.Options.Discovery = new DiscoveryOptions();
                ipfsOther.Options.Discovery = new DiscoveryOptions();
            }
        }

        [TestMethod]
        public async Task GetsBlock_Cidv1()
        {
            await ipfs.StartAsync();
            await ipfsOther.StartAsync();
            try
            {
                var data = Guid.NewGuid().ToByteArray();
                var putResult = await ipfsOther.Block.PutAsync(data, "raw", MultiHash.ComputeHash(data, "sha2-512"));

                var remote = await ipfsOther.LocalPeer;
                await ipfs.Swarm.ConnectAsync(remote.Addresses.First());

                var cts = new CancellationTokenSource(3000);
                var block = await ipfs.Block.GetAsync(putResult.Id, cts.Token);
                Assert.IsNotNull(block);
                CollectionAssert.AreEqual(data, block);
            }
            finally
            {
                await ipfsOther.StopAsync();
                await ipfs.StopAsync();
            }
        }

        [TestMethod]
        public async Task GetBlock_Timeout()
        {
            var block = new DagNode(Encoding.UTF8.GetBytes("BitswapApiTest unknown block"));

            await ipfs.StartAsync();
            try
            {
                var cts = new CancellationTokenSource(300);
                ExceptionAssert.Throws<TaskCanceledException>(() =>
                {
                    var _ = ipfs.Block.GetAsync(block.Id, cts.Token).Result;
                });

                Assert.AreEqual(0, (await ipfs.Bitswap.WantsAsync()).Count());
            }
            finally
            {
                await ipfs.StopAsync();
            }
        }

        [TestMethod]
        public async Task PeerLedger()
        {
            await ipfs.StartAsync();
            try
            {
                var peer = await ipfsOther.LocalPeer;
                var ledger = await ipfs.Bitswap.LedgerAsync(peer);
                Assert.IsNotNull(ledger);
            }
            finally
            {
                await ipfs.StopAsync();
            }
        }

    }
}
