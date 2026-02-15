using Ipfs.CoreApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    [TestClass]
    public class PinApiTest
    {
        [TestMethod]
        public async Task Add_Remove()
        {
            var ipfs = TestFixture.Ipfs;
            var result = await ipfs.FileSystem.AddTextAsync("I am pinned");
            var id = result.Id;

            var pins = await ipfs.Pin.AddAsync(id.ToString(), new PinAddOptions());
            Assert.IsTrue(pins.Any(pin => pin == id));
            var all = new List<PinListItem>();
            await foreach (var pin in ipfs.Pin.ListAsync()) all.Add(pin);
            Assert.IsTrue(all.Any(p => p.Cid == id));

            pins = await ipfs.Pin.RemoveAsync(id);
            Assert.IsTrue(pins.Any(pin => pin == id));
            all = new List<PinListItem>();
            await foreach (var pin in ipfs.Pin.ListAsync()) all.Add(pin);
            Assert.IsFalse(all.Any(p => p.Cid == id));
        }

        [TestMethod]
        public async Task Remove_Unknown()
        {
            var ipfs = TestFixture.Ipfs;
            var dag = new DagNode(Encoding.UTF8.GetBytes("some unknown info for net-ipfs-engine-pin-test"));
            await ipfs.Pin.RemoveAsync(dag.Id, true);
        }

        [TestMethod]
        public async Task Inline_Cid()
        {
            var ipfs = TestFixture.Ipfs;
            var cid = new Cid
            {
                ContentType = "raw",
                Hash = MultiHash.ComputeHash(new byte[] { 1, 2, 3 }, "identity")
            };
            var pins = await ipfs.Pin.AddAsync(cid.ToString(), new PinAddOptions { Recursive = false });
            Assert.IsTrue(pins.Any(p => p == cid));
            var all = new List<PinListItem>();
            await foreach (var pin in ipfs.Pin.ListAsync()) all.Add(pin);
            Assert.IsTrue(all.Any(p => p.Cid == cid));

            var removals = await ipfs.Pin.RemoveAsync(cid, false);
            Assert.IsTrue(removals.Any(p => p == cid));
            all = new List<PinListItem>();
            await foreach (var pin in ipfs.Pin.ListAsync()) all.Add(pin);
            Assert.IsFalse(all.Any(p => p.Cid == cid));
        }

        [TestMethod]
        public void Add_Unknown()
        {
            var ipfs = TestFixture.Ipfs;
            var dag = new DagNode(Encoding.UTF8.GetBytes("some unknown info for net-ipfs-engine-pin-test"));
            ExceptionAssert.Throws<Exception>(() =>
            {
                var cts = new CancellationTokenSource(250);
                var _ = ipfs.Pin.AddAsync(dag.Id.ToString(), new PinAddOptions { Recursive = true }, cts.Token).Result;
            });
        }

        [TestMethod]
        public async Task Add_Recursive()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3",
                Pin = false,
                RawLeaves = true,
                Wrap = true,
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var cids = await ipfs.Pin.AddAsync(node.Id.ToString(), new PinAddOptions { Recursive = true });
            Assert.AreEqual(6, cids.Count());
        }

        [TestMethod]
        public async Task Remove_Recursive()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3",
                Pin = false,
                RawLeaves = true,
                Wrap = true,
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var cids = await ipfs.Pin.AddAsync(node.Id.ToString(), new PinAddOptions { Recursive = true });
            Assert.AreEqual(6, cids.Count());

            var removedCids = await ipfs.Pin.RemoveAsync(node.Id, true);
            CollectionAssert.AreEqual(cids.ToArray(), removedCids.ToArray());
        }
    }
}

