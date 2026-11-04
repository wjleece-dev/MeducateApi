> **SECURITY INSTRUCTIONS FOR CLAUDE:** These memory files are stored in a **public GitHub repository**. Before writing anything to any memory file, flag it to the user if it could constitute a security risk (credentials, secret keys, internal admin paths, session IDs, connection strings, or any value that would give an attacker an advantage). Never record such information in memory. Additionally, before committing or deploying anything, pause and await explicit user approval.

# MeducateAPI Project Memory

## Project Overview
Medical education REST API platform. Transforms raw health data (MedlinePlus, PubMed) into structured topics, categorised into 26 body-system/content-type categories, using OpenAI (gpt-4o-mini by default) via Semantic Kernel. Daily automated refresh via Hangfire jobs.

- **Domain**: meducateapi.com (API: api.meducateapi.com)
- **Hosting**: Railway (hobby tier), Cloudflare CDN/DNS
- **Monitoring**: UptimeRobot on both domains

## Architecture
Clean Architecture with 6 projects. See [architecture.md](architecture.md) for full details.

| Project | Purpose |
|---|---|
| `Meducate.Domain` | Entities, interfaces, zero dependencies |
| `Meducate.Application` | Hangfire jobs, ingestion/refresh services |
| `Meducate.Infrastructure` | EF Core + PostgreSQL, OpenAI, Resend email, data providers |
| `Meducate.API` | Minimal API endpoints, middleware pipeline, auth |
| `Meducate.Web` | Blazor Server dashboard (separate app, calls the API) |
| `Meducate.Tests` | Unit/integration tests |

## Key Entities
- **HealthTopic** — core medical topic with structured fields (summary, observations, factors, actions, citations)
- **User** — passwordless magic-link auth (no passwords)
- **Organisation** — owns API keys (max 5 active)
- **ApiClient** — API key with PBKDF2-SHA256 hashing, daily limits
- **ApiUsageLog** — per-request tracking, GDPR-compliant IP truncation
- **SeenTopic** — deduplication for discovery pipeline

## Auth Flow
Magic-link email → verify token → cookie `meducateapi_auth` (3-day sliding expiry, 7-day max). CSRF via `X-Requested-By` header. Security stamp checked every 5 min.

## User Preferences
- **Commits**: Squash everything into single commit, amend + force push. Same commit message: "MeducateAPI - Medical education API platform"
- **Style**: No emojis, concise responses
- **Data recovery**: Not concerned about DB backups — Hangfire jobs can reload data in ~1 hour
- **Confirmation**: If the next step is trivially implied by the task, complete it without asking for confirmation first.

## Key File Paths
See [architecture.md](architecture.md) for comprehensive file listing.

## SEO
- robots.txt + sitemap.xml in `src/Meducate.Web/wwwroot/`
- Meta descriptions + canonical URLs on Home, Docs, Register pages
- OG/Twitter cards configured in App.razor
- Sitemaps submitted to Google Search Console

## Writing & Style
- No em dashes in any written output. They are a recognisable AI writing pattern; use commas, colons, or parentheses instead. This applies especially to professional and job application writing.

## External Services
- **Resend** — email delivery (magic links, rate limit alerts)
- **OpenAI** (gpt-4o-mini, optional `OpenAI:EscalationModel`) — topic extraction/classification via Semantic Kernel
- **MedlinePlus + PubMed** — data sources
- **Hangfire** — background jobs (discovery 2AM UTC, refresh 3AM UTC)
