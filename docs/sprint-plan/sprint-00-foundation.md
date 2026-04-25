# Sprint 0 — Foundation & Infrastructure

| | |
|---|---|
| **Sprint** | 0 |
| **Duration** | Week 1 (5 days) |
| **Milestone** | M0 — Infrastructure live on both laptops |
| **Goal** | Stand up the full Docker Compose stack, wire M.E.AI to Ollama, validate CUDA, configure OIDC, emit the first OTLP trace, and create the golden-set scaffold. |

---

## Prerequisites

- Docker Desktop with WSL2 backend installed and running on both dev laptops.
- NVIDIA Container Toolkit confirmed pre-installed (see `docs/AI-assistant.md` Appendix D).
- Access to corporate OIDC IdP (client ID + secret).
- GitLab repo created; CI pipeline skeleton in place.

---

## Environment Variables (Sprint 0 additions to `.env`)

All values below must be populated in `.env` before any `docker compose up`.

```dotenv
# ── Ollama ────────────────────────────────────────────────────────────────────
OLLAMA_BASE_URL=http://ollama:11434
OLLAMA_CHAT_MODEL=qwen3:8b
OLLAMA_EMBEDDING_MODEL=bge-small-en-v1.5
OLLAMA_NUM_PARALLEL=1
OLLAMA_NUM_GPU=999

# ── Elasticsearch ─────────────────────────────────────────────────────────────
ES_URL=http://elasticsearch:9200
ES_USERNAME=elastic
ES_PASSWORD=changeme
ES_INDEX_DOCS=docs
ES_INDEX_CATALOG_TERMS=catalog_terms
ES_INDEX_SCHEMA_CARDS=schema_cards

# ── OIDC ──────────────────────────────────────────────────────────────────────
OIDC_AUTHORITY=https://your-idp.example.com
OIDC_CLIENT_ID=ollapoc
OIDC_CLIENT_SECRET=<secret>
OIDC_CALLBACK_PATH=/signin-oidc

# ── Observability ─────────────────────────────────────────────────────────────
OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:4317
OTEL_SERVICE_NAME=rag-server
OTEL_CRAWLER_SERVICE_NAME=rag-ingestion

# ── RAG server ────────────────────────────────────────────────────────────────
RAG_HOST_PORT=8080
RAG_QUEUE_MAX_DEPTH=50
RAG_EMBEDDING_CACHE_SIZE=1000

# ── Aspire Dashboard ──────────────────────────────────────────────────────────
ASPIRE_DASHBOARD_UI_PORT=18888
ASPIRE_DASHBOARD_OTLP_GRPC_PORT=4317
ASPIRE_DASHBOARD_OTLP_HTTP_PORT=4318
ASPIRE_DASHBOARD_UNSECURED=true
```

---

## Tasks

### Day 1 — Repository & Project Skeleton

- [ ] **T0.1** Initialise Git repository on GitLab; create `main` and `dev` branches; add `.gitignore` (`.env`, `bin/`, `obj/`, `*.user`).
- [ ] **T0.2** Create solution file: `dotnet new sln -n ollapoc`.
- [ ] **T0.3** Create main project: `dotnet new web -n RagServer -o src/RagServer`.
- [ ] **T0.4** Create test project: `dotnet new xunit -n RagServer.Tests -o src/RagServer.Tests`; add project reference.
- [ ] **T0.5** Add solution references for both projects.
- [ ] **T0.6** Create directory scaffold inside `src/RagServer/`:
  ```
  Endpoints/
  Pipelines/
  Router/
  Ingestion/
  Tools/
  Compiler/
  Infrastructure/
  Options/
  ```
- [ ] **T0.7** Create `.env.example` at repo root (copy from `.env`, blank out secret values). Commit `.env.example`; add `.env` to `.gitignore`.

**Acceptance:** `dotnet build src/RagServer` succeeds with zero warnings.

---

### Day 1 — Docker Compose

- [ ] **T0.8** Create `docker-compose.yml` at repo root with four services:
  - `ollama` — `ollama/ollama:latest`, `--gpus all`, port `${RAG_QUEUE_MAX_DEPTH:-50}`, volume `ollama_data:/root/.ollama`, env `OLLAMA_NUM_PARALLEL`, `OLLAMA_NUM_GPU`.
  - `elasticsearch` — `docker.elastic.co/elasticsearch/elasticsearch:9.3.3`, single-node, `xpack.security.enabled=true`, heap `512m` for dev, port `9200`.
  - `rag-server` — build from `src/RagServer/Dockerfile` (created T0.9), port `${RAG_HOST_PORT:-8080}`, `env_file: .env`.
  - `aspire-dashboard` — `mcr.microsoft.com/dotnet/aspire-dashboard:latest`, ports `${ASPIRE_DASHBOARD_UI_PORT:-18888}` and `${ASPIRE_DASHBOARD_OTLP_GRPC_PORT:-4317}`, env `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=${ASPIRE_DASHBOARD_UNSECURED}`.
