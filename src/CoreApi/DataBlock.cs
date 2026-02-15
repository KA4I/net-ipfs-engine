using System.Runtime.Serialization;

namespace Ipfs.Engine.CoreApi;

[DataContract]
internal class DataBlock : IBlockStat
{
    [DataMember]
    public byte[] DataBytes { get; set; } = [];

    public Stream DataStream => new MemoryStream(DataBytes, false);

    [DataMember]
    public required Cid Id { get; set; }

    [DataMember]
    public int Size { get; set; }
}