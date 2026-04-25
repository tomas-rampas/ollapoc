using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RagServer.Options;

namespace RagServer.Infrastructure;

/// <summary>
/// Serialises all LLM calls through a bounded channel (backpressure = HTTP 429).
/// The consumer forwards both the host-shutdown token and the per-request caller token
/// so that a client disconnect cancels the in-flight LLM call.
/// </summary>
public sealed class LlmRequestQueue : IHostedService
{
    private readonly Channel<WorkItem> _channel;
    private Task? _consumer;

    public LlmRequestQueue(IOptions<RagOptions> opts)
    {
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(opts.Value.QueueMaxDepth)
        {
            // DropWrite: TryWrite returns false when full → caller gets 429 immediately
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken callerCt)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrote = _channel.Writer.TryWrite(new WorkItem(
            async ct2 => (object?)await work(ct2),
            tcs,
            callerCt));

        if (!wrote)
            throw new HttpRequestException("LLM request queue is full", null, HttpStatusCode.TooManyRequests);

        // WaitAsync(callerCt) lets the caller cancel waiting even while the item is queued
        return (T)(await tcs.Task.WaitAsync(callerCt))!;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _consumer = ConsumeAsync(ct);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _channel.Writer.Complete();
        if (_consumer is not null)
            // Respect the host shutdown grace-period token
            await _consumer.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task ConsumeAsync(CancellationToken hostCt)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(hostCt))
        {
            // Caller cancelled while the item was sitting in the queue — discard without executing.
            // Check CallerCt (observable without TCS cooperation) rather than Tcs.Task.IsCanceled
            // which is never set by the enqueuer.
            if (item.CallerCt.IsCancellationRequested)
            {
                item.Tcs.TrySetCanceled(item.CallerCt);
                continue;
            }

            // Link host-shutdown token with per-request caller token so either cancels the work
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostCt, item.CallerCt);
            try
            {
                item.Tcs.SetResult(await item.Work(linked.Token));
            }
            catch (Exception ex)
            {
                item.Tcs.SetException(ex);
            }
        }
    }

    private sealed record WorkItem(
        Func<CancellationToken, Task<object?>> Work,
        TaskCompletionSource<object?> Tcs,
        CancellationToken CallerCt);
}
