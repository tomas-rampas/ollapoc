# Sprint 3 — UC-3 Data Pipeline: IR Layer

| | |
|---|---|
| **Sprint** | 3 |
| **Duration** | Weeks 6–7 (10 days) |
| **Milestone** | M3 — UC-3 IR generation validates against JSON Schema for all 30 golden-set queries |
| **Goal** | Define the `QuerySpec` Intermediate Representation, build the JSON Schema validator, ingest schema cards into ES, craft and tune the NL→IR prompt, and achieve ≥80% first-pass IR schema validity on the golden set. |
| **Depends on** | Sprint 2 complete (M2). `ResolveEntity` and `catalog_terms` available. |

---

## Sequential Thinking — Why This Sprint Is High Variance

The IR generation quality is the single most uncertain element of the entire POC.

**Root cause chain (5 Why):**
1. IR generation sometimes fails → the prompt is too open-ended for an 8B model.
2. Prompt is open-ended → the model has no schema constraint in context.
3. No schema constraint → the model doesn't know which fields are required vs optional.
4. Required vs optional ambiguous → the model invents fields or omits required ones.
5. Fields invented/omitted → the JSON Schema validator rejects, triggering a costly retry.

**Mitigation:** Embed the full JSON Schema in the system prompt; include 3–5 few-shot examples covering the common filter patterns; use `max_tokens=200` to prevent verbosity; validate early (before compilation).

---

## Prerequisites

- Sprint 2 all Done items checked.
- Operational ES index exists with known entity names (needed for schema-card ingestion).
- At least one entity mapping retrievable via `GET /{index}/_mapping`.

---

## Environment Variables (Sprint 3 additions to `.env`)

```dotenv
# ── UC-3 IR generation ────────────────────────────────────────────────────────
RAG_UC3_IR_MAX_TOKENS=250
RAG_UC3_MAX_RETRIES=1
RAG_UC3_SCHEMA_CARD_CACHE_TTL_SECONDS=300
RAG_UC3_ENTITY_RESOLVE_THRESHOLD=0.5

# ── Schema-card ingestion ─────────────────────────────────────────────────────
INGESTION_ES_MAPPING_CRON=0 */6 * * *
ES_OPERATIONAL_INDEX_PATTERN=business_*
ES_OPERATIONAL_MAX_SAMPLE_VALUES=5

# ── Elasticsearch (operational) ───────────────────────────────────────────────
ES_OPERATIONAL_URL=${ES_URL}
ES_OPERATIONAL_USERNAME=${ES_USERNAME}
ES_OPERATIONAL_PASSWORD=${ES_PASSWORD}
```

---

## Tasks

### Week 6, Day 1 — QuerySpec IR Design & JSON Schema

- [ ] **T3.1** Create `src/RagServer/Compiler/Ir/` directory and define the IR model:

  ```csharp
  // QuerySpec.cs
  public record QuerySpec(
      string Entity,
      IReadOnlyList<Filter> Filters,
      TimeRange? TimeRange,
      IReadOnlyList<SortClause> Sort,
      IReadOnlyList<Aggregation> Aggregations,
      int? Limit
  );

  // Filter.cs
  public record Filter(
      string Field,
      FilterOperator Operator,   // Eq, Ne, Gt, Gte, Lt, Lte, Contains, In, IsNull, IsNotNull
      JsonElement? Value,
      IReadOnlyList<JsonElement>? Values   // for In operator
  );

  // TimeRange.cs
  public record TimeRange(
      string Field,
      DateTimeOffset? From,
      DateTimeOffset? To,
      string? RelativePeriod   // e.g. "today", "last_7_days", "this_month"
  );

  // SortClause.cs
  public record SortClause(string Field, SortDirection Direction);

  // Aggregation.cs
  public record Aggregation(AggregationType Type, string? Field, string? Name);
  ```

- [ ] **T3.2** Create `src/RagServer/Compiler/Ir/QuerySpecJsonSchema.cs` — returns the JSON Schema string as an embedded resource. The schema must:
  - Mark `Entity` as `required`.
  - List all `FilterOperator` enum values in the schema's `enum` constraint.
  - Mark `TimeRange.RelativePeriod` as an `enum` (`today`, `yesterday`, `last_7_days`, `last_30_days`, `this_month`, `this_year`, `last_year`).
  - Set `additionalProperties: false` on every object to catch invented fields.
- [ ] **T3.3** Write unit tests for the JSON Schema itself:
  - Valid minimal IR (entity only) → passes.
  - Valid complex IR (filters + time range + sort + aggregation + limit) → passes.
  - Unknown operator string → fails.
  - `additionalProperties` violation → fails.
  - Missing `Entity` → fails.

**Acceptance:** Schema unit tests all pass; JSON Schema stored as embedded resource (not hardcoded string in production code).

---

### Week 6, Day 2 — IR Validator

- [ ] **T3.4** Add NuGet: `JsonSchema.Net` (or `NJsonSchema`) for JSON Schema validation.
- [ ] **T3.5** Create `src/RagServer/Compiler/Ir/QuerySpecValidator.cs`:
  - Method `Validate(string jsonString) -> ValidationResult` (record: `IsValid`, `Errors: IReadOnlyList<string>`).
  - Uses the embedded JSON Schema from T3.2.
  - Returns all validation errors, not just the first.
  - Emits OTel span `compiler.ir_validate` with attribute `ir.valid=true/false`, `ir.error_count`.
- [ ] **T3.6** Write unit tests for `QuerySpecValidator`:
  - 5 valid IR samples → `IsValid = true`.
  - 5 invalid samples (missing field, wrong type, unknown enum) → `IsValid = false` with non-empty `Errors`.

**Acceptance:** All validator tests pass.

---

### Week 6, Day 2 — Schema-Card Model & ES Index

- [ ] **T3.7** Define `schema_cards` index mapping:
  ```json
  {
    "entity_name":    { "type": "keyword" },
    "index_name":     { "type": "keyword" },
    "schema_json":    { "type": "keyword", "index": false },
    "description":    { "type": "text" },
    "last_refreshed": { "type": "date" },
    "vector":         { "type": "dense_vector", "dims": 384, "index": true, "similarity": "cosine" }
  }
  ```
  Add to `IndexBootstrapper`.
- [ ] **T3.8** Create `src/RagServer/Compiler/SchemaCard.cs`:
  ```csharp
  public record SchemaCard(
      string EntityName,
      string IndexName,
      IReadOnlyList<SchemaField> Fields,
      string? Description
  );
  public record SchemaField(
      string Name,
      string EsType,
      bool IsKeyword,    // true if type=keyword or sub-field .keyword exists
      bool IsDate,
      bool IsNested,
      IReadOnlyList<string>? SampleValues,
      string? SemanticNote
  );
  ```

**Acceptance:** `schema_cards` index exists after `IndexBootstrapper` runs.

---

### Week 6, Day 3 — Schema-Card Ingestion

- [ ] **T3.9** Create `src/RagServer/Ingestion/Catalog/SchemaCardIngester.cs`:
  - Reads `GET /{index}/_mapping` for each index matching `ES_OPERATIONAL_INDEX_PATTERN`.
  - Parses the mapping into `SchemaCard` (handles `text`, `keyword`, `date`, `long`, `integer`, `float`, `boolean`, `nested`, and sub-field `.keyword`).
  - Optionally fetches sample values via `GET /{index}/_search` with `aggs: { terms: { field: <keyword_field>, size: 5 } }` for keyword fields.
  - Serialises `SchemaCard` to compact JSON (the `schema_json` field).
  - Embeds `"<EntityName> <field1> <field2> …"` as vector.
  - Upserts to `schema_cards` index.
- [ ] **T3.10** Schedule with `INGESTION_ES_MAPPING_CRON`.
- [ ] **T3.11** Create `src/RagServer/Compiler/SchemaCardCache.cs`:
  - In-memory dictionary keyed by `entityName`.
  - Loads from `schema_cards` ES index on first access; refreshes every `RAG_UC3_SCHEMA_CARD_CACHE_TTL_SECONDS`.
  - Thread-safe.
  - Emits OTel span attribute `schema_card.cache_hit=true/false`.
- [ ] **T3.12** Add admin endpoint `POST /admin/reindex?source=schema-cards` to trigger on-demand refresh.

**Acceptance:** After running ingestion on a business ES index, `schema_cards` contains a document with the correct field list; `SchemaCardCache.GetAsync("entity_name")` returns it without ES call on second access.

---

### Week 6, Day 4 — NL → IR Prompt Design

This is the most research-intensive task in the sprint. Budget a full day.

