using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Infrastructure.Catalog;

public sealed class MongoBusinessRulesRepository : IBusinessRulesRepository
{
    private readonly IMongoCollection<RulesDocument> _collection;

    public MongoBusinessRulesRepository(MongoOptions options)
    {
        var client = new MongoClient(options.ConnectionString);
        var db = client.GetDatabase(options.Database);
        _collection = db.GetCollection<RulesDocument>(options.RulesCollection);
    }

    public async Task<IReadOnlyList<BusinessRule>> GetRulesAsync(
        string entityName,
        bool? mandatoryOnly = null,
        CancellationToken ct = default)
    {
        using var activity = RagActivitySource.Source.StartActivity("catalog.get_rules");
        activity?.SetTag("entity", entityName);
        activity?.SetTag("mandatoryOnly", mandatoryOnly);

        var entityCode = entityName.ToLowerInvariant().Replace(" ", "");

        var filterBuilder = Builders<RulesDocument>.Filter;
        var filter = filterBuilder.Eq("entityCode", entityCode)
                   & filterBuilder.Eq("isActive", true);

        if (mandatoryOnly == true)
            filter &= filterBuilder.Eq("ruleType", "MANDATORY");

        var docs = await _collection
            .Find(filter)
            .ToListAsync(ct);

        return docs
            .Select(d => new BusinessRule(
                d.RuleId,
                d.EntityCode,
                d.RuleName,
                d.RuleType,
                d.Description,
                d.Conditions
                    .Select(c => new RuleCondition(c.AttributeCode, c.Operator, c.Value))
                    .ToList(),
                d.Severity,
                d.Owner,
                d.RegulatoryReference,
                d.IsActive))
            .ToList();
    }
}

internal sealed class RulesDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("ruleId")]
    public string RuleId { get; set; } = "";

    [BsonElement("entityCode")]
    public string EntityCode { get; set; } = "";

    [BsonElement("ruleName")]
    public string RuleName { get; set; } = "";

    [BsonElement("ruleType")]
    public string RuleType { get; set; } = "";

    [BsonElement("description")]
    public string Description { get; set; } = "";

    [BsonElement("conditions")]
    public List<RuleConditionDoc> Conditions { get; set; } = [];

    [BsonElement("severity")]
    public string Severity { get; set; } = "";

    [BsonElement("owner")]
    public string Owner { get; set; } = "";

    [BsonElement("regulatoryReference")]
    public string? RegulatoryReference { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; }
}

internal sealed class RuleConditionDoc
{
    [BsonElement("attributeCode")]
    public string AttributeCode { get; set; } = "";

    [BsonElement("operator")]
    public string Operator { get; set; } = "";

    [BsonElement("value")]
    public string Value { get; set; } = "";
}
