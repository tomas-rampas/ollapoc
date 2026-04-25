# Sprint 1 — UC-1 Documentation Q&A Pipeline

| | |
|---|---|
| **Sprint** | 1 |
| **Duration** | Weeks 2–3 (10 days) |
| **Milestone** | M1 — UC-1 Documentation Q&A working |
| **Goal** | Ingest Confluence and Jira content, build the hybrid ES retrieval pipeline, generate grounded answers with citations, and validate against 30 golden-set queries at ≥70% retrieval recall@5. |
| **Depends on** | Sprint 0 complete (M0). |

---

## Prerequisites

- Sprint 0 all Done items checked.
- Confluence service account credentials available (read-only).
- Jira service account credentials available (read-only).
- At least one Confluence space and one Jira project accessible.
- ES `docs` index does not yet exist (will be created by T1.2).

---

## Environment Variables (Sprint 1 additions to `.env`)

```dotenv
# ── Confluence ────────────────────────────────────────────────────────────────
CONFLUENCE_BASE_URL=https://yourcompany.atlassian.net/wiki
CONFLUENCE_USERNAME=svc-ai-assistant@yourcompany.com
CONFLUENCE_API_TOKEN=<token>
CONFLUENCE_SPACES=ENG,OPS,PROD
CONFLUENCE_INCREMENTAL_CRON=0 * * * *
CONFLUENCE_FULL_SYNC_CRON=0 2 * * *
CONFLUENCE_RATE_LIMIT_RPS=5

# ── Jira ──────────────────────────────────────────────────────────────────────
JIRA_BASE_URL=https://yourcompany.atlassian.net
JIRA_USERNAME=svc-ai-assistant@yourcompany.com
JIRA_API_TOKEN=<token>
JIRA_PROJECTS=ENG,OPS
JIRA_INCREMENTAL_CRON=0 * * * *
JIRA_RATE_LIMIT_RPS=5

# ── Ingestion tuning ──────────────────────────────────────────────────────────
INGESTION_CHUNK_TARGET_TOKENS=500
INGESTION_CHUNK_OVERLAP_TOKENS=50
INGESTION_BATCH_EMBED_SIZE=32

# ── Retrieval ─────────────────────────────────────────────────────────────────
RAG_DOCS_TOP_K=5
RAG_DOCS_RRF_RANK_CONSTANT=60
```

---

## Tasks

### Week 2, Day 1 — Elasticsearch `docs` Index

- [ ] **T1.1** Create `src/RagServer/Infrastructure/Elasticsearch/IndexDefinitions.cs` with static methods returning `CreateIndexRequest` objects for each index. Keep field mappings as strongly-typed C# (no JSON string literals).
- [ ] **T1.2** Implement `docs` index mapping:
  ```json
  {
    "content":          { "type": "text" },
    "vector":           { "type": "dense_vector", "dims": 384, "index": true, "similarity": "cosine" },
    "source_type":      { "type": "keyword" },
    "source_id":        { "type": "keyword" },
    "title":            { "type": "text" },
    "url":              { "type": "keyword" },
    "space_or_project": { "type": "keyword" },
    "last_modified":    { "type": "date" },
    "chunk_index":      { "type": "integer" }
  }
  ```
- [ ] **T1.3** Create `src/RagServer/Infrastructure/Elasticsearch/IndexBootstrapper.cs` — `IHostedService` that creates missing indices on startup using `IndexDefinitions`. Idempotent (check `indices.exists` before creating).
- [ ] **T1.4** Register `IndexBootstrapper` in `Program.cs`.
- [ ] **T1.5** Write a unit test asserting the mapping JSON round-trips correctly.

**Acceptance:** `docker compose up` → `docs` index exists in ES after `rag-server` starts; `GET /health/es` includes index in response.

---

### Week 2, Day 1 — Embedding Cache

