# Sprint 5 — Demo Preparation & Senior Management Presentation

| | |
|---|---|
| **Sprint** | 5 |
| **Duration** | Weeks 10–11 (10 days) |
| **Milestone** | M5 — Demo-ready on both laptops; GPU server spec delivered |
| **Goal** | Polish the demo experience, produce the GPU server procurement spec backed by measured data, rehearse with stakeholders, and ensure a flawless live demo. |
| **Depends on** | Sprint 4 complete (M4). Full golden set results available. |

---

## Prerequisites

- Sprint 4 all Done items checked.
- A/B results (8B vs 14B) documented.
- Latency and token-per-second numbers collected from both laptops.
- Demo date and audience confirmed (open question §13.3 in design doc).
- Both laptops charged and connected to the same network as the demo data sources.

---

## Environment Variables (Sprint 5 additions to `.env`)

```dotenv
# ── Demo mode ────────────────────────────────────────────────────────────────
DEMO_MODE=true
DEMO_STATS_ENABLED=true
DEMO_DEBUG_PANEL=false
DEMO_PRE_WARMED_QUERIES=true

# ── Aspire Dashboard (demo binding) ──────────────────────────────────────────
ASPIRE_DASHBOARD_UI_PORT=18888
ASPIRE_BIND_LOCALHOST_ONLY=true

# ── Model selection for demo ──────────────────────────────────────────────────
# Work laptop (8 GB): override with qwen3:8b
# Home laptop (16 GB): override with qwen3:14b
# Both set via environment, not hardcoded:
OLLAMA_CHAT_MODEL=qwen3:8b
```

---

## Tasks

### Week 10, Day 1 — Demo Content Curation

- [ ] **T5.1** Select 3 UC-1 demo queries from the golden set:
  - One definitional (`"What is a counterparty?"`)
  - One procedural (`"How do I onboard a new client?"`)
  - One that shows Jira (recent issue context or a process question)
  - All must return in ≤5 s p50 on the work laptop (Qwen3 8B). Verify timing.
- [ ] **T5.2** Select 3 UC-2 demo queries:
  - One attribute listing with CDE flag (`"Give me all CDE attributes of Client Account"`)
  - One with MongoDB extension attributes
  - One showing relationships (`"What entities are related to Counterparty?"`)
  - All must resolve correctly and return in ≤6 s. Verify timing.
- [ ] **T5.3** Select 3 UC-3 demo queries:
  - One simple filter + date range (`"All counterparties updated today"`)
  - One where 14B clearly outperforms 8B (complex aggregation or nested filter)
  - One intentional failure (model gets it wrong on first try → retry succeeds) — the honest failure case
  - All success cases must return in ≤10 s on their target model. Verify timing.
- [ ] **T5.4** Create `src/DemoQueries.json` (not checked into GitLab main — store on a demo branch or local config) with all 9 curated queries and their expected answer summaries.
- [ ] **T5.5** Add a `/demo/warmup` endpoint (admin-only) that sends all 9 demo queries through the pipeline to warm the embedding cache and model context. Trigger this 5 minutes before demo starts.

**Acceptance:** All 9 queries tested on both laptops; timing verified; warmup endpoint functional.

---

### Week 10, Day 2 — Latency Profiling & GPU Server Spec

This is a deliverable, not a polish task.

- [ ] **T5.6** Run the 9 demo queries 5 times each on the work laptop (Qwen3 8B). Record from Aspire traces:
  - `rag.request_duration_ms` p50 and p95 per pipeline.
  - `rag.tokens_per_second` mean.
  - VRAM used (from `nvidia-smi` during inference).
- [ ] **T5.7** Run the same on the home laptop (Qwen3 14B). Record the same metrics.
- [ ] **T5.8** Create `docs/gpu-server-spec.md` with the following structure:

  ```markdown
  # Production GPU Server Specification

  ## Measured Performance (POC)
  | Environment | Model | Pipeline | p50 (ms) | p95 (ms) | tok/s | VRAM (GB) |
  |---|---|---|---|---|---|---|
  | Work laptop (RTX PRO 2000 Blackwell, 8 GB) | Qwen3 8B | UC-1 | ... | ... | ... | ... |
  ...

  ## Requirements for Production Pilot
  - Concurrency target: 5–20 concurrent users, 1–3 simultaneous in-flight LLM calls.
  - Quality floor: Qwen3 14B Q4_K_M or better for UC-3.
  - Latency ceiling: UC-3 no-retry p50 ≤ 8 s.
  - VRAM headroom: ≥4 GB above model size for KV cache.

  ## Recommended Configurations

  ### Option A — Single-GPU Workstation (recommended minimum)
  - GPU: RTX PRO 4000 Blackwell or RTX 5000 Ada (24–32 GB VRAM)
  - RAM: 64 GB
  - Storage: 1 TB NVMe
  - Rationale: ...
  - Estimated cost: £X–Y

  ### Option B — Single-GPU Server (recommended for scale)
  - GPU: NVIDIA L40S (48 GB VRAM)
  - RAM: 128 GB
  - Storage: 2 TB NVMe
  - Rationale: ...
  - Estimated cost: £X–Y

  ### Option C — CPU-only fallback (if GPU budget denied)
  - Server: 64 GB RAM, 16-core CPU
  - Model: Qwen3 4B Q4_K_M
  - Expected degradation: UC-3 p50 ~15 s vs 5 s on GPU
  ```

