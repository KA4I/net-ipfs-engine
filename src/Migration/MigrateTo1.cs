namespace Ipfs.Engine.Migration;

internal class MigrateTo1 : IMigration
{
    private class Pin1
    {
        public required Cid Id { get; set; }
    }

    public int Version => 1;

    public bool CanUpgrade => true;

    public bool CanDowngrade => true;

    public async Task DowngradeAsync(IpfsEngine ipfs, CancellationToken cancel = default)
    {
        string path = Path.Combine(ipfs.Options.Repository.Folder, "pins");
        DirectoryInfo folder = new(path);
        if (!folder.Exists)
        {
            return;
        }

        FileStore<Cid, Pin1> store = new()
        {
            Folder = path,
            NameToKey = (cid) => cid.Hash.ToBase32(),
            KeyToName = (key) => new MultiHash(key.FromBase32())
        };

        var files = folder.EnumerateFiles().Where(fi => fi.Length != 0);
        foreach (FileInfo fi in files)
        {
            try
            {
                var name = store.KeyToName(fi.Name);
                var pin = await store.GetAsync(name, cancel).ConfigureAwait(false);
                await using (File.Create(Path.Combine(store.Folder, pin.Id))) { }
                File.Delete(store.GetPath(name));
            }
            catch
            {
                // Migration best-effort; skip corrupt entries.
            }
        }
    }

    public async Task UpgradeAsync(IpfsEngine ipfs, CancellationToken cancel = default)
    {
        string path = Path.Combine(ipfs.Options.Repository.Folder, "pins");
        DirectoryInfo folder = new(path);
        if (!folder.Exists)
        {
            return;
        }

        FileStore<Cid, Pin1> store = new()
        {
            Folder = path,
            NameToKey = (cid) => cid.Hash.ToBase32(),
            KeyToName = (key) => new MultiHash(key.FromBase32())
        };

        var files = folder.EnumerateFiles().Where(fi => fi.Length == 0);
        foreach (FileInfo fi in files)
        {
            try
            {
                Cid cid = Cid.Decode(fi.Name);
                await store.PutAsync(cid, new Pin1 { Id = cid }, cancel).ConfigureAwait(false);
                File.Delete(fi.FullName);
            }
            catch
            {
                // Migration best-effort; skip corrupt entries.
            }
        }
    }
}