- [ ] **T0.9** Create `src/RagServer/Dockerfile` (multi-stage: `sdk:10.0` build → `aspnet:10.0` runtime).
- [ ] **T0.10** Create `docker-compose.override.yml` for local dev (bind-mount source, hot reload, skip OIDC enforcement).

**Acceptance:** `docker compose up -d` → all four containers `healthy`; `curl http://localhost:9200` returns ES cluster info; `http://localhost:18888` loads Aspire Dashboard UI.

---

### Day 2 — Ollama & NVIDIA Verification

- [ ] **T0.11** Pull models into Ollama:
  ```bash
  docker exec ollama ollama pull qwen3:8b
  docker exec ollama ollama pull bge-small-en-v1.5
  ```
- [ ] **T0.12** Verify GPU: run `nvidia-smi` inside a test container; verify Ollama process appears in `nvidia-smi` while a model is loaded.
- [ ] **T0.13** Add NuGet packages to `RagServer.csproj`:
  ```
  Microsoft.Extensions.AI
  Microsoft.Extensions.AI.Abstractions
  OllamaSharp
  Elastic.Clients.Elasticsearch (9.x)
  OpenTelemetry.Extensions.Hosting
  OpenTelemetry.Instrumentation.AspNetCore
  OpenTelemetry.Instrumentation.Http
  OpenTelemetry.Exporter.OpenTelemetryProtocol
  ```
- [ ] **T0.14** Create `src/RagServer/Options/OllamaOptions.cs` with `BaseUrl`, `ChatModel`, `EmbeddingModel`, `NumParallel` properties; bind from env in `Program.cs`.
- [ ] **T0.15** Register `OllamaSharp.OllamaApiClient` as `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` in DI; read endpoint and models from `OllamaOptions`.
- [ ] **T0.16** Smoke-test: add a temporary `GET /health/llm` endpoint that calls `IChatClient` with `"Hello"` and returns the first 50 chars of the response; verify via `curl`.

**Acceptance:** `GET /health/llm` returns a non-empty string within 30 s; Aspire Dashboard shows a trace with an outbound HTTP span to Ollama.

---

### Day 2 — Elasticsearch Client

- [ ] **T0.17** Create `src/RagServer/Options/ElasticsearchOptions.cs` with `Url`, `Username`, `Password` properties.
- [ ] **T0.18** Register `Elastic.Clients.Elasticsearch.ElasticsearchClient` in DI; configure with `ElasticsearchClientSettings` reading from `ElasticsearchOptions`.
- [ ] **T0.19** Add temporary `GET /health/es` endpoint that calls `client.Cluster.HealthAsync()` and returns cluster status.

**Acceptance:** `/health/es` returns `"green"` or `"yellow"`.

---

### Day 3 — OpenTelemetry & OIDC

- [ ] **T0.20** Wire OpenTelemetry in `Program.cs`:
  - `AddOpenTelemetry()` with `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`.
  - `AddOtlpExporter()` using `OTEL_EXPORTER_OTLP_ENDPOINT`.
  - Set `OTEL_SERVICE_NAME` from env.
- [ ] **T0.21** Add a custom `ActivitySource` constant `RagServer.Telemetry.Sources.RagServer` for application spans.
- [ ] **T0.22** Configure OIDC in `Program.cs`:
  - `AddAuthentication().AddOpenIdConnect()` reading `OidcOptions` (Authority, ClientId, ClientSecret, CallbackPath) from env.
  - Protect all routes except `/health/*` with `[Authorize]`.
  - In `docker-compose.override.yml` (local dev), allow anonymous to bypass OIDC (`SKIP_AUTH=true` flag checked in `Program.cs`).
- [ ] **T0.23** Add `GET /health` (unauthenticated) returning build version and stack status.

**Acceptance:** Aspire Dashboard receives traces from `rag-server`; `/health/llm` shows as a nested span under an incoming HTTP span. OIDC redirects correctly on a non-override `docker compose` run.

---

### Day 3 — Request Queue

- [ ] **T0.24** Create `src/RagServer/Infrastructure/LlmRequestQueue.cs`:
  - `Channel<Func<CancellationToken, Task>>` with bounded capacity (`RAG_QUEUE_MAX_DEPTH`).
  - `IHostedService` background consumer that drains one item at a time (enforces `OLLAMA_NUM_PARALLEL=1`).
  - Expose `EnqueueAsync<T>` returning `Task<T>`; throw `429` if channel is full.
- [ ] **T0.25** Register `LlmRequestQueue` as `IHostedService` and as a singleton in DI.

**Acceptance:** Two concurrent requests to `/health/llm` result in one queued (visible as `429` or delayed response in traces).

---

### Day 4 — Blazor UI Skeleton

