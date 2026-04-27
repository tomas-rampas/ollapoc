# ollapoc Demo Runbook

A step-by-step guide to running the live AI Knowledge Assistant demo on a developer laptop with NVIDIA GPU.

---

## 1. Prerequisites

- **Git:** Clone the repository: `git clone https://github.com/...ollapoc.git`
- **Docker & Docker Compose:** Latest stable (ships with Docker Desktop on Windows/Mac or standalone on Linux)
- **NVIDIA GPU on host machine:** RTX 2000 Blackwell (8 GB VRAM) or better (RTX 3080 16 GB recommended)
- **NVIDIA Container Toolkit:** Pre-installed on demo laptops; verify with:
  ```bash
  docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
  ```
  Should print a GPU table. If this fails, the toolkit is not available.
- **.env file:** Copy `.env.example` to `.env` and customise:
  ```bash
  cp .env.example .env
  ```
  Key variables:
  - `OLLAMA_NUM_PARALLEL=1` (preserve single-request queue)
  - `OLLAMA_NUM_GPU=999` (offload all layers to GPU if VRAM allows)
  - `SKIP_AUTH=true` (for dev/demo; must also have `IsDevelopment()` true)
  - `AB_TEST_ENABLED=false` (set to `true` for 8B vs 14B side-by-side on home laptop only)
  - `DEMO_MODE=true` and `DEMO_STATS_ENABLED=true` (enable stats panel)

---

## 2. Startup Sequence

### 2.1 Start Infrastructure (all 7 services)

```bash
docker compose up -d
```

Wait for all containers to become healthy. Verify:

```bash
docker compose ps
```

Expected output: all 7 services (`ollama`, `elasticsearch`, `mssql`, `mongodb`, `confluence-mock`, `rag-server`, `aspire-dashboard`) with status `Up (healthy)`.

> **Note:** `mssql` and `mongodb` perform first-boot initialisation (schema creation and seed data). Allow up to 60 seconds on a cold start.

### 2.2 Pull LLM and Embedding Models into Ollama

```bash
# Chat model (Qwen3 8B, ~5 GB after quantization)
docker exec ollama ollama pull qwen3:8b

# Embedding model (bge-small-en-v1.5, ~300 MB)
docker exec ollama ollama pull bge-small-en-v1.5
```

Both pulls may take 5–10 minutes depending on network. You can proceed to the next step while they download in the background.

### 2.3 Start the RAG Server

From the repository root or from `src/RagServer/`:

```bash
dotnet run
```

Wait for the message `Now listening on: http://localhost:8080`. The server is ready for requests.

### 2.4 Open the Chat UI and Aspire Dashboard

In your browser:

- **Chat UI:** `http://localhost:8080/` — Blazor server component with streaming messages and stats panel
- **Aspire Dashboard:** `http://localhost:18888` — OpenTelemetry traces, metrics, logs
- **Confluence mock:** `http://localhost:8090/wiki/spaces/MDM/pages/1002` — verify citation links are reachable

Leave both tabs open during the demo. Switch to the dashboard tab when you want to show the trace timeline.

### 2.5 First-time only: Index Confluence Mock Pages

Run once after first boot (or after `docker compose down -v`):

```bash
curl -X POST http://localhost:8080/admin/reindex?source=confluence
```

Wait ~30 seconds. This crawls the 15 MDM documentation pages from the Confluence mock and indexes them into Elasticsearch. The `docs` pipeline (UC-1) will return empty results until this step completes.

### 2.6 Reset after SQL schema changes

If the SQL Server catalog schema was changed (new entities, attributes, or EF model changes), drop the persistent volumes and restart:

```bash
docker compose down -v
docker compose up -d
```

Then repeat steps 2.2–2.5. The `mssql` container re-runs `EnsureCreatedAsync` on first boot and seeds all 10 MDM entities and 162 attributes.

---

## 3. Pre-Warming the Embedding Cache

Before the live demo, pre-warm the embedding cache by routing all 18 curated demo queries through the `IntentRouter`. This ensures the first set of user queries snap instantly.

```bash
curl -X POST http://localhost:8080/demo/warmup
```

Expected response (200 OK) — 18 queries, 6 per pipeline:

```json
{
  "warmed": 18,
  "queries": [
    { "query": "What is a counterparty in financial services?",                  "pipeline": "Docs" },
    { "query": "How does KYC onboarding work?",                                  "pipeline": "Docs" },
    { "query": "What is a DVP settlement instruction?",                          "pipeline": "Docs" },
    { "query": "What are trading books and banking books?",                      "pipeline": "Docs" },
    { "query": "How is FATCA status determined for a counterparty?",             "pipeline": "Docs" },
    { "query": "What is a Critical Data Element?",                               "pipeline": "Docs" },
    { "query": "What are the mandatory attributes for Book?",                    "pipeline": "Metadata" },
    { "query": "Who is the data owner of Legal Name for Client Account?",        "pipeline": "Metadata" },
    { "query": "What rules are defined for Counterparty?",                       "pipeline": "Metadata" },
    { "query": "What are the mandatory rules for Settlement Instruction?",       "pipeline": "Metadata" },
    { "query": "What critical data elements does Currency have?",                "pipeline": "Metadata" },
    { "query": "What relationships does Counterparty have?",                     "pipeline": "Metadata" },
    { "query": "Show me the top 10 trades with status FAILED sorted by amount descending", "pipeline": "Data" },
    { "query": "How many trades were settled last month?",                       "pipeline": "Data" },
    { "query": "List all counterparties with more than 5 active trades",         "pipeline": "Data" },
    { "query": "What were the total notional amounts by currency last quarter?", "pipeline": "Data" },
    { "query": "Show me all settlement failures in the last 7 days",            "pipeline": "Data" },
    { "query": "Find all open trades where notional exceeds 1 million",         "pipeline": "Data" }
  ]
}
```

The embedding cache is now primed with these queries. Subsequent identical queries are served from the in-process LRU cache.

---

## 4. Demo Script — Sample Queries by Pipeline

### UC-1: Documentation Q&A (Confluence MDM pages)

Demonstrates RAG: hybrid BM25 + kNN search → grounded generation → **clickable citation links** to the Confluence mock.

**Query 1:**
```
What is a counterparty in financial services?
```
**Expected:** Definition from the Counterparty Data Model page (`http://localhost:8090/wiki/spaces/MDM/pages/1002`). Citation link appears below the answer.

**Query 2:**
```
How does KYC onboarding work?
```
**Expected:** Onboarding workflow steps from the KYC page, with citation link to page 1009.

**Query 3:**
```
How is FATCA status determined for a counterparty?
```
**Expected:** FATCA classification rules from the FATCA and Tax Compliance page (1014), with citation.

> Citation links are always visible below each Docs answer. Clicking them opens the rendered page from the Confluence mock.

### UC-2: Metadata Queries (SQL Server + MongoDB Catalog)

Demonstrates function-calling: model invokes 7 catalog tools → SQL / MongoDB queries → structured results.

**Query 1 (mandatory attributes):**
```
What are the mandatory attributes for Book?
```
**Expected:** List of `IsMandatory=true` attributes from the Book entity (BookCode, LegalEntity, AssetClass, etc.). Stats panel shows Tool Calls ≥ 2 (`ResolveEntity` + `GetEntityAttributes`).

**Query 2 (business rules):**
```
What rules are defined for Counterparty?
```
**Expected:** 7 business rules (MANDATORY + CONDITIONAL) from MongoDB `entity_rules` collection. Stats panel shows Tool Calls ≥ 2 (`ResolveEntity` + `GetEntityRules`).

**Query 3 (complex attributes):**
```
What source systems does Counterparty use?
```
**Expected:** system_map children (MUREX, BLOOMBERG, SUMMIT, GBS) via `GetChildAttributes`. Demonstrates complex multi-value attribute drill-down.

### UC-3: Data Queries (NL → IR → Elasticsearch DSL)

Demonstrates the hardest path: natural language → typed `QuerySpec` IR → C# DSL compiler → ES `_search` → formatted answer.

**Query 1 (filter + sort):**
```
Show me the top 10 trades with status FAILED sorted by amount descending
```
**Expected:** Table of up to 10 rows. Stats panel shows `IR First Try = yes` and `Result Rows`.

**Query 2 (time-range aggregation):**
```
How many trades were settled last month?
```
**Expected:** Single number. Demonstrates `TimeRange` with `RelativePeriod = last_month`.

**Query 3 (threshold filter):**
```
Find all open trades where notional exceeds 1 million
```
**Expected:** Trade list with notional values. Demonstrates numeric range filter compilation.

---

## 5. Observability — Showing the Trace Timeline

During the demo, a second browser tab (Aspire Dashboard at `:18888`) provides live observability proof.

### 5.1 Pre-Demo Setup

1. Open `http://localhost:18888` in a second browser tab before the demo starts.
2. Go to **Traces** view.
3. Set the time filter to "Last 5 minutes" so recent requests are visible.

### 5.2 During Demo

After running a query in the Chat tab:

1. Switch to the Aspire Dashboard tab.
2. Click the most recent trace (topmost in the list) to expand its timeline.
3. **The trace shows:**
   - **HTTP** — entire request from Chat UI to server response.
   - **Router** — intent classification (docs/metadata/data).
   - **Embedding** — query vectorization via `bge-small-en-v1.5` (1–2 ms GPU time).
   - **Elasticsearch retrieval** — BM25 + kNN search or DSL compilation (varies 10–100 ms).
   - **LLM call** — Qwen3 inference (typically 1–3 s for chat, 200–500 ms for IR generation).
   - **Tool calls** (UC-2 only) — SQL/Mongo catalog lookups, stacked sequentially.
   - **Formatting** — result assembly and NL rendering.

This visual proof demonstrates that **the system is doing real, measurable work** — not magic, but orchestrated, timed operations. Senior stakeholders value this transparency.

### 5.3 Metrics View (Optional)

Click **Metrics** in the dashboard to see custom RagMetrics:

- `rag.request_duration_ms` — histogram per pipeline (p50, p95, p99).
- `rag.tokens_per_second` — LLM throughput sampled per request.
- `rag.ir_first_try_success` — Data pipeline validation success rate.
- `rag.queue_depth` — real-time request queue backlog (should be 0–1 during demo).

---

## 6. Troubleshooting

### 6.1 "Model not loaded" / `HTTP 500` during first query

**Cause:** Ollama is still pulling the chat model or embedding model.

**Solution:**
```bash
docker exec ollama ollama list
```
Should show `qwen3:8b` and `bge-small-en-v1.5` with a date. If either is missing, re-run the pull commands in §2.2.

### 6.2 Elasticsearch not ready (`ConnectionRefused`, `HTTP 503`)

**Cause:** ES container is initializing (typical on first startup, ~20 seconds).

**Solution:**
```bash
docker logs elasticsearch | tail -20
```
Wait for `"started"` message. Then retry the query.

Alternatively, restart the entire stack:
```bash
docker compose restart
```

### 6.3 `HTTP 429` (Too Many Requests)

**Cause:** Request queue is full. The demo user sent requests faster than the LLM can process them.

**Mitigation:**
- Wait 10–30 seconds for the queue to drain.
- In production, tune `OLLAMA_NUM_PARALLEL` and queue size in `appsettings.json` (`RagOptions.QueueSize`).
- For the demo, this is expected under stress; explain that the server is rate-limiting to preserve quality.

### 6.4 Chat UI shows "internal_error"

**Cause:** Server-side exception (usually in LLM call, ES query, or tool invocation).

**Solution:**
```bash
# View RAG server logs
dotnet run
```
Logs in the terminal window will show the full exception. Common causes:
- ES mapping mismatch (UC-3 entity not in `schema_cards` index).
- Tool argument length exceeded (max 512 chars per arg in UC-2).
- Ollama out of memory (VRAM exhaustion; check `nvtop`).

### 6.5 Demo Stats panel not visible

**Cause:** `DEMO_STATS_ENABLED` set to `false` in `.env` or `appsettings.json`.

**Solution:**
```bash
# In .env or via env var before dotnet run:
export DEMO_STATS_ENABLED=true
dotnet run
```

Refresh the Chat UI browser tab. The stats panel sidebar should appear.

### 6.6 GPU not being used (Ollama using CPU only)

**Cause:** NVIDIA Container Toolkit not available, or `--gpus all` not passed to Ollama container.

**Solution:**
```bash
# Verify toolkit:
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi

# Check Ollama GPU assignment:
docker logs ollama | grep -i gpu
```

If logs show `"GPU not available"`, the toolkit is missing. On Windows, ensure WSL2 is active and Docker Desktop uses WSL2 backend.

### 6.7 First query is slow, subsequent queries snap

**Expected behavior.** The embedding cache is warming up. After pre-warming (§3), the first few demo queries should be ~1–2 seconds. Identical queries thereafter are served from the in-process LRU (typically <50 ms).

---

## 7. Post-Demo Cleanup

```bash
# Stop and remove containers
docker compose down

# Free GPU VRAM (especially if switching to CPU-only testing)
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
```

---

## 8. Quick Reference: Environment Variables

| Variable | Default | Purpose |
|---|---|---|
| `SKIP_AUTH` | `false` | When `true` and in development, skip OIDC auth |
| `DEMO_MODE` | `false` | Enable demo-specific features |
| `DEMO_STATS_ENABLED` | `true` | Show stats panel in Chat.razor |
| `DEMO_DEBUG_PANEL` | `false` | Render citation details in messages |
| `AB_TEST_ENABLED` | `false` | Round-robin between MODEL_A and MODEL_B |
| `AB_TEST_MODEL_A` | `qwen3:8b` | Model A for A/B testing |
| `AB_TEST_MODEL_B` | `qwen3:14b` | Model B for A/B testing |
| `OLLAMA_NUM_PARALLEL` | `1` | Max concurrent Ollama inference calls |
| `OLLAMA_NUM_GPU` | `(auto)` | Layers to offload to GPU (999 = all) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | (auto) | Dashboard OTLP endpoint |

---

*See `docs/AI-assistant.md` for full architecture and design rationale.*
