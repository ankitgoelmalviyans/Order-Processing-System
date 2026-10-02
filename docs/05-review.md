# 05 · Review

**Stage goal:** ask AI for a targeted review and verify each material finding before acting on it.

## 1. How the review was run

- **Tool:** Claude Code `/code-review` at `high` effort, over the `src/` tree after the stack was working and all tests were green (commit `e8e0d95`).
- **Focus:** correctness bugs, error handling, concurrency, configuration, and behaviour behind the gateway.
- **Rule applied:** no finding is accepted on the AI's word. Each one was **reproduced against the running Docker stack** where possible, or confirmed by reading the code. Fixes come with a regression test.

## 2. Findings, verification and decisions

| # | Finding (AI) | How I verified it | Result before fix | Decision | Fix + regression test |
|---|---|---|---|---|---|
| F1 | `CreatedAtAction` builds `Location` from the internal Host; the API ignores YARP's `X-Forwarded-*` | `curl -D -` POST through the gateway | `Location: http://orders-api:8080/api/orders/…` (an internal hostname leaked, and unusable by clients) | **Accept** | `UseForwardedHeaders` (XFF, Proto, Host; trusted because the API is private). Now `Location: http://localhost:8080/…`. Test `Location_header_uses_the_forwarded_public_host` |
| F2 | `(page - 1) * pageSize` overflows; `page` has no upper bound | `GET /api/orders?page=2147483647&pageSize=100` | **500** (`OFFSET must not be negative`) | **Accept** | `Page ≤ int.MaxValue / MaxPageSize` → 400. Test case added to `List_with_invalid_query_returns_400` |
| F3 | Unit price is validated against `numeric(18,2)` but the total isn't, so a valid request can overflow `total_amount` | POST qty 10 × 9,999,999,999,999,999.99 | **500** (numeric field overflow) | **Accept, modified** | Cap unit price at 1,000,000, so the worst-case total (100 × 10,000 × 1e6 = 1e12) always fits. Tests: integration case + `Worst_case_valid_order_total_fits_numeric_18_2` |
| F4 | `BadHttpRequestException` (e.g. 413 body too large) falls into the catch-all → 500 | 31 MB POST straight to orders-api (curl container on the compose network) | Confirmed by reading the code (the catch-all arm) | **Accept** | Map to `e.StatusCode`. Re-test: **413**, logged at Info |
| F5 | My earlier fix (silencing `ExceptionHandlerMiddleware` at `Fatal`) also hides real failures | Request from F6 below: it failed with a 500 body and left **no log line at all** | Confirmed | **Accept, modified** | The reviewer suggested an MVC filter; I chose a **Serilog filter that drops only *expected* exception types** from that category, keeping one list (`GlobalExceptionHandler.IsExpected`). Re-test: stopped Postgres → 500 **is** logged at ERR with stack trace; 4xx produce 0 ERR lines |
| F6 | If the ProblemDetails writer declines (e.g. `Accept: text/plain`), the handler returns `false` and the middleware rethrows | `curl -H 'Accept: text/plain' /api/orders/{unknown}` | Status 404 but a **500 "An error occurred" body**, and no log | **Accept** | Fall back to writing `application/problem+json` and always return `true`. Test `Not_found_is_still_404_when_client_does_not_accept_json` |
| F7 | The resilience defaults (10 s per attempt, 30 s total) can cancel a long promote; the server then rolls back the UPDATE | Read the code and the library defaults; not reproduced (needs a huge backlog) | Plausible | **Accept** | 60 s per attempt, 3 min total, circuit-breaker sampling 2 min (must be ≥ 2× the attempt timeout) |
| F8 | Domain duplicate-product check groups **untrimmed** ids but stores trimmed ones | Read the code: `"SKU-1"` and `"SKU-1 "` pass the domain check | Confirmed (validator caught it, but the domain didn't) | **Accept** | Group on `Trim()`; domain also rejects blank product id/name. Test `Create_with_duplicate_product_ids_differing_only_by_whitespace_is_rejected` |
| F9 | Owned item collection has no defined order | Created A1 B2 C3 D4, then GET | Returned **C3 A1 B2 D4** | **Accept** | `LineNumber` on `OrderItem` + migration `AddOrderItemLineNumber`; `Items` sorted by it; `lineNumber` in the API. Test `Items_are_returned_in_the_order_they_were_placed` |
| F10 | Worker duplicates the Serilog output template from BuildingBlocks | Read the code: true | n/a | **Reject** | BuildingBlocks references the ASP.NET Core framework; the worker deliberately runs on the slimmer `runtime` image without it. One duplicated format string is cheaper than splitting a shared library for it. |

**Summary:** 10 findings; 9 accepted (2 with a different fix than suggested); 1 rejected with a reason. Six of the accepted findings were real, reproducible failures in a codebase whose 126 tests were all green at the time.

## 3. Security notes (manual review)

| Topic | Status |
|---|---|
| Internal endpoint exposure | Not routed by the gateway, and no host port on orders-api; verified by the smoke test. **Residual risk:** any container on the compose network can call it. Next step: a shared-secret header, or mTLS between services. |
| Forwarded headers trust | `KnownNetworks`/`KnownProxies` are cleared, which is acceptable *only* because orders-api isn't reachable from outside. Restrict this to the gateway's network if that changes. |
| Log injection | The correlation id is restricted to `[A-Za-z0-9-_.]` and ≤ 64 chars; otherwise it's replaced. |
| Error leakage | 500 responses carry a generic message; details only go to the logs. |
| Secrets | The Postgres password is a dev default, overridable via `.env` (git-ignored). Not production-grade. The optional pgAdmin runs in desktop mode with no login: local development only. |
| Containers | Run as the non-root `app` user on alpine images. By default only the gateway port is published. The optional `docker-compose.tools.yml` also publishes PostgreSQL and pgAdmin, bound to `127.0.0.1` only, so they're reachable from this machine but not from the network. |
| AuthN/AuthZ | **Not implemented** (out of scope for the brief). Any caller can cancel any order. This is the first thing to add. |

A dedicated `/security-review` run wasn't done: it reviews a pending branch diff, and this work was committed straight to `main`. The table above is a manual pass instead.

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| Targeted review of `src/` | 10 findings (table above) | **9 Accept** (F3, F5 modified) / **1 Reject** (F10) | Each one reproduced live or confirmed in code; regression tests for F1, F2, F3, F6, F8, F9; full suite 136/136; smoke 24/24 |
| F5 fix approach | Move expected exceptions into an MVC exception filter or result types | **Modify**: a log filter on exception type instead. It's smaller, keeps one error-mapping place, and doesn't touch the controllers. | DB-down 500 logged with stack trace; 4xx not logged as errors |
| F3 fix approach | Validate the computed total | **Modify**: cap the unit price, which mathematically bounds every total | `Worst_case_valid_order_total_fits_numeric_18_2` |