- [ ] **T0.26** Add Blazor Server to `RagServer` project:
  - `Program.cs`: `AddRazorComponents()`, `AddInteractiveServerComponents()`.
  - `App.razor`, `Routes.razor`, `MainLayout.razor` scaffold.
- [ ] **T0.27** Create `Pages/Chat.razor` with:
  - A text input and send button.
  - A message list displaying streamed assistant responses (using `IAsyncEnumerable` piped via SignalR).
  - A placeholder "Demo Stats panel" sidebar (empty for now; populated in Sprint 4).
  - A model badge showing `${OLLAMA_CHAT_MODEL}` value.
- [ ] **T0.28** Create `Endpoints/ChatEndpoint.cs` — `POST /api/chat` accepting `{ message: string }`, enqueueing an `IChatClient.CompleteStreamingAsync` call, streaming the result back as Server-Sent Events (SSE).
- [ ] **T0.29** Wire Blazor `Chat.razor` to call `/api/chat` and render streaming tokens as they arrive.

**Acceptance:** Open `http://localhost:8080`; type "Hello"; see the assistant response stream token-by-token in the browser; Aspire Dashboard shows the full trace.

---

### Day 5 — Golden-Set Scaffold & Evaluation Harness

- [ ] **T0.30** Create SQL Server tables (migration via EF Core):
  ```sql
  IngestionCursor (Source VARCHAR(50) PK, LastSyncedAt DATETIME2, Cursor NVARCHAR(MAX))
  IngestionRun    (Id INT IDENTITY PK, Source VARCHAR(50), StartedAt DATETIME2,
                   FinishedAt DATETIME2 NULL, DocsProcessed INT, Errors INT)
  EvalQueries     (Id INT IDENTITY PK, UseCase CHAR(4), Query NVARCHAR(MAX),
                   ExpectedAnswer NVARCHAR(MAX) NULL, ExpectedIR NVARCHAR(MAX) NULL,
                   Tags NVARCHAR(MAX) NULL)
  EvalResults     (Id INT IDENTITY PK, RunAt DATETIME2, EvalQueryId INT FK,
                   Environment VARCHAR(50), Passed BIT, Score FLOAT, Notes NVARCHAR(MAX))
  ```
- [ ] **T0.31** Create `src/RagServer.Tests/Evaluation/GoldenSetRunner.cs` — reads `EvalQueries` from SQL, calls the pipeline endpoint, writes results to `EvalResults`.
- [ ] **T0.32** Seed 5 placeholder `EvalQueries` rows (one per use case + intent router): dummy queries for now; real queries added in Sprints 1–4.
- [ ] **T0.33** Add GitLab CI job `eval:nightly` that runs `GoldenSetRunner` and fails if pass rate < configured threshold.
- [ ] **T0.34** Create `src/RagServer/Options/SqlServerOptions.cs` with `ConnectionString`; bind from `SQL_CONNECTION_STRING` env.

**Acceptance:** `dotnet test --filter "FullyQualifiedName~GoldenSetRunner"` runs without throwing; writes rows to `EvalResults`; all 5 placeholder queries produce a result row (pass/fail doesn't matter yet).

---

### Day 5 — Intent Router Skeleton

- [ ] **T0.35** Create `src/RagServer/Router/IntentRouter.cs` with method `RouteAsync(string query) -> PipelineKind` (enum: `Docs | Metadata | Data`).
- [ ] **T0.36** Implement rule-based tier only for now:
  - `"how"`, `"what is"`, `"explain"`, `"describe"` → `Docs`.
  - `"attributes"`, `"fields"`, `"cde"`, `"entity"` → `Metadata`.
  - `"give me all"`, `"list all"`, `"show me"`, `"find"` + entity noun → `Data`.
  - Fallback → `Docs`.
- [ ] **T0.37** Register `IntentRouter` in DI; inject into `ChatEndpoint`.
- [ ] **T0.38** Add unit tests in `RagServer.Tests` covering at least 10 routing cases.

**Acceptance:** `dotnet test --filter "FullyQualifiedName~IntentRouterTests"` all pass; routing decision logged as a span attribute `rag.pipeline`.

---

## Sprint 0 Definition of Done

- [ ] `docker compose up -d` → four containers `healthy` on both laptops.
- [ ] CUDA verified via `nvidia-smi` inside Docker on both laptops.
- [ ] `/health/llm` returns streaming response; Aspire trace visible.
- [ ] Blazor UI renders and streams chat responses.
- [ ] OIDC working (or SKIP_AUTH bypass working for local dev).
- [ ] OpenTelemetry traces reaching Aspire Dashboard.
- [ ] Request queue operational (`429` on overflow).
- [ ] EF Core migration applied; `EvalQueries` seeded.
- [ ] `GoldenSetRunner` smoke test passes.
- [ ] `IntentRouter` unit tests all pass.
- [ ] `.env.example` committed with all Sprint 0 variables documented.
- [ ] Zero hardcoded config values in source code.
