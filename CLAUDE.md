# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**ollapoc** — A self-hosted AI Knowledge Assistant POC built on .NET 10. It enables business users to query enterprise data via natural language using three pipelines:

- **UC-1 (Docs):** RAG over Confluence and Jira via hybrid Elasticsearch search.
- **UC-2 (Metadata):** Tool-calling against a SQL Server catalog (MongoDB for extension attributes).
- **UC-3 (Data):** NL → typed IR → Elasticsearch DSL compiler, with validation/retry loop.

All LLM inference runs locally via **Ollama** (Qwen3 8B/14B Q4_K_M). Orchestration is a single **.NET 10 ASP.NET Core** service. See `docs/AI-assistant.md` for the full design.

## Tech Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Web + UI | ASP.NET Core minimal APIs + Blazor Server |
| LLM abstraction | `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) |
| LLM backend | Ollama via `OllamaSharp` |
| Chat model | Qwen3 8B Q4_K_M (default), 14B on 16 GB GPU |
| Embedding model | bge-small-en-v1.5 (384 dims) |
| Vector + search | Elasticsearch 9.x (`Elastic.Clients.Elasticsearch` 9.x) |
| SQL catalog | EF Core 9 on SQL Server |
| Mongo extension | `MongoDB.Driver` |
| Resilience | Polly v8 |
| Observability | OpenTelemetry → .NET Aspire Dashboard (`:18888`) |
| Containerisation | Docker Compose |

**Note:** `Elastic.Clients.Elasticsearch` major version must match the ES server major version (both 9.x).

## Architecture

All services run in a single `docker-compose.yml`:
- **rag-server** (`:8080`) — ASP.NET Core app hosting the chat endpoint, admin endpoints, and ingestion `IHostedService`
- **ollama** (`:11434`) — local LLM inference, CUDA when available
- **elasticsearch** (`:9200`) — dual-role: RAG vector store + operational data store
- **aspire-dashboard** (`:18888` UI, `:4317` OTLP)

### Project Layout (planned)

```
src/
  RagServer/           ← Main ASP.NET Core project
    Endpoints/         ← Chat, admin, health
    Pipelines/         ← DocsPipeline, MetadataPipeline, DataPipeline
    Router/            ← IntentRouter (rule-based + model fallback)
    Ingestion/         ← IHostedService crawlers (Confluence, Jira, SQL, Mongo)
    Tools/             ← Catalog tool functions (ResolveEntity, GetEntityAttributes, etc.)
    Compiler/          ← IR → Elasticsearch DSL compiler (QuerySpec → ES Query DSL)
    Infrastructure/    ← ES client, Ollama client, EF context, Mongo client
  RagServer.Tests/
docker-compose.yml
```

### Data Pipelines

**UC-1 (Docs):** embed query → ES hybrid RRF (BM25 + kNN) → top-K chunks → grounded Qwen3 prompt → streaming answer with citations.

**UC-2 (Metadata):** M.E.AI function-calling loop with tools `ResolveEntity`, `GetEntityAttributes`, `GetEntityExtensions`, `ListCDE`, `GetEntityRelationships`. SQL Server is authoritative; MongoDB supplements.

**UC-3 (Data):** NL → Qwen3 generates `QuerySpec` IR JSON → C# validates schema → `IrToDslCompiler` emits ES DSL → `_validate/query` → `_search` → format answer. One retry on validation failure with error fed back to model.

### Elasticsearch Indices

| Index | Purpose |
|---|---|
| `docs` | Confluence/Jira chunks + 384-dim vectors (cosine HNSW) |
| `catalog_terms` | Canonical entity/attribute names for fuzzy `ResolveEntity` |
| `schema_cards` | Per-entity schema descriptions for UC-3 prompting |
| *(operational)* | Existing business data indices queried by compiled DSL |

### Key IR Shape (UC-3)

```csharp
public record QuerySpec(
    string Entity,
    IReadOnlyList<Filter> Filters,
    TimeRange? TimeRange,
    IReadOnlyList<SortClause> Sort,
    IReadOnlyList<Aggregation> Aggregations,
    int? Limit
);
```

The `IrToDslCompiler` is plain C# — deterministic, testable. Date math, term-vs-keyword, nested fields, and range queries are encoded here, not left to the model.

## Development Commands

```bash
# Start all infrastructure (Ollama, ES, Aspire Dashboard)
docker compose up -d

# Pull the default chat model into Ollama
docker exec ollama ollama pull qwen3:8b

# Pull the embedding model
docker exec ollama ollama pull bge-small-en-v1.5

# Run the RAG server (from src/RagServer/)
dotnet run

# Run tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~IrToDslCompilerTests"
```

## NVIDIA Container Toolkit Verification

Both dev laptops have the toolkit pre-installed. To verify CUDA is available inside Docker:

```bash
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

Ollama detects CUDA automatically when `--gpus all` is set on the container.

## Observability

Every request emits a single OTLP trace: HTTP → router → embedding → ES retrieval → LLM call → tool calls → ES query. View traces at `http://localhost:18888`. Configure services with `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:4317`.

## Important Design Decisions

- **Request queue:** one in-flight Ollama call per instance (`OLLAMA_NUM_PARALLEL=1`). Queue with backpressure; reject with `429` when full.
- **IR not raw DSL:** the model generates a typed `QuerySpec` IR; the C# compiler emits DSL. This confines edge-case handling (date math, keyword vs text) to deterministic C# code.
- **Embedding cache:** in-process LRU (~1000 entries) for common query embeddings; schema-card cache rebuilt on ES mapping change only.
- **Streaming:** all user-visible output streams via SignalR.
- **No conversation persistence:** sessions are in-memory only (POC scope).
- **ES 9.x hybrid retrieval:** use the native RRF retriever (`rrf` in the `retriever` clause) for BM25 + kNN fusion — this is the recommended default over manual score combination.
