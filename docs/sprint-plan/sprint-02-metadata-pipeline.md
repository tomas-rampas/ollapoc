# Sprint 2 — UC-2 Metadata Queries Pipeline

| | |
|---|---|
| **Sprint** | 2 |
| **Duration** | Weeks 4–5 (10 days) |
| **Milestone** | M2 — UC-2 Metadata Queries working |
| **Goal** | Implement the tool-calling pipeline against the SQL Server catalog and MongoDB extension attributes, including entity resolution via ES, and validate at ≥80% exact-match tool selection on 30 golden-set queries. |
| **Depends on** | Sprint 1 complete (M1). `catalog_terms` ES index available. |

---

## Prerequisites

- Sprint 1 all Done items checked.
- SQL Server catalog database accessible (connection string, read-only account).
- MongoDB catalog database accessible (connection string, read-only account).
- Entity/attribute/CDE schema understood (tables, columns, FK relationships).
- At least one entity with known attributes for test queries.

---

## Environment Variables (Sprint 2 additions to `.env`)

```dotenv
# ── SQL Server Catalog ────────────────────────────────────────────────────────
SQL_CONNECTION_STRING=Server=your-sql-server;Database=CatalogDB;User Id=svc_ai;Password=<pwd>;TrustServerCertificate=true;
SQL_CATALOG_SCHEMA=dbo
SQL_ENTITY_TABLE=Entities
SQL_ATTRIBUTE_TABLE=Attributes
SQL_CDE_TABLE=CriticalDataElements
SQL_RELATIONSHIP_TABLE=EntityRelationships

# ── MongoDB Catalog ───────────────────────────────────────────────────────────
MONGO_CONNECTION_STRING=mongodb://svc_ai:<pwd>@your-mongo-host:27017
MONGO_DATABASE_NAME=CatalogExtensions
MONGO_EXTENSIONS_COLLECTION=EntityExtensions

# ── Catalog ingestion ─────────────────────────────────────────────────────────
INGESTION_CATALOG_SQL_CRON=*/15 * * * *
INGESTION_CATALOG_MONGO_CRON=*/15 * * * *

# ── Tool-calling ──────────────────────────────────────────────────────────────
RAG_METADATA_MAX_TOOL_ROUNDS=8
RAG_METADATA_SYSTEM_PROMPT_EXTRA=
```

---

## Tasks

### Week 4, Day 1 — EF Core Catalog Domain

- [ ] **T2.1** Add NuGet packages:
  ```
  Microsoft.EntityFrameworkCore.SqlServer
  Microsoft.EntityFrameworkCore.Design
  MongoDB.Driver
  ```
- [ ] **T2.2** Create `src/RagServer/Infrastructure/Catalog/CatalogDbContext.cs` with `DbSet<Entity>`, `DbSet<Attribute>`, `DbSet<CriticalDataElement>`, `DbSet<EntityRelationship>`.
- [ ] **T2.3** Create entity classes in `src/RagServer/Infrastructure/Catalog/Entities/`:
  ```csharp
  // Entity.cs
  public record CatalogEntity(int Id, string CanonicalName, string DisplayName,
      string? Description, string? BusinessDomain, bool IsActive);

  // Attribute.cs
  public record CatalogAttribute(int Id, int EntityId, string FieldName,
      string DataType, bool IsNullable, bool IsCde, string? Classification,
      string? Description);

  // CriticalDataElement.cs
  public record CriticalDataElement(int Id, int EntityId, int AttributeId,
      string CdeCode, string? BusinessDefinition, string? DataOwner);

  // EntityRelationship.cs
  public record EntityRelationship(int Id, int FromEntityId, int ToEntityId,
      string RelationshipType, string? Description);
  ```
- [ ] **T2.4** Configure `CatalogDbContext` with table names from `SQL_*_TABLE` options (use `IConfiguration` at startup to set `modelBuilder.Entity<…>().ToTable(…)`).
- [ ] **T2.5** Register `CatalogDbContext` in DI with connection string from `SQL_CONNECTION_STRING`. Use `AddDbContextPool` (read-only, high-throughput).
- [ ] **T2.6** Write a smoke-test that counts rows in `Entities` table and asserts `> 0`.

**Acceptance:** `dotnet test --filter "FullyQualifiedName~CatalogDbContextTests"` passes.

---

### Week 4, Day 1 — MongoDB Extension Client

- [ ] **T2.7** Create `src/RagServer/Options/MongoOptions.cs` binding `MONGO_*` env vars.
- [ ] **T2.8** Create `src/RagServer/Infrastructure/Catalog/MongoExtensionRepository.cs`:
  - Method `GetExtensionsAsync(int entityId) -> Task<IReadOnlyList<EntityExtension>>`.
  - `EntityExtension` record: `EntityId`, `FieldName`, `BusinessGlossaryTag`, `CustomFlags`, `Notes`.
  - Uses `IMongoCollection<BsonDocument>` with `MONGO_EXTENSIONS_COLLECTION`.
- [ ] **T2.9** Register `MongoClient` and `MongoExtensionRepository` in DI.

**Acceptance:** Integration test retrieves extensions for a known `entityId`.

---

### Week 4, Day 2 — Catalog Ingestion into ES `catalog_terms`

- [ ] **T2.10** Define `catalog_terms` index mapping:
  ```json
  {
    "canonical_name":  { "type": "keyword" },
    "display_name":    { "type": "text" },
    "aliases":         { "type": "text" },
    "entity_id":       { "type": "integer" },
    "term_type":       { "type": "keyword" },
    "vector":          { "type": "dense_vector", "dims": 384, "index": true, "similarity": "cosine" }
  }
  ```
  Add to `IndexBootstrapper`.
- [ ] **T2.11** Create `src/RagServer/Ingestion/Catalog/CatalogTermsIngester.cs`:
  - Reads all `CatalogEntity` rows from SQL Server.
  - For each entity: create a document with `canonical_name`, `display_name`, aliases (comma-split from a `Aliases` column if present).
  - Embed `"<DisplayName> <CanonicalName> <Aliases>"` as the vector.
  - Upsert to `catalog_terms` index.
  - Also ingest attribute names (term_type=`attribute`) and CDE codes (term_type=`cde`).
- [ ] **T2.12** Schedule `CatalogTermsIngester` with `INGESTION_CATALOG_SQL_CRON` via `IngestionScheduler`.
- [ ] **T2.13** Add MongoDB extension attributes ingestion: for each entity, upsert extension field names as additional `catalog_terms` documents.

**Acceptance:** `catalog_terms` index non-empty after ingestion; contains both entity and attribute documents.

---

### Week 4, Day 3 — Tool Implementations

Create `src/RagServer/Tools/CatalogTools.cs` containing the five tools. Each tool is a plain C# method decorated with `[Description]` attributes for M.E.AI function-calling.

- [ ] **T2.14** `ResolveEntity(string text) -> Task<ResolveEntityResult>`:
  - Embeds `text`.
  - Searches `catalog_terms` (kNN over `vector` field, `term_type=entity`).
  - Returns `{ CanonicalName, EntityId, Score }` or `null` if score < 0.5.
  - Emits OTel span `tool.resolve_entity`.

- [ ] **T2.15** `GetEntityAttributes(string canonicalName, bool includeCde = false) -> Task<IReadOnlyList<AttributeResult>>`:
  - Resolves `canonicalName` → `entityId` via `CatalogDbContext`.
  - Queries `Attributes` where `EntityId = entityId`.
  - If `includeCde`, joins with `CriticalDataElements`.
  - Returns list of `{ FieldName, DataType, IsNullable, IsCde, Classification, Description }`.
  - Emits OTel span `tool.get_entity_attributes`.

- [ ] **T2.16** `GetEntityExtensions(int entityId) -> Task<IReadOnlyList<EntityExtension>>`:
  - Delegates to `MongoExtensionRepository.GetExtensionsAsync`.
  - Emits OTel span `tool.get_entity_extensions`.

- [ ] **T2.17** `ListCDE(string? canonicalEntityName = null) -> Task<IReadOnlyList<CdeResult>>`:
  - If `canonicalEntityName` provided, filter by entity; otherwise return all CDEs.
  - Joins `CriticalDataElements` + `Attributes` + `CatalogEntities`.
  - Emits OTel span `tool.list_cde`.

- [ ] **T2.18** `GetEntityRelationships(string canonicalName) -> Task<IReadOnlyList<RelationshipResult>>`:
  - Resolves entity → `entityId`.
  - Queries `EntityRelationships` where `FromEntityId = entityId OR ToEntityId = entityId`.
  - Returns list of `{ FromEntity, ToEntity, RelationshipType, Description }`.
  - Emits OTel span `tool.get_entity_relationships`.

- [ ] **T2.19** Write unit tests for each tool (mock `CatalogDbContext` with `InMemoryDatabase`; mock `MongoExtensionRepository`). At least 3 test cases per tool.

**Acceptance:** `dotnet test --filter "FullyQualifiedName~CatalogToolsTests"` passes.

---

### Week 4, Day 4 — M.E.AI Function-Calling Loop

- [ ] **T2.20** Create `src/RagServer/Pipelines/Metadata/MetadataPipeline.cs`:
  - Builds an `AIFunction` list from the five tool methods using `AIFunctionFactory.Create(…)`.
  - Constructs system prompt:
    ```
    You are a metadata assistant with access to the company data catalog.
    Use the available tools to look up entity attributes, CDEs, and relationships.
    When both SQL and MongoDB data are relevant, call SQL tools first, then MongoDB.
    Always call ResolveEntity first to canonicalise entity names before calling other tools.
    Return a clear, well-formatted answer. If a tool returns no data, say so explicitly.
    ```
  - Calls `IChatClient.CompleteStreamingAsync` with `ChatOptions { Tools = aiTools }` through `LlmRequestQueue`.
  - Implements the tool-calling loop: while the response contains tool calls, execute them and feed results back as `ToolCallResultMessage`, up to `RAG_METADATA_MAX_TOOL_ROUNDS` rounds.
  - After the loop, streams the final answer.
  - Emits OTel span `rag.metadata_pipeline` with child spans per tool call round.
- [ ] **T2.21** Create `src/RagServer/Pipelines/Metadata/ToolCallLogger.cs` — middleware that logs each tool call name and arguments as a structured log entry (correlated to request trace ID).
- [ ] **T2.22** Wire `MetadataPipeline` into `ChatEndpoint` when `IntentRouter` returns `PipelineKind.Metadata`.

**Acceptance:** Ask "Give me all CDE attributes belonging to the Client Account entity." → model calls `ResolveEntity("Client Account")`, then `GetEntityAttributes("Client_Account", includeCde=true)`, returns formatted table; all tool calls visible as child spans in Aspire.

---

### Week 4, Day 5 — Tool-Call Middleware & Metrics

- [ ] **T2.23** Register a custom OTel metric counter `rag.tool_calls_total` (tags: `tool_name`, `pipeline`) in `Program.cs`.
- [ ] **T2.24** Instrument `MetadataPipeline` to increment `rag.tool_calls_total` on each tool invocation.
- [ ] **T2.25** Add metric `rag.tool_rounds_per_request` (histogram) recording total rounds per request.
- [ ] **T2.26** Verify metrics appear in Aspire Dashboard under the Metrics tab.

**Acceptance:** After 3 metadata queries, Aspire Dashboard shows non-zero `rag.tool_calls_total` with tool name breakdown.

---

### Week 5, Day 1 — MongoDB Catalog Ingestion

- [ ] **T2.27** Create `src/RagServer/Ingestion/Catalog/MongoExtensionIngester.cs`:
  - Polls MongoDB `EntityExtensions` collection for changes since last sync cursor.
  - Upserts extension field names into `catalog_terms` index (term_type=`extension_attribute`).
  - Updates `IngestionCursor` for source `"mongo-catalog"`.
- [ ] **T2.28** Schedule with `INGESTION_CATALOG_MONGO_CRON`.
- [ ] **T2.29** Add admin endpoint `POST /admin/reindex?source=catalog-sql|catalog-mongo`.

**Acceptance:** Extension attributes from MongoDB appear in `catalog_terms` index; retrievable via `ResolveEntity`.

---

### Week 5, Day 2 — Intent Router Refinement for UC-2

- [ ] **T2.30** Improve rule-based router for metadata vs data disambiguation:
  - `"attributes"`, `"fields"`, `"schema"`, `"cde"`, `"entity"`, `"column"` keywords → `Metadata` (unless followed by a filter phrase like `"where"` or `"that have"`).
  - `"give me all"`, `"list all records"`, `"show me records"` → `Data`.
  - Add 10 new test cases covering the UC-2 vs UC-3 boundary.
- [ ] **T2.31** Add model-fallback classification tests for ambiguous UC-2/UC-3 prompts (e.g. `"Show me all attributes of Client Account that are CDEs"` → should route to `Metadata`, not `Data`).

**Acceptance:** All intent router tests pass including new UC-2 boundary cases.

---

### Week 5, Days 3–4 — Golden Set UC-2 & Evaluation

- [ ] **T2.32** Seed `EvalQueries` with 30 UC-2 questions. Include:
  - 10 attribute listing queries (`"List all attributes of…"`, `"What fields does… have?"`)
  - 10 CDE queries (`"What are the CDEs for…?"`, `"Which attributes of… are critical data elements?"`)
  - 5 relationship queries (`"What entities are related to…?"`)
  - 5 extension attribute queries (`"Are there any additional annotations for…?"`)
- [ ] **T2.33** Extend `GoldenSetRunner` for UC-2 metrics:
  - **Tool selection exact match:** did the model call the expected tools in the expected order?
  - **Tool argument match:** were the arguments correct (e.g. `canonicalName` correctly resolved)?
- [ ] **T2.34** Run golden set; record results in `EvalResults` with `Environment=work-laptop`.
- [ ] **T2.35** If pass rate < 80%, diagnose:
  - Is `ResolveEntity` returning incorrect canonical names? → improve `catalog_terms` aliases.
  - Is the model skipping MongoDB after SQL? → strengthen system prompt ordering instruction.
  - Is the model hallucinating tool arguments? → add stricter JSON schema to tool definitions.

**Acceptance:** ≥80% exact-match tool selection on UC-2 golden set.

---

### Week 5, Day 5 — Blazor UI Updates for UC-2

- [ ] **T2.36** Update `Chat.razor`:
  - Show "Tool calls made: N" in the Demo Stats sidebar (read from response metadata).
  - For metadata answers, render a table if the response contains pipe-delimited markdown.
  - Show `pipeline=metadata` indicator.
- [ ] **T2.37** Add a "tool calls" expandable panel (debug mode only) showing each tool name + arguments + truncated result.

**Acceptance:** Metadata response renders a formatted table; tool call count shows in Demo Stats sidebar.

---

## Sprint 2 Definition of Done

- [ ] `catalog_terms` index populated with entities, attributes, CDEs, and extension fields.
- [ ] All five catalog tools implemented, tested, and emitting OTel spans.
- [ ] `MetadataPipeline` function-calling loop working end-to-end.
- [ ] SQL + MongoDB both queried in correct priority order.
- [ ] Tool-call metrics (`rag.tool_calls_total`) visible in Aspire.
- [ ] UC-2 golden set: 30 questions, ≥80% exact-match tool selection, results in `EvalResults`.
- [ ] Intent router correctly distinguishes UC-2 from UC-3 prompts.
- [ ] Blazor UI shows tool-call count and table-formatted metadata responses.
- [ ] All new `.env` variables documented in `.env.example`.
- [ ] `dotnet test` passes with zero failures.
