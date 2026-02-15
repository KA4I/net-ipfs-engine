using Ipfs;
using Ipfs.Engine.BlockExchange;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk;
using Semver;
using System.Linq;

namespace Ipfs.Engine.BlockExchange
{
    [TestClass]
    public class Bitswap12Test
    {
        [TestMethod]
        public void Protocol_Properties()
        {
            var bs = new Bitswap12();
            Assert.AreEqual("ipfs/bitswap", bs.Name);
            Assert.AreEqual(new SemVersion(1, 2), bs.Version);
            Assert.AreEqual("/ipfs/bitswap/1.2.0", bs.ToString());
        }

        [TestMethod]
        public void Bitswap_Includes_Bitswap12()
        {
            var bitswap = new Bitswap();
            Assert.IsInstanceOfType(bitswap.Protocols[0], typeof(Bitswap12));
            Assert.IsInstanceOfType(bitswap.Protocols[1], typeof(Bitswap11));
            Assert.IsInstanceOfType(bitswap.Protocols[2], typeof(Bitswap1));
        }

        [TestMethod]
        public void Protocol_Order()
        {
            var bitswap = new Bitswap();
            // Bitswap 1.2.0 should be tried first (highest version)
            Assert.AreEqual(3, bitswap.Protocols.Length);
            Assert.AreEqual("/ipfs/bitswap/1.2.0", bitswap.Protocols[0].ToString());
            Assert.AreEqual("/ipfs/bitswap/1.1.0", bitswap.Protocols[1].ToString());
            Assert.AreEqual("/ipfs/bitswap/1.0.0", bitswap.Protocols[2].ToString());
        }
    }
}
