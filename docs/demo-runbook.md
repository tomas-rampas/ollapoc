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

### 2.1 Start Infrastructure (Ollama, Elasticsearch, Aspire Dashboard)

```bash
docker compose up -d
```

Wait for all containers to become healthy (~30 seconds). Verify:

```bash
docker compose ps
```

Expected output: all 4 services with status `Up`.

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

Leave both tabs open during the demo. Switch to the dashboard tab when you want to show the trace timeline.

---

## 3. Pre-Warming the Embedding Cache

Before the live demo, pre-warm the embedding cache by routing all 9 demo queries through the `IntentRouter`. This ensures the first set of user queries snap instantly.

```bash
curl -X POST http://localhost:8080/demo/warmup
```

Expected response (200 OK):

```json
{
  "warmed": 9,
  "queries": [
    { "query": "What is the settlement fail reason for STP trades?", "pipeline": "Docs" },
    { "query": "How does the reconciliation process work?", "pipeline": "Docs" },
    { "query": "What counterparty data is required for trade booking?", "pipeline": "Docs" },
    { "query": "What attributes does the Trade entity have?", "pipeline": "Metadata" },
    { "query": "Show me all critical data elements for Settlement", "pipeline": "Metadata" },
    { "query": "What relationships does Counterparty have?", "pipeline": "Metadata" },
    { "query": "Show me the top 10 trades with status FAILED sorted by amount descending", "pipeline": "Data" },
    { "query": "How many trades were settled last month?", "pipeline": "Data" },
    { "query": "List all counterparties with more than 5 active trades", "pipeline": "Data" }
  ]
}
```

The embedding cache is now primed with these common queries. Subsequent identical queries will be served from the in-process LRU cache.

---

## 4. Demo Script — Sample Queries by Pipeline

### UC-1: Documentation Q&A (Confluence/Jira)

Demonstrates RAG: hybrid search (BM25 + dense vector) → grounded generation → citations.

**Query 1 (foundational):**
```
What is a counterparty and what is its purpose?
```
**Expected answer type:** Glossary-style definition extracted from Confluence documentation. Should cite the source page.

**Query 2 (operational):**
```
How does the reconciliation process work?
```
**Expected answer type:** Step-by-step workflow from ingested Jira workflows or Confluence process docs.

**Query 3 (integration):**
```
What data fields are required to book a trade?
```
**Expected answer type:** List of mandatory fields with types (pulled from schema docs or Confluence pages).

### UC-2: Metadata Queries (SQL + MongoDB Catalog)

Demonstrates function-calling: model invokes catalog tools → SQL/Mongo queries → structured results.

**Query 1 (entity introspection):**
```
What attributes does the Trade entity have?
```
**Expected answer type:** Bulleted list of field names, types, and nullability from SQL Server catalog.

**Query 2 (CDE discovery):**
```
Show me all critical data elements for Settlement
```
**Expected answer type:** Table of CDE fields, classifications, and descriptions from the Catalog database.

**Query 3 (relationships):**
```
What relationships does Counterparty have?
```
**Expected answer type:** Entity relationship diagram or text description (PK → FK links) to Trade, Settlement, etc.

### UC-3: Data Queries (NL → IR → ES DSL)

Demonstrates the hardest path: natural language translated to typed Elasticsearch DSL, executed, formatted.

**Query 1 (sorting & filtering):**
```
Show me the top 10 trades with status FAILED sorted by amount descending
```
**Expected answer type:** Table with 10 rows (or fewer if fewer than 10 exist). Show columns: ID, Status, Amount, Trade Date. Pipeline should emit `IR First Try = yes` if the query validated on the first attempt.

**Query 2 (aggregation):**
```
How many trades were settled last month?
```
**Expected answer type:** Single number with explanation, e.g., "327 trades were settled in March 2026." Demonstrates `TimeRange` with `RelativePeriod` token `last_month`.

**Query 3 (complex filtering):**
```
List all counterparties with more than 5 active trades
```
**Expected answer type:** Counterparty names and active trade counts. Demonstrates nested filtering and aggregation.

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
