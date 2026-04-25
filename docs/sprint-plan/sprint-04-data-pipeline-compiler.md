# Sprint 4 — UC-3 Data Pipeline: Compiler, Evaluation & Demo Stats

| | |
|---|---|
| **Sprint** | 4 |
| **Duration** | Weeks 8–9 (10 days) |
| **Milestone** | M4 — UC-3 end-to-end working; Demo Stats panel live |
| **Goal** | Implement the `IrToDslCompiler`, integrate ES `_validate/query` and `_search`, wire the complete UC-3 pipeline, run the full three-pipeline golden set, perform A/B model comparison, and polish the Blazor Demo Stats panel. |
| **Depends on** | Sprint 3 complete (M3). IR generates and validates. |

---

## Prerequisites

- Sprint 3 all Done items checked.
- Operational ES index contains real or representative business data (needed for `_search` to return meaningful results).
- Home laptop (RTX 3080, 16 GB VRAM) available for Qwen3 14B A/B test.
- `QuerySpec` IR types locked (breaking changes after this sprint are expensive).

---

## Environment Variables (Sprint 4 additions to `.env`)

```dotenv
# ── UC-3 Compiler / search ────────────────────────────────────────────────────
RAG_UC3_MAX_RESULT_ROWS=50
RAG_UC3_RESULT_FORMAT_MAX_TOKENS=400
RAG_UC3_VALIDATE_BEFORE_SEARCH=true
RAG_UC3_ES_VALIDATE_PATH=/_validate/query

# ── A/B test ──────────────────────────────────────────────────────────────────
AB_TEST_MODEL_A=qwen3:8b
AB_TEST_MODEL_B=qwen3:14b
AB_TEST_ENABLED=false

# ── Demo Stats ────────────────────────────────────────────────────────────────
DEMO_STATS_ENABLED=true
```

---

## Tasks

### Week 8, Day 1 — IrToDslCompiler: Core Structure

The compiler is deterministic C# — no model calls, no external I/O. Every edge case handled here is one fewer model failure.

- [ ] **T4.1** Create `src/RagServer/Compiler/IrToDslCompiler.cs` with method:
  ```csharp
  public SearchRequest Compile(QuerySpec ir, SchemaCard card);
  ```
- [ ] **T4.2** Implement entity → index name resolution: look up `card.IndexName` from `SchemaCardCache` using `ir.Entity`.
- [ ] **T4.3** Implement filter compilation — `BuildFilterQuery(Filter f, SchemaCard card) -> Query`:
  - `Eq` → `term` query if `IsKeyword`, else `match` query.
  - `Ne` → `must_not: [term/match]`.
  - `Gt/Gte/Lt/Lte` → `range` query.
  - `Contains` → `match` query (full-text).
  - `In` → `terms` query.
  - `IsNull` → `must_not: [exists]`.
  - `IsNotNull` → `exists` query.
  - Throw `CompilerException` for unknown operator (compile-time catch, not runtime).
- [ ] **T4.4** Write unit tests for each filter operator (at least 2 cases per operator):
  - Keyword field + `Eq` → `term` query.
  - Text field + `Eq` → `match` query.
  - Date field + `Gte` + `Lte` → `range` query with correct format.
  - `In` with 3 values → `terms` query with all 3.
  - `IsNull` → `must_not: exists`.

**Acceptance:** Filter unit tests all pass.

---

### Week 8, Day 2 — IrToDslCompiler: Date Math & Time Range

- [ ] **T4.5** Implement `TimeRange` compilation — `BuildTimeRangeQuery(TimeRange tr, SchemaCard card) -> Query`:
  - If `RelativePeriod` provided, resolve to ES date math:
    ```
    "today"        → from: "now/d", to: "now/d"
    "yesterday"    → from: "now-1d/d", to: "now-1d/d"
    "last_7_days"  → from: "now-7d/d", to: "now"
    "last_30_days" → from: "now-30d/d", to: "now"
    "this_month"   → from: "now/M", to: "now/M"
    "this_year"    → from: "now/y", to: "now/y"
    "last_year"    → from: "now-1y/y", to: "now-1y/y"
    ```
  - If `From`/`To` provided (absolute), use ISO 8601 strings directly.
  - Validate that `tr.Field` exists in `card.Fields` and `IsDate = true`; throw `CompilerException` if not.
- [ ] **T4.6** Write unit tests for all 7 relative periods and absolute date range.
- [ ] **T4.7** Implement `Sort` compilation: for each `SortClause`, map `Direction.Asc/Desc` → ES `"asc"/"desc"`; use `.keyword` sub-field if field is `text` type.
- [ ] **T4.8** Implement `Aggregation` compilation:
  - `Count` → `value_count` agg on the first keyword/date field in the card.
  - `Distinct` (field required) → `cardinality` agg.
  - `Min/Max/Avg/Sum` (field required) → corresponding ES metric agg.
  - `GroupBy` (field required) → `terms` agg, size 10.

**Acceptance:** Date math unit tests pass; aggregation tests pass.

---

### Week 8, Day 3 — IrToDslCompiler: Integration & Limit