- [ ] **T5.9** Have the spec reviewed by the engineer; sign off the numbers before the demo.

**Acceptance:** `docs/gpu-server-spec.md` committed with real measured numbers, not estimates.

---

### Week 10, Day 3 — Aspire Dashboard Demo Configuration

- [ ] **T5.10** Pre-configure Aspire Dashboard for the demo:
  - Create a saved trace query: `service.name = rag-server AND duration > 1000ms`.
  - Screenshot the trace timeline for one UC-3 query (IR → compile → ES validate → ES search → format) and save as `docs/aspire-trace-demo.png`.
  - Identify which 3–4 metrics to highlight live during the demo (latency, tokens/s, queue depth, IR success rate).
- [ ] **T5.11** Add instructions to `docs/demo-runbook.md`:
  - How to open the Aspire Dashboard at `:18888`.
  - How to navigate to the Traces view and filter to last 5 minutes.
  - How to click on a UC-3 trace and expand child spans.
  - How to switch to the Metrics view and point to `rag.tokens_per_second`.
- [ ] **T5.12** Bind Aspire Dashboard to `localhost` only (`ASPIRE_BIND_LOCALHOST_ONLY=true`) for the demo — prevents audience from accessing it on the same network.

**Acceptance:** `docs/demo-runbook.md` exists; Aspire Dashboard navigable in under 30 seconds by following the runbook.

---

### Week 10, Day 4 — Demo Runbook & Failover Plan

- [ ] **T5.13** Create `docs/demo-runbook.md` (or extend from T5.11) with step-by-step demo script:

  **Pre-demo checklist (30 minutes before):**
  - [ ] `docker compose up -d` on primary laptop → all containers `healthy`.
  - [ ] `/demo/warmup` called → 9 queries warm; embedding cache populated.
  - [ ] `nvidia-smi` running in a side terminal — GPU utilisation visible.
  - [ ] Aspire Dashboard open in browser tab 2, filtered to "last 15 minutes", Traces view.
  - [ ] Blazor UI open in browser tab 1 — login via OIDC.
  - [ ] Backup laptop powered on, `docker compose up -d` completed, same pre-checks done.
  - [ ] Backup screen recording ready (5-minute walkthrough of all 9 queries).

  **Demo flow (25 minutes):**
  1. (2 min) Hardware framing slide.
  2. (5 min) UC-3 — "All counterparties updated today" — live trace in Aspire.
  3. (5 min) UC-3 — 8B vs 14B comparison — same complex query on both laptops (or A/B via config).
  4. (5 min) UC-1 — Documentation Q&A with citations.
  5. (4 min) UC-2 — Metadata with tool calls shown in demo stats.
  6. (2 min) Intentional failure case + retry.
  7. (2 min) GPU server spec reveal.

  **Failover triggers and responses:**
  - Ollama hangs: `docker restart ollama`; apologise, continue on backup laptop.
  - ES not responding: show trace of healthy ES spans from earlier; explain cached results.
  - Network/OIDC failure: use `SKIP_AUTH=true` override; explain POC auth bypass.
  - Demo laptop failure: switch to backup laptop (identical state).
  - Total failure: play backup screen recording.

- [ ] **T5.14** Write `docs/demo-slides-outline.md` — 6-slide structure for the hardware framing and GPU spec reveal.

**Acceptance:** Runbook reviewed by a colleague; failover scenarios tested (simulate Ollama restart during a query).

---

### Week 10, Day 5 — Blazor UI Polish

- [ ] **T5.15** UI polish checklist:
  - [ ] Loading spinner while streaming (animated dots or progress bar).
  - [ ] Clear "Pipeline: docs / metadata / data" badge visible on every response.
  - [ ] Model badge updates in real time when model is changed via `OLLAMA_CHAT_MODEL`.
  - [ ] Demo Stats panel always visible on medium/large screens (not collapsed by default).
  - [ ] Citation links open in a new tab.
  - [ ] Error messages (queue full, ES unreachable) shown in a non-alarming banner (not raw exception text).
  - [ ] Session is cleared cleanly on page refresh (no orphaned streaming connections).
