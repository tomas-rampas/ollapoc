# ollapoc — AI Knowledge Assistant POC

A self-hosted AI assistant that lets business users query enterprise data, metadata, and documentation in natural language. Three pipelines run over a financial Reference and Master Data Management (RMDM) dataset:

| Pipeline | Use Case | How it works |
|---|---|---|
| **Docs (UC-1)** | *"What is a DVP settlement instruction?"* | RAG over 15 MDM Confluence pages — hybrid BM25 + kNN search → grounded Qwen3 answer with citation links |
| **Metadata (UC-2)** | *"What are the mandatory attributes for Book?"* | Tool-calling loop against SQL Server catalog + MongoDB business rules — 7 tools, up to 10 turns |
| **Data (UC-3)** | *"Show me all settlement failures in the last 7 days"* | NL → typed `QuerySpec` IR → C# DSL compiler → Elasticsearch query → formatted answer |

All LLM inference runs locally via **Ollama** (Qwen3 8B Q4_K_M). The orchestration layer is **.NET 10 ASP.NET Core + Blazor Server**. No cloud services required.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| Docker Desktop (or Docker Engine + Compose) | Latest stable |
| NVIDIA GPU | 8 GB VRAM minimum (RTX 2000 Blackwell / RTX 3060 or better) |
| NVIDIA Container Toolkit | Verify: `docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi` |
| .NET 10 SDK | For running the RAG server outside Docker or running tests |

---

## Quickstart

```bash
# 1. Configure environment
cp .env.example .env
# Edit .env — set SKIP_AUTH=true for local dev (no IdP required)

# 2. Start all infrastructure (7 services — ES, Ollama, SQL Server, MongoDB, Confluence mock, Aspire Dashboard)
docker compose up -d
# First boot: mssql and mongodb initialise their data — allow ~60 s

# 3. Pull LLM and embedding models into Ollama (~5 GB total, one-time)
docker exec ollama ollama pull qwen3:8b
docker exec ollama ollama pull bge-small-en-v1.5

# 4. Start the RAG server
cd src/RagServer
dotnet run
# Wait for: "Now listening on: http://localhost:8080"

# 5. Index the Confluence mock pages (first time only)
curl -X POST http://localhost:8080/admin/reindex?source=confluence
# Wait ~30 s — indexes 15 MDM documentation pages into Elasticsearch
```

Open in your browser:
- **Chat UI:** http://localhost:8080
- **Aspire Dashboard (traces + metrics):** http://localhost:18888
- **Confluence mock (citation links):** http://localhost:8090/wiki/spaces/MDM/pages/1002

---

## Tech Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Web + UI | ASP.NET Core minimal APIs + Blazor Server |
| LLM | Ollama — Qwen3 8B Q4_K_M (chat), bge-small-en-v1.5 (embeddings) |
| Vector + search | Elasticsearch 9.x — BM25 + kNN RRF hybrid |
| SQL catalog | EF Core 9 on SQL Server 2022 (Developer Edition) |
| MongoDB | Business rules store (36 rules across 6 MDM entities) |
| Confluence mock | Python FastAPI — 15 MDM documentation pages |
| Observability | OpenTelemetry → .NET Aspire Dashboard |
| Containerisation | Docker Compose (7 services, all health-gated) |

---

## Running Tests

```bash
dotnet test src/RagServer.Tests/
# 176 tests, ~40 s
```

---

## Pre-warming the Demo

Send all 18 curated queries through the routing cache before a live demo:

```bash
curl -X POST http://localhost:8080/demo/warmup
```

---

## Resetting After Schema Changes

If you update the SQL catalog schema, drop the persistent volumes to let `EnsureCreatedAsync` rebuild:

```bash
docker compose down -v
docker compose up -d
```

---

## Documentation

| Document | Contents |
|---|---|
| [`docs/AI-assistant.md`](docs/AI-assistant.md) | Full architecture, data flows, design decisions, component catalog |
| [`docs/demo-runbook.md`](docs/demo-runbook.md) | Step-by-step demo guide with troubleshooting |
| [`CLAUDE.md`](CLAUDE.md) | Development commands, MCP guidelines, key architecture decisions |
| [`.env.example`](.env.example) | All environment variables with descriptions and defaults |
