using ICSharpCode.SharpZipLib.Tar;
using Ipfs.CoreApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    [TestClass]
    public class FileSystemApiTest
    {

        [TestMethod]
        public async Task AddText()
        {
            var ipfs = TestFixture.Ipfs;
            var node = (UnixFileSystem.FileSystemNode) await ipfs.FileSystem.AddTextAsync("hello world");
            Assert.AreEqual("Qmf412jQZiuVUtdgnB36FXFX7xg5V6KEbSJ4dpQuhkLyfD", (string)node.Id);
            Assert.AreEqual("", node.Name);
            Assert.AreEqual(0, node.Links.Count());

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);

            var actual = await ipfs.FileSystem.ListAsync(node.Id);
            Assert.AreEqual(node.Id, actual.Id);
            Assert.AreEqual(node.IsDirectory, actual.IsDirectory);
            Assert.AreEqual(node.Links.Count(), actual.Links.Count());
            Assert.AreEqual(node.Size, actual.Size);
        }

        [TestMethod]
        public async Task AddEmptyText()
        {
            var ipfs = TestFixture.Ipfs;
            var node = (UnixFileSystem.FileSystemNode)await ipfs.FileSystem.AddTextAsync("");
            Assert.AreEqual("QmbFMke1KXqnYyBBWxB74N4c5SBnJMVAiMNRcGu6x1AwQH", (string)node.Id);
            Assert.AreEqual("", node.Name);
            Assert.AreEqual(0, node.Links.Count());

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("", text);
        
            var actual = await ipfs.FileSystem.ListAsync(node.Id);
            Assert.AreEqual(node.Id, actual.Id);
            Assert.AreEqual(node.IsDirectory, actual.IsDirectory);
            Assert.AreEqual(node.Links.Count(), actual.Links.Count());
            Assert.AreEqual(node.Size, actual.Size);
        }

        [TestMethod]
        [Ignore("TODO: Object API is no longer available on IpfsEngine")]
        public async Task AddEmpty_Check_Object()
        {
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AddDuplicateWithPin()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Pin = true
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual("Qmf412jQZiuVUtdgnB36FXFX7xg5V6KEbSJ4dpQuhkLyfD", (string)node.Id);
            var pinCids = new List<Cid>();
            await foreach (var pin in ipfs.Pin.ListAsync())
                pinCids.Add(pin.Cid);
            CollectionAssert.Contains(pinCids.ToArray(), node.Id);

            options.Pin = false;
            node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual("Qmf412jQZiuVUtdgnB36FXFX7xg5V6KEbSJ4dpQuhkLyfD", (string)node.Id);
            Assert.AreEqual(0, node.Links.Count());
            // Explicitly remove the pin since adding with Pin=false does not auto-unpin
            await ipfs.Pin.RemoveAsync(node.Id);
            pinCids.Clear();
            await foreach (var pin in ipfs.Pin.ListAsync())
                pinCids.Add(pin.Cid);
            CollectionAssert.DoesNotContain(pinCids.ToArray(), node.Id);
        }

        [TestMethod]
        public async Task Add_SizeChunking()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3"
            };
            options.Pin = true;
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var links = node.Links.ToArray();
            Assert.AreEqual("QmVVZXWrYzATQdsKWM4knbuH5dgHFmrRqW3nJfDgdWrBjn", (string)node.Id);
            Assert.AreEqual(false, node.IsDirectory);
            Assert.AreEqual(4, links.Length);
            Assert.AreEqual("QmevnC4UDUWzJYAQtUSQw4ekUdqDqwcKothjcobE7byeb6", (string)links[0].Id);
            Assert.AreEqual("QmTdBogNFkzUTSnEBQkWzJfQoiWbckLrTFVDHFRKFf6dcN", (string)links[1].Id);
            Assert.AreEqual("QmPdmF1n4di6UwsLgW96qtTXUsPkCLN4LycjEUdH9977d6", (string)links[2].Id);
            Assert.AreEqual("QmXh5UucsqF8XXM8UYQK9fHXsthSEfi78kewr8ttpPaLRE", (string)links[3].Id);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public async Task StreamBehaviour()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3",
                Pin = true,
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var stream = await ipfs.FileSystem.ReadFileAsync(node.Id);
            Assert.AreEqual(11, stream.Length);
            Assert.IsTrue(stream.CanRead);
            Assert.IsFalse(stream.CanWrite);
            Assert.IsTrue(stream.CanSeek);
        }

        [TestMethod]
        public async Task Add_HashAlgorithm()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Hash = "blake2b-256",
                RawLeaves = true
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual("bafk2bzaceaswza5ss4iu2ia3galz6pyo6dfm5f4dmiw2lf2de22dmf4k533ba", (string)node.Id);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public void AddFile()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "hello world");
            try
            {
                var ipfs = TestFixture.Ipfs;
                var node = (UnixFileSystem.FileSystemNode)ipfs.FileSystem.AddFileAsync(path).Result;
                Assert.AreEqual("Qmf412jQZiuVUtdgnB36FXFX7xg5V6KEbSJ4dpQuhkLyfD", (string)node.Id);
                Assert.AreEqual(0, node.Links.Count());
                Assert.AreEqual(Path.GetFileName(path), node.Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        [Ignore("TODO: Encoding is no longer available in AddFileOptions")]
        public void AddFile_CidEncoding()
        {
        }

        [TestMethod]
        public void AddFile_Large()
        {
            AddFile(); // warm up

            var path = "star_trails.mp4";
            var ipfs = TestFixture.Ipfs;
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var node = ipfs.FileSystem.AddFileAsync(path).Result;
            stopWatch.Stop();
            Console.WriteLine("Add file took {0} seconds.", stopWatch.Elapsed.TotalSeconds);

            Assert.AreEqual("QmeZkAUfUFPq5YWGBan2ZYNd9k59DD1xW62pGJrU3C6JRo", (string)node.Id);

            var k = 8 * 1024;
            var buffer1 = new byte[k];
            var buffer2 = new byte[k];
            stopWatch.Restart();
            using (var localStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var ipfsStream = ipfs.FileSystem.ReadFileAsync(node.Id).Result)
            {
                while (true)
                {
                    var n1 = localStream.Read(buffer1, 0, k);
                    var n2 = ipfsStream.Read(buffer2, 0, k);
                    Assert.AreEqual(n1, n2);
                    if (n1 == 0)
                        break;
                    for (var i = 0; i < n1; ++i)
                    {
                        if (buffer1[i] != buffer2[i])
                            Assert.Fail("data not the same");
                    }
                }
            }
            stopWatch.Stop();
            Console.WriteLine("Readfile file took {0} seconds.", stopWatch.Elapsed.TotalSeconds);
        }


        /// <seealso href="https://github.com/richardschneider/net-ipfs-engine/issues/125"/>
        [TestMethod]
        public void AddFile_Larger()
        {
            AddFile(); // warm up

            var path = "starx2.mp4";
            var ipfs = TestFixture.Ipfs;
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var node = ipfs.FileSystem.AddFileAsync(path).Result;
            stopWatch.Stop();
            Console.WriteLine("Add file took {0} seconds.", stopWatch.Elapsed.TotalSeconds);

            Assert.AreEqual("QmeFhfB4g2GFbxYb7usApWzq8uC1vmuxJajFpiJiT5zLoy", (string)node.Id);

            var k = 8 * 1024;
            var buffer1 = new byte[k];
            var buffer2 = new byte[k];
            stopWatch.Restart();
            using (var localStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var ipfsStream = ipfs.FileSystem.ReadFileAsync(node.Id).Result)
            {
                while (true)
                {
                    var n1 = localStream.Read(buffer1, 0, k);
                    var n2 = ipfsStream.Read(buffer2, 0, k);
                    Assert.AreEqual(n1, n2);
                    if (n1 == 0)
                        break;
                    for (var i = 0; i < n1; ++i)
                    {
                        if (buffer1[i] != buffer2[i])
                            Assert.Fail("data not the same");
                    }
                }
            }
            stopWatch.Stop();
            Console.WriteLine("Readfile file took {0} seconds.", stopWatch.Elapsed.TotalSeconds);
        }

        [TestMethod]
        public async Task AddFile_Wrap()
        {
            var path = "hello.txt";
            File.WriteAllText(path, "hello world");
            try
            {
                var ipfs = TestFixture.Ipfs;
                var options = new AddFileOptions
                {
                    Wrap = true
                };
                var node = await ipfs.FileSystem.AddFileAsync(path, options);
                Assert.AreEqual("QmNxvA5bwvPGgMXbmtyhxA1cKFdvQXnsGnZLCGor3AzYxJ", (string)node.Id);
                Assert.AreEqual(true, node.IsDirectory);
                Assert.AreEqual(1, node.Links.Count());
                Assert.AreEqual("hello.txt", node.Links.First().Name);
                Assert.AreEqual("Qmf412jQZiuVUtdgnB36FXFX7xg5V6KEbSJ4dpQuhkLyfD", (string)node.Links.First().Id);
                Assert.AreEqual(19UL, node.Links.First().Size);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Add_Raw()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                RawLeaves = true
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual("bafkreifzjut3te2nhyekklss27nh3k72ysco7y32koao5eei66wof36n5e", (string)node.Id);
            Assert.AreEqual(11UL, node.Size);
            Assert.AreEqual(0, node.Links.Count());
            Assert.AreEqual(false, node.IsDirectory);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public async Task Add_Inline()
        {
            var ipfs = TestFixture.Ipfs;
            var original = ipfs.Options.Block.AllowInlineCid;
            try
            {
                ipfs.Options.Block.AllowInlineCid = true;

                var node = await ipfs.FileSystem.AddTextAsync("hiya");
                Assert.AreEqual(1, node.Id.Version);
                Assert.IsTrue(node.Id.Hash.IsIdentityHash);
                Assert.AreEqual(4UL, node.Size);
                Assert.AreEqual(0, node.Links.Count());
                Assert.AreEqual(false, node.IsDirectory);
                Assert.AreEqual("bafyaadakbieaeeqenbuxsyiyaq", node.Id.Encode());
                var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
                Assert.AreEqual("hiya", text);
            }
            finally
            {
                ipfs.Options.Block.AllowInlineCid = original;
            }
        }

        [TestMethod]
        public async Task Add_RawAndChunked()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                RawLeaves = true,
                Chunker = "size-3"
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var links = node.Links.ToArray();
            Assert.AreEqual("QmUuooB6zEhMmMaBvMhsMaUzar5gs5KwtVSFqG4C1Qhyhs", (string)node.Id);
            Assert.AreEqual(false, node.IsDirectory);
            Assert.AreEqual(4, links.Length);
            Assert.AreEqual("bafkreigwvapses57f56cfow5xvoua4yowigpwcz5otqqzk3bpcbbjswowe", (string)links[0].Id);
            Assert.AreEqual("bafkreiew3cvfrp2ijn4qokcp5fqtoknnmr6azhzxovn6b3ruguhoubkm54", (string)links[1].Id);
            Assert.AreEqual("bafkreibsybcn72tquh2l5zpim2bba4d2kfwcbpzuspdyv2breaq5efo7tq", (string)links[2].Id);
            Assert.AreEqual("bafkreihfuch72plvbhdg46lef3n5zwhnrcjgtjywjryyv7ffieyedccchu", (string)links[3].Id);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public async Task Add_Protected()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                ProtectionKey = "self"
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual("cms", node.Id.ContentType);
            Assert.AreEqual(0, node.Links.Count());
            Assert.AreEqual(false, node.IsDirectory);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public async Task Add_Protected_Chunked()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                ProtectionKey = "self",
                Chunker = "size-3"
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            Assert.AreEqual(4, node.Links.Count());
            Assert.AreEqual(false, node.IsDirectory);

            var text = await ipfs.FileSystem.ReadAllTextAsync(node.Id);
            Assert.AreEqual("hello world", text);
        }

        [TestMethod]
        public async Task Add_OnlyHash()
        {
            var ipfs = TestFixture.Ipfs;
            var nodes = new string[] {
                "QmVVZXWrYzATQdsKWM4knbuH5dgHFmrRqW3nJfDgdWrBjn",
                "QmevnC4UDUWzJYAQtUSQw4ekUdqDqwcKothjcobE7byeb6",
                "QmTdBogNFkzUTSnEBQkWzJfQoiWbckLrTFVDHFRKFf6dcN",
                "QmPdmF1n4di6UwsLgW96qtTXUsPkCLN4LycjEUdH9977d6",
                "QmXh5UucsqF8XXM8UYQK9fHXsthSEfi78kewr8ttpPaLRE"
            };
            foreach (var n in nodes) {
                await ipfs.Block.RemoveAsync(n, ignoreNonexistent: true);
            }

            var options = new AddFileOptions
            {
                Chunker = "size-3",
                OnlyHash = true,
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var links = node.Links.ToArray();
            Assert.AreEqual(nodes[0], (string)node.Id);
            Assert.AreEqual(nodes.Length - 1, links.Length);
            for (var i = 0; i < links.Length; ++i)
            {
                Assert.AreEqual(nodes[i+1], (string)links[i].Id);
            }

            // TODO: Need a method to test that the CId is not held locally.
            //foreach (var n in nodes)
            //{
            //    Assert.IsNull(await ipfs.Block.StatAsync(n));
            //}
        }

        [TestMethod]
        public async Task ReadWithOffset()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3"
            };
            var node = await ipfs.FileSystem.AddTextAsync(text, options);

            for (var offset = 0; offset <= text.Length; ++offset)
            {
                using (var data = await ipfs.FileSystem.ReadFileAsync(node.Id, offset))
                using (var reader = new StreamReader(data))
                {
                    var s = reader.ReadToEnd();
                    Assert.AreEqual(text.Substring(offset), s);
                }
            }
        }

        [TestMethod]
        public async Task Read_RawWithLength()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                RawLeaves = true
            };
            var node = await ipfs.FileSystem.AddTextAsync(text, options);

            for (var offset = 0; offset < text.Length; ++offset)
            {
                for (var length = text.Length + 1; 0 < length; --length)
                {
                    using (var data = await ipfs.FileSystem.ReadFileAsync(node.Id, offset, length))
                    using (var reader = new StreamReader(data))
                    {
                        var s = reader.ReadToEnd();
                        Assert.AreEqual(text.Substring(offset, Math.Min(11 - offset, length)), s, $"o={offset} l={length}");
                    }
                }
            }
        }

        [TestMethod]
        public async Task Read_ChunkedWithLength()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3"
            };
            var node = await ipfs.FileSystem.AddTextAsync(text, options);

            for (var length = text.Length + 1; 0 < length; --length)
            {
                using (var data = await ipfs.FileSystem.ReadFileAsync(node.Id, 0, length))
                using (var reader = new StreamReader(data))
                {
                    var s = reader.ReadToEnd();
                    Assert.AreEqual(text.Substring(0, Math.Min(11, length)), s, $"l={length}");
                }
            }
        }

        [TestMethod]
        public async Task Read_ProtectedWithLength()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                ProtectionKey = "self"
            };
            var node = await ipfs.FileSystem.AddTextAsync(text, options);

            for (var offset = 0; offset < text.Length; ++offset)
            {
                for (var length = text.Length + 1; 0 < length; --length)
                {
                    using (var data = await ipfs.FileSystem.ReadFileAsync(node.Id, offset, length))
                    using (var reader = new StreamReader(data))
                    {
                        var s = reader.ReadToEnd();
                        Assert.AreEqual(text.Substring(offset, Math.Min(11 - offset, length)), s, $"o={offset} l={length}");
                    }
                }
            }
        }

        [TestMethod]
        public async Task Read_ProtectedChunkedWithLength()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Chunker = "size-3",
                ProtectionKey = "self"
            };
            var node = await ipfs.FileSystem.AddTextAsync(text, options);

            for (var offset = 0; offset < text.Length; ++offset)
            {
                for (var length = text.Length + 1; 0 < length; --length)
                {
                    using (var data = await ipfs.FileSystem.ReadFileAsync(node.Id, offset, length))
                    using (var reader = new StreamReader(data))
                    {
                        var s = reader.ReadToEnd();
                        Assert.AreEqual(text.Substring(offset, Math.Min(11 - offset, length)), s, $"o={offset} l={length}");
                    }
                }
            }
        }

        [TestMethod]
        public async Task Read_ProtectedMissingKey()
        {
            var text = "hello world";
            var ipfs = TestFixture.Ipfs;
            var key = await ipfs.Key.CreateAsync("alice", "rsa", 2048);
            try
            {
                var options = new AddFileOptions { ProtectionKey = key.Name };
                var node = await ipfs.FileSystem.AddTextAsync(text, options);
                Assert.AreEqual(text, await ipfs.FileSystem.ReadAllTextAsync(node.Id));

                await ipfs.Key.RemoveAsync(key.Name);
                ExceptionAssert.Throws<KeyNotFoundException>(() =>
                {
                    var _ = ipfs.FileSystem.ReadAllTextAsync(node.Id).Result;
                });
            }
            finally
            {
                try { await ipfs.Key.RemoveAsync(key.Name); }
                catch (Exception) { /* already removed */ }
            }
        }

        [TestMethod]
        public async Task AddFile_WithProgress()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "hello world");
            try
            {
                var ipfs = TestFixture.Ipfs;
                TransferProgress lastProgress = null;
                var options = new AddFileOptions
                {
                    Chunker = "size-3",
                    Progress = new Progress<TransferProgress>(t =>
                    {
                        lastProgress = t;
                    })
                };
                var result = await ipfs.FileSystem.AddFileAsync(path, options);

                // Progress reports get posted on another synchronisation context
                // so they can come in later.
                var stop = DateTime.Now.AddSeconds(3);
                while (DateTime.Now < stop && lastProgress?.Bytes != 11UL)
                {
                    await Task.Delay(10);
                }
                Assert.AreEqual(11UL, lastProgress.Bytes);
                Assert.AreEqual(Path.GetFileName(path), lastProgress.Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task AddDirectory()
        {
            var ipfs = TestFixture.Ipfs;
            var temp = MakeTemp();
            try
            {
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: false);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts))
                {
                    dir = node;
                }

                Assert.IsTrue(dir.IsDirectory);
                var files = dir.Links.ToArray();
                Assert.AreEqual(2, files.Length);
                Assert.AreEqual("alpha.txt", files[0].Name);
                Assert.AreEqual("beta.txt", files[1].Name);

                Assert.AreEqual("alpha", await ipfs.FileSystem.ReadAllTextAsync(files[0].Id));
                Assert.AreEqual("beta", await ipfs.FileSystem.ReadAllTextAsync(files[1].Id));

                Assert.AreEqual("alpha", await ipfs.FileSystem.ReadAllTextAsync(dir.Id + "/alpha.txt"));
                Assert.AreEqual("beta", await ipfs.FileSystem.ReadAllTextAsync(dir.Id + "/beta.txt"));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        public async Task AddDirectoryRecursive()
        {
            var ipfs = TestFixture.Ipfs;
            var temp = MakeTemp();
            try
            {
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: true);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts))
                {
                    dir = node;
                }

                Assert.IsTrue(dir.IsDirectory);
                var files = dir.Links.ToArray();
                Assert.AreEqual(3, files.Length);
                Assert.AreEqual("alpha.txt", files[0].Name);
                Assert.AreEqual("beta.txt", files[1].Name);
                Assert.AreEqual("x", files[2].Name);
                Assert.AreNotEqual(0UL, files[0].Size);
                Assert.AreNotEqual(0UL, files[1].Size);

                var rootFiles = (await ipfs.FileSystem.ListAsync(dir.Id)).Links.ToArray();
                Assert.AreEqual(3, rootFiles.Length);
                Assert.AreEqual("alpha.txt", rootFiles[0].Name);
                Assert.AreEqual("beta.txt", rootFiles[1].Name);
                Assert.AreEqual("x", rootFiles[2].Name);

                var xfiles = (await ipfs.FileSystem.ListAsync(rootFiles[2].Id)).Links.ToArray();
                Assert.AreEqual(2, xfiles.Length);
                Assert.AreEqual("x.txt", xfiles[0].Name);
                Assert.AreEqual("y", xfiles[1].Name);

                var yfiles = (await ipfs.FileSystem.ListAsync(xfiles[1].Id)).Links.ToArray();
                Assert.AreEqual(1, yfiles.Length);
                Assert.AreEqual("y.txt", yfiles[0].Name);

                Assert.AreEqual("x", await ipfs.FileSystem.ReadAllTextAsync(dir.Id + "/x/x.txt"));
                Assert.AreEqual("y", await ipfs.FileSystem.ReadAllTextAsync(dir.Id + "/x/y/y.txt"));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        public async Task AddDirectory_WithHashAlgorithm()
        {
            var ipfs = TestFixture.Ipfs;
            var alg = "keccak-512";
            var options = new AddFileOptions { Hash = alg };
            var temp = MakeTemp();
            try
            {
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: false);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts, options))
                {
                    dir = node;
                }

                Assert.IsTrue(dir.IsDirectory);
                Assert.AreEqual(alg, dir.Id.Hash.Algorithm.Name);

                foreach (var link in dir.Links)
                {
                    Assert.AreEqual(alg, link.Id.Hash.Algorithm.Name);
                }
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        [Ignore("Encoding is no longer available in AddFileOptions")]
        public void AddDirectory_WithCidEncoding()
        {
        }

        [TestMethod]
        public async Task AddDirectoryRecursive_ObjectLinks()
        {
            var ipfs = TestFixture.Ipfs;
            var temp = MakeTemp();
            try
            {
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: true);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts))
                {
                    dir = node;
                }
                Assert.IsTrue(dir.IsDirectory);

                // Walk the tree using ListAsync (replaces removed Object.LinksAsync)
                var cid = dir.Id;
                var i = 0;
                var allLinks = new List<IFileSystemLink>();
                while (cid != null)
                {
                    var listed = await ipfs.FileSystem.ListAsync(cid);
                    allLinks.AddRange(listed.Links);
                    cid = (i < allLinks.Count) ? allLinks[i++].Id : null;
                }

                Assert.AreEqual(6, allLinks.Count);
                Assert.AreEqual("alpha.txt", allLinks[0].Name);
                Assert.AreEqual("beta.txt", allLinks[1].Name);
                Assert.AreEqual("x", allLinks[2].Name);
                Assert.AreEqual("x.txt", allLinks[3].Name);
                Assert.AreEqual("y", allLinks[4].Name);
                Assert.AreEqual("y.txt", allLinks[5].Name);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        [Ignore("https://github.com/richardschneider/net-ipfs-engine/issues/74")]
        public async Task ReadTextFromNetwork()
        {
            var ipfs = TestFixture.Ipfs;
            await ipfs.StartAsync();

            try
            {
                var folder = "QmS4ustL54uo8FzR9455qaxZwuMiUhyvMcX9Ba8nUH4uVv";
                await ipfs.Block.RemoveAsync(folder, true);

                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var text = await ipfs.FileSystem.ReadAllTextAsync($"{folder}/about", cts.Token);
                StringAssert.Contains(text, "IPFS -- Inter-Planetary File system");
            }
            finally
            {
                await ipfs.StopAsync();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task Read_From_OtherNode()
        {
            using (var a = new TempNode())
            using (var b = new TempNode())
            using (var c = new TempNode())
            {
                var psk = new PeerTalk.Cryptography.PreSharedKey().Generate();

                // Start bootstrap node.
                b.Options.Discovery.DisableMdns = true;
                b.Options.Swarm.MinConnections = 0;
                b.Options.Swarm.PrivateNetworkKey = psk;
                b.Options.Discovery.BootstrapPeers = new MultiAddress[0];
                await b.StartAsync();
                var bootstrapPeers = new MultiAddress[]
                {
                    (await b.LocalPeer).Addresses.First()
                };
                Console.WriteLine($"B is {await b.LocalPeer}");

                // Node that has the content.
                c.Options.Discovery.DisableMdns = true;
                c.Options.Swarm.MinConnections = 0;
                c.Options.Swarm.PrivateNetworkKey = psk;
                c.Options.Discovery.BootstrapPeers = bootstrapPeers;
                await c.StartAsync();
                await c.Swarm.ConnectAsync(bootstrapPeers[0]);
                Console.WriteLine($"C is {await c.LocalPeer}");

                var fsn = await c.FileSystem.AddTextAsync("some content");
                var cid = fsn.Id;

                // Node that reads the content.
                a.Options.Discovery.DisableMdns = true;
                a.Options.Swarm.MinConnections = 0;
                a.Options.Swarm.PrivateNetworkKey = psk;
                a.Options.Discovery.BootstrapPeers = bootstrapPeers;
                await a.StartAsync();
                Console.WriteLine($"A is {await a.LocalPeer}");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var content = await a.FileSystem.ReadAllTextAsync(cid, cts.Token);
                Assert.AreEqual("some content", content);
            }
        }

        [TestMethod]
        public async Task GetTar()
        {
            var ipfs = TestFixture.Ipfs;
            var temp = MakeTemp();
            try
            {
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: true);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts))
                {
                    dir = node;
                }
                var dirid = dir.Id.Encode();

                var tar = await ipfs.FileSystem.GetAsync(dir.Id);
                var archive = TarArchive.CreateInputTarArchive(tar);
                var files = new List<string>();
                archive.ProgressMessageEvent += (a, e, m) =>
                {
                    files.Add(e.Name);
                };
                archive.ListContents();

                Assert.AreEqual($"{dirid}", files[0]);
                Assert.AreEqual($"{dirid}/alpha.txt", files[1]);
                Assert.AreEqual($"{dirid}/beta.txt", files[2]);
                Assert.AreEqual($"{dirid}/x", files[3]);
                Assert.AreEqual($"{dirid}/x/x.txt", files[4]);
                Assert.AreEqual($"{dirid}/x/y", files[5]);
                Assert.AreEqual($"{dirid}/x/y/y.txt", files[6]);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        public async Task GetTar_RawLeaves()
        {
            var ipfs = TestFixture.Ipfs;
            var temp = MakeTemp();
            try
            {
                var options = new AddFileOptions { RawLeaves = true };
                var (fileParts, folderParts) = GetPartsFromDirectory(temp, recursive: true);

                IFileSystemNode dir = null;
                await foreach (var node in ipfs.FileSystem.AddAsync(fileParts, folderParts, options))
                {
                    dir = node;
                }
                var dirid = dir.Id.Encode();

                var tar = await ipfs.FileSystem.GetAsync(dir.Id);
                var archive = TarArchive.CreateInputTarArchive(tar);
                var files = new List<string>();
                archive.ProgressMessageEvent += (a, e, m) =>
                {
                    files.Add(e.Name);
                };
                archive.ListContents();

                Assert.AreEqual($"{dirid}", files[0]);
                Assert.AreEqual($"{dirid}/alpha.txt", files[1]);
                Assert.AreEqual($"{dirid}/beta.txt", files[2]);
                Assert.AreEqual($"{dirid}/x", files[3]);
                Assert.AreEqual($"{dirid}/x/x.txt", files[4]);
                Assert.AreEqual($"{dirid}/x/y", files[5]);
                Assert.AreEqual($"{dirid}/x/y/y.txt", files[6]);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [TestMethod]
        public async Task GetTar_EmptyDirectory()
        {
            var ipfs = TestFixture.Ipfs;

            IFileSystemNode dir = null;
            await foreach (var node in ipfs.FileSystem.AddAsync([], []))
            {
                dir = node;
            }

            var tar = await ipfs.FileSystem.GetAsync(dir.Id);
            Assert.AreEqual(3 * 512, tar.Length);
        }

        [TestMethod]
        public async Task Isssue108()
        {
            var ipfs = TestFixture.Ipfs;
            var options = new AddFileOptions
            {
                Hash = "blake2b-256",
                RawLeaves = true
            };
            var node = await ipfs.FileSystem.AddTextAsync("hello world", options);
            var other = await ipfs.FileSystem.ListAsync(node.Id);
            Assert.AreEqual(node.Id, other.Id);
            Assert.AreEqual(node.IsDirectory, other.IsDirectory);
            Assert.AreEqual(node.Size, other.Size);
        }

        [TestMethod]
        public async Task Read_SameFile_DifferentCids()
        {
            var ipfs = TestFixture.Ipfs;
            var text = "\"hello world\" \r\n";
            var node = await ipfs.FileSystem.AddTextAsync(text);
            var cids = new Cid[]
            {
                node.Id,
                new Cid
                {
                    ContentType = node.Id.ContentType,
                    Version = 1,
                    Encoding = node.Id.Encoding,
                    Hash = node.Id.Hash,
                },
                new Cid
                {
                    ContentType = node.Id.ContentType,
                    Version = 1,
                    Encoding = "base32",
                    Hash = node.Id.Hash,
                },
            };
            foreach (var cid in cids)
            {
                using (var cts = new CancellationTokenSource(3000))
                {
                    var got = await ipfs.FileSystem.ReadAllTextAsync(cid, cts.Token);
                    Assert.AreEqual(text, got);
                }
            }
        }

        public static string MakeTemp()
        {
            var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var x = Path.Combine(temp, "x");
            var xy = Path.Combine(x, "y");
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(x);
            Directory.CreateDirectory(xy);

            File.WriteAllText(Path.Combine(temp, "alpha.txt"), "alpha");
            File.WriteAllText(Path.Combine(temp, "beta.txt"), "beta");
            File.WriteAllText(Path.Combine(x, "x.txt"), "x");
            File.WriteAllText(Path.Combine(xy, "y.txt"), "y");
            return temp;
        }

        private static (FilePart[] files, FolderPart[] folders) GetPartsFromDirectory(string path, bool recursive)
        {
            path = Path.GetFullPath(path);
            var fileParts = new List<FilePart>();
            var folderParts = new List<FolderPart>();
            EnumerateParts(path, path, recursive, fileParts, folderParts);
            return (fileParts.ToArray(), folderParts.ToArray());
        }

        private static void EnumerateParts(string basePath, string currentPath, bool recursive, List<FilePart> files, List<FolderPart> folders)
        {
            foreach (var file in Directory.EnumerateFiles(currentPath).OrderBy(s => s))
            {
                var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                files.Add(new FilePart { Name = relativePath, Data = new MemoryStream(File.ReadAllBytes(file)) });
            }

            if (recursive)
            {
                foreach (var dir in Directory.EnumerateDirectories(currentPath).OrderBy(s => s))
                {
                    var relativePath = Path.GetRelativePath(basePath, dir).Replace('\\', '/');
                    folders.Add(new FolderPart { Name = relativePath });
                    EnumerateParts(basePath, dir, recursive, files, folders);
                }
            }
        }
    }
}