using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipfs.Engine
{
    /// <summary>
    ///   Tests for Kubo 0.40 feature alignment.
    /// </summary>
    [TestClass]
    public class Kubo040FeaturesTest
    {
        [TestMethod]
        public void BlockSize_Is_2MiB()
        {
            var options = new BlockOptions();
            Assert.AreEqual(2 * 1024 * 1024, options.MaxBlockSize, "MaxBlockSize should be 2 MiB per Kubo 0.40");
        }

        [TestMethod]
        public void CidProfile_UnixfsV1_2025()
        {
            var import = new ImportOptions();
            import.ApplyProfile("unixfs-v1-2025");

            Assert.AreEqual(1, import.CidVersion, "CID version should be 1");
            Assert.IsTrue(import.RawLeaves, "Raw leaves should be true");
            Assert.AreEqual("size-1048576", import.Chunker, "Chunker should be 1 MiB");
            Assert.AreEqual("sha2-256", import.HashAlgorithm);
            Assert.AreEqual("balanced", import.UnixFSDAGLayout);
        }

        [TestMethod]
        public void CidProfile_UnixfsV0_2015()
        {
            var import = new ImportOptions();
            import.ApplyProfile("unixfs-v0-2015");

            Assert.AreEqual(0, import.CidVersion, "CID version should be 0");
            Assert.IsFalse(import.RawLeaves, "Raw leaves should be false");
            Assert.AreEqual("size-262144", import.Chunker, "Chunker should be 256 KiB");
            Assert.AreEqual("sha2-256", import.HashAlgorithm);
            Assert.AreEqual("balanced", import.UnixFSDAGLayout);
        }

        [TestMethod]
        public void CidProfile_LegacyCidV0_Alias()
        {
            var import = new ImportOptions();
            import.ApplyProfile("legacy-cid-v0");

            Assert.AreEqual(0, import.CidVersion);
            Assert.IsFalse(import.RawLeaves);
        }

        [TestMethod]
        public void CidProfile_Unknown_Throws()
        {
            var import = new ImportOptions();
            ExceptionAssert.Throws<ArgumentException>(() => import.ApplyProfile("nonexistent-profile"));
        }

        [TestMethod]
        public void Import_Defaults()
        {
            var options = new IpfsEngineOptions();
            Assert.IsNotNull(options.Import);
            Assert.AreEqual(0, options.Import.CidVersion, "Default CID version is 0");
            Assert.IsFalse(options.Import.RawLeaves, "Default raw leaves is false");
            Assert.AreEqual("size-262144", options.Import.Chunker);
            Assert.AreEqual("balanced", options.Import.UnixFSDAGLayout);
            Assert.AreEqual(256 * 1024, options.Import.UnixFSHAMTShardingSize);
        }

        [TestMethod]
        public void IpnsRecord_SequenceTracking()
        {
            // Test IPNS PubSub validation - sequence number dedup (Kubo 0.40)
            var record1 = new CoreApi.NameApi.IpnsRecord { Sequence = 1, Value = System.Text.Encoding.UTF8.GetBytes("/ipfs/Qm1") };
            var record2 = new CoreApi.NameApi.IpnsRecord { Sequence = 2, Value = System.Text.Encoding.UTF8.GetBytes("/ipfs/Qm2") };
            var record1Dup = new CoreApi.NameApi.IpnsRecord { Sequence = 1, Value = System.Text.Encoding.UTF8.GetBytes("/ipfs/Qm1") };

            var testPeer = "test-peer-" + Guid.NewGuid();

            // First record accepted
            Assert.IsTrue(CoreApi.NameApi.TryAcceptRecord(testPeer, record1));

            // Duplicate rejected
            Assert.IsFalse(CoreApi.NameApi.TryAcceptRecord(testPeer, record1Dup));

            // Higher sequence accepted
            Assert.IsTrue(CoreApi.NameApi.TryAcceptRecord(testPeer, record2));

            // Lower sequence rejected
            Assert.IsFalse(CoreApi.NameApi.TryAcceptRecord(testPeer, record1));
        }
    }
}
