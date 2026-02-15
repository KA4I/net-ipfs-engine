using Ipfs.CoreApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ipfs.Engine.CoreApi;

internal class ConfigApi(IpfsEngine ipfs) : IConfigApi
{
    private static readonly JObject defaultConfiguration = JObject.Parse("""
    {
      "Addresses": {
        "API": "/ip4/127.0.0.1/tcp/5001",
        "Gateway": "/ip4/127.0.0.1/tcp/8080",
        "Swarm": [
          "/ip4/0.0.0.0/tcp/4001",
          "/ip6/::/tcp/4001"
        ]
      }
    }
    """);

    private JObject? configuration;

    public async Task<JObject> GetAsync(CancellationToken cancel = default)
    {
        // If first time, load the configuration into memory.
        if (configuration is null)
        {
            string path = Path.Combine(ipfs.Options.Repository.ExistingFolder(), "config");
            if (File.Exists(path))
            {
                using var reader = File.OpenText(path);
                using var jtr = new JsonTextReader(reader);
                configuration = await JObject.LoadAsync(jtr).ConfigureAwait(false);
            }
            else
            {
                await ReplaceAsync(defaultConfiguration).ConfigureAwait(false);
            }
        }

        return configuration!;
    }

    public async Task<JToken> GetAsync(string key, CancellationToken cancel = default)
    {
        JToken? config = await GetAsync(cancel).ConfigureAwait(false);
        var keys = key.Split('.');
        foreach (var name in keys)
        {
            config = ((JObject?)config)?[name];
            if (config == null)
                throw new KeyNotFoundException($"Configuration setting '{key}' does not exist.");
        }
        return config;
    }

    public Task ReplaceAsync(JObject config)
    {
        ArgumentNullException.ThrowIfNull(config);
        configuration = config;
        return SaveAsync();
    }

    public Task SetAsync(string key, string value, CancellationToken cancel = default)
    {
        return SetAsync(key, JToken.FromObject(value), cancel);
    }

    public async Task SetAsync(string key, JToken value, CancellationToken cancel = default)
    {
        var config = await GetAsync(cancel).ConfigureAwait(false);

        // If needed, create the setting owner keys.
        var keys = key.Split('.');
        foreach (var name in keys.Take(keys.Length - 1))
        {
            var token = config[name] as JObject;
            if (token is null)
            {
                token = new JObject();
                config[name] = token;
            }
            config = token;
        }

        config[keys.Last()] = value;
        await SaveAsync().ConfigureAwait(false);
    }

    private async Task SaveAsync()
    {
        string path = Path.Combine(ipfs.Options.Repository.Folder, "config");
        using var fs = File.OpenWrite(path);
        using var writer = new StreamWriter(fs);
        using var jtw = new JsonTextWriter(writer) { Formatting = Formatting.Indented };
        await configuration!.WriteToAsync(jtw).ConfigureAwait(false);
    }
}
