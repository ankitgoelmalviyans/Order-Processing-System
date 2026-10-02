# Technical walkthrough

A guide to the codebase for the coding walkthrough. It covers how the work was carried out, what every folder does, how a request moves through the code, and where each requirement is implemented.

| Looking for… | Read |
|---|---|
| What the system does and how to start it | [README.md](../README.md) |
| **How the code is organised and how it works** | **this file** |
| How AI was used in the SDLC and how the evidence files were made | [ai-sdlc-process.md](ai-sdlc-process.md) |
| Why things were decided, and how AI was used at each stage | [01-planning](01-planning.md) → [02-design](02-design.md) → [03-build](03-build.md) → [04-testing](04-testing.md) → [05-review](05-review.md) → [06-reflection](06-reflection.md) |

---

## 1. How the work was carried out

| Step | What happened | Output |
|---|---|---|
| 1. Understand | Read the brief; AI asked 3 scoping questions (stack, service split, UI); developer chose .NET 8, 2 services + gateway, Swagger only | Scope ([01-planning](01-planning.md) §3) |
| 2. Plan | AI listed ambiguities and acceptance criteria and wrote an implementation plan in *plan mode* (read-only); the developer approved it before any code was written | [00-approved-plan](00-approved-plan.md) (root commit `bc542e3`), acceptance criteria ([01-planning](01-planning.md) §4) |
| 3. Design | Architecture, data model, concurrency strategy and test strategy chosen, with trade-offs | [02-design](02-design.md) |
| 4. Environment | Installed the .NET 8 SDK in WSL; confirmed Docker + Compose; `git init` | [03-build](03-build.md) §2 |
| 5. Build, inside out | **Domain** (rules, no dependencies) → **Application** (use cases) → **Infrastructure** (database) → **API** → **Worker** → **Gateway** → **Docker** | Commits `996b13a` … `e8e0d95` |
| 6. Test at each layer | Unit tests with the domain; integration tests on real Postgres with the API; worker tests with a fake clock; smoke test on the real Docker stack | [04-testing](04-testing.md) |
| 7. Review | AI code review; each finding reproduced on the running stack, then fixed with a regression test, or rejected | Commit `d118b51`, [05-review](05-review.md) |
| 8. Document | README, SDLC files, this walkthrough, CI pipeline | Commits `4de1c03` … `a3330c7` |
| 9. Publish | Pushed to GitHub; CI runs on every push: tests, test report, coverage, Docker smoke test | Commits `71cab66` onwards, [ai-sdlc-process](ai-sdlc-process.md) §3 Phase 7 |

**Why inside out?** The domain holds the business rules and depends on nothing, so it can be built and tested first in milliseconds. Each outer layer then only adds wiring around rules that are already proven.

---

## 2. Folder structure

```
Order-Processing-System/
│
├── docker-compose.yml          ← starts everything: postgres, orders-api, status-worker, gateway
├── docker-compose.tools.yml    ← optional: PostgreSQL on 127.0.0.1:5433 + pgAdmin on :5050
├── .env.example                ← optional overrides (job interval, ports, password)
├── .gitattributes              ← LF line endings everywhere (shell scripts break with CRLF)
├── OrderProcessing.sln         ← solution with all projects
├── global.json                 ← pins .NET SDK 8
├── Directory.Build.props       ← shared settings for every project (net8.0, nullable, warnings = errors)
├── Directory.Packages.props    ← ONE place for all NuGet versions (central package management)
├── .config/dotnet-tools.json   ← dotnet-ef tool for migrations
│
├── src/
│   ├── Gateway/OrderProcessing.Gateway/          ← SERVICE 1: public entry point (YARP reverse proxy)
│   │
│   ├── Services/Orders/                          ← SERVICE 2: orders (owns the database)
│   │   ├── OrderProcessing.Orders.Domain/          business rules, no dependencies
│   │   ├── OrderProcessing.Orders.Application/     use cases, validation, contracts
│   │   ├── OrderProcessing.Orders.Infrastructure/  EF Core + PostgreSQL
│   │   └── OrderProcessing.Orders.Api/             HTTP: controllers, errors, Swagger, startup
│   │
│   ├── Services/StatusWorker/OrderProcessing.StatusWorker/   ← SERVICE 3: background job
│   │
│   └── BuildingBlocks/OrderProcessing.BuildingBlocks/        ← small shared library (correlation id, logging)
│
├── tests/
│   ├── OrderProcessing.Orders.UnitTests/          Domain + Application, in memory
│   ├── OrderProcessing.Orders.IntegrationTests/   real API + real PostgreSQL (Testcontainers)
│   └── OrderProcessing.StatusWorker.UnitTests/    worker with a fake clock
│
├── scripts/
│   ├── smoke-test.sh           ← end-to-end checks against the running Docker stack
│   └── test-summary.py         ← turns .trx test results into the CI run's test report
├── requests/orders.http        ← clickable sample requests (VS Code REST Client / Rider / VS)
├── tools/pgadmin/servers.json  ← pre-registers the database in pgAdmin (tools file only)
├── .github/workflows/ci.yml    ← CI: build, tests, test report, coverage, then compose + smoke test
└── docs/                       ← approved plan, SDLC evidence, AI process, this walkthrough
```

