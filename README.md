# MeducateAPI

**A medical education API that transforms raw health data into structured, searchable knowledge.**

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)
![OpenAI](https://img.shields.io/badge/OpenAI-GPT--4-412991?logo=openai&logoColor=white)
![Hangfire](https://img.shields.io/badge/Hangfire-1.8-5C2D91)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)
![Railway](https://img.shields.io/badge/Railway-Deployed-0B0D0E?logo=railway)

<img src="docs/swagger-preview.png" alt="MeducateAPI Swagger docs — Topics endpoints" width="800">

## What It Does

MeducateAPI automatically discovers and aggregates medical topics from authoritative sources like MedlinePlus and PubMed, then uses LLM processing to extract structured data — summaries, symptoms, risk factors, and treatments — all classified into body-system and content-type categories. The result is a clean REST API that developers can use to build health education tools without wrangling raw medical data themselves.

## Live

- **Web Dashboard** — [meducateapi.com](https://meducateapi.com)
- **REST API** — [api.meducateapi.com](https://api.meducateapi.com)

## Key Features

- **Magic-link authentication** — passwordless email sign-in with cookie-based sessions
- **API key management** — create, rename, and revoke keys per organisation (max 5 active)
- **Rate limiting** — two-tier system: 60 req/min per key + configurable daily limits with 80% usage alerts
- **Automated topic discovery** — daily Hangfire jobs fetch new topics from MedlinePlus at 2 AM UTC
- **Automated topic refresh** — existing topics re-processed daily at 3 AM UTC for accuracy
- **Nightly drift detection** — a rotating batch of live topics is re-checked against their original source text each night; low keyword overlap flags likely hallucination or staleness, and structural failures (missing fields, invalid categories) trigger an email alert
- **Topic classification** — LLM-powered categorisation into 24 standardised medical categories
- **ICD-11 coding** — diagnosable topics (Disease, Disorder, Syndrome, Symptom, Mental Health) are matched against the WHO ICD-11 API for a real, verifiable diagnostic code — not LLM-generated
- **Interactive Swagger docs** — full OpenAPI spec with try-it-out support
- **Usage dashboard** — Blazor Server UI for managing keys, viewing usage, and browsing docs

## Architecture

```mermaid
graph TD
    subgraph Presentation
        API[Meducate.API<br/><i>Minimal APIs · Middleware · Swagger</i>]
        Web[Meducate.Web<br/><i>Blazor Server Dashboard</i>]
    end

    subgraph Application
        App[Meducate.Application<br/><i>Hangfire Jobs · Ingestion · DTOs</i>]
    end

    subgraph Infrastructure
        Infra[Meducate.Infrastructure<br/><i>EF Core · Semantic Kernel · Resend</i>]
    end

    subgraph Domain
        Dom[Meducate.Domain<br/><i>Entities · Interfaces · Contracts</i>]
    end

    subgraph External
        PG[(PostgreSQL)]
        OAI[OpenAI]
        MLP[MedlinePlus]
        PM[PubMed]
        RS[Resend]
    end

    API --> App
    Web --> App
    App --> Dom
    Infra --> Dom
    API --> Infra
    Web --> Infra
    Infra --> PG
    Infra --> OAI
    Infra --> MLP
    Infra --> PM
    Infra --> RS
```

The project follows **Clean Architecture** with four layers, each depending only inward:

| Layer | Project | Responsibility |
|---|---|---|
| **Domain** | `Meducate.Domain` | Entities, repository interfaces, service contracts — zero external dependencies |
| **Application** | `Meducate.Application` | Business logic, Hangfire jobs, ingestion/refresh orchestration, DTOs |
| **Infrastructure** | `Meducate.Infrastructure` | EF Core + PostgreSQL, Semantic Kernel + OpenAI, Resend email, API key hashing |
| **API / Web** | `Meducate.API` / `Meducate.Web` | Minimal API endpoints, middleware pipeline, Blazor Server dashboard |

## Tech Stack

| Category | Technology |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Frontend | Blazor Server |
| Database | PostgreSQL via EF Core (Npgsql) |
| AI / LLM | Microsoft Semantic Kernel + OpenAI GPT-4 |
| Background Jobs | Hangfire with PostgreSQL storage |
| Email | Resend API |
| Auth | Cookie-based with magic-link verification |
| API Docs | Swashbuckle (Swagger / OpenAPI) |
| Hosting | Railway |

## Data Pipeline

```mermaid
graph LR
    MLP[MedlinePlus] --> DJ
    PM[PubMed] --> DJ
    DJ[TopicDiscoveryJob<br/><i>Daily · 2 AM UTC</i>] --> CL
    CL[LLM Classification<br/><i>Topic type · Category</i>] --> EX
    EX[LLM Extraction<br/><i>Summary · Symptoms<br/>Factors · Treatments</i>] --> QC
    QC[Quality Control<br/><i>Synonym merging<br/>Field validation</i>] --> PG
    PG[(PostgreSQL)] --> API
    API[REST API<br/><i>/api/v1/topics/*</i>]
```

## API Endpoints

### Topics (requires API key)

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/v1/topics` | List all topics (paginated, filterable by type and category) |
| `GET` | `/api/v1/topics/search` | Search topics by name (same type and category filters) |
| `GET` | `/api/v1/topics/{name}` | Get a single topic by exact name |
| `GET` | `/api/v1/topics/icd11/{code}` | Get the topic carrying a WHO ICD-11 code |
| `GET` | `/api/v1/topics/types` | List available topic types, with counts |
| `GET` | `/api/v1/topics/categories` | List medical categories, with counts |
| `GET` | `/api/v1/topics/changes?since=` | Topics added or updated since a timestamp, newest first |

A [Postman collection](https://meducateapi.com/meducate-api.postman_collection.json) covers all of these with example parameters and tests (Postman: Import, paste the URL).

### Auth

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/users/register` | Request a magic link |
| `POST` | `/api/v1/users/verify` | Verify token and start session |
| `POST` | `/api/v1/auth/logout` | Sign out |
| `GET` | `/api/v1/users/me` | Get current user info |
| `DELETE` | `/api/v1/users/me` | Delete account |

### Organisations & Keys

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/v1/orgs` | Create an organisation |
| `POST` | `/api/v1/orgs/{id}/keys` | Generate an API key |
| `GET` | `/api/v1/orgs/{id}/keys` | List API keys |
| `PATCH` | `/api/v1/orgs/{orgId}/keys/{keyId}` | Rename a key |
| `DELETE` | `/api/v1/orgs/{orgId}/keys/{keyId}` | Revoke a key |

### System

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/health` | Database health check |

## Sample Response

`GET /api/v1/topics/Type 2 Diabetes`

```json
{
  "id": "a1b2c3d4-5678-9abc-def0-1234567890ab",
  "name": "Type 2 Diabetes",
  "summary": "A chronic metabolic disorder in which the body becomes resistant to insulin or doesn't produce enough, leading to elevated blood glucose levels.",
  "topicType": "Disease",
  "category": "Endocrine, Nutritional & Metabolic",
  "icd11Code": "5A11",
  "icd11Title": "Type 2 diabetes mellitus",
  "symptoms": [
    "Increased Thirst And Frequent Urination",
    "Unexplained Weight Loss",
    "Blurred Vision",
    "Slow-Healing Sores"
  ],
  "causes": [
    "Insulin Resistance",
    "Genetic Predisposition",
    "Obesity And Physical Inactivity"
  ],
  "treatments": [
    "Blood Sugar Monitoring",
    "Oral Medications Or Insulin Therapy",
    "Dietary Management And Regular Exercise"
  ],
  "citations": [
    "MedlinePlus - National Library of Medicine",
    "PubMed Central"
  ],
  "tags": ["diabetes", "metabolic", "insulin", "blood-sugar", "chronic"],
  "lastUpdated": "2026-02-28T02:00:00Z",
  "version": 3
}
```

> Response field names adapt to topic type — for example, a Symptom returns `relatedSymptoms`, `associatedConditions`, and `management` instead of `symptoms`, `causes`, and `treatments`.

## Design Decisions

**Semantic Kernel** was chosen over direct OpenAI SDK calls because it provides a clean abstraction for prompt orchestration and makes it straightforward to swap models or add prompt filters without touching business logic. **Hangfire** handles the daily discovery and refresh pipeline — its PostgreSQL-backed persistence means jobs survive restarts, and the built-in dashboard makes it easy to monitor and retry failures. **Magic-link auth** eliminates password management entirely, which is appropriate for a developer-facing tool where security matters but registration friction should be minimal. The overall **Clean Architecture** keeps the domain free of infrastructure concerns, so the data providers, LLM processor, and persistence layer can all be replaced independently — useful when external APIs change or better models become available.

## Roadmap

**Data scope**
- Expand to drugs, procedures, vaccines, and diagnostics
- Cross-reference multiple sources per topic (MedlinePlus + PubMed + WHO)

**API features**
- Webhooks for topic update notifications
- SDKs / client libraries

**Infrastructure**
- Observability dashboards / APM (structured JSON logging in production already ships — this is the next layer on top)
