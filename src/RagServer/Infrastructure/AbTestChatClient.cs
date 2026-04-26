using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RagServer.Options;

namespace RagServer.Infrastructure;

/// <summary>
/// Wraps two <see cref="IChatClient"/> instances and routes requests round-robin
/// between them when A/B testing is enabled. When disabled, all requests go to
/// clientA. Does not own or dispose the injected clients.
/// </summary>
public sealed class AbTestChatClient(
    IChatClient clientA,
    IChatClient clientB,
    IOptions<AbTestOptions> opts)
    : IChatClient
{
    // long avoids practical overflow; & 1 avoids negative-modulus edge-cases
    private long _counter = -1;

    private IChatClient PickClient() =>
        !opts.Value.Enabled ? clientA :
        ((Interlocked.Increment(ref _counter) & 1) == 0 ? clientA : clientB);

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => PickClient().GetResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => PickClient().GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
        => clientA.GetService(serviceType, serviceKey);

    /// <summary>
    /// Does not dispose the injected clients — the caller owns them.
    /// </summary>
    public void Dispose() { }
}
