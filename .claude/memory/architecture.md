# Architecture Details

## API Endpoints

### Topics (require API key via `X-Api-Key` header)
- `GET /api/v1/topics` — paginated list (skip, take, type)
- `GET /api/v1/topics/search?query=` — search by name
- `GET /api/v1/topics/{name}` — single topic by exact name
- `GET /api/v1/topics/types` — available topic types
- `GET /api/v1/topics/categories` — categories with counts
- `GET /api/v1/topics/icd11/{code}` — topic by WHO ICD-11 code
- `GET /api/v1/topics/changes?since=` — topics added or updated since a time

### Auth
- `POST /api/v1/users/register` — request magic link
- `POST /api/v1/users/verify` — verify token, set cookie
- `POST /api/v1/auth/logout` — sign out
- `GET /api/v1/users/me` — current user info
- `DELETE /api/v1/users/me` — delete account (requires fresh auth <10min)

### Organisations (authenticated)
- `POST /api/v1/orgs` — create org
- `POST /api/v1/orgs/{id}/keys` — generate API key
- `GET /api/v1/orgs/{id}/keys` — list keys + usage
- `PATCH /api/v1/orgs/{orgId}/keys/{keyId}` — rename key
- `DELETE /api/v1/orgs/{orgId}/keys/{keyId}` — revoke key
- `GET /api/v1/orgs/{id}/usage/history` — 7-day daily usage
- `GET /api/v1/orgs/{id}/usage/top-endpoints` — top 5 endpoints

### System
- `GET /health` (API) — DB health check
- `GET|HEAD /health` (Web) — simple 200 OK

## Middleware Pipeline (API, in order)
1. ForwardedHeaders
2. GlobalExceptionMiddleware (RFC 7807 Problem Details)
3. SecurityHeadersMiddleware (CSP, X-Frame-Options, etc.)
4. CorrelationIdMiddleware (X-Correlation-Id)
5. RequestTimingMiddleware
6. RateLimiter (300 req/min per client address; the 60 req/min per-key limit is applied after authentication in ApiKeyMiddleware)
7. Authentication/Authorization (cookie-based)
8. CsrfProtectionMiddleware (X-Requested-By for mutations)
9. ApiKeyMiddleware (validates X-Api-Key, caches 30s, enforces daily limits)
10. UsageLoggingMiddleware (logs to DB, 80% usage email alert)

## Blazor Web Pages
- `/` — Home (landing page)
- `/register` — Magic-link login/signup (mode=create for signup)
- `/verify` — Email verification status
- `/create-organisation` — Post-verification org setup
- `/dashboard` — API keys, usage chart (7-day bar chart), top endpoints
- `/api-key?orgId=` — Generate new key
- `/account` — Account settings, deletion
- `/docs` — Embedded Swagger UI (filtered to /api/topics only)
- `/auth/verify?token=` — Token proxy endpoint (Program.cs minimal API)
- `/auth/logout` — Cookie clear + redirect (Program.cs minimal API)

## Web Program.cs Special Routes
- `/auth/verify` — proxies token to API, forwards Set-Cookie, redirects
- `/auth/logout` — deletes cookie, returns HTML that breaks Blazor circuit
- `/api-docs/swagger.json` — proxies + filters swagger spec (topics only)
- `/health` — GET+HEAD for UptimeRobot

## Key Services
| Service | Location | Purpose |
|---|---|---|
| TopicIngestionService | Application | Discovery pipeline orchestration |
| TopicRefreshService | Application | Refresh existing topics on source change |
| TopicBackfillService | Application | Backfill missing fields |
| SemanticKernelLLMProcessor | Infrastructure/LLM | OpenAI extraction/classification (TextCleaner post-processes output) |
| ApiKeyService | Infrastructure/ApiKeys | Key CRUD, validation, usage tracking |
| EmailService | Infrastructure/Email | Resend-based email delivery |
| MedlinePlusDataProvider | Infrastructure/DataProviders | MedlinePlus XML source |
| PubMedDataProvider | Infrastructure/DataProviders | PubMed source |
| ApiService | Web/Services | Frontend HTTP client to API |

## Database
- PostgreSQL via EF Core (Npgsql)
- Connection string managed via environment variables
- Auto-migrates on startup
- 7 migrations
- Seed data: 5 sample topics

## Constants (ApiConstants.cs)
- Default page size: 50, max: 200
- Max query length: 200
- Max key name: 100 chars
- Max keys per org: 5
- Auth cookie: 8hr expiry, 7-day max
- Fresh auth window: 10 min
- Usage warning: 80% threshold

## Hangfire Jobs
- TopicDiscoveryJob: `0 2 * * *` (2 AM UTC)
- TopicRefreshJob: `0 3 * * *` (3 AM UTC) + runs on startup in production
- Auth-protected admin dashboard (path not stored here)