- [ ] **T4.9** Assemble the full `SearchRequest` from filter + time range + sort + aggregations + limit:
  - `size = ir.Limit ?? RAG_UC3_MAX_RESULT_ROWS` (capped at `RAG_UC3_MAX_RESULT_ROWS`).
  - If aggregations present and `Limit` not set, set `size = 0` (aggregation-only query).
  - Combine filter and time range with `bool: { must: [filter_clauses] }`.
  - Apply sort clauses to `SearchRequest.Sort`.
  - Apply aggregation clauses to `SearchRequest.Aggregations`.
- [ ] **T4.10** Create `src/RagServer/Compiler/CompilerException.cs` (inherits `Exception`) — thrown on unresolvable field, unknown operator, or schema mismatch.
- [ ] **T4.11** Write 5 end-to-end compiler integration tests using the full `QuerySpec` → `SearchRequest` path:
  - Simple `Eq` filter.
  - Date relative period + sort.
  - Aggregation (`Count`).
  - Combined filter + time range + sort + limit.
  - `CompilerException` thrown for unknown field.

**Acceptance:** All compiler integration tests pass; `dotnet test --filter "FullyQualifiedName~IrToDslCompilerTests"` green.

---

### Week 8, Day 4 — ES Validation & Search

- [ ] **T4.12** Create `src/RagServer/Pipelines/Data/EsQueryExecutor.cs`:
  - Method `ValidateAsync(SearchRequest req, string index) -> ValidationResult`:
    - Posts to `GET /{index}/_validate/query?explain=true`.
    - Returns `IsValid`, `Explanation` list.
    - Emits OTel span `compiler.es_validate`.
  - Method `SearchAsync(SearchRequest req, string index) -> SearchResult`:
    - Calls `ElasticsearchClient.SearchAsync<JsonElement>(req)`.
    - Returns `{ Total, Hits: IReadOnlyList<JsonElement>, Aggregations: JsonElement? }`.
    - Emits OTel span `compiler.es_search` with attributes `search.total_hits`, `search.took_ms`.
- [ ] **T4.13** Add Polly resilience on `EsQueryExecutor`:
  - Retry transient ES errors (503, timeout): 2 attempts.
  - Circuit breaker: open after 3 consecutive failures.

**Acceptance:** Integration test: compile a simple `QuerySpec` → validate against local ES → validate succeeds; search returns results from seeded data.

---

### Week 8, Day 5 — Full UC-3 DataPipeline

- [ ] **T4.14** Create `src/RagServer/Pipelines/Data/DataPipeline.cs`:
  - Step 1: `IrGenerationLoop.GenerateAsync(query)` → `QuerySpec`.
  - Step 2 (if IR valid): `IrToDslCompiler.Compile(ir, card)` → `SearchRequest`.
  - Step 3 (if `RAG_UC3_VALIDATE_BEFORE_SEARCH=true`): `EsQueryExecutor.ValidateAsync` → if invalid, treat as IR failure and trigger retry with ES error message.
  - Step 4: `EsQueryExecutor.SearchAsync` → `SearchResult`.
  - Step 5: format result with `ResultFormatter`.
  - Step 6: stream final natural-language answer.
  - Emits OTel span `rag.data_pipeline` enclosing all child spans.
- [ ] **T4.15** Create `src/RagServer/Pipelines/Data/ResultFormatter.cs`:
  - Input: `SearchResult` (hits as JSON), `QuerySpec` (for context), `SchemaCard`.
  - Builds a prompt: `"Format the following search results as a clear, concise answer. Include counts if relevant.\n\nQuery intent: {ir.Entity} with {filter summary}\nResults:\n{top_10_rows_as_table}"`.
  - Calls `IChatClient.CompleteStreamingAsync` with `max_tokens = RAG_UC3_RESULT_FORMAT_MAX_TOKENS`.
  - Emits OTel span `compiler.result_format`.
- [ ] **T4.16** Wire `DataPipeline` into `ChatEndpoint` when `IntentRouter` returns `PipelineKind.Data`.

**Acceptance:** Ask "give me all counterparties updated today" → produces a natural-language answer with result count; full trace in Aspire shows: IR generate → ES validate → ES search → format → LLM.

---

### Week 9, Day 1 — Demo Stats Panel

- [ ] **T4.17** Create `src/RagServer/Pipelines/PipelineResult.cs`:
  ```csharp
  public record PipelineResult(
      string Pipeline,           // "docs" | "metadata" | "data"
      long LatencyMs,
      int TokensGenerated,
      float TokensPerSecond,
      string ModelName,
      int? ToolCallCount,        // UC-2 only
      bool? IrValidFirstTry,     // UC-3 only
      int? TotalResultRows       // UC-3 only
  );
  ```