- [ ] **T5.16** Test the UI at 1920×1080 (presentation resolution) — check font sizes and panel layout.
- [ ] **T5.17** Disable debug panels (`DEMO_DEBUG_PANEL=false`) so retrieved chunks and raw tool results are hidden from the demo audience.

**Acceptance:** UI tested at demo resolution; debug panels hidden; no raw error stack traces visible.

---

### Week 11, Day 1 — Both Laptops Synchronisation

- [ ] **T5.18** Sync both laptops to identical state:
  - Same Git commit (tagged `demo-v1`).
  - Same `.env` (except `OLLAMA_CHAT_MODEL`: 8b on work laptop, 14b on home laptop).
  - Same Ollama models pulled and verified.
  - Same ES data snapshot (export from work laptop, import on home laptop via `elasticdump` or snapshot API).
  - Both laptops pass the pre-demo checklist from T5.13.
- [ ] **T5.19** Run NVIDIA Container Toolkit verification on both laptops:
  ```bash
  docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi
  ```
- [ ] **T5.20** Run the full 9 demo queries on both laptops; verify timing and answer quality match expectations.

**Acceptance:** Both laptops independently pass the full pre-demo checklist.

---

### Week 11, Day 2 — Stakeholder Rehearsal

- [ ] **T5.21** Conduct a full dress rehearsal with at least one observer (ideally the target demo audience contact or a technical manager).
- [ ] **T5.22** Record timing for each demo segment against the runbook.
- [ ] **T5.23** Document feedback from the rehearsal; fix any issues found.
- [ ] **T5.24** If UC-3 latency feels too slow during the demo:
  - Pre-warm with the exact demo query 30 minutes before (KV cache warm).
  - Or switch to a lower-complexity demo query that completes in ≤7 s.
  - Do NOT switch to a faster model without re-running the golden set.

**Acceptance:** Dress rehearsal completed; all 25 demo minutes within timing; no blocking issues outstanding.

---

### Week 11, Days 3–4 — Final Golden Set & Regression

- [ ] **T5.25** Run the complete 90-query golden set one final time on both laptops.
- [ ] **T5.26** Verify no regression vs Sprint 4 baseline (pass rates must not have dropped by >5 pp on any pipeline).
- [ ] **T5.27** Archive final `EvalResults` export as `docs/eval-results-final.csv`.
- [ ] **T5.28** Update `docs/gpu-server-spec.md` with final measured numbers if they differ from T5.6 results.

**Acceptance:** Final golden set report committed; no regressions; spec numbers locked.

---

### Week 11, Day 5 — Documentation & Handover

- [ ] **T5.29** Update `docs/AI-assistant.md` (the design doc) with:
  - Final measured latencies (fill in the aspirational table with actual p50/p95).
  - IR first-try validity rates (8B and 14B).
  - ES recall@5 for UC-1.
  - Tool selection accuracy for UC-2.
- [ ] **T5.30** Create `docs/next-steps.md` outlining Phase 2 items:
  - ACL-aware retrieval.
  - Conversation persistence.
  - Reranker (bge-reranker-base sidecar).
  - Production GPU server setup.
  - Grafana/Tempo for production observability.
  - Fine-tuning (only if base-model quality bottleneck confirmed).
- [ ] **T5.31** Tag the repo `demo-v1`; push to GitLab.
- [ ] **T5.32** Verify CI pipeline passes on the tagged commit.

**Acceptance:** `demo-v1` tag pushed; `docs/AI-assistant.md` updated with actuals; `docs/next-steps.md` committed.

---

## Sprint 5 Definition of Done

- [ ] 9 demo queries curated, timed, and verified on both laptops.
- [ ] `/demo/warmup` endpoint functional.
- [ ] `docs/gpu-server-spec.md` with real measured numbers and 3 hardware options.
- [ ] `docs/demo-runbook.md` with step-by-step script and failover plan.
- [ ] Aspire Dashboard pre-configured; trace screenshot saved.
- [ ] Both laptops synchronised and passing pre-demo checklist.
- [ ] Dress rehearsal completed; no blocking issues.
- [ ] Final 90-query golden set run; no regressions; results archived.
- [ ] `docs/AI-assistant.md` updated with actual performance data.
- [ ] `demo-v1` tag pushed to GitLab.
- [ ] CI pipeline green on `demo-v1`.
