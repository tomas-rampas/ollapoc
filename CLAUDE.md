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
| Embedding model | all-minilm (384 dims, all-MiniLM-L6-v2) |
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
docker exec ollama ollama pull all-minilm

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

## MCP Usage Guidelines

Use these MCPs proactively — don't skip them when they apply.

### Context7 — library and framework API docs
**Always use before writing code that calls an external library API**, especially when:
- The API may have changed between major versions (e.g. `Elastic.Clients.Elasticsearch` 8→9, `Polly` 7→8, `Microsoft.Extensions.AI` 9→10, `OllamaSharp` 4→5)
- You are unsure of the exact method signature, fluent API shape, or required namespace
- The library is less common or niche (e.g. `NCrontab`, `HtmlAgilityPack` quirks, ES retriever types)
- You encounter a compile error caused by an API mismatch — re-query Context7 before guessing a fix

Workflow:
1. `mcp__context7__resolve-library-id` — resolve the package name to a library ID
2. `mcp__context7__query-docs` — query for the specific API (constructor, method, options)
3. If first query is insufficient, retry with `researchMode: true`

Key library IDs confirmed for this project:
- `/elastic/elasticsearch-net` — Elastic.Clients.Elasticsearch 9.x (RRF retriever, mappings, bulk API)
- `/app-vnext/polly` — Polly v8 (`ResiliencePipelineBuilder`, `RetryStrategyOptions`, `CircuitBreakerStrategyOptions`)

**Do not** use Context7 for: general C# language features, refactoring, business logic, or things derivable from the codebase itself.

---

### Sequential Thinking — multi-step reasoning and planning
**Use for any non-trivial task requiring structured analysis** before writing code:
- Sprint planning, feature design, or architectural decisions where the approach is not obvious
- Debugging a subtle bug where multiple root causes are possible
- Evaluating trade-offs between implementation approaches (e.g. interface vs concrete class, sync vs async pattern)
- Breaking down a large task into ordered subtasks with dependency awareness

Tool: `mcp__sequential-thinking__sequentialthinking`

Do not use for simple, single-step tasks (renaming a method, adding a field, writing a straightforward test).

---

### Filesystem MCP — large-scale file operations
**Use when the built-in Read/Write/Edit tools are insufficient**, specifically:
- Reading many files in one call (`mcp__filesystem__read_multiple_files`)
- Getting a directory tree for exploration (`mcp__filesystem__directory_tree`)
- Moving or renaming files (`mcp__filesystem__move_file`)
- Creating an entire new directory structure (`mcp__filesystem__create_directory`)
- Checking file metadata / sizes (`mcp__filesystem__get_file_info`, `mcp__filesystem__list_directory_with_sizes`)

**Do not** use filesystem MCP as a default for single-file reads or edits — the built-in `Read` and `Edit` tools are faster and preferred for those.

---

### Serena — semantic code navigation and symbol-level editing
**Use for refactoring that spans multiple files or requires understanding symbol relationships**:
- Renaming a class, method, or property across the entire codebase (`mcp__serena__rename_symbol`)
- Finding all callers / references to a symbol (`mcp__serena__find_referencing_symbols`)
- Getting a high-level overview of what's in a namespace or project (`mcp__serena__get_symbols_overview`)
- Replacing a method body precisely without affecting surrounding code (`mcp__serena__replace_symbol_body`)
- Safe deletion of a symbol with reference validation (`mcp__serena__safe_delete_symbol`)

Always call `mcp__serena__initial_instructions` at the start of any Serena-assisted session. Activate the project with `mcp__serena__activate_project` if symbols are not resolving.

**Do not** use Serena for simple single-file edits — use `Edit` directly. Serena adds value when the task requires understanding the symbol graph.

---

## Important Design Decisions

- **Request queue:** one in-flight Ollama call per instance (`OLLAMA_NUM_PARALLEL=1`). Queue with backpressure; reject with `429` when full.
- **IR not raw DSL:** the model generates a typed `QuerySpec` IR; the C# compiler emits DSL. This confines edge-case handling (date math, keyword vs text) to deterministic C# code.
- **Embedding cache:** in-process LRU (~1000 entries) for common query embeddings; schema-card cache rebuilt on ES mapping change only.
- **Streaming:** all user-visible output streams via SignalR.
- **No conversation persistence:** sessions are in-memory only (POC scope).
- **ES 9.x hybrid retrieval:** use the native RRF retriever (`rrf` in the `retriever` clause) for BM25 + kNN fusion — this is the recommended default over manual score combination.
