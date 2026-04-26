using Microsoft.Extensions.AI;
using Moq;
using RagServer.Infrastructure;
using RagServer.Options;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Infrastructure;

public class AbTestChatClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="Mock{IChatClient}"/> whose <c>GetResponseAsync</c> returns a
    /// <see cref="ChatResponse"/> whose text equals <paramref name="identity"/>.
    /// This lets tests assert which underlying client was routed to.
    /// </summary>
    private static Mock<IChatClient> BuildMockClient(string identity)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, identity)));

        // Stub GetStreamingResponseAsync to return an empty async enumerable
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ChatResponseUpdate>());

        return mock;
    }

    /// <summary>
    /// Creates two mocks (A="client-a", B="client-b") and wraps them in an
    /// <see cref="AbTestChatClient"/> configured with the given <paramref name="enabled"/> flag.
    /// </summary>
    private static (AbTestChatClient Client, Mock<IChatClient> MockA, Mock<IChatClient> MockB)
        BuildAbTestClient(bool enabled)
    {
        var mockA = BuildMockClient("client-a");
        var mockB = BuildMockClient("client-b");

        var options = Create(new AbTestOptions
        {
            Enabled = enabled,
            ModelA  = "qwen3:8b",
            ModelB  = "qwen3:14b"
        });

        var client = new AbTestChatClient(mockA.Object, mockB.Object, options);
        return (client, mockA, mockB);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Given_AbTestDisabled_When_CalledTwice_Then_AlwaysRoutesToClientA()
    {
        var (client, mockA, mockB) = BuildAbTestClient(enabled: false);
        var messages = Array.Empty<ChatMessage>();

        var r1 = await client.GetResponseAsync(messages);
        var r2 = await client.GetResponseAsync(messages);

        Assert.Equal("client-a", r1.Text);
        Assert.Equal("client-a", r2.Text);

        // clientB must never be called
        mockB.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_AbTestEnabled_When_CalledTwice_Then_RoutesAltAB()
    {
        var (client, _, _) = BuildAbTestClient(enabled: true);
        var messages = Array.Empty<ChatMessage>();

        var r1 = await client.GetResponseAsync(messages);
        var r2 = await client.GetResponseAsync(messages);

        // First call → counter goes 0 (even) → clientA; second → 1 (odd) → clientB
        Assert.Equal("client-a", r1.Text);
        Assert.Equal("client-b", r2.Text);
    }

    [Fact]
    public async Task Given_AbTestEnabled_When_CalledThreeTimes_Then_RoutesABAPattern()
    {
        var (client, _, _) = BuildAbTestClient(enabled: true);
        var messages = Array.Empty<ChatMessage>();

        var r1 = await client.GetResponseAsync(messages);
        var r2 = await client.GetResponseAsync(messages);
        var r3 = await client.GetResponseAsync(messages);

        // A → B → A
        Assert.Equal("client-a", r1.Text);
        Assert.Equal("client-b", r2.Text);
        Assert.Equal("client-a", r3.Text);
    }

    [Fact]
    public async Task Given_AbTestDisabled_When_GetStreamingResponse_Then_DelegatesToClientA()
    {
        var (client, mockA, mockB) = BuildAbTestClient(enabled: false);
        var messages = Array.Empty<ChatMessage>();

        // Consume the streaming response (even if empty) to trigger the call
        await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }

        mockA.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockB.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_AbTestEnabled_When_Enabled_Then_ClientBReceivesCall()
    {
        var (client, mockA, mockB) = BuildAbTestClient(enabled: true);
        var messages = Array.Empty<ChatMessage>();

        // First call → clientA (even counter)
        await client.GetResponseAsync(messages);
        // Second call → clientB (odd counter)
        await client.GetResponseAsync(messages);

        mockB.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AbTestEnabled_WhenCalledFourTimes_ThenTwoCallsToEachClient()
    {
        var (client, mockA, mockB) = BuildAbTestClient(enabled: true);
        var messages = Array.Empty<ChatMessage>();

        for (var i = 0; i < 4; i++)
            await client.GetResponseAsync(messages);

        mockA.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        mockB.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
