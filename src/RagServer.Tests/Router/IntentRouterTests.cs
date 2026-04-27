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
    // Conceptual phrasing that previously fell through to Tier 1.5 (entity-name) → Data
    [InlineData("What meaning does currency have in the Reference data area?")]
    [InlineData("Can you elaborate about Counterparty?")]
    [InlineData("Can you be more comprehensive about the trade lifecycle?")]
    [InlineData("What does LEI stand for?")]
    // Inverted question form ("What X is?") — "what the/a/an" triggers Docs before entity-name Tier 1.5
    [InlineData("What the Currency is?")]
    [InlineData("What a counterparty is?")]
    [InlineData("What an LEI is?")]
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
    [InlineData("Do we have Currency and Country in the Catalogue?")]
    [InlineData("Is Currency in the catalog?")]
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

    // ── Context-aware follow-up routing ──────────────────────────────────────
    [Fact]
    public async Task ContextMemory_Elaborate_ContinuesWithPreviousMetadata()
    {
        var router = BuildRouter();
        var result = await router.RouteAsync("Can you elaborate about that?", PipelineKind.Metadata);
        Assert.Equal(PipelineKind.Metadata, result);
    }

    [Fact]
    public async Task ContextMemory_Elaborate_ContinuesWithPreviousData()
    {
        var router = BuildRouter();
        var result = await router.RouteAsync("Please elaborate more", PipelineKind.Data);
        Assert.Equal(PipelineKind.Data, result);
    }

    [Fact]
    public async Task ContextMemory_ConceptualQuestion_IgnoresContext()
    {
        // "meaning" is conceptual, not a follow-up indicator — Docs rule wins regardless of context
        var router = BuildRouter();
        var result = await router.RouteAsync("What meaning does currency have?", PipelineKind.Data);
        Assert.Equal(PipelineKind.Docs, result);
    }

    [Fact]
    public async Task ContextMemory_WhatIs_IgnoresContext()
    {
        // "what is" is always Docs — context cannot override it
        var router = BuildRouter();
        var result = await router.RouteAsync("What is LEI?", PipelineKind.Metadata);
        Assert.Equal(PipelineKind.Docs, result);
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
