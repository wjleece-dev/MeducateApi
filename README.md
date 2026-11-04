# MeducateAPI

**A medical education API that transforms raw health data into structured, searchable knowledge.**

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)
![OpenAI](https://img.shields.io/badge/OpenAI-GPT--4o--mini-412991?logo=openai&logoColor=white)
![Hangfire](https://img.shields.io/badge/Hangfire-1.8-5C2D91)
![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4?logo=blazor)
![Railway](https://img.shields.io/badge/Railway-Deployed-0B0D0E?logo=railway)

<img src="docs/swagger-preview.png" alt="MeducateAPI Swagger docs, Topics endpoints" width="800">

## What It Does

MeducateAPI automatically discovers and aggregates medical topics from authoritative sources like MedlinePlus and PubMed, then uses LLM processing to extract structured data (summaries, symptoms, risk factors, and treatments), all classified into body-system and content-type categories. The result is a clean REST API that developers can use to build health education tools without wrangling raw medical data themselves.

## Live

- **Web Dashboard**: [meducateapi.com](https://meducateapi.com)
- **REST API**: [api.meducateapi.com](https://api.meducateapi.com)

## Key Features

- **Magic-link authentication**: passwordless email sign-in with cookie-based sessions
- **API key management**: create, rename, and revoke keys per organisation (max 5 active)
- **Rate limiting**: two-tier system: 60 req/min per key + configurable daily limits with 80% usage alerts
- **Automated topic discovery**: daily Hangfire jobs fetch new topics from MedlinePlus at 2 AM UTC
- **Automated topic refresh**: existing topics re-processed daily at 3 AM UTC for accuracy
- **Nightly drift detection**: a rotating batch of live topics is re-checked against their original source text each night; low keyword overlap flags likely hallucination or staleness, and structural failures (missing fields, invalid categories) trigger an email alert
- **Topic classification**: LLM-powered categorisation into 26 standardised medical categories
- **ICD-11 coding**: diagnosable topics (Disease, Disorder, Syndrome, Symptom, Mental Health) are matched against the WHO ICD-11 API for a real, verifiable diagnostic code, not LLM-generated
- **OpenAPI spec and Postman collection**: full OpenAPI 3 document at `/swagger/v1/swagger.json`, plus a ready-made Postman collection with tests
- **Usage dashboard**: Blazor Server UI for managing keys, viewing usage, and browsing docs

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
| **Domain** | `Meducate.Domain` | Entities, repository interfaces, service contracts, zero external dependencies |
| **Application** | `Meducate.Application` | Business logic, Hangfire jobs, ingestion/refresh orchestration, DTOs |
| **Infrastructure** | `Meducate.Infrastructure` | EF Core + PostgreSQL, Semantic Kernel + OpenAI, Resend email, API key hashing |
| **API / Web** | `Meducate.API` / `Meducate.Web` | Minimal API endpoints, middleware pipeline, Blazor Server dashboard |

## Tech Stack

| Category | Technology |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Frontend | Blazor Server |
| Database | PostgreSQL via EF Core (Npgsql) |
| AI / LLM | Microsoft Semantic Kernel + OpenAI (gpt-4o-mini, optional stronger model for topics that fail a quality check) |
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

`GET /api/v1/topics/Type 2 Diabetes` (real response, lists shortened here)

```json
{
  "id": "a1000000-0000-0000-0000-000000000002",
  "name": "Type 2 Diabetes",
  "summary": "Type 2 diabetes is a chronic disease characterized by high blood glucose levels due to insulin resistance and inadequate insulin production. Common symptoms include increased thirst and urination, increased hunger, fatigue, blurred vision, numbness or tingling in the feet or hands, sores that do not heal, and unexplained weight loss, though many individuals may not experience noticeable symptoms. Treatment focuses on managing blood glucose through lifestyle changes like healthy eating and physical activity, along with medications, if necessary.",
  "topicType": "Disease",
  "category": "Endocrine, Nutritional & Metabolic",
  "icd11Code": "5A11",
  "icd11Title": "Type 2 diabetes mellitus",
  "symptoms": [
    "Increased thirst",
    "Increased urination",
    "Increased hunger",
    "Fatigue",
    "Blurred vision"
  ],
  "causes": [
    "Being overweight or having obesity",
    "Not being physically active",
    "Genetics and family history",
    "Age over 45"
  ],
  "treatments": [
    "Healthy eating",
    "Regular physical activity (e.g., 150 total minutes of activity per week)",
    "Monitoring blood glucose levels",
    "Medications (oral medications, insulin, and other injectable medicines if necessary)"
  ],
  "citations": [
    "National Institute of Diabetes and Digestive and Kidney Diseases"
  ],
  "tags": [
    "diabetes",
    "insulin",
    "blood sugar",
    "glucose",
    "obesity"
  ],
  "lastUpdated": "2026-11-03T15:35:26.826677Z",
  "version": 11
}
```

> Response field names adapt to topic type: for example, a Symptom returns `relatedSymptoms`, `associatedConditions`, and `management` instead of `symptoms`, `causes`, and `treatments`.

## Design Decisions

**Semantic Kernel** was chosen over direct OpenAI SDK calls because it provides a clean abstraction for prompt orchestration and makes it straightforward to swap models or add prompt filters without touching business logic. **Hangfire** handles the daily discovery and refresh pipeline: its PostgreSQL-backed persistence means jobs survive restarts, and the built-in dashboard makes it easy to monitor and retry failures. **Magic-link auth** eliminates password management entirely, which is appropriate for a developer-facing tool where security matters but registration friction should be minimal. The overall **Clean Architecture** keeps the domain free of infrastructure concerns, so the data providers, LLM processor, and persistence layer can all be replaced independently, which is useful when external APIs change or better models become available.

## Roadmap

**Data scope**
- Expand to drugs, procedures, vaccines, and diagnostics
- Cross-reference multiple sources per topic (MedlinePlus + PubMed + WHO)

**API features**
- Webhooks for topic update notifications
- SDKs / client libraries

**Infrastructure**
- Observability dashboards / APM (structured JSON logging in production already ships, and this is the next layer on top)
