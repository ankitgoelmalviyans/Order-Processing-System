# 00 · Approved implementation plan (original)

> **About this file.** This is the plan written in Claude Code *plan mode* (read-only, no code changes possible)
> and **approved by the developer before any code was written**:
>
> | Event | Time (UTC, 2026-10-02) |
> |---|---|
> | Plan written and approved | **08:58** |
> | First code commit (scaffold + domain) | 09:19 |
>
> It was kept outside the repository by the tool and **added to the repo afterwards**, as the root commit (author date = when the plan was written). Everything below the line is the **original text, unchanged**. It still mentions the pending environment checks and the "one commit per feature" intent; the actual outcomes are recorded in [01-planning](01-planning.md) to [06-reflection](06-reflection.md) and [ai-sdlc-process](ai-sdlc-process.md).

---

# Plan: E-commerce Order Processing System (.NET 8 microservices + Docker Compose + SDLC evidence docs)

## Context
Take-home assignment: build a backend that creates orders (multi-item), gets an order by ID, lists orders (optional status filter), updates status (PENDING → PROCESSING → SHIPPED → DELIVERED), cancels orders only while PENDING, and runs a background job that promotes PENDING → PROCESSING every 5 minutes. The reviewers also want to see how AI was used: what it was used for, what issues came up, and how they were fixed. The slides ask for this to be recorded stage by stage in `01-planning.md` … `06-reflection.md`, with an **Accept / Modify / Reject** decision and a verification for each important AI interaction.

Decisions already made with the user: **.NET 8 / C#**, **2 services + YARP gateway**, **Swagger only (no UI)**, and the whole thing starts with **`docker compose up`**.

Environment facts: `\\wsl.localhost\Ubuntu\home\ankit\Order-Processing-System` is empty and isn't a git repo. Windows has no .NET SDK, only Node 24. The Docker/SDK check inside WSL is still pending. Because every image builds with a multi-stage Dockerfile, the reviewer needs nothing but Docker. For build and test while implementing, I'll use the .NET 8 SDK in WSL if it's there, and the `mcr.microsoft.com/dotnet/sdk:8.0` container if it isn't.

## Architecture
```
client ──► gateway (YARP, :8080) ──► orders-api (:8080 internal) ──► postgres:16
                                         ▲
             status-worker ──HTTP──► /internal/jobs/promote-pending   (not routed by gateway)
```
- **orders-api** owns the database. No other service touches the DB, so there's no shared-database anti-pattern.
- **status-worker** is a .NET Worker Service. `PeriodicTimer` fires every `Worker__IntervalSeconds` (default 300; compose demo override 30) and calls the internal endpoint through a typed HttpClient with standard resilience (retry/backoff).
- **gateway** is YARP. It routes `/api/**` and `/swagger/**` to orders-api and blocks `/internal/**`. It adds or propagates an `X-Correlation-Id` header.

## Solution layout
```
OrderProcessing.sln
docker-compose.yml, .env.example, .gitignore, .editorconfig, README.md
src/Gateway/OrderProcessing.Gateway/
src/Services/Orders/OrderProcessing.Orders.Domain/          Order aggregate, OrderItem, OrderStatus, transition rules, domain exceptions
src/Services/Orders/OrderProcessing.Orders.Application/     OrderService (use cases), DTOs, FluentValidation validators, IOrderRepository
src/Services/Orders/OrderProcessing.Orders.Infrastructure/  OrdersDbContext (Npgsql), EF config, repository, migrations
src/Services/Orders/OrderProcessing.Orders.Api/             controllers, ProblemDetails exception handler, Swagger, health checks, migrate-on-startup
src/Services/StatusWorker/OrderProcessing.StatusWorker/     BackgroundService + typed OrdersApiClient
tests/OrderProcessing.Orders.UnitTests/                     xUnit + FluentAssertions
tests/OrderProcessing.Orders.IntegrationTests/              WebApplicationFactory + Testcontainers PostgreSQL
tests/OrderProcessing.StatusWorker.UnitTests/
requests/orders.http, scripts/smoke-test.sh, .github/workflows/ci.yml
docs/01-planning.md … docs/06-reflection.md
```

## Domain and data model
- `Order { Id (Guid), CustomerId, Status, Items, TotalAmount, CreatedAt, UpdatedAt, Version (Guid concurrency token) }`
- `OrderItem { Id, ProductId, ProductName, Quantity (>0), UnitPrice (decimal >0), LineTotal }`. `TotalAmount` is computed on the server and never trusted from the client.
- `OrderStatus`: PENDING, PROCESSING, SHIPPED, DELIVERED, **CANCELLED**. CANCELLED is needed to represent "cancel", and it's documented as a design decision.
- Transitions live in the domain (`Order.ChangeStatus`, `Order.Cancel`). Allowed moves are PENDING→PROCESSING→SHIPPED→DELIVERED, plus PENDING→CANCELLED. Anything else throws `InvalidOrderStateTransitionException`, which maps to **409**.
- The status is stored as a string, with an index on `(Status, CreatedAt)`.