- [ ] **T4.18** Populate `PipelineResult` from span data and response metadata at the end of each pipeline.
- [ ] **T4.19** Return `PipelineResult` as HTTP response headers (non-streaming metadata) or as a final SSE event `event: stats`.
- [ ] **T4.20** Update `Chat.razor` Demo Stats sidebar to display all fields of `PipelineResult`:

  ```
  ┌─────────────────────────────┐
  │  Demo Stats                 │
  ├─────────────────────────────┤
  │  Pipeline      docs         │
  │  Model         qwen3:8b     │
  │  Latency       2 340 ms     │
  │  Tokens        187          │
  │  Tokens/s      79.8         │
  │  Tool calls    —            │
  │  IR first try  —            │
  │  Result rows   —            │
  └─────────────────────────────┘
  ```

- [ ] **T4.21** Bind model badge in UI to `PipelineResult.ModelName` (dynamic — changes when model is swapped via config).

**Acceptance:** Demo Stats panel updates after each response with correct pipeline, latency, and token metrics.

---

### Week 9, Day 2 — A/B Model Comparison

- [ ] **T4.22** Create `src/RagServer/Infrastructure/AbTestChatClient.cs`:
  - When `AB_TEST_ENABLED=true`, routes alternate requests to `AB_TEST_MODEL_A` and `AB_TEST_MODEL_B` (round-robin or configurable).
  - Logs which model handled each request as OTel attribute `llm.model`.
- [ ] **T4.23** On the home laptop: pull Qwen3 14B:
  ```bash
  docker exec ollama ollama pull qwen3:14b
  ```
- [ ] **T4.24** Run the UC-3 golden set with `AB_TEST_MODEL_A=qwen3:8b` and `AB_TEST_MODEL_B=qwen3:14b`, recording:
  - IR first-try validity rate per model.
  - End-to-end latency per model.
  - Token-per-second throughput per model.
- [ ] **T4.25** Record results in `EvalResults` with `Environment=home-laptop-8b` and `Environment=home-laptop-14b`.
- [ ] **T4.26** Document findings (quality delta, latency delta) in `docs/sprint-plan/sprint-04-ab-test-results.md`.

**Acceptance:** A/B results table populated; at least one UC-3 query shows measurable quality improvement at 14B.

---

### Week 9, Day 3 — Full Golden Set Run (All Pipelines)

- [ ] **T4.27** Run the complete golden set (UC-1 + UC-2 + UC-3, 90 queries total) on both laptops:
  - Work laptop: Qwen3 8B.
  - Home laptop: Qwen3 14B.
- [ ] **T4.28** Compute aggregate metrics:
  - UC-1 recall@5.
  - UC-2 tool selection exact match rate.
  - UC-3 IR first-try validity; DSL execution success rate.
  - p50 and p95 end-to-end latency per pipeline per environment.
- [ ] **T4.29** Update `docs/sprint-plan/sprint-04-golden-set-summary.md` with the results table.

**Acceptance:** All three golden sets run to completion; `EvalResults` table has rows for all environments.

---

### Week 9, Day 4 — Performance Instrumentation

- [ ] **T4.30** Add OTel histograms:
  - `rag.request_duration_ms` (tags: `pipeline`, `environment`, `model`).
  - `rag.tokens_per_second` (tags: `pipeline`, `model`).
  - `rag.ir_first_try_success` (counter, tags: `model`).
  - `rag.es_search_duration_ms` (tags: `pipeline`).
  - `rag.queue_depth` (gauge, sampled every 5 s).
- [ ] **T4.31** Verify all metrics visible in Aspire Dashboard > Metrics tab.
- [ ] **T4.32** Create a saved Aspire view (screenshot in `docs/sprint-plan/aspire-metrics-view.png`) showing the key metrics for the demo.

**Acceptance:** All 5 custom metrics emit values during a golden-set run; Aspire screenshot captured.

---

### Week 9, Day 5 — Phi-4-mini A/B (Optional)

> This task is conditional: execute only if Qwen3 8B UC-3 IR validity < 75% on the work laptop.

- [ ] **T4.33** Pull `phi4-mini:3.8b` into Ollama on the work laptop.
- [ ] **T4.34** Run the UC-3 golden set with Phi-4-mini; record in `EvalResults` as `Environment=work-laptop-phi4mini`.
- [ ] **T4.35** Compare IR first-try validity, latency, and token throughput against Qwen3 8B.
- [ ] **T4.36** Document conclusion in `docs/sprint-plan/sprint-04-phi4-comparison.md`.

**Acceptance (conditional):** If Phi-4-mini outperforms Qwen3 8B on UC-3 with lower latency, update `OLLAMA_CHAT_MODEL` recommendation in `.env.example`.

---

## Sprint 4 Definition of Done

- [ ] `IrToDslCompiler` handles all filter operators, date math, sort, aggregations — all unit tests pass.
- [ ] ES `_validate/query` integrated before every `_search`; validation errors trigger retry.
- [ ] `DataPipeline` end-to-end: NL → IR → DSL → ES → formatted answer; streaming.
- [ ] Demo Stats panel shows live metrics for all three pipelines.
- [ ] A/B comparison (8B vs 14B) completed; results documented.
- [ ] Full golden set (90 queries) run on both laptops; results in `EvalResults`.
- [ ] OTel metrics: 5 custom instruments emitting; Aspire screenshot saved.
- [ ] `dotnet test` passes with zero failures.
- [ ] All new `.env` variables documented in `.env.example`.