- [ ] **T3.13** Create `src/RagServer/Pipelines/Data/IrPromptBuilder.cs`:
  - Inputs: `string userQuery`, `SchemaCard card`.
  - Output: `IReadOnlyList<ChatMessage>` (system + user messages).
  - System prompt template (parameterise via embedded resource or `IOptions<DataPipelineOptions>`):
    ```
    You are a query translator. Convert the user's natural language query into a
    structured JSON object matching the schema below.

    ENTITY: {card.EntityName}
    INDEX: {card.IndexName}

    FIELDS:
    {foreach field: "- {field.Name} ({field.EsType}{', keyword' if IsKeyword}
                        {', date' if IsDate}{': ' + SemanticNote if SemanticNote}"}

    JSON SCHEMA:
    {QuerySpecJsonSchema}

    RULES:
    1. Output ONLY valid JSON. No prose, no markdown code block.
    2. Use "today" in RelativePeriod when the user says "today", "now", "current".
    3. For date fields, prefer RelativePeriod over absolute From/To when the intent is relative.
    4. Use the "In" operator for lists of values ("either A or B").
    5. Set Limit to 10 unless the user specifies a number or says "all".
    6. If the user asks to count, add an Aggregation with Type="Count".

    EXAMPLES:
    {few_shot_examples}
    ```
  - Few-shot examples (minimum 5) covering: simple filter, date relative period, sort, aggregation, combined.
- [ ] **T3.14** Create `src/RagServer/Options/DataPipelineOptions.cs` binding:
  - `IrMaxTokens` from `RAG_UC3_IR_MAX_TOKENS`.
  - `MaxRetries` from `RAG_UC3_MAX_RETRIES`.
  - `EntityResolveThreshold` from `RAG_UC3_ENTITY_RESOLVE_THRESHOLD`.
