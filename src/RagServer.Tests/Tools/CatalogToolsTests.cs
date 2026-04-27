using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Tools;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Tools;

public class CatalogToolsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CatalogDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"catalogtools-{Guid.NewGuid()}")
            .Options;
        var db = new CatalogDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    private static Mock<IEmbeddingGenerator<string, Embedding<float>>> BuildEmbeddingMock()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[384]))]));
        return mock;
    }

    private static CatalogTools BuildTools(
        CatalogDbContext db,
        IMongoExtensionRepository? mongo = null,
        Mock<IEmbeddingGenerator<string, Embedding<float>>>? embeddingMock = null)
    {
        var embMock = embeddingMock ?? BuildEmbeddingMock();
        var es = new ElasticsearchClient(new ElasticsearchClientSettings(new Uri("http://localhost:19999")));
        var mongoRepo = mongo ?? new NullMongoExtensionRepository();
        var rulesRepo = new NullBusinessRulesRepository();
        var opts = Create(new RagOptions());
        return new CatalogTools(db, embMock.Object, es, mongoRepo, rulesRepo, opts,
            NullLogger<CatalogTools>.Instance);
    }

    // ── ResolveEntityAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveEntityAsync_EsUnavailable_ReturnsNull()
    {
        // Given ES is unreachable and an embedding mock that returns a zero vector
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When resolving an entity name
        var result = await tools.ResolveEntityAsync("trade", CancellationToken.None);

        // Then null is returned without throwing
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEntityAsync_CallsEmbeddingGenerator_WithInputName()
    {
        // Given an embedding mock that captures its inputs
        using var db = BuildDb();
        string? capturedInput = null;
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<string> inputs, EmbeddingGenerationOptions? _, CancellationToken _) =>
            {
                capturedInput = inputs.FirstOrDefault();
            })
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[384]))]));

        var tools = BuildTools(db, embeddingMock: mock);

        // When resolving "counterparty"
        await tools.ResolveEntityAsync("counterparty", CancellationToken.None);

        // Then GenerateAsync was called once with that exact name
        mock.Verify(e => e.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("counterparty", capturedInput);
    }

    // ── GetEntityAttributesAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetEntityAttributes_GivenLocationEntity_ReturnsElevenAttributes()
    {
        // Given the seeded database has 11 Location attributes (Ids 179-189)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching attributes for Location
        var result = await tools.GetEntityAttributesAsync("Location", ct: CancellationToken.None);

        // Then 11 attributes are returned
        Assert.Equal(11, result.Count);
    }

    [Fact]
    public async Task GetEntityAttributes_GivenLocationEntity_ContainsExpectedAttributeNames()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching attributes for Location
        var result = await tools.GetEntityAttributesAsync("Location", ct: CancellationToken.None);

        // Then the known attribute names are present
        var names = result.Select(a => a.Name).ToHashSet();
        Assert.Contains("LocationId",   names);
        Assert.Contains("LocationName", names);
        Assert.Contains("City",         names);
        Assert.Contains("Postcode",     names);
        Assert.Contains("Country",      names);
        Assert.Contains("Status",       names);
        Assert.Contains("BusinessType", names);
    }

    [Fact]
    public async Task GetEntityAttributes_GivenUnknownEntity_ReturnsEmpty()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching attributes for a non-existent entity
        var result = await tools.GetEntityAttributesAsync("NonExistentEntity", ct: CancellationToken.None);

        // Then an empty list is returned
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEntityAttributes_GivenSettlementEntity_ReturnsThreeAttributes()
    {
        // Given the seeded database has 3 Settlement attributes (Ids 8-10)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching attributes for Settlement
        var result = await tools.GetEntityAttributesAsync("Settlement", ct: CancellationToken.None);

        // Then exactly 3 attributes are returned
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetEntityAttributes_GivenSettlementEntity_ContainsExpectedAttributeNames()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching Settlement attributes
        var result = await tools.GetEntityAttributesAsync("Settlement", ct: CancellationToken.None);

        // Then settlement-specific attribute names are present
        var names = result.Select(a => a.Name).ToHashSet();
        Assert.Contains("SettlementId",   names);
        Assert.Contains("SettlementDate", names);
        Assert.Contains("Amount",         names);
    }

    [Fact]
    public async Task GetEntityAttributes_ReturnsCorrectDataTypes()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching Location attributes
        var result = await tools.GetEntityAttributesAsync("Location", ct: CancellationToken.None);

        // Then LocationId has datatype string and IsNullable = false
        var locationId = result.Single(a => a.Name == "LocationId");
        Assert.Equal("string", locationId.DataType);
        Assert.False(locationId.IsNullable);

        // And City has datatype string and IsNullable = true
        var city = result.Single(a => a.Name == "City");
        Assert.Equal("string", city.DataType);
        Assert.True(city.IsNullable);
    }

    // ── GetEntityExtensionsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetEntityExtensions_GivenNullRepository_ReturnsEmpty()
    {
        // Given a NullMongoExtensionRepository (returns empty for any entity)
        using var db = BuildDb();
        var tools = BuildTools(db, mongo: new NullMongoExtensionRepository());

        // When fetching extensions for any entity
        var result = await tools.GetEntityExtensionsAsync("Trade", CancellationToken.None);

        // Then an empty list is returned
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEntityExtensions_DelegatesToRepository()
    {
        // Given a mock repository configured to return known extensions
        using var db = BuildDb();
        var expectedExtensions = new List<ExtensionAttribute>
        {
            new("riskCategory", "HIGH", "Internal risk classification"),
            new("dataLineage",  "SourceSystem.Trade", null)
        };

        var mongoMock = new Mock<IMongoExtensionRepository>();
        mongoMock.Setup(r => r.GetExtensionsAsync("Trade", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedExtensions);

        var tools = BuildTools(db, mongo: mongoMock.Object);

        // When fetching extensions for Trade
        var result = await tools.GetEntityExtensionsAsync("Trade", CancellationToken.None);

        // Then the repository is called once with the correct entity name
        mongoMock.Verify(r => r.GetExtensionsAsync("Trade", It.IsAny<CancellationToken>()), Times.Once);

        // And the returned extensions match what the repository provided
        Assert.Equal(2, result.Count);
        Assert.Equal("riskCategory", result[0].Name);
        Assert.Equal("HIGH",         result[0].Value);
        Assert.Equal("dataLineage",  result[1].Name);
    }

    [Fact]
    public async Task GetEntityExtensions_PassesEntityNameToRepository()
    {
        // Given a mock repository that captures the entity name
        using var db = BuildDb();
        string? capturedEntityName = null;

        var mongoMock = new Mock<IMongoExtensionRepository>();
        mongoMock.Setup(r => r.GetExtensionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string name, CancellationToken _) => capturedEntityName = name)
            .ReturnsAsync(new List<ExtensionAttribute>());

        var tools = BuildTools(db, mongo: mongoMock.Object);

        // When fetching extensions for Counterparty
        await tools.GetEntityExtensionsAsync("Counterparty", CancellationToken.None);

        // Then the repository received exactly "Counterparty"
        Assert.Equal("Counterparty", capturedEntityName);
    }

    // ── ListCDEAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListCDE_GivenNullFilter_ReturnsAllSixteenCDEs()
    {
        // Given the seeded database has 16 CDEs total (Trade CDEs removed in Sprint 8)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When listing all CDEs with no filter
        var result = await tools.ListCDEAsync(null, CancellationToken.None);

        // Then all 16 CDEs are returned
        Assert.Equal(16, result.Count);
    }

    [Fact]
    public async Task ListCDE_GivenEmptyStringFilter_ReturnsAllSixteenCDEs()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When listing with an empty string (treated as no filter)
        var result = await tools.ListCDEAsync("", CancellationToken.None);

        // Then all 16 CDEs are returned (whitespace guard in implementation)
        Assert.Equal(16, result.Count);
    }

    [Fact]
    public async Task ListCDE_GivenCounterpartyFilter_ReturnsThreeCDEs()
    {
        // Given the seeded database has 3 Counterparty CDEs (Lei, LegalName, FatcaStatus)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When listing CDEs filtered to Counterparty
        var result = await tools.ListCDEAsync("Counterparty", CancellationToken.None);

        // Then exactly 3 CDEs are returned
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListCDE_GivenCounterpartyFilter_ContainsExpectedCdeNames()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When listing Counterparty CDEs
        var result = await tools.ListCDEAsync("Counterparty", CancellationToken.None);

        // Then the known Counterparty CDE names are present
        var names = result.Select(c => c.Name).ToHashSet();
        Assert.Contains("Lei",         names);
        Assert.Contains("LegalName",   names);
        Assert.Contains("FatcaStatus", names);
    }

    [Fact]
    public async Task ListCDE_GivenSettlementFilter_ReturnsOneCDE()
    {
        // Given the seeded database has 1 Settlement CDE (Id 5)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When listing Settlement CDEs
        var result = await tools.ListCDEAsync("Settlement", CancellationToken.None);

        // Then exactly 1 CDE is returned
        Assert.Single(result);
        Assert.Equal("SettlementDate", result[0].Name);
        Assert.Equal("Operations",     result[0].GovernanceOwner);
    }

    [Fact]
    public async Task ListCDE_GivenUnknownEntity_ReturnsEmpty()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When filtering by an entity that has no CDEs
        var result = await tools.ListCDEAsync("Portfolio", CancellationToken.None);

        // Then an empty list is returned
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListCDE_IncludesRegulatoryReferences()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching Counterparty CDEs
        var result = await tools.ListCDEAsync("Counterparty", CancellationToken.None);

        // Then regulatory references are populated for GLEIF CDEs
        var legalName = result.Single(c => c.Name == "LegalName");
        Assert.Equal("GLEIF", legalName.RegulatoryReference);

        var lei = result.Single(c => c.Name == "Lei");
        Assert.Equal("ISO 17442 / GLEIF", lei.RegulatoryReference);
    }

    // ── GetEntityRelationshipsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetEntityRelationships_GivenLocation_ReturnsThreeRelationships()
    {
        // Given the seeded database has 3 relationships where Location is the source
        // (locatedInRegion, classifiedBySic, classifiedByNace)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for Location
        var result = await tools.GetEntityRelationshipsAsync("Location", CancellationToken.None);

        // Then exactly 3 relationships are returned
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetEntityRelationships_GivenLocation_ContainsExpectedRelationshipTypes()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for Location
        var result = await tools.GetEntityRelationshipsAsync("Location", CancellationToken.None);

        // Then all three known relationship types are present
        var types = result.Select(r => r.RelationshipType).ToHashSet();
        Assert.Contains("locatedInRegion",  types);
        Assert.Contains("classifiedBySic",  types);
        Assert.Contains("classifiedByNace", types);
    }

    [Fact]
    public async Task GetEntityRelationships_GivenCounterparty_ReturnsTwoRelationships()
    {
        // Given the seeded database:
        // Counterparty→hasAccount→ClientAccount (source), SettlementInstruction→issuedForCounterparty→Counterparty (target)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for Counterparty (source + target)
        var result = await tools.GetEntityRelationshipsAsync("Counterparty", CancellationToken.None);

        // Then 2 relationships are returned
        Assert.Equal(2, result.Count);
        var types = result.Select(r => r.RelationshipType).ToHashSet();
        Assert.Contains("hasAccount",            types);
        Assert.Contains("issuedForCounterparty", types);
    }

    [Fact]
    public async Task GetEntityRelationships_GivenSettlement_ReturnsNoRelationships()
    {
        // Given the seeded database has no relationships referencing Settlement
        // (the Trade→settledBy→Settlement relationship was removed with Trade entity)
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for Settlement
        var result = await tools.GetEntityRelationshipsAsync("Settlement", CancellationToken.None);

        // Then no relationships are returned
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEntityRelationships_ReturnsSourceAndTargetInclusively()
    {
        // Given the seeded data: Location → locatedInRegion → Region
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for Region (target only)
        var regionResult = await tools.GetEntityRelationshipsAsync("Region", CancellationToken.None);

        // Then Region as target is found
        Assert.Single(regionResult);
        Assert.Equal("Location",         regionResult[0].SourceEntity);
        Assert.Equal("Region",           regionResult[0].TargetEntity);
        Assert.Equal("locatedInRegion",  regionResult[0].RelationshipType);
    }

    [Fact]
    public async Task GetEntityRelationships_GivenTrulyUnknownEntity_ReturnsEmpty()
    {
        // Given the seeded database
        using var db = BuildDb();
        var tools = BuildTools(db);

        // When fetching relationships for an entity that doesn't exist at all
        var result = await tools.GetEntityRelationshipsAsync("NonExistentEntity", CancellationToken.None);

        // Then an empty list is returned
        Assert.Empty(result);
    }
}
