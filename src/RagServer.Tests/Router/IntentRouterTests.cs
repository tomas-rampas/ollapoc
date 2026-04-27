using Microsoft.Extensions.AI;
using Moq;
using RagServer.Options;
using RagServer.Router;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Router;

public class IntentRouterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────
    private static IntentRouter BuildRouter(string? modelReply = "docs")
    {
        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, modelReply ?? "docs")));
        return new IntentRouter(mockClient.Object, Create(new RagOptions()));
    }

    // ── Rule-based: Docs ──────────────────────────────────────────────────────
    [Theory]
    [InlineData("What is a counterparty?")]
    [InlineData("How does the ISDA agreement work?")]
    [InlineData("What are the onboarding steps?")]
    [InlineData("Explain the netting process")]
    [InlineData("Describe the risk framework")]
    [InlineData("What is the purpose of the trade lifecycle?")]
    public async Task RuleBased_Docs_Queries(string query)
    {
        var router = BuildRouter();
        var result = await router.RouteAsync(query);
        Assert.Equal(PipelineKind.Docs, result);
    }

    // ── Rule-based: Metadata ──────────────────────────────────────────────────
    [Theory]
    [InlineData("What attributes does a Trade entity have?")]
    [InlineData("List the fields on the Counterparty schema")]
    [InlineData("Show me the columns for the Position entity")]
    [InlineData("What are the critical data elements for risk reporting?")]
    [InlineData("What metadata does a Contract entity expose?")]
    public async Task RuleBased_Metadata_Queries(string query)
    {
        var router = BuildRouter();
        var result = await router.RouteAsync(query);
        Assert.Equal(PipelineKind.Metadata, result);
    }

    // ── Rule-based: Data ──────────────────────────────────────────────────────
    [Theory]
    [InlineData("Give me all open trades for counterparty 42")]
    [InlineData("Give me some counterparties")]
    [InlineData("Give me any active positions")]
    [InlineData("List all positions with notional above 1M")]
    [InlineData("Show me the records for desk FX in Q1")]
    [InlineData("Find all trades booked yesterday")]
    [InlineData("Count of contracts by status")]
    // Conditional / filtered phrasing (previously routed to Docs)
    [InlineData("Can you give UK counterparties?")]
    [InlineData("I meant those counterparties which incorporation_country equals to GB")]
    [InlineData("Can you provide detail of Barclays Plc?")]
    [InlineData("Active books")]
    [InlineData("UK settlements")]
    [InlineData("Tell me about the counterparties from France")]
    public async Task RuleBased_Data_Queries(string query)
    {
        var router = BuildRouter();
        var result = await router.RouteAsync(query);
        Assert.Equal(PipelineKind.Data, result);
    }

    // ── Model fallback ────────────────────────────────────────────────────────
    [Fact]
    public async Task ModelFallback_ReturnsMetadata_WhenModelSaysMetadata()
    {
        var router = BuildRouter("metadata");
        var result = await router.RouteAsync("Tell me something about XYZ");
        Assert.Equal(PipelineKind.Metadata, result);
    }

    [Fact]
    public async Task ModelFallback_ReturnsData_WhenModelSaysData()
    {
        var router = BuildRouter("data");
        var result = await router.RouteAsync("XYZ 789 bespoke");
        Assert.Equal(PipelineKind.Data, result);
    }

    [Fact]
    public async Task ModelFallback_DefaultsDocs_WhenModelReturnsUnknown()
    {
        var router = BuildRouter("unknown_garbage");
        var result = await router.RouteAsync("something completely unrecognized");
        Assert.Equal(PipelineKind.Docs, result);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Cache_ReturnsSameResultOnSecondCall_WithoutCallingModelAgain()
    {
        var callCount = 0;
        var mockClient = new Mock<IChatClient>();
        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "data"));
            });

        var router = new IntentRouter(mockClient.Object, Create(new RagOptions()));
        const string query = "some unique unmatched query 12345";

        var r1 = await router.RouteAsync(query);
        var r2 = await router.RouteAsync(query);

        Assert.Equal(PipelineKind.Data, r1);
        Assert.Equal(PipelineKind.Data, r2);
        Assert.Equal(1, callCount); // model called only once
    }

    // ── Edge cases ────────────────────────────────────────────────────────────
    [Fact]
    public async Task EmptyQuery_DefaultsToDocsViaCaseFallback()
    {
        var router = BuildRouter("docs");
        var result = await router.RouteAsync("");
        Assert.Equal(PipelineKind.Docs, result);
    }
}
