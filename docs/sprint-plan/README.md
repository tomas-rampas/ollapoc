# ollapoc — Implementation Plan

| | |
|---|---|
| **Version** | 1.0 |
| **Based on design** | `docs/AI-assistant.md` v0.5 |
| **Date** | April 2026 |
| **Total duration** | ~11 weeks (single engineer) |

---

## 1. Sequential Thinking Analysis

Sequential thinking forces us to reason step-by-step, deferring judgment until we understand the full causal chain.

### Step 1 — What is the terminal goal?

A working demo for senior management that proves a self-hosted small-model stack handles three distinct enterprise AI use cases at acceptable quality and latency, on developer-laptop hardware — and produces a GPU server procurement spec backed by measured data.

### Step 2 — What must be true immediately before that?

- All three pipelines (UC-1, UC-2, UC-3) must work end-to-end with real content.
- The evaluation harness must produce credible pass-rate numbers.
- The Aspire Dashboard trace view must be demo-ready.
- Both dev laptops must run the stack reliably.

### Step 3 — What does each pipeline depend on?

| Pipeline | Hard dependencies |
|---|---|
| UC-1 (Docs) | Ollama + bge-small + ES docs index + Confluence/Jira ingestion |
| UC-2 (Metadata) | Ollama function-calling + SQL Server catalog + MongoDB extension |
| UC-3 (Data) | UC-2's entity resolution + IR schema + IrToDslCompiler + ES operational index |

This gives a strict ordering: Foundation → UC-1 → UC-2 → UC-3 → Demo.

### Step 4 — Where are the highest-variance tasks?

1. **IR generation quality** — Qwen3 8B may not reliably produce valid `QuerySpec` JSON for complex queries.
2. **Hybrid retrieval relevance** — RRF tuning and chunk sizing may need iteration.
3. **Tool-calling reliability** — M.E.AI function-calling loop under real prompts.
4. **Ingestion throughput** — Atlassian rate limits and content quality unknown.
5. **Latency targets** — Empirical; all targets are aspirational until measured.

### Step 5 — What is the minimum path to a credible demo?

Even if UC-3 is rough, a demo showing UC-1 and UC-2 with live Aspire traces and the Demo Stats panel is still compelling. Therefore: **never let UC-3 work block demo readiness.**

### Step 6 — What cross-cutting concerns must be in from day one?

- OpenTelemetry instrumentation (every span matters for the demo trace view).
- Golden-set scaffolding (needed for measuring progress throughout).
- `.env`-driven configuration (required for switching models, ports, and credentials across environments without code changes).
- Streaming (perceived latency — must be in from the first chat response).

---

## 2. Five Whys Applied to Key Decisions

### 2.1 Why use an Intermediate Representation instead of generating ES DSL directly?

1. **Why?** — The model sometimes generates invalid DSL. → Because DSL is complex and its edge cases (date math, term-vs-keyword, nested fields) are hard to specify in a prompt.
2. **Why is DSL hard to specify?** — The full DSL surface is large; system prompts can't enumerate every case. → The model lacks enough signal to distinguish edge cases reliably.
3. **Why can't we fix it with a better prompt?** — Prompt tuning helps at the margin but doesn't eliminate failures at a 10B-parameter scale. → We need a deterministic layer to absorb the long tail.
4. **Why not just retry?** — Retries add latency (9 s → 22 s p50 on UC-3 with retry). Retries also fail if the model reproduces the same error. → We need a smaller, well-typed target the model can hit reliably.
5. **Why does a smaller target help?** — `QuerySpec` has fewer fields than full DSL, uses plain JSON, and its schema can be expressed as JSON Schema and embedded in the prompt. The C# compiler then handles all edge cases deterministically. → **Root cause addressed:** complexity moved out of the model into verifiable, testable C# code.

### 2.2 Why run inference locally (Ollama) rather than via cloud API?

1. **Why?** — Constraint: external API fallback is explicitly out of scope.
2. **Why is it out of scope?** — The data being queried (Confluence, Jira, SQL catalog) may contain PII and commercially sensitive information.
3. **Why does that prevent cloud API use?** — Sending document chunks and query results to a third-party API creates data residency and compliance exposure that hasn't been cleared.
4. **Why hasn't it been cleared?** — The POC needs to move fast; legal/compliance review of a cloud AI vendor would extend the timeline by months.
5. **Why is that a problem?** — The senior management demo has a fixed window. → **Root cause addressed:** local inference eliminates the compliance blocker, delivers the demo on schedule, and produces the GPU-server argument as a side effect.

### 2.3 Why Elasticsearch for both vector store and operational data?

