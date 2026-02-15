using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi;

internal class PubSubApi(IpfsEngine ipfs) : IPubSubApi
{
    public async Task<IEnumerable<Peer>> PeersAsync(string? topic = null, CancellationToken cancel = default)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        return await pubsub.PeersAsync(topic, cancel).ConfigureAwait(false);
    }

    public async Task PublishAsync(string topic, string message, CancellationToken cancel = default)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        await pubsub.PublishAsync(topic, message, cancel).ConfigureAwait(false);
    }

    public async Task PublishAsync(string topic, byte[] message, CancellationToken cancel = default)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        await pubsub.PublishAsync(topic, message, cancel).ConfigureAwait(false);
    }

    public async Task PublishAsync(string topic, Stream message, CancellationToken cancel = default)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        await pubsub.PublishAsync(topic, message, cancel).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(string topic, Action<IPublishedMessage> handler, CancellationToken cancellationToken)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        await pubsub.SubscribeAsync(topic, handler, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<string>> SubscribedTopicsAsync(CancellationToken cancel = default)
    {
        var pubsub = await ipfs.PubSubService.ConfigureAwait(false);
        return await pubsub.SubscribedTopicsAsync(cancel).ConfigureAwait(false);
    }
}
