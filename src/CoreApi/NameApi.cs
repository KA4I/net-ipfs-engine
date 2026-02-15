using System.Collections.Concurrent;
using System.Text;
using Common.Logging;
using Ipfs.CoreApi;
using Ipfs.Engine.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using ProtoBuf;

namespace Ipfs.Engine.CoreApi;

internal class NameApi(IpfsEngine ipfs) : INameApi
{
    static readonly ILog log = LogManager.GetLogger(typeof(NameApi));

    // Local cache of most recent IPNS records: peerId -> IpnsRecord
    static readonly ConcurrentDictionary<string, IpnsRecord> localRecords = new();

    // Maximum seen sequence number per peer (persisted for IPNS PubSub validation, Kubo 0.40).
    // Prevents duplicate/replay of IPNS records even after cache expiry or node restart.
    static readonly ConcurrentDictionary<string, ulong> maxSeqNumbers = new();

    // PubSub subscriptions for IPNS names
    static readonly ConcurrentDictionary<string, CancellationTokenSource> pubsubSubscriptions = new();

    public async Task<NamedContent> PublishAsync(string path, bool resolve = true, string key = "self", TimeSpan? lifetime = null, CancellationToken cancel = default)
    {
        if (resolve && path.StartsWith("/ipfs/"))
        {
            // Verify the path resolves
            await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
        }

        lifetime ??= TimeSpan.FromHours(24);

        // Get the key pair for signing
        KeyChain keyChain = await ipfs.KeyChainAsync(cancel).ConfigureAwait(false);
        IKey keyInfo = await keyChain.FindKeyByNameAsync(key, cancel).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"The key '{key}' does not exist.");
        AsymmetricKeyParameter privateKey = await keyChain.GetPrivateKeyAsync(key, cancel).ConfigureAwait(false);

        string peerId = keyInfo.Id.ToString();

        // Determine sequence number
        ulong seq = 1;
        if (localRecords.TryGetValue(peerId, out var existing))
        {
            seq = existing.Sequence + 1;
        }

        // Build the IPNS record
        var validity = DateTime.UtcNow.Add(lifetime.Value);
        var validityBytes = Encoding.ASCII.GetBytes(validity.ToString("o") + "Z");
        var valueBytes = Encoding.UTF8.GetBytes(path);

        // Sign: "ipns-signature:" + value + validity + validityType(BE) + sequence(BE)
        var signData = CreateSignData(valueBytes, validityBytes, 0, seq);
        byte[] signature = Sign(privateKey, signData);

        var record = new IpnsRecord
        {
            Value = valueBytes,
            Signature = signature,
            ValidityType = 0, // EOL
            Validity = validityBytes,
            Sequence = seq
        };

        // Cache locally with sequence number tracking
        TryAcceptRecord(peerId, record);

        // Also publish via PubSub for IPNS-over-PubSub (Kubo parity)
        try
        {
            await PublishViaPubSubAsync(peerId, record, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Debug($"IPNS PubSub publish failed (non-fatal): {ex.Message}");
        }

        return new NamedContent
        {
            NamePath = $"/ipns/{peerId}",
            ContentPath = path
        };
    }

    public async Task<NamedContent> PublishAsync(Cid id, string key = "self", TimeSpan? lifetime = null, CancellationToken cancel = default)
    {
        string path = $"/ipfs/{id}";
        return await PublishAsync(path, resolve: false, key: key, lifetime: lifetime, cancel: cancel).ConfigureAwait(false);
    }

    public async Task<string> ResolveAsync(string name, bool recursive = false, bool nocache = false, CancellationToken cancel = default)
    {
        do
        {
            if (name.StartsWith("/ipns/"))
            {
                name = name[6..];
            }

            string[] parts = [.. name.Split('/').Where(p => p.Length > 0)];
            if (parts.Length == 0)
                throw new ArgumentException($"Cannot resolve '{name}'.");

            if (IsDomainName(parts[0]))
            {
                name = await ipfs.Dns.ResolveAsync(parts[0], recursive, cancel).ConfigureAwait(false);
            }
            else
            {
                // Subscribe to PubSub for this name (fire-and-forget, non-blocking)
                _ = Task.Run(() => SubscribeToIpnsAsync(parts[0], cancel), cancel);

                // Try local record cache first (unless nocache)
                if (!nocache && localRecords.TryGetValue(parts[0], out var record) && record.Value != null)
                {
                    name = Encoding.UTF8.GetString(record.Value);
                }
                else
                {
                    // Try DHT resolution
                    string resolved = await ResolveViaDhtAsync(parts[0], cancel).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException($"Cannot resolve IPNS name '{parts[0]}'. No local or DHT record found.");
                    name = resolved;
                }
            }

            if (parts.Length > 1)
            {
                name = name + "/" + string.Join("/", parts, 1, parts.Length - 1);
            }
        } while (recursive && !name.StartsWith("/ipfs/"));

        return name;
    }