- [ ] **T1.6** Create `src/RagServer/Infrastructure/EmbeddingCache.cs` wrapping `IEmbeddingGenerator<string, Embedding<float>>`:
  - LRU cache keyed by query string, max `RAG_EMBEDDING_CACHE_SIZE` entries.
  - Thread-safe (`ConcurrentDictionary` + eviction linked list or `MemoryCache` with size limit).
  - Emits OTel span attribute `embedding.cache_hit=true/false`.
- [ ] **T1.7** Register as singleton decorator over the raw `IEmbeddingGenerator` in DI.

**Acceptance:** Second call with same query string returns without hitting Ollama (verified via Aspire trace — no outbound span to Ollama on cache hit).

---

### Week 2, Day 2 — Chunking

- [ ] **T1.8** Create `src/RagServer/Ingestion/TextChunker.cs`:
  - Input: plain text string.
  - Strategy: sentence-aware split targeting `INGESTION_CHUNK_TARGET_TOKENS` tokens (use `SharpToken` or a simple word-count approximation: `tokens ≈ words × 1.33`).
  - Overlap: copy last `INGESTION_CHUNK_OVERLAP_TOKENS` token equivalent from previous chunk.
  - Output: `IEnumerable<string>`.
- [ ] **T1.9** Write unit tests for `TextChunker`:
  - Empty input → no chunks.
  - Short text (< target) → one chunk.
  - Long text → correct count with overlap.
  - No chunk exceeds `target × 1.2` tokens.

**Acceptance:** All `TextChunkerTests` pass.

---

### Week 2, Days 2–3 — Confluence Ingestion

- [ ] **T1.10** Add NuGet: `Polly` (already planned), `Microsoft.Extensions.Http.Polly` for `HttpClient` retry.
- [ ] **T1.11** Create `src/RagServer/Options/ConfluenceOptions.cs` binding `CONFLUENCE_*` env vars.
- [ ] **T1.12** Create `src/RagServer/Ingestion/Confluence/ConfluenceCrawler.cs`:
  - Uses `HttpClient` with Polly retry (3 attempts, exponential backoff, jitter) and token-bucket rate limit at `CONFLUENCE_RATE_LIMIT_RPS`.
  - `IncrementalSyncAsync(CancellationToken)`: reads `IngestionCursor` for `Source="confluence"`, calls `GET /rest/api/content?spaceKey=…&lastModified=…&limit=50`, pages through results, processes each page.
  - `FullSyncAsync(CancellationToken)`: same but ignores cursor.
  - Each page: fetch full body via `GET /rest/api/content/{id}?expand=body.storage,version,space`, convert `body.storage` (Confluence storage format / HTML) to plain text via a simple HTML stripper (`HtmlAgilityPack`).
- [ ] **T1.13** Create `src/RagServer/Ingestion/Confluence/ConfluenceContentNormaliser.cs`:
  - Input: Confluence `body.storage` HTML string.
  - Output: clean plain text (strip tags, expand macros to their text content, normalise whitespace).
- [ ] **T1.14** Create `src/RagServer/Ingestion/DocumentEmbedder.cs` (shared by Confluence + Jira):
  - Input: `IEnumerable<DocumentChunk>` (record with `Content`, `Metadata`).
  - Batches embedding calls (`INGESTION_BATCH_EMBED_SIZE`).
  - Upserts to `docs` index via `ElasticsearchClient.BulkAsync`.
  - Emits OTel spans: `ingestion.embed_batch` (token count), `ingestion.es_upsert` (doc count).
- [ ] **T1.15** Wire `ConfluenceCrawler` into an `IHostedService` (`IngestionScheduler`) using cron expressions from `CONFLUENCE_INCREMENTAL_CRON` and `CONFLUENCE_FULL_SYNC_CRON`.

**Acceptance:** Run full sync → `docs` index contains documents from at least one Confluence space; `IngestionRun` table has a completed row with `DocsProcessed > 0`.

---

### Week 2, Day 4 — Jira Ingestion

