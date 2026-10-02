# 04 · Test

**Stage goal:** run normal, invalid-input and edge-case checks, and record the real results.

## 1. Test pyramid

| Layer | Project / script | Count | Runs against |
|---|---|---|---|
| Unit | `OrderProcessing.Orders.UnitTests` | 86 | Domain + Application in memory, `FakeTimeProvider` |
| Unit | `OrderProcessing.StatusWorker.UnitTests` | 8 | Worker with a fake clock and a stub HTTP handler |
| Integration | `OrderProcessing.Orders.IntegrationTests` | 42 | Real ASP.NET pipeline (`WebApplicationFactory`) + real **PostgreSQL 16** (Testcontainers) |
| End-to-end | `scripts/smoke-test.sh` | 23 (+1) | The `docker compose` stack through the **gateway** |

## 2. Test matrix

| Feature | Normal | Invalid input | Edge cases |
|---|---|---|---|
| Create | 201 + Location; server total `119.48` for 2×49.99 + 19.50 | No items; missing/blank customer; qty 0/−1; price 0/−1; price with 3 dp; blank product id; `items:[null]`; malformed JSON; `"quantity":"lots"` → all 400 with field keys | Duplicate product differing only by case or whitespace (F8); 101 items; price > 1,000,000 (F3 overflow) |
| Get | 200 with the same data as the create response | Unknown id → 404 problem+json | `Accept: text/plain` still → 404 (F6); items returned in submitted order (F9) |
| List | Filter by status, by customer, both | `status=NOT_A_STATUS`, `status=7`, `page=0`, `pageSize=0/101` → 400 | Paging: 5 orders / pageSize 2 → 2+2+1, no overlap; `page=2147483647` → 400 (F2) |
| Update status | Full PENDING→PROCESSING→SHIPPED→DELIVERED | `{}`, `{"status":"UNKNOWN"}`, `{"status":1}` → 400; unknown id → 404 | **All 25 (from, to) pairs** checked against the state machine, in both the transition table and the aggregate |
| Cancel | PENDING → 200 CANCELLED | Unknown id → 404 | Cancel twice → 409; cancel PROCESSING/SHIPPED/DELIVERED → 409; **5 concurrent cancels → exactly one 200** |
| Background job | Promotes only PENDING; cancelled stays cancelled | Orders API down → logged, next tick still runs | Idempotent (second run promotes 0); min-age cutoff; **cancel that read the order before the job ran → 409, order stays PROCESSING** |
| Scheduling | Called once per 5 min for 3 ticks | n/a | Nothing at 4 min 59 s; `RunOnStartup`; new correlation id per run; clean shutdown |
| Cross-cutting | Correlation id echoed, or generated if missing; `/health` "Healthy" | n/a | `Location` uses `X-Forwarded-Host`/`Proto` (F1); 31 MB body → 413, not 500 (F4, manual) |

## 3. Results (real runs, 2026-10-02)

```
$ dotnet test
Passed!  - Failed: 0, Passed:  8, Skipped: 0, Total:  8 - OrderProcessing.StatusWorker.UnitTests.dll
Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86 - OrderProcessing.Orders.UnitTests.dll
Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42 - OrderProcessing.Orders.IntegrationTests.dll
```

```
$ WORKER_INTERVAL_SECONDS=30 docker compose up --build -d
$ docker compose ps
SERVICE         STATUS
gateway         Up 12 seconds (healthy)
orders-api      Up 21 seconds (healthy)
postgres        Up 2 minutes (healthy)
status-worker   Up 12 seconds

$ WAIT_FOR_WORKER=1 ./scripts/smoke-test.sh
Health and routing
  PASS gateway health is 200
  PASS swagger document is served through the gateway
  PASS internal endpoint is NOT exposed by the gateway
Create and get        ... 6 PASS
Validation            ... 3 PASS
List and filter       ... 3 PASS
Status transitions    ... 5 PASS
Cancel                ... 3 PASS
Background job (waiting up to 90s for PENDING -> PROCESSING)
  PASS worker promoted the order to PROCESSING
Result: 24 passed, 0 failed

$ docker compose logs status-worker
[09:35:10 INF] status-worker eae7355f… PendingOrderPromotionWorker: Promotion run finished: 1 orders moved PENDING -> PROCESSING

$ curl -H 'X-Correlation-Id: demo-123' http://localhost:8080/api/orders?pageSize=1    # gateway → api
X-Correlation-Id: demo-123
[09:35:13 INF] orders-api demo-123 RequestLoggingMiddleware: HTTP GET /api/orders responded 200
```

**Coverage** (`dotnet test --collect:"XPlat Code Coverage"`, line coverage per assembly from the suite that targets it):

| Assembly | Line | Notes |
|---|---|---|
| Domain | 94.7 % | |
| Application | 99.3 % | |
| Api | 93.7 % | |
| BuildingBlocks | 100 % | |
| Infrastructure | 73.4 % | Uncovered lines are mostly EF-generated migration/designer code |
| StatusWorker | 62.2 % | Uncovered lines are mostly `Program.cs` host bootstrap; worker logic is fully covered |

## 4. Do the tests actually catch bugs? (mutation checks)

Every test passed on the first run, which is a warning sign in itself. So I deliberately broke the code and confirmed the tests fail:

| Mutation | Tests that failed | Result |
|---|---|---|
| Allow `PROCESSING → CANCELLED` in the transition table | 3 unit (transition pairs, `Cancel_is_rejected_once_order_has_left_pending`) + 1 integration (`Cancel_is_rejected_once_order_is_processing`) | ✅ caught |
| Remove `Version` rotation from the bulk promote `UPDATE` | `ConcurrencyTests.Cancel_loses_if_background_job_promoted_the_order_after_it_was_read` | ✅ caught. This is the exact lost-update race the token prevents. |

(Both mutations were reverted with `git checkout`. My first attempt at the second mutation produced invalid C# because of a bad `sed`; it was redone properly.)

## 5. Problems found by testing

| Found by | Problem | Outcome |
|---|---|---|
| Smoke test, first run | 10 of 22 checks failed with 404: a greedy regex in the AI-written script grabbed an item id (see 03, B5) | Script fixed; assertions strengthened |
| Smoke test, first run | Expected 4xx were logged at ERROR with stack traces (see 03, B6) | Fixed (and re-fixed after review F5) |
| My own verification | A grep for `' ERR '` returned "0 errors". The log format is `[hh:mm:ss ERR]`, so the check could never match. | Noticed when the DB-down 500 *also* showed 0 errors; re-ran with `ERR\]` and confirmed 4 ERR lines, all from that one request |

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| What to test for a state machine | Generate all 25 (from, to) pairs via `MemberData` and compare against an explicit allow-list in the test | **Accept** | 51 transition tests; the mutation check proves they bite |
| Testing a 5-minute timer | `FakeTimeProvider` + `PeriodicTimer(interval, timeProvider)` instead of real waits | **Accept** | 6 worker tests run in ~50 ms |
| Testing the race condition | Interleave two units of work by hand (read → job runs → save) instead of hoping threads collide | **Accept** | Deterministic; it's the test that caught mutation 2 |
| "All green on first run" | Run mutation checks before trusting the suite | **Accept** | See section 4 |
| E2E script | Bash + curl only, with no `jq` dependency, so it runs anywhere | **Modify**: parsing bug fixed (B5) | 24/24 |