1. **Why?** — Avoid adding a separate vector database (Qdrant, Weaviate) alongside the existing ES operational cluster.
2. **Why avoid a separate vector database?** — Ops overhead: another service to run, monitor, and back up.
3. **Why is ops overhead a problem for a POC?** — Single-engineer build; every extra service is a week of integration.
4. **Why not use pgvector on SQL Server instead?** — SQL Server is already used for the catalog; mixing RAG vectors into the same DB creates schema noise and complicates backup/restore.
5. **Why is ES 9.x the right choice specifically?** — ES 9.x (Lucene 10) brings improved HNSW performance, native RRF retriever (one API call instead of two), and BBQ for future scale. → **Root cause addressed:** ES is already operational infrastructure, 9.x offers the best tradeoff, and dual-role use keeps the stack minimal.

---

## 3. Milestones

| # | Milestone | End of Sprint | Evidence of completion |
|---|---|---|---|
| **M0** | Infrastructure live on both laptops | Sprint 0 | `docker compose up` → chat smoke test → OTLP trace visible in Aspire |
| **M1** | UC-1 Documentation Q&A working | Sprint 1 | 30 golden-set questions pass at ≥70% recall@5; citations render |
| **M2** | UC-2 Metadata Queries working | Sprint 2 | 30 golden-set questions pass at ≥80% exact-match tool selection |
| **M3** | UC-3 Data Queries — IR generation working | Sprint 3 | IR validates against JSON Schema for all 30 golden-set queries |
| **M4** | UC-3 Data Queries — end-to-end working | Sprint 4 | DSL executes on ES; pass rate ≥70%; Demo Stats panel live |
| **M5** | Demo-ready on both laptops | Sprint 5 | Full demo rehearsal passed; hardware spec documented |

---

## 4. Sprint Overview

| Sprint | Name | Duration | Key deliverables |
|---|---|---|---|
| [Sprint 0](sprint-00-foundation.md) | Foundation & Infrastructure | Week 1 | Docker Compose, Ollama, ES, Aspire, M.E.AI wired, OIDC, golden-set scaffolding |
| [Sprint 1](sprint-01-docs-pipeline.md) | UC-1 Documentation Q&A | Weeks 2–3 | Ingestion, chunking, embedding, RRF retrieval, grounded generation, citations, Blazor UI |
| [Sprint 2](sprint-02-metadata-pipeline.md) | UC-2 Metadata Queries | Weeks 4–5 | EF Core catalog, MongoDB tools, M.E.AI function-calling loop, ResolveEntity |
| [Sprint 3](sprint-03-data-pipeline-ir.md) | UC-3 IR Layer | Weeks 6–7 | QuerySpec IR, JSON Schema, schema-card ingestion, NL→IR prompt, validation |
| [Sprint 4](sprint-04-data-pipeline-compiler.md) | UC-3 Compiler + Eval | Weeks 8–9 | IrToDslCompiler, retry loop, A/B model test, Demo Stats panel, full golden set |
| [Sprint 5](sprint-05-demo-prep.md) | Demo Preparation | Weeks 10–11 | Demo script, latency profiling, GPU spec, stakeholder rehearsal |

---

## 5. Configuration Strategy

All environment-specific and secret values are in **`.env`** at the project root. `docker-compose.yml` and `appsettings.json` reference these variables only — no hardcoded values anywhere.

See [`.env.example`](../../.env.example) at the repo root for the full variable reference.

### Variable naming convention

```
<SERVICE>_<CONCERN>_<DETAIL>
```

Examples: `ES_URL`, `OLLAMA_CHAT_MODEL`, `CONFLUENCE_API_TOKEN`, `RAG_QUEUE_MAX_DEPTH`.

---

## 6. Definition of Done (global)

A task is **Done** when:

- [ ] Code compiles with zero warnings (`dotnet build -warnaserror`).
- [ ] All relevant unit tests pass (`dotnet test`).
- [ ] No secrets or hardcoded values — all configuration reads from environment / `IOptions<T>`.
- [ ] OpenTelemetry spans emitted for every externally-observable operation.
- [ ] The relevant golden-set queries produce the expected output.
- [ ] `docker compose up` still works from a clean state.
- [ ] The sprint MD file task is checked off.

---

## 7. Risk Register (summary)

| Risk | Sprint affected | Mitigation |
|---|---|---|
| Qwen3 8B IR quality insufficient | Sprint 3–4 | A/B vs 14B; fallback prompt with more schema context |
| Hybrid retrieval relevance poor | Sprint 1 | Chunk size tuning; add `bge-reranker-base` sidecar |
| Atlassian rate limits | Sprint 1 | Token-bucket throttling; off-hours backfill |
| Schema drift breaks UC-3 silently | Sprint 3–4 | Schema-card refresh on mapping change; delta alert |
| Latency targets missed | Sprint 4–5 | Profiling + levers in §10.5 of design doc |
| Demo laptop divergence | Sprint 5 | Sync both laptops before rehearsal; record backup video |

---

*Sprint files: each sprint MD is self-contained and can be executed independently once its prerequisites are met.*