- [ ] **T1.16** Create `src/RagServer/Options/JiraOptions.cs` binding `JIRA_*` env vars.
- [ ] **T1.17** Create `src/RagServer/Ingestion/Jira/JiraCrawler.cs`:
  - JQL incremental: `project IN (…) AND updated >= "YYYY-MM-DD HH:MM" ORDER BY updated ASC`.
  - Fetches issue summary + description + comments (ADF format).
  - Converts ADF → plain text via `src/RagServer/Ingestion/Jira/AdfNormaliser.cs` (walk the ADF JSON tree recursively, extract text nodes).
- [ ] **T1.18** Register `JiraCrawler` in `IngestionScheduler` with `JIRA_INCREMENTAL_CRON`.
- [ ] **T1.19** Add admin endpoint `POST /admin/reindex?source=confluence|jira` (admin role only) to trigger a full sync on demand.

**Acceptance:** Jira issues appear in `docs` index; issue URL in the `url` field resolves to the correct Jira issue.

---

### Week 2, Day 5 — Hybrid Retrieval (RRF)

- [ ] **T1.20** Create `src/RagServer/Pipelines/Docs/DocsRetriever.cs`:
  - Input: query string.
  - Embed query using cached `IEmbeddingGenerator`.
  - Build ES `SearchRequest` using the **RRF retriever** composition:
    ```json
    {
      "retriever": {
        "rrf": {
          "retrievers": [
            { "standard": { "query": { "match": { "content": "<query>" } } } },
            { "knn": { "field": "vector", "query_vector": [...], "num_candidates": 100, "k": 20 } }
          ],
          "rank_constant": 60,
          "rank_window_size": 100
        }
      },
      "size": 5
    }
    ```
  - Return `IReadOnlyList<RetrievedChunk>` (record: `Content`, `Title`, `Url`, `SourceType`, `Score`).
  - Emit OTel span `rag.retrieval` with attributes `retrieval.top_k`, `retrieval.query_length`.
- [ ] **T1.21** Write unit/integration tests for `DocsRetriever` against a real local ES instance (use `Testcontainers.Elasticsearch` or assume local ES from `docker compose`).

**Acceptance:** Given a seeded `docs` index with 10+ docs, a query returns ≤5 chunks ranked by RRF score; span visible in Aspire.

---

### Week 3, Day 1 — Grounded Generation

- [ ] **T1.22** Create `src/RagServer/Pipelines/Docs/DocsPipeline.cs`:
  - Calls `DocsRetriever.RetrieveAsync(query)`.
  - Assembles a system prompt:
    ```
    You are a documentation assistant. Answer only from the provided context.
    Cite sources using [1], [2], ... markers corresponding to the context blocks below.
    Context:
    [1] <Title1>\n<Content1>
    [2] <Title2>\n<Content2>
    ...
    ```
  - Calls `IChatClient.CompleteStreamingAsync` through the `LlmRequestQueue`.
  - Streams tokens back to caller.
  - Emits OTel span `rag.docs_pipeline` with child spans for retrieval and LLM call.
- [ ] **T1.23** Create `src/RagServer/Pipelines/Docs/CitationExtractor.cs`:
  - Parses `[n]` markers in the completed answer.
  - Returns `IReadOnlyList<Citation>` (record: `Index`, `Url`, `Title`).
- [ ] **T1.24** Wire `DocsPipeline` into `ChatEndpoint` when `IntentRouter` returns `PipelineKind.Docs`.

**Acceptance:** Ask "What is a counterparty?" → streaming answer with `[1]`, `[2]` markers → citation links rendered in Blazor UI; full trace in Aspire.

---

### Week 3, Day 2 — Citation Rendering in Blazor

- [ ] **T1.25** Update `Chat.razor`:
  - After streaming completes, parse citations from the final message.
  - Render a "Sources" list below the assistant message: clickable hyperlinks to `url`.
  - Show `SourceType` badge (`confluence` or `jira`) next to each link.
- [ ] **T1.26** Add "Show retrieved context" debug panel (collapsed by default):
  - Renders the raw retrieved chunks with their scores.
  - Only visible in dev/debug mode (controlled by `ASPNETCORE_ENVIRONMENT=Development`).