- [ ] **T3.15** Embed few-shot examples in `src/RagServer/Pipelines/Data/Resources/few_shot_examples.json` (not hardcoded in C# — loaded at startup).

**Acceptance:** `IrPromptBuilder.Build("give me all counterparties updated today", card)` returns a prompt that includes the schema JSON and at least one matching few-shot example.

---

### Week 6, Day 5 — NL → IR Generation

- [ ] **T3.16** Create `src/RagServer/Pipelines/Data/IrGenerator.cs`:
  - Input: `string userQuery`, `SchemaCard card`.
  - Step 1: call `ResolveEntity(userQuery)` to get canonical entity name (reuses the UC-2 tool).
  - Step 2: fetch schema card from `SchemaCardCache`.
  - Step 3: build prompt via `IrPromptBuilder`.
  - Step 4: call `IChatClient.CompleteAsync` (NOT streaming — we need the full JSON) with `max_tokens = RAG_UC3_IR_MAX_TOKENS`, through `LlmRequestQueue`.
  - Step 5: strip any markdown fences from the response.
  - Step 6: validate with `QuerySpecValidator`.
  - Return `(QuerySpec? ir, ValidationResult validation, string rawJson)`.
  - Emit OTel span `compiler.ir_generate` with attributes: `ir.entity`, `ir.valid`, `ir.raw_token_count`.
- [ ] **T3.17** Write unit tests for `IrGenerator` using a mocked `IChatClient`:
  - Valid JSON response → returns parsed `QuerySpec`.
  - JSON with markdown fences → strips and parses correctly.
  - Invalid JSON → returns `ValidationResult` with errors.
  - `null` response from model → returns validation error.

**Acceptance:** Unit tests pass; integration test against real Ollama for one golden-set query produces a valid `QuerySpec`.

---

### Week 7, Day 1 — Retry Loop (IR Layer)

- [ ] **T3.18** Create `src/RagServer/Pipelines/Data/IrGenerationLoop.cs`:
  - Wraps `IrGenerator` with a retry loop (max `RAG_UC3_MAX_RETRIES` retries).
  - On validation failure: append the error messages to the conversation history and call `IChatClient.CompleteAsync` again with the error context:
    ```
    The JSON you produced was invalid:
    - {error1}
    - {error2}
    Please produce a corrected version. Output ONLY valid JSON.
    ```
  - Track `firstTrySuccess` boolean (for the Demo Stats panel and metrics).
  - Emit OTel span `compiler.ir_loop` with attributes: `ir.retry_count`, `ir.first_try_success`.
- [ ] **T3.19** Register custom OTel metric `rag.ir_first_try_success_rate` (counter with `success=true/false` tag).
- [ ] **T3.20** Write unit tests for retry behaviour:
  - First attempt valid → no retry, `firstTrySuccess = true`.
  - First attempt invalid, second valid → one retry, `firstTrySuccess = false`.
  - Both attempts invalid → return last error; mark as failure.

**Acceptance:** Retry unit tests pass; `rag.ir_first_try_success_rate` metric visible in Aspire.

---

### Week 7, Day 2 — Schema-Card Retrieval for Ambiguous Queries

- [ ] **T3.21** For queries where `ResolveEntity` returns `Score < RAG_UC3_ENTITY_RESOLVE_THRESHOLD`, implement a fallback:
  - Search `schema_cards` index using BM25 on the user query.
  - Return the best-matching schema card.
  - If still no match, return a `400` with message `"Could not determine the target entity. Please specify the entity name explicitly."`.
- [ ] **T3.22** Log schema-card resolution path (entity resolved via `catalog_terms` vs `schema_cards`) as OTel span attribute.

**Acceptance:** Query "show me records updated today" (no entity specified) returns a meaningful error; query "counterparties updated today" resolves to `Counterparty` schema card.

---

### Week 7, Days 3–4 — Golden Set UC-3 (IR Layer)

- [ ] **T3.23** Seed `EvalQueries` with 30 UC-3 questions targeting the operational ES index. Include:
  - 10 simple filter queries (single field equals/contains).
  - 8 date-relative queries (`"today"`, `"last 7 days"`, `"this month"`).
  - 5 sort queries (`"ordered by date descending"`).
  - 4 aggregation queries (`"how many"`, `"count of"`).
  - 3 combined queries (filter + sort + limit).
- [ ] **T3.24** Extend `GoldenSetRunner` for UC-3 IR metrics:
  - **IR schema validity:** does the generated IR validate against JSON Schema?
  - **Entity match:** does `QuerySpec.Entity` match the expected entity?
  - **Key filter match:** are the expected filters present in `QuerySpec.Filters`?
- [ ] **T3.25** Run golden set on work laptop (Qwen3 8B); record results.
- [ ] **T3.26** If IR validity < 80% on first try:
  - Review which queries fail; categorise error types.
  - Adjust few-shot examples to cover the failing patterns.
  - Add schema constraint to enforce problematic fields.
  - Document the failure categories in `docs/sprint-plan/sprint-03-ir-failure-log.md`.
- [ ] **T3.27** Run golden set on home laptop (Qwen3 14B) if available; record results as `Environment=home-laptop-14b`.

**Acceptance:** ≥80% IR schema validity on first try for UC-3 golden set (work laptop, Qwen3 8B).

---

### Week 7, Day 5 — Prompt Economy Review

This task directly addresses the latency optimisation principle from the design doc.

- [ ] **T3.28** Measure token count of the IR system prompt for three representative entities: `tokenCount = words × 1.33`.
- [ ] **T3.29** If system prompt > 800 tokens, apply economy measures:
  - Remove redundant rule text (combine overlapping rules).
  - Shorten field descriptions (use `SemanticNote` only when genuinely ambiguous).
  - Reduce few-shot examples from 5 to 3 (keep the most diverse).
  - Target ≤600 tokens for the system prompt; field list and schema add on top.
- [ ] **T3.30** Measure first-token latency before and after economy review; record in `EvalResults` as a custom metric.

**Acceptance:** System prompt for a typical entity ≤600 tokens (excluding schema and fields); first-token latency measured and documented.

---

## Sprint 3 Definition of Done

- [ ] `QuerySpec` IR model defined with all types.
- [ ] JSON Schema for `QuerySpec` with `additionalProperties: false` — embedded resource.
- [ ] `QuerySpecValidator` unit tests all pass.
- [ ] `schema_cards` ES index populated for all operational entities.
- [ ] `SchemaCardCache` serving cards without repeated ES calls.
- [ ] `IrPromptBuilder` with ≥5 few-shot examples as embedded resource.
- [ ] `IrGenerator` producing and validating IR from natural language.
- [ ] Retry loop with error feedback working; `rag.ir_first_try_success_rate` metric emitted.
- [ ] UC-3 golden set (IR layer): 30 questions, ≥80% first-try schema validity, results in `EvalResults`.
- [ ] First-token latency measured and documented.
- [ ] All new `.env` variables documented in `.env.example`.
- [ ] `dotnet test` passes with zero failures.