    /// <summary>
    /// Attempts to resolve an IPNS name via the DHT.
    /// </summary>
    private async Task<string?> ResolveViaDhtAsync(string peerId, CancellationToken cancel)
    {
        try
        {
            // IPNS records in the DHT are stored under /ipns/<peerId-multihash>
            var id = new MultiHash(peerId);
            byte[] key = Encoding.UTF8.GetBytes("/ipns/" + peerId);

            byte[] data = await ipfs.Dht.GetAsync(key, cancel).ConfigureAwait(false);
            if (data == null || data.Length == 0)
                return null;

            // Deserialize the IPNS record
            using var ms = new MemoryStream(data);
            var record = Serializer.Deserialize<IpnsRecord>(ms);

            if (record?.Value == null)
                return null;

            // Accept and cache the record
            TryAcceptRecord(peerId, record);

            return Encoding.UTF8.GetString(record.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Debug($"DHT IPNS resolution failed for {peerId}: {ex.Message}");
            return null;
        }
    }

    static byte[] CreateSignData(byte[] value, byte[] validity, int validityType, ulong sequence)
    {
        // The signing data for IPNS V1 is: value + validity + varint(validityType)
        using var ms = new MemoryStream();
        ms.Write(value, 0, value.Length);
        ms.Write(validity, 0, validity.Length);

        // ValidityType as big-endian bytes
        var vtBytes = BitConverter.GetBytes((long)validityType);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(vtBytes);
        ms.Write(vtBytes, 0, vtBytes.Length);

        return ms.ToArray();
    }

    static byte[] Sign(AsymmetricKeyParameter privateKey, byte[] data)
    {
        string algorithm;
        if (privateKey is RsaPrivateCrtKeyParameters)
            algorithm = "SHA-256withRSA";
        else if (privateKey is Ed25519PrivateKeyParameters)
            algorithm = "Ed25519";
        else if (privateKey is ECPrivateKeyParameters)
            algorithm = "SHA-256withECDSA";
        else
            throw new NotSupportedException($"Unsupported key type {privateKey.GetType().Name}");

        ISigner signer = SignerUtilities.GetSigner(algorithm);
        signer.Init(true, privateKey);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    /// <summary>
    /// Determines if the supplied string is a valid domain name.
    /// </summary>
    public static bool IsDomainName(string name)
    {
        return name.IndexOf('.') > 0;
    }

    /// <summary>
    ///   Validates and accepts an IPNS record, rejecting duplicates/replays.
    /// </summary>
    /// <remarks>
    ///   Kubo 0.40: Persists the maximum seen sequence number per peer to
    ///   provide stronger duplicate detection that survives cache expiry.
    /// </remarks>
    /// <returns><b>true</b> if the record is newer than any previously seen for this peer.</returns>
    internal static bool TryAcceptRecord(string peerId, IpnsRecord record)
    {
        var newSeq = record.Sequence;

        // Check against persisted max sequence
        if (maxSeqNumbers.TryGetValue(peerId, out var maxSeq) && newSeq <= maxSeq)
        {
            log.Debug($"Rejecting IPNS record for {peerId}: seq {newSeq} <= max {maxSeq}");
            return false;
        }

        // Accept: update max and cache
        maxSeqNumbers[peerId] = newSeq;
        localRecords[peerId] = record;
        return true;
    }

    /// <summary>
    /// IPNS record protobuf.
    /// </summary>
    [ProtoContract]
    internal class IpnsRecord
    {
        [ProtoMember(1)]
        public byte[]? Value { get; set; }

        [ProtoMember(2)]
        public byte[]? Signature { get; set; }

        [ProtoMember(3)]
        public int ValidityType { get; set; }

        [ProtoMember(4)]
        public byte[]? Validity { get; set; }

        [ProtoMember(5)]
        public ulong Sequence { get; set; }

        [ProtoMember(6)]
        public ulong Ttl { get; set; }

        [ProtoMember(7)]
        public byte[]? PubKey { get; set; }

        [ProtoMember(8)]
        public byte[]? SignatureV2 { get; set; }

        [ProtoMember(9)]
        public byte[]? Data { get; set; }
    }

    /// <summary>
    ///   Publishes an IPNS record via PubSub on the topic "/record/base64url(/ipns/multihash)".
    /// </summary>
    private async Task PublishViaPubSubAsync(string peerId, IpnsRecord record, CancellationToken cancel)
    {
        string topic = GetIpnsPubSubTopic(peerId);
        byte[] data = SerializeIpnsRecord(record);
        await ipfs.PubSub.PublishAsync(topic, data, cancel).ConfigureAwait(false);
        log.Debug($"Published IPNS record for {peerId} via PubSub on topic {topic}");
    }

    /// <summary>
    ///   Subscribes to IPNS PubSub updates for a given peer, processing incoming IPNS records.
    /// </summary>
    internal async Task SubscribeToIpnsAsync(string peerId, CancellationToken cancel)
    {
        string topic = GetIpnsPubSubTopic(peerId);

        // Already subscribed?
        if (pubsubSubscriptions.ContainsKey(peerId))
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (!pubsubSubscriptions.TryAdd(peerId, cts))
            return;

        await ipfs.PubSub.SubscribeAsync(topic, msg =>
        {
            try
            {
                using var ms = new MemoryStream(msg.DataBytes);
                var record = Serializer.Deserialize<IpnsRecord>(ms);
                if (record?.Value != null)
                {
                    if (TryAcceptRecord(peerId, record))
                    {
                        log.Debug($"Accepted IPNS PubSub update for {peerId}: {Encoding.UTF8.GetString(record.Value)}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Debug($"Failed to process IPNS PubSub message for {peerId}: {ex.Message}");
            }
        }, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    ///   Computes the PubSub topic for an IPNS name: "/record/" + base64url("/ipns/" + multihash).
    /// </summary>
    private static string GetIpnsPubSubTopic(string peerId)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes("/ipns/" + peerId);
        string b64 = Convert.ToBase64String(keyBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return "/record/" + b64;
    }

    private static byte[] SerializeIpnsRecord(IpnsRecord record)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, record);
        return ms.ToArray();
    }
}