## API (via gateway `http://localhost:8080`)
| Method | Path | Notes |
|---|---|---|
| POST | `/api/orders` | `{customerId, items[]}` returns 201 + Location. Validation: ≥1 item, qty>0, price>0, no duplicate productId |
| GET | `/api/orders/{id}` | 404 if missing |
| GET | `/api/orders?status=&page=&pageSize=` | optional filter, paged, newest first; invalid status → 400 |
| PATCH | `/api/orders/{id}/status` | `{status}`; state machine enforced → 409 on illegal move |
| POST | `/api/orders/{id}/cancel` | 409 unless PENDING |
| POST | `/internal/jobs/promote-pending` | worker only; returns `{promotedCount}` |
| GET | `/health` | DB health check, used by compose `depends_on: service_healthy` |

All errors use RFC 7807 ProblemDetails, written by a .NET 8 `IExceptionHandler`.

## Concurrency and job semantics (key walkthrough points)
- **Promote job**: one atomic `ExecuteUpdateAsync` (`UPDATE … SET status='PROCESSING' WHERE status='PENDING'`) that also rotates `Version`. It's idempotent, so overlapping runs or several worker replicas are harmless.
- **Cancel vs job race**: cancel and status changes load the aggregate and save it with the `Version` concurrency token. A `DbUpdateConcurrencyException` becomes a 409, so exactly one side wins.
- **Edge case**: should an order placed 1 second before a tick be promoted? Read literally, the requirement says every PENDING order is promoted on each tick. I'll add an optional `MinPendingAgeSeconds` setting (default 0) and record the choice in 01-planning.md.

## Build order (one feature at a time, as the slides ask; a git commit after each)
1. `git init`, scaffold the solution and projects, Dockerfiles, compose with postgres + health checks, and an empty api that serves `/health`.
2. Create order + Get by ID (domain, validator, repository, migration) with unit and integration tests.
3. List orders with the status filter and paging, plus tests.
4. Update status through the state machine, plus tests (table-driven transition tests).
5. Cancel order, including the concurrency-conflict test.
6. Internal promote endpoint, the status-worker service, and worker unit tests (fake HttpMessageHandler).
7. YARP gateway, correlation ID, Serilog console logging in every service.
8. `requests/orders.http`, `scripts/smoke-test.sh` (E2E against compose), and a GitHub Actions CI run of `dotnet build` + `dotnet test`.
9. README (quick start, mermaid architecture, API table, design patterns used, AI usage summary) and the docs/0x files.

## SDLC evidence docs (`docs/`)
Each file follows the slide's stage and ends with an **AI interaction log** table: *Context | AI suggestion | Decision (Accept/Modify/Reject) | Verification*.
- 01-planning: requirements restated, assumptions and ambiguities (CANCELLED state, job semantics, money/validation rules), acceptance criteria per feature
- 02-design: architecture, data model, options I considered (shared DB vs API call, monolith vs services, conditional update vs concurrency token) and why I chose each
- 03-build: per-feature notes, with the AI-generated code I inspected and changed
- 04-testing: test matrix covering normal, invalid-input and edge cases, plus the commands and results I actually ran
- 05-review: findings from a targeted AI review (`/code-review`, security review), each one verified or rejected
- 06-reflection: decisions I made myself, rework, and lessons

Issues are recorded **as they really happen during the build**, never invented. 06-reflection and the "human decision" columns are drafted, but marked for you to review and put in your own words before you submit, because you'll be the one defending them in the walkthrough.

## Patterns to call out (for the design-pattern round)
Clean/layered architecture, Repository, Aggregate + State machine (domain-enforced transitions), Options pattern, Hosted/Background service, Typed HttpClient + resilience (Retry), API Gateway, Optimistic concurrency, Global exception handler → ProblemDetails, Dependency Injection.

## Verification
1. `docker compose up --build -d`. All 4 containers should report healthy (`docker compose ps`).
2. Open Swagger at `http://localhost:8080/swagger`. Create an order (201), get it, list with `?status=PENDING`, try an illegal transition (409), and cancel a PENDING order (200), then cancel it again (409).
3. With `Worker__IntervalSeconds=30` in compose, wait for a tick. PENDING orders should become PROCESSING, and the worker logs should show `promotedCount`.
4. Confirm `curl http://localhost:8080/internal/jobs/promote-pending` returns 404, so the gateway doesn't expose it.
5. `dotnet test` (unit + Testcontainers integration) passes, and `scripts/smoke-test.sh` passes against the running stack.
6. Paste the real outputs into docs/04-testing.md.

## Open items before or while implementing
- Confirm that Docker Desktop with WSL integration (or Docker Engine in WSL) is available. If not, the stack can't be started for verification.
- Pushing to a GitHub remote is left to you, or I'll do it if you ask.
