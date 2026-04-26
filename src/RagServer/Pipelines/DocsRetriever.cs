using System.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RagServer.Infrastructure.Docs;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Pipelines;

/// <summary>
/// Retrieves document chunks from Elasticsearch using hybrid RRF (BM25 + kNN).
/// </summary>
public sealed class DocsRetriever(
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ElasticsearchClient es,
    IOptions<RagOptions> opts)
{
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.retrieval");
        activity?.SetTag("rag.query_length", query.Length);

        var topK = opts.Value.DocsTopK;
        var rankConst = opts.Value.RrfRankConstant;

        // Generate query embedding
        var embedResult = await embeddings.GenerateAsync([query], cancellationToken: ct);
        var qvec = embedResult[0].Vector.ToArray();

        // Build Standard (BM25) retriever
        var standardRetriever = new Retriever
        {
            Standard = new StandardRetriever
            {
                Query = new MatchQuery { Field = "content", Query = query }
            }
        };

        // Build KNN retriever
        var knnRetriever = new Retriever
        {
            Knn = new KnnRetriever
            {
                Field = "vector",
                QueryVector = qvec,
                K = topK,
                NumCandidates = topK * 5
            }
        };

        var resp = await es.SearchAsync<System.Text.Json.JsonElement>(s => s
            .Indices("docs")
            .Size(topK)
            .Retriever(r => r
                .Rrf(rrf => rrf
                    .RankConstant(rankConst)
                    .RankWindowSize(topK * 2)
                    .Retrievers(
                        new Union<Retriever, RRFRetrieverComponent>(standardRetriever),
                        new Union<Retriever, RRFRetrieverComponent>(knnRetriever)
                    )
                )
            ), ct);

        if (!resp.IsValidResponse)
        {
            activity?.SetStatus(ActivityStatusCode.Error, resp.ElasticsearchServerError?.ToString() ?? "ES error");
            return [];
        }

        var chunks = resp.Hits
            .Select(hit =>
            {
                var src = hit.Source;
                var content    = src.TryGetProperty("content",     out var c)  ? c.GetString()  ?? "" : "";
                var title      = src.TryGetProperty("title",       out var t)  ? t.GetString()  ?? "" : "";
                var url        = src.TryGetProperty("url",         out var u)  ? u.GetString()  ?? "" : "";
                var sourceType = src.TryGetProperty("source_type", out var st) ? st.GetString() ?? "" : "";
                var score      = (float)(hit.Score ?? 0.0);
                return new RetrievedChunk(content, title, url, sourceType, score);
            })
            .ToList();

        activity?.SetTag("rag.hits_count", chunks.Count);
        return chunks;
    }
}
