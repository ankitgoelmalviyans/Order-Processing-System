# 02 · Design

**Stage goal:** compare data-model and architecture options, choose an approach and explain the trade-offs.

## 1. Architecture

```
client ─► gateway (YARP :8080) ─► orders-api ─► PostgreSQL
                                     ▲
          status-worker ── HTTP ─────┘  POST /internal/jobs/promote-pending (not routed by gateway)
```

| Decision | Options considered | Chosen | Why / trade-off accepted |
|---|---|---|---|
| Service split | Monolith · 2 services + gateway · event-driven with a broker | **2 services + gateway** (developer's choice) | Shows service boundaries, an API gateway and an independently scheduled worker, while keeping the walkthrough explainable. Trade-off: no broker, so nothing is "event-driven". Listed as a next step. |
| How the worker changes orders | (a) Worker writes to the same DB · (b) Worker calls an Orders API endpoint | **(b)** | One writer per database, so rules and concurrency stay in one service. (a) is the classic "shared database" anti-pattern. Cost: one internal HTTP hop, plus the endpoint must be hidden from the public. |
| Hiding the internal endpoint | API key · network isolation | **Network isolation**: gateway routes only `/api/orders/**` and `/swagger/**`; orders-api publishes no host port | Simple and verifiable (`smoke-test.sh` asserts 404). Limitation: anything else inside the compose network could call it (see 05-review). |
| Gateway | Nginx · YARP · none | **YARP** | Same stack (.NET); config-driven routes; active health checks; easy to add auth or rate limiting later. |
| Database | PostgreSQL · SQL Server · in-memory | **PostgreSQL 16** | Small image, first-class EF Core provider, real transactions for the concurrency story. |

## 2. Data model

```
orders                                   order_items
-------------------------------          ---------------------------------
id            uuid  PK                   id            uuid  PK
customer_id   varchar(64)  (idx)         order_id      uuid  FK → orders (cascade)
status        varchar(20)               line_number   int        ← added after review (F9)
total_amount  numeric(18,2)              product_id    varchar(64)
created_at    timestamptz                product_name  varchar(200)
updated_at    timestamptz                quantity      int
version       uuid  (concurrency token)  unit_price    numeric(18,2)
INDEX (status, created_at)
```

- **Aggregate:** `Order` is the aggregate root and `OrderItem` is an EF *owned* collection. There is no item repository, and items can't change after placement.
- **Status stored as text** (`'Pending'`), not an int: readable in SQL and safe if the enum is reordered.
- **`(status, created_at)` index:** serves both "list by status, newest first" and the job's `WHERE status = 'Pending'`.
- **Total stored**, not calculated in SQL: lists don't need to join the items to show totals. It's written once at creation, so it can't drift.

## 3. Concurrency design (key walkthrough point)

| Operation | Mechanism |
|---|---|
| Cancel / PATCH status | Load aggregate → domain method validates the transition → `SaveChanges` with `WHERE version = @original`. 0 rows affected → `DbUpdateConcurrencyException` → **409 Concurrent update**. |
| Background promote | A single `UPDATE orders SET status='Processing', updated_at=@now, version=@new WHERE status='Pending' AND created_at <= @cutoff`. Atomic and idempotent; **rotating `version`** makes any in-flight cancel that read the old row fail instead of overwriting PROCESSING. |

Options considered:

| Option | Verdict |
|---|---|
| Conditional `UPDATE … WHERE status = 'Pending'` for cancel too (no token) | Works, but puts the state-machine rule in SQL as well as the domain: two sources of truth. |
| Pessimistic lock (`SELECT … FOR UPDATE`) | Correct, but holds locks across the request and adds a deadlock risk for no benefit at this scale. |
| PostgreSQL `xmin` as the token | Zero-maintenance, but Postgres-specific, and it can't be rotated deliberately from the bulk update. **Chosen instead: an application-managed `Guid Version`.** |

## 4. API design

- REST resource `/api/orders`. `cancel` is a POST sub-resource (it's a business action, not a field update). Status changes use `PATCH /status`.
- Enums travel as `"PENDING"`-style strings; numeric enum values are rejected (`allowIntegerValues: false`).
- All errors are RFC 7807 ProblemDetails from one `IExceptionHandler`: 400 validation (with field errors), 404, 409 (illegal transition *or* concurrent update), 500 without internals.
- FluentValidation is the **single** validation source. MVC's implicit `[Required]` for non-nullable reference types is switched off so clients don't get two differently worded error sets.

## 5. Background job design

- `BackgroundService` + **`PeriodicTimer`**: no drift, and a slow run delays the next tick instead of stacking runs. It takes a `TimeProvider`, so tests can advance a fake clock by 5 minutes instantly.
- The per-run `try/catch` logs and continues: one failed run never kills the worker.
- A typed `HttpClient` is resolved **per run from a new scope**, so a singleton never holds a stale handler.
- `AddStandardResilienceHandler` gives retry with backoff and jitter, plus a circuit breaker and timeouts. Retrying a POST is normally risky; here it's safe because the endpoint is idempotent.

## 6. Testing strategy (decided at design time)

| Option | Verdict |
|---|---|
| EF InMemory / SQLite for integration tests | Rejected: they don't behave like Postgres for `ExecuteUpdate`, concurrency tokens, `numeric` precision or `timestamptz`. |
| **Testcontainers PostgreSQL + WebApplicationFactory** | **Chosen**: the real API pipeline against the real engine; Docker is already required. |
| Real-time waits for the 5-minute job | Rejected: slow and flaky. **`FakeTimeProvider`** advances time deterministically. |

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| How should the worker update orders? | Call an internal Orders API endpoint instead of sharing the DB | **Accept** | `smoke-test.sh`: `/internal/**` returns 404 through the gateway; worker log shows "1 orders moved PENDING -> PROCESSING" |
| Cancel vs. job race | App-managed `Guid Version` token, rotated by the bulk update | **Accept** | Mutation test: removing the rotation makes `ConcurrencyTests.Cancel_loses_if_background_job_promoted…` fail (04-testing) |
| Integration test database | Testcontainers PostgreSQL rather than SQLite or InMemory | **Accept** | 42 integration tests run against `postgres:16-alpine` |
| Enum JSON format | `JsonNamingPolicy.SnakeCaseUpper` so the API shows `PENDING` while C# keeps `Pending` | **Accept** | `Update_status_with_missing_or_invalid_status_returns_400` (`{"status":1}` → 400) |
| Item ordering | No explicit ordering for owned items (implicit assumption) | **Rejected later**: the review (F9) showed EF returns items in arbitrary order → `line_number` column added | `Items_are_returned_in_the_order_they_were_placed` |