### Every source file, and what it is for

**Domain**: *what is allowed* (pure C#, no packages)

| File | Purpose |
|---|---|
| [Order.cs](../src/Services/Orders/OrderProcessing.Orders.Domain/Order.cs) | Aggregate root. `Create()` enforces the invariants and calculates the total; `ChangeStatus()` / `Cancel()` are the **only** ways to change status; each change rotates `Version` |
| [OrderItem.cs](../src/Services/Orders/OrderProcessing.Orders.Domain/OrderItem.cs) | An order line (`LineNumber`, product, quantity, price, `LineTotal`). Can't change after the order is placed |
| [OrderStatus.cs](../src/Services/Orders/OrderProcessing.Orders.Domain/OrderStatus.cs) | `Pending, Processing, Shipped, Delivered, Cancelled` |
| [OrderStatusTransitions.cs](../src/Services/Orders/OrderProcessing.Orders.Domain/OrderStatusTransitions.cs) | **The state machine**: a table of the allowed `from → to` moves |
| [Exceptions.cs](../src/Services/Orders/OrderProcessing.Orders.Domain/Exceptions.cs) | `InvalidOrderException` (→ 400), `InvalidOrderStateTransitionException` (→ 409) |

**Application**: *what the system does* (use cases)

| File | Purpose |
|---|---|
| [OrderService.cs](../src/Services/Orders/OrderProcessing.Orders.Application/OrderService.cs) | One method per use case: Create, GetById, List, UpdateStatus, Cancel, PromotePending |
| [Contracts.cs](../src/Services/Orders/OrderProcessing.Orders.Application/Contracts.cs) | Request/response records (the API's JSON shapes), kept separate from the domain |
| [Validators.cs](../src/Services/Orders/OrderProcessing.Orders.Application/Validators.cs) | FluentValidation rules: item limits, price ≤ 2 decimals, paging bounds, … |
| [IOrderRepository.cs](../src/Services/Orders/OrderProcessing.Orders.Application/IOrderRepository.cs) | Persistence contract (Infrastructure implements it, so the dependency points inward) |
| [Exceptions.cs](../src/Services/Orders/OrderProcessing.Orders.Application/Exceptions.cs) | `OrderNotFoundException` (→ 404), `ConcurrencyConflictException` (→ 409) |
| [OrderProcessingOptions.cs](../src/Services/Orders/OrderProcessing.Orders.Application/OrderProcessingOptions.cs) | `MinPendingAgeSeconds` setting for the job |
| [DependencyInjection.cs](../src/Services/Orders/OrderProcessing.Orders.Application/DependencyInjection.cs) | `AddOrdersApplication()`: registers the service, validators and `TimeProvider` |

**Infrastructure**: *how data is stored*

| File | Purpose |
|---|---|
| [OrdersDbContext.cs](../src/Services/Orders/OrderProcessing.Orders.Infrastructure/Persistence/OrdersDbContext.cs) | EF Core context |
| [OrderConfiguration.cs](../src/Services/Orders/OrderProcessing.Orders.Infrastructure/Persistence/OrderConfiguration.cs) | Table/column mapping, `numeric(18,2)`, status as text, `(status, created_at)` index, `Version` as the concurrency token, items as an owned collection |
| [OrderRepository.cs](../src/Services/Orders/OrderProcessing.Orders.Infrastructure/Persistence/OrderRepository.cs) | Queries and saves. `PromotePendingAsync` = one bulk `UPDATE`; `SaveChangesAsync` turns a concurrency clash into `ConcurrencyConflictException` |
| `Persistence/Migrations/` | `InitialCreate` + `AddOrderItemLineNumber` (generated by `dotnet ef`) |
| [DependencyInjection.cs](../src/Services/Orders/OrderProcessing.Orders.Infrastructure/DependencyInjection.cs) | `AddOrdersInfrastructure()`; `MigrateOrdersDatabaseAsync()` applies migrations at startup, with retries |
| [DesignTimeDbContextFactory.cs](../src/Services/Orders/OrderProcessing.Orders.Infrastructure/Persistence/DesignTimeDbContextFactory.cs) | Lets `dotnet ef migrations add` run without starting the API |

**API**: *the HTTP edge*

| File | Purpose |
|---|---|
| [Program.cs](../src/Services/Orders/OrderProcessing.Orders.Api/Program.cs) | Startup: DI, JSON enum format, Swagger, health check, forwarded headers, middleware order, migrations |
| [OrdersController.cs](../src/Services/Orders/OrderProcessing.Orders.Api/Controllers/OrdersController.cs) | The 5 public endpoints. Thin: each one calls a single `OrderService` method |
| [InternalJobsController.cs](../src/Services/Orders/OrderProcessing.Orders.Api/Controllers/InternalJobsController.cs) | `POST /internal/jobs/promote-pending`, called only by the worker and hidden from Swagger |
| [GlobalExceptionHandler.cs](../src/Services/Orders/OrderProcessing.Orders.Api/GlobalExceptionHandler.cs) | **One place** mapping exceptions to HTTP status + ProblemDetails JSON |

**StatusWorker**: *the background job*

| File | Purpose |
|---|---|
| [PendingOrderPromotionWorker.cs](../src/Services/StatusWorker/OrderProcessing.StatusWorker/PendingOrderPromotionWorker.cs) | `BackgroundService` loop on `PeriodicTimer` (every 300 s); one failed run never stops the loop |
| [OrdersApiClient.cs](../src/Services/StatusWorker/OrderProcessing.StatusWorker/OrdersApiClient.cs) | Typed `HttpClient` that calls the internal endpoint and sends a correlation id |
| [WorkerOptions.cs](../src/Services/StatusWorker/OrderProcessing.StatusWorker/WorkerOptions.cs) | Interval, run-on-startup, API URL (validated at startup) |
| [Program.cs](../src/Services/StatusWorker/OrderProcessing.StatusWorker/Program.cs) | Host setup, retry/circuit breaker/timeouts for the HTTP call |

**Gateway** and **BuildingBlocks**

| File | Purpose |
|---|---|
| [Gateway/Program.cs](../src/Gateway/OrderProcessing.Gateway/Program.cs) | Correlation id, request logging, `/health`, `/` → `/swagger`, YARP proxy |
| [Gateway/appsettings.json](../src/Gateway/OrderProcessing.Gateway/appsettings.json) | **The routing table**: only `/api/orders/**` and `/swagger/**` are forwarded; active health check on orders-api |
| [CorrelationIdMiddleware.cs](../src/BuildingBlocks/OrderProcessing.BuildingBlocks/CorrelationIdMiddleware.cs) | Reuses or creates `X-Correlation-Id`, adds it to every log line and to the response |
| [LoggingExtensions.cs](../src/BuildingBlocks/OrderProcessing.BuildingBlocks/LoggingExtensions.cs) | Serilog console format shared by the web services |

### Layer dependency rule

```
          Api ──────────────┐
           │                ▼
           │         Infrastructure
           ▼                │
      Application ◄─────────┘
           │
           ▼
         Domain          (depends on nothing)
```

Dependencies only point **inward**. Application defines `IOrderRepository`; Infrastructure implements it. So you could swap the database without touching business rules, and test the rules without a database.

---

## 3. How a request flows through the code

### 3.1 Create an order: `POST /api/orders`

```
Client
  │  POST http://localhost:8080/api/orders
  ▼
gateway            CorrelationIdMiddleware → YARP route "orders-api" → http://orders-api:8080
  ▼
orders-api pipeline (Program.cs, in this order)
  1. UseForwardedHeaders      makes Location use the public host, not orders-api:8080
  2. UseCorrelationId         same id as the gateway, so logs line up
  3. UseSerilogRequestLogging "HTTP POST /api/orders responded 201 in 12 ms"
  4. UseExceptionHandler      catches anything thrown below → GlobalExceptionHandler
  5. MapControllers
  ▼
OrdersController.Create(request)
  ▼
OrderService.CreateAsync
  a. createValidator.ValidateAndThrowAsync   bad input → ValidationException → 400
  b. Order.Create(customerId, items, now)    domain re-checks invariants, numbers lines,
                                             status = PENDING, total = Σ qty × price
  c. repository.AddAsync + SaveChangesAsync  INSERT orders + order_items (one transaction)
  d. OrderResponse.From(order)
  ▼
CreatedAtAction → 201 Created + Location: http://localhost:8080/api/orders/{id}
```

### 3.2 Cancel an order: `POST /api/orders/{id}/cancel` (the concurrency case)

```
OrderService.CancelAsync
  1. repository.GetByIdAsync(id)        not found → OrderNotFoundException → 404
  2. order.Cancel(now)
       → ChangeStatus(Cancelled)
       → OrderStatusTransitions.CanTransition(Pending, Cancelled)?   no → 409 "Invalid status transition"
       → Status = Cancelled, Version = new Guid
  3. repository.SaveChangesAsync
       SQL: UPDATE orders SET status='Cancelled', version=@new
            WHERE id=@id AND version=@versionWeRead
       0 rows (the job changed it after step 1) → ConcurrencyConflictException → 409 "Concurrent update"
```

This is why a cancel can never overwrite PROCESSING, even if the job runs between steps 1 and 3. It's proved by [ConcurrencyTests.cs](../tests/OrderProcessing.Orders.IntegrationTests/ConcurrencyTests.cs).

### 3.3 Background job: PENDING → PROCESSING every 5 minutes

```
status-worker                                          orders-api
─────────────                                          ──────────
PeriodicTimer ticks (300 s)
  RunOnceAsync
    new scope → OrdersApiClient
    POST http://orders-api:8080/internal/jobs/promote-pending  ─►  InternalJobsController
      (retry + circuit breaker; safe because idempotent)            OrderService.PromotePendingAsync
                                                                     cutoff = now − MinPendingAgeSeconds
                                                                     OrderRepository.PromotePendingAsync:
                                                                       UPDATE orders
                                                                       SET status='Processing', updated_at=@now, version=@new
                                                                       WHERE status='Pending' AND created_at <= @cutoff
    ◄──────────────────────────────────────────────  200 {"promotedCount": 3}
  log "Promotion run finished: 3 orders moved PENDING -> PROCESSING"
  (any exception → logged, loop continues to the next tick)
```

Design points:
- **One SQL statement:** atomic, fast, and running it twice does no harm (idempotent).
- **The worker never touches the database**, so orders-api stays the only writer.
- The gateway doesn't route `/internal/**`, so the public can't trigger the job.

### 3.4 List orders: `GET /api/orders?status=PENDING&customerId=c1&page=1&pageSize=20`

`OrdersController.List` → `OrderService.ListAsync` (validates status, page and pageSize) → `OrderRepository.ListAsync`: optional `WHERE status` / `WHERE customer_id`, `COUNT(*)` for the total, `ORDER BY created_at DESC, id` (stable, so pages never overlap), `OFFSET/LIMIT` → `PagedResponse { items, page, pageSize, totalCount, totalPages }`.

### 3.5 Update status: `PATCH /api/orders/{id}/status` `{"status":"SHIPPED"}`

The same shape as cancel. `order.ChangeStatus(Shipped)` asks the state machine; an illegal move (e.g. PENDING → DELIVERED) returns 409, and the concurrency token protects the save.

### 3.6 Errors: how an exception becomes an HTTP response

Code anywhere throws a typed exception, and [GlobalExceptionHandler.cs](../src/Services/Orders/OrderProcessing.Orders.Api/GlobalExceptionHandler.cs) maps it:

| Exception | HTTP | Example |
|---|---|---|
| `ValidationException` (FluentValidation) | 400 + field errors | `items: []` |
| `InvalidOrderException` (domain) | 400 | invariant broken outside the API path |
| `OrderNotFoundException` | 404 | unknown id |
| `InvalidOrderStateTransitionException` | 409 | cancel a SHIPPED order |
| `ConcurrencyConflictException` | 409 | job changed the order mid-request |
| `BadHttpRequestException` | its own code (e.g. 413) | 31 MB body |
| anything else | 500, generic message | database down. The details are only logged. |

Controllers contain **no** try/catch. That keeps them to one line each.

---

## 4. Where each requirement is implemented

| Requirement | Endpoint | Code | Tests |
|---|---|---|---|
| Create order with multiple items | `POST /api/orders` | `OrderService.CreateAsync`, `Order.Create`, `CreateOrderRequestValidator` | `OrderTests`, `ValidatorTests`, `OrdersApiTests.Create_*` |
| Retrieve order by ID | `GET /api/orders/{id}` | `OrderService.GetByIdAsync`, `OrderRepository.GetByIdAsync` | `Get_*`, `Items_are_returned_in_the_order_they_were_placed` |
| Statuses + update | `PATCH /api/orders/{id}/status` | `OrderStatusTransitions`, `Order.ChangeStatus` | `OrderStatusTransitionsTests` (all 25 pairs), `Update_status_*` |
| Background job every 5 min | (internal) | `PendingOrderPromotionWorker`, `OrderRepository.PromotePendingAsync` | `PendingOrderPromotionWorkerTests`, `Promote_pending_*`, `ConcurrencyTests` |
| List, optionally by status | `GET /api/orders?status=` | `OrderService.ListAsync`, `OrderRepository.ListAsync` | `List_*` |
| Cancel only if PENDING | `POST /api/orders/{id}/cancel` | `Order.Cancel` → state machine | `Cancel_*`, `Concurrent_cancel_requests_produce_exactly_one_success` |

---

## 5. What happens on `docker compose up --build`

1. **Build:** each service has a multi-stage [Dockerfile](../src/Services/Orders/OrderProcessing.Orders.Api/Dockerfile). The `sdk:8.0` image restores (cached layer) and publishes; the result is copied into a small `aspnet:8.0-alpine` / `runtime:8.0-alpine` image that runs as a **non-root** user.
2. **postgres** starts. Its healthcheck `pg_isready` must pass first.
3. **orders-api** starts only when postgres is *healthy*, applies EF migrations (creates or updates tables), then reports healthy on `/health` (which also checks the DB).
4. **status-worker** and **gateway** start only when orders-api is *healthy*.
5. Only the gateway's port `8080` is published. Postgres and orders-api are reachable only on the private Docker network. To browse the data, add `-f docker-compose.tools.yml`; it publishes Postgres on `127.0.0.1:5433` and pgAdmin on `127.0.0.1:5050`.

```
postgres (healthy) ─► orders-api (migrate → healthy) ─┬─► gateway (:8080)
                                                      └─► status-worker
```

---

## 6. Running and developing without Docker

Needs the .NET 8 SDK. Start only a database in Docker, then run each service from your IDE or terminal:

```bash
docker run -d --name orders-db -p 5432:5432 \
  -e POSTGRES_DB=orders -e POSTGRES_USER=orders -e POSTGRES_PASSWORD=orders_dev_password postgres:16-alpine

dotnet run --project src/Services/Orders/OrderProcessing.Orders.Api        # http://localhost:5001/swagger
dotnet run --project src/Gateway/OrderProcessing.Gateway                   # http://localhost:8080
dotnet run --project src/Services/StatusWorker/OrderProcessing.StatusWorker -- --Worker:IntervalSeconds=30
```

The default `appsettings.json` values already point at `localhost` (API on 5001, gateway → 5001, worker → 5001).

| Task | Command |
|---|---|
| Build | `dotnet build` (warnings fail the build) |
| All tests | `dotnet test` (Docker must be running for the integration tests) |
| One test class | `dotnet test --filter FullyQualifiedName~ConcurrencyTests` |
| Add a DB migration | `dotnet ef migrations add <Name> -p src/Services/Orders/OrderProcessing.Orders.Infrastructure -o Persistence/Migrations` |
| End-to-end | `docker compose up --build -d && ./scripts/smoke-test.sh` |
| Browse the database | `docker compose -f docker-compose.yml -f docker-compose.tools.yml up -d` → pgAdmin http://localhost:5050, or any SQL client on `localhost:5433` |
| Test report like CI | `dotnet test --logger "trx;LogFileName=<project>.trx" --results-directory TestResults` per project, then `python3 scripts/test-summary.py TestResults/*.trx` |
| Add a NuGet package | Add the version to `Directory.Packages.props`, then `<PackageReference Include="…" />` (no version) in the project |

---

## 7. Suggested 5-minute live demo

1. `docker compose up --build -d` → `docker compose ps` (all healthy).
2. Open `http://localhost:8080/swagger` → **POST** an order with 2 items → show `201`, the `Location` header and the server-calculated total.
3. **GET** it by id → **GET** the list with `status=PENDING`.
4. **PATCH** to `DELIVERED` → `409` (can't skip steps). **PATCH** to `PROCESSING` → `200`. **Cancel** → `409` (no longer PENDING).
5. Create a second order and cancel it → `200 CANCELLED`.
6. Show the job: `docker compose logs -f status-worker` (start with `WORKER_INTERVAL_SECONDS=30` for the demo) → a new PENDING order becomes PROCESSING.
7. `curl -X POST localhost:8080/internal/jobs/promote-pending` → `404` (internal endpoint not public).
8. Run `./scripts/smoke-test.sh` → 23 PASS.
9. Optional: show the latest green CI run on GitHub → **Summary** (test report by class, coverage, smoke test from a clean machine).

---

## 8. Likely walkthrough questions

| Question | Short answer | Look at |
|---|---|---|
| Why is the worker a separate service? | It scales and deploys independently of the API, and a crash in one doesn't take down the other. It owns no data. | §3.3 |
| Why does the worker call the API instead of the DB? | One writer per database: the rules and concurrency handling live in one place, avoiding the shared-database anti-pattern. | [02-design](02-design.md) §1 |
| What if two worker instances run at once? | Harmless: the `UPDATE … WHERE status='Pending'` is atomic and idempotent, so the second run promotes 0. | `Promote_pending_moves_only_pending_orders_and_is_idempotent` |
| What if a customer cancels while the job runs? | Optimistic concurrency: exactly one wins, and the other gets 409. No lost update. | §3.2, `ConcurrencyTests` |
| Why `PeriodicTimer` and not `Task.Delay` or Hangfire/Quartz? | No drift or overlapping runs, testable with `TimeProvider`, and no extra infrastructure for one fixed-interval job. A scheduler library would be justified with many jobs or cron rules. | `PendingOrderPromotionWorker` |
| What if the API is down when the job fires? | Retries with backoff inside the call, then a logged error; the next tick tries again. | `A_failed_run_does_not_stop_later_runs` |
| Where are the business rules? | Only in the Domain. `OrderStatusTransitions` is the single state-machine table. | §2 Domain |
| How is validation done? | FluentValidation in Application (detailed 400s), plus domain invariants as a second line of defence. | `Validators.cs`, `Order.Create` |
| Why Testcontainers instead of an in-memory DB? | Tests run against the same engine as production: bulk update, concurrency token, numeric precision. | [02-design](02-design.md) §6 |
| How would you scale this? | Several orders-api replicas behind YARP (stateless); several workers are safe (idempotent); then a read replica, keyset paging, and events via an outbox. | [06-reflection](06-reflection.md) §4 |
| What's missing for production? | Auth, idempotency keys on create, secrets management, tracing, rate limiting. | [06-reflection](06-reflection.md) §4 |
| How do you know it works outside your machine? | GitHub Actions builds from a clean checkout, runs all tests (integration tests on a real Postgres), then starts the Docker stack and runs the smoke test, on every push. | [ci.yml](../.github/workflows/ci.yml), the CI badge in the README |
| Which design patterns are used? | Aggregate, state machine, repository, optimistic concurrency, background service, API gateway, typed client + retry/circuit breaker, options, central exception handler, DI. | [README](../README.md#design-patterns-used) |
