# AGENTS.md

Context for Claude sessions working on MeducateAPI. Keep this file up to date as the project evolves.

---

## What this project is

MeducateAPI is a medical education REST API that automatically discovers health topics from MedlinePlus and PubMed, processes them through GPT-4 via Semantic Kernel, and serves structured data (summary, observations, risk factors, actions, citations) over a clean API. It is a portfolio project hosted live at [meducateapi.com](https://meducateapi.com).

- **Web dashboard**: meducateapi.com (Blazor Server)
- **REST API**: api.meducateapi.com (ASP.NET Core Minimal APIs)
- **Hosting**: Railway (hobby tier), Cloudflare CDN/DNS
- **Monitoring**: UptimeRobot on both domains

---

## Solution layout

```
src/
  Meducate.Domain/          Entities, repository interfaces, service contracts. Zero dependencies.
  Meducate.Application/     Business logic, Hangfire jobs, DTOs, ingestion/refresh orchestration.
  Meducate.Infrastructure/  EF Core + PostgreSQL, Semantic Kernel + OpenAI, Resend email, API key hashing.
  Meducate.API/             Minimal API endpoints, middleware pipeline, Swagger, auth setup.
  Meducate.Web/             Blazor Server dashboard (separate app, calls Meducate.API over HTTP).
tests/
  Meducate.Tests/            xUnit unit tests. No mocking framework — real implementations or hand-rolled fakes.
  Meducate.IntegrationTests/ xUnit + WebApplicationFactory<Program> against a real Testcontainers-provisioned
                              Postgres — actual HTTP calls, actual migrations, actual demo-key seed. Only
                              IEmailService is faked (avoids sending real email); Hangfire's background worker
                              is disabled via Hangfire:DisableServer so no job executes during the test run.
                              Requires Docker locally; runs fine in CI (GitHub-hosted runners ship Docker).
```

Clean Architecture: dependencies point inward only. Domain has no external dependencies. Infrastructure implements Domain interfaces.

---

## Key entities (Meducate.Domain/Entities/)

| Entity | Purpose |
|---|---|
| `HealthTopic` | Core medical topic. Fields: Name, Summary, Observations, Factors, Actions, Citations, Category, TopicType, Icd11Code, Icd11Title, Tags, Version. Internal fields (RawSource, SourceHash, NeedsLlmReprocessing, ReprocessAttempts, OriginalName, LastSourceRefresh) are `[JsonIgnore]`. |
| `User` | Passwordless. Holds email, verification token + expiry, security stamp, consent timestamps. |
| `Organisation` | One per user. Owns API keys. |
| `ApiClient` | API key with PBKDF2-hashed secret + salt. Tracks DailyLimit, IsActive, ExpiresAt, LastUsedAt. |
| `ApiUsageLog` | Per-request log. Stores path, method, status, query string, organisation name. No raw IP (GDPR). |
| `SeenTopic` | Deduplication table. Records every topic name the ingestion pipeline has classified, including non-medical and filtered types, so they are never re-discovered. |

---

## Auth flow

Magic-link email only, no passwords.

1. `POST /api/users/register` creates or retrieves user, sends verification or login email via Resend.
2. `POST /api/users/verify` validates token, calls `AuthSignIn.SignInAsync`, sets cookie `meducateapi_auth`.
3. Cookie: 3-day sliding expiry, 7-day absolute max (`MaxAuthAgeSeconds`). Security stamp checked every 5 min (`StampCheckIntervalSeconds = 300`).
4. CSRF protection via `X-Requested-By` header (checked in `CsrfProtectionMiddleware`).
5. Account deletion requires a fresh session (signed in within last 10 min, `FreshAuthWindowSeconds = 600`).

---

## API key system

- Format: `{keyId}.{secret}`
- Secret PBKDF2-hashed on creation (`ApiKeyHasher`: SHA-256, 100k iterations, 16-byte salt, 32-byte hash, stored as separate `Salt` column). Plain key shown once only.
- Max 5 active keys per org (`MaxKeysPerOrg = 5`). Managed via the Organisations endpoints below (create/list/rename/delete key).
- `ApiKeyMiddleware` validates the key and enforces the daily limit. Caches client (30s TTL) and usage count (15s TTL) in `IMemoryCache`.
- `UsageLoggingMiddleware` logs every authenticated request and fires an 80%-threshold email warning via Hangfire.
- Rate limit headers `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` are set only on `[RequiresApiKey]` endpoints, only after successful key validation (`ApiKeyMiddleware`) — not on every response.
- Global rate limit: 60 req/min, via ASP.NET Core `RateLimiter` partitioned by API key ID (or by IP for unauthenticated requests).

**Demo key** (seeded at startup, intentionally public):
```
X-Api-Key: d3m0000000000000000000000000key1.MEDUCATE_PUBLIC_DEMO_2026
```
Limit: 300 requests/day.

---

All public API routes are versioned under `/api/v1` via `app.MapGroup("/api/v1")` in `MiddlewarePipeline.cs`. `/health`, `/hangfire`, and `/internal/*` are intentionally unversioned (operational/internal, not the public API surface).

## Topic endpoints (all require `X-Api-Key`)

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/topics` | Paginated list. Params: `skip`, `take` (max 200), `type`. |
| `GET` | `/api/v1/topics/search` | Name partial-match. Same pagination params + `type` filter. |
| `GET` | `/api/v1/topics/{name}` | Single topic by exact name. |
| `GET` | `/api/v1/topics/types` | Distinct topic types in the DB. |

Response field names adapt to `TopicType` via `HealthTopicJsonConverter`. Each `TopicType` (Disease, Symptom, Drug, Procedure, Diagnostic Test, Vaccine, Anatomy, Nutrient, Mental Health, Lifestyle) gets its own field-name triplet — e.g. a Disease returns `symptoms`/`causes`/`treatments`, a Symptom returns `relatedSymptoms`/`associatedConditions`/`management`. `icd11Code`/`icd11Title` are constant field names regardless of type — null unless the topic is a diagnosable type with a confident WHO ICD-11 match (see "ICD-11 coding" below).

---

## Auth/account & organisation endpoints (`Meducate.API/Endpoints/AuthEndpoints.cs`, `OrgEndpoints.cs`, `WaitlistEndpoints.cs`)

Require the `MeducateAPIAuth` cookie (`[Authorize]`) unless noted:

| Method | Path | Notes |
|---|---|---|
| `POST` | `/api/v1/auth/logout` | Sign out. |
| `POST` | `/api/v1/users/register` | `[AllowAnonymous]`. Register/sign-in via magic link. Honeypot + timing bot checks. |
| `POST` | `/api/v1/users/verify` | `[AllowAnonymous]`. Validates token, signs in. |
| `GET` | `/api/v1/users/me` | Current user + org + whether they have active keys. |
| `DELETE` | `/api/v1/users/me` | Delete account. Requires fresh session (`FreshAuthWindowSeconds`). |
| `POST` | `/api/v1/waitlist` | `[AllowAnonymous]`. Waitlist signup. |
| `POST` | `/api/v1/orgs` | Create the caller's organisation (one per user). |
| `POST` | `/api/v1/orgs/{id}/keys` | Create an API key for an org (enforces `MaxKeysPerOrg`). |
| `GET` | `/api/v1/orgs/{id}/keys` | List active keys + today's usage per key. |
| `PATCH` | `/api/v1/orgs/{orgId}/keys/{keyId}` | Rename a key. |
| `DELETE` | `/api/v1/orgs/{orgId}/keys/{keyId}` | Revoke (disable) a key. |
| `GET` | `/api/v1/orgs/{id}/usage/history` | Daily usage counts across the org's keys. |
| `GET` | `/api/v1/orgs/{id}/usage/top-endpoints` | Most-used endpoints across the org's keys. |

Health checks (unauthenticated): `GET /health`, `/health/live`, `/health/ready`.

---

## Background jobs (Hangfire, PostgreSQL-backed)

| Job | Schedule | What it does |
|---|---|---|
| `TopicDiscoveryJob` | 2 AM UTC daily | Discovers new topics from all providers, LLM-classifies and extracts structured data, deduplicates, removes stale topics, backfills missing fields. |
| `TopicRefreshJob` | 3 AM UTC daily | Re-processes existing topics whose source has changed or are flagged `NeedsLlmReprocessing`. |
| `DataIntegrityCheckJob` | 4 AM UTC daily | Verifies data quality after refresh. |

On non-development startup, `TopicRefreshJob` is enqueued immediately to catch up on any missed run.

Hangfire dashboard: `/hangfire`, protected by `HangfireDashboardAuthFilter` — a standalone shared-password scheme unrelated to user auth. Password comes from `Hangfire:DashboardPassword` config, submitted via `?password=` query string, then remembered in its own `HangfireAuth` cookie (12hr).

---

## Internal endpoints (require `X-Internal-Token` header, not public)

```
POST /internal/jobs/{jobName}              Enqueue a named job
GET  /internal/jobs/{jobName}/last-run     Last run result
GET  /internal/topics/sample               Random topic sample for inspection
GET  /internal/topics/fact-check           Topics with raw source attached for QA (count max 10, use skip to page; global 60/min limit applies)
GET  /internal/waitlist                    Pro waitlist signups
POST /internal/topics/reprocess?type=&name=  Flag topics for LLM re-extraction on the next refresh
POST /internal/topics/retype               Body {"Name":"NewType"}: fix a misclassified topic (clears category/ICD-11, re-extracts)
POST /internal/topics/resync               Make every topic re-fetch its source on the next refresh
POST /internal/topics/sanitize             Apply TextCleaner to all stored topics
```

Known job names: `data-integrity-check`, `refresh-medical-conditions`, `discover-medical-conditions`.

---

## Middleware pipeline order

```
ForwardedHeaders -> HTTPS redirect/HSTS -> ResponseCompression -> Routing -> CORS
-> GlobalExceptionMiddleware -> SecurityHeadersMiddleware -> ETagMiddleware
-> CorrelationIdMiddleware -> RequestTimingMiddleware -> RateLimiter
-> Authentication -> Authorization -> CsrfProtectionMiddleware
-> ApiKeyMiddleware -> UsageLoggingMiddleware
-> Swagger -> HangfireDashboard -> Endpoints
```

---

## Data providers

- `MedlinePlusDataProvider` — fetches topic index from MedlinePlus XML API.
- `PubMedDataProvider` — fetches from PubMed E-utilities.
- Both implement `IMedicalDataProvider` (`DiscoverTopicsAsync`, `GetKnownTopicNamesAsync`, `FetchTopicDataAsync`, `SourceName`).

---

## LLM processing (Semantic Kernel + OpenAI)

- Kernel built in `SemanticKernelBuilder`. Model defaults to `gpt-4o-mini` unless overridden by `OpenAI:Model` config.
- `ILLMProcessor` methods:
  - `ClassifyTopicNamesAsync` — assigns a TopicType to each candidate.
  - `ClassifyTopicCategoriesAsync` — assigns a category to each candidate.
  - `ParseHealthTopicAsync` — extracts structured fields from raw source.
  - `VerifyHealthTopicAsync` — second-pass quality check.
  - `CompareBroaderNameAsync` — resolves synonym collisions.
  - `MatchOriginalNamesAsync` — matches source-provider names back to canonical topic names.
  - `ShouldProcessTopicType` — filters non-medical / non-processable types.
  - `GetValidCategories()` — allowed category list.
- 500ms throttle between LLM calls (`LlmThrottle`, in `TopicIngestionService` / `TopicRefreshService`) to avoid rate-limit errors.

---

## ICD-11 coding (WHO API, not LLM-generated)

- `IIcd11CodingService.LookupAsync` — looked up via a real call to the WHO ICD-11 API's MMS search endpoint, not generated by the LLM (LLMs aren't reliable for exact code strings).
- `Icd11ApiClient` (`Meducate.Infrastructure/Icd11/`) — OAuth2 client-credentials token (cached ~55 min), searches `id.who.int/icd/release/11/{release}/mms/search?q={name}`, takes the first result with a leaf `theCode` (chapters/blocks have none) as the match.
- `NullIcd11CodingService` — registered instead when `Icd11:ClientId`/`Icd11:ClientSecret` aren't configured; always returns no match. Used by default in local dev, CI, and integration tests.
- Only topics whose `TopicType` is in `TopicConstants.Icd11CodeableTypes` (Disease, Disorder, Syndrome, Symptom, Mental Health) get looked up — other types (Drug, Procedure, etc.) aren't ICD-11-codeable.
- `TopicBackfillService.BackfillIcd11CodesAsync` runs as the final phase of both `TopicIngestionService.IngestAsync` and `TopicRefreshService.RefreshAllAsync`, after categorization (lookup uses the topic name only, but logically depends on the topic already being a real, categorized entity).
- No forced/guessed codes — unmatched topics keep `Icd11Code`/`Icd11Title` null and are retried on the next cycle, same cautious approach as uncategorized topics.

---

## Configuration keys (never commit real values)

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `OpenAI:ApiKey` | OpenAI key |
| `OpenAI:Model` | Model ID (default `gpt-4o-mini`) |
| `Resend:ApiToken` | Resend email API key |
| `Resend:FromAddress` | From-address used when sending via Resend |
| `Internal:TriggerToken` | Bearer token for `/internal/*` endpoints |
| `OpenAI:EscalationModel` | Optional stronger model (e.g. `gpt-4.1`). A topic that already failed a quality check once is retried on it. Unset = never escalate. |
| `App:BaseUrl` | Public base URL used to build magic-link URLs |
| `Cors:AllowedOrigins` | Array of allowed origins for CORS |
| `Hangfire:DashboardPassword` | Password for the `/hangfire` dashboard (see above) |
| `Hangfire:DisableServer` | Set to `"true"` to skip starting the Hangfire worker — no queued/recurring job executes. Used by integration tests; also a safe knob for local dev against a real DB. |
| `Admin:AlertEmail` | Recipient for data-integrity-check and error alerts |
| `PubMed:ApiKey` | PubMed E-utilities API key |
| `Api:BaseUrl` / `Api:PublicUrl` | (Meducate.Web) base URLs for calling `Meducate.API` |
| `Icd11:ClientId` / `Icd11:ClientSecret` | WHO ICD-11 API OAuth2 client credentials (free registration at icd.who.int/icdapi). Optional — if unset, ICD-11 coding falls back to a no-op (`Icd11Code`/`Icd11Title` stay null) instead of failing startup. |

Use `dotnet user-secrets` locally. Railway environment variables in production.

---

## Tests

`Meducate.Tests` — xUnit, no mocking framework, real implementations or hand-rolled fakes. Run with `dotnet test tests/Meducate.Tests`. Covers middleware, helpers, DTOs, entity logic, the ingestion/refresh/LLM-classification pipeline, and ICD-11 search-response parsing (`Icd11ApiClientTests` — pure `ParseSearchResponse`, no real WHO API calls).

`Meducate.IntegrationTests` — xUnit + `WebApplicationFactory<Program>` against a real Testcontainers Postgres. Run with `dotnet test tests/Meducate.IntegrationTests` (needs Docker running locally). `dotnet test` at the repo root (or `dotnet test MeducateAPI.slnx`) runs both.

---

## Web app (Meducate.Web)

Blazor Server. Calls `Meducate.API` over HTTP via `ApiService`. Auth cookies forwarded via `CookieForwardingHandler`. Pages: Home, Docs, Pricing, Register, Verify, Dashboard, Account, CreateOrganisation, GenerateKey, Blog, FAQ, UseCases, Privacy, Terms, and SEO landing pages.

---

## Coding conventions

- Minimal API endpoints in `Endpoints/`, each file a static class with a `MapXEndpoints` extension method.
- DI registration in `DependencyInjection/` extension methods — only `Meducate.API` and `Meducate.Infrastructure` have one; `Meducate.Infrastructure`'s also registers Application-layer services (jobs, ingestion/refresh services). `Meducate.Web` registers its own services inline in `Program.cs`.
- EF Core config via `IEntityTypeConfiguration<T>` in `Persistence/Configurations/`.
- Migrations in `Meducate.Infrastructure/Migrations/`. Apply with `dotnet ef migrations add` targeting the Infrastructure project. `ApplyMigrationsAsync()` runs at startup.
- `internal sealed` on everything that isn't part of a project's public API. Each project grants `InternalsVisibleTo` to the projects/tests that consume it, so `internal` types (e.g. `ITopicRepository`, `ILLMProcessor`) do cross project boundaries via IVT rather than staying strictly project-local.
- No comments unless the why is non-obvious. No XML doc blocks.

---

## Commit convention

Regular commits with short, descriptive messages explaining the why, not just the what. No squashing, no force-pushing master.

---

## Security reminders

- This repo is public (portfolio piece). Never commit secrets, connection strings, API keys, or internal tokens.
- The demo key and its daily limit are intentionally public — they are not secrets.
- Before writing anything sensitive to memory files or committing, flag it to the user first.
