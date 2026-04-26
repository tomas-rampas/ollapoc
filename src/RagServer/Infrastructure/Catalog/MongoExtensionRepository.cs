using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RagServer.Options;

namespace RagServer.Infrastructure.Catalog;

public sealed class MongoExtensionRepository : IMongoExtensionRepository
{
    private readonly IMongoCollection<ExtensionDocument> _collection;

    public MongoExtensionRepository(MongoOptions options)
    {
        var client = new MongoClient(options.ConnectionString);
        var db     = client.GetDatabase(options.Database);
        _collection = db.GetCollection<ExtensionDocument>(options.ExtensionsCollection);
    }

    public async Task<IReadOnlyList<ExtensionAttribute>> GetExtensionsAsync(
        string entityName, CancellationToken ct = default)
    {
        var filter = Builders<ExtensionDocument>.Filter
            .Regex(d => d.EntityName, new BsonRegularExpression($"^{Regex.Escape(entityName)}$", "i"));

        var doc = await _collection
            .Find(filter)
            .FirstOrDefaultAsync(ct);

        if (doc is null)
            return [];

        return doc.Attributes
            .Select(a => new ExtensionAttribute(a.Name, a.Value, a.Description))
            .ToList();
    }
}

internal sealed class ExtensionDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("entityName")]
    public string EntityName { get; set; } = "";

    [BsonElement("attributes")]
    public List<ExtensionAttributeDoc> Attributes { get; set; } = [];
}

internal sealed class ExtensionAttributeDoc
{
    [BsonElement("name")]
    public string Name { get; set; } = "";

    [BsonElement("value")]
    public string Value { get; set; } = "";

    [BsonElement("description")]
    public string? Description { get; set; }
}