**Acceptance:** Citations appear as hyperlinks; debug panel expands on click.

---

### Week 3, Day 3 — Intent Router — Model Fallback

- [ ] **T1.27** Extend `IntentRouter`:
  - If the rule-based tier returns no confident match (ambiguous prompt), call `IChatClient` with a constrained prompt:
    ```
    Classify the following query as exactly one of: docs, metadata, data.
    Reply with only the single word.
    Query: "<user query>"
    ```
  - Parse the single-word response; default to `docs` on invalid output.
  - Cache classification result by query hash (in-process, small LRU — 100 entries).
  - Emit OTel span `rag.router` with attribute `router.method=rules|model`.
- [ ] **T1.28** Add unit tests for the rule tier with 15 additional edge cases.
- [ ] **T1.29** Add an integration test that routes an ambiguous prompt through the model tier.

**Acceptance:** Router tests pass; model-tier classification visible as a child span under `rag.router`.

---

### Week 3, Days 4–5 — Golden Set UC-1 & Evaluation

- [ ] **T1.30** Seed `EvalQueries` with 30 UC-1 questions drawn from real Confluence content (or representative synthetic questions if content is not yet available). Include:
  - 10 definitional (`"What is…"`)
  - 10 procedural (`"How do I…"`)
  - 10 comparative or relational (`"What is the difference between…"`)
- [ ] **T1.31** Extend `GoldenSetRunner` for UC-1 metrics:
  - **Retrieval recall@5:** does the answer reference the expected source document?
  - **Faithfulness check (heuristic):** does the answer contain at least one sentence from the retrieved chunks?
- [ ] **T1.32** Run the golden set; record baseline pass rate in `EvalResults`.
- [ ] **T1.33** If recall@5 < 70%, investigate:
  - Chunking strategy (try smaller/larger chunks).
  - `rank_window_size` (increase from 100 to 200).
  - `RAG_DOCS_TOP_K` (increase from 5 to 8).
  - Document whether the shortfall is retrieval or generation quality.
- [ ] **T1.34** Commit final chunking/retrieval parameters back to `.env.example` with comments explaining the tuning rationale.

**Acceptance:** ≥70% recall@5 on the UC-1 golden set; evaluation run row in `EvalResults` with `Environment=work-laptop`.

---

### Week 3, Day 5 — Polly Resilience for Ingestion

- [ ] **T1.35** Add Polly `ResiliencePipeline` to `DocumentEmbedder`:
  - Retry on transient ES errors (5xx, timeout): 3 attempts, exponential backoff.
  - Circuit breaker: open after 5 consecutive failures; half-open after 30 s.
  - Emit OTel span attributes on retry: `polly.retry_attempt`, `polly.circuit_state`.
- [ ] **T1.36** Add same Polly pipeline to Confluence and Jira `HttpClient` registrations.

**Acceptance:** Simulated ES outage (stop ES container briefly) → ingestion retries and recovers when ES restarts; circuit-breaker state visible in Aspire metrics.

---

## Sprint 1 Definition of Done

- [ ] `docs` index exists with correct mapping; `catalog_terms` index created (empty — populated in Sprint 2).
- [ ] Confluence and Jira crawlers ingest incrementally; cursors persisted in `IngestionCursor`.
- [ ] Hybrid RRF retrieval returns ranked chunks for any text query.
- [ ] `DocsPipeline` produces streaming grounded answers with citation markers.
- [ ] Blazor UI renders citations as hyperlinks.
- [ ] Debug panel shows retrieved chunks in dev mode.
- [ ] Intent router handles ambiguous prompts via model fallback.
- [ ] UC-1 golden set: 30 questions, ≥70% recall@5, results in `EvalResults`.
- [ ] Polly resilience on all external calls.
- [ ] All new `.env` variables documented in `.env.example`.
- [ ] Zero hardcoded config values.
- [ ] `dotnet test` passes with zero failures.
