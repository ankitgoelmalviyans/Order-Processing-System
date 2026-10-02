# 01 · Understand and plan

**Stage goal:** clarify requirements, edge cases and acceptance criteria, then approve a testable feature spec.
**AI tool:** Claude Code (Claude Opus 5.5) in VS Code, plan mode (read-only until the plan was approved).
**Original plan:** [00-approved-plan.md](00-approved-plan.md), approved at 08:58 UTC before any code (root commit `bc542e3`). This file expands on it.

## 1. Requirements as given

1. Create an order with multiple items.
2. Retrieve order details by order ID.
3. Statuses PENDING, PROCESSING, SHIPPED, DELIVERED. A background job promotes PENDING → PROCESSING every 5 minutes.
4. List all orders, optionally filtered by status.
5. Cancel an order, but only while it is PENDING.

Non-functional requirements (from the brief and the follow-up conversation): any language; must be easy to run and demo (→ Docker Compose); microservices welcome; reviewers will look at design patterns; and AI usage must be explained.

## 2. Ambiguities and how they were resolved

| # | Question | Resolution | Decided by |
|---|---|---|---|
| A1 | The status list has no "cancelled" value. How is a cancelled order represented? | Add a **CANCELLED** terminal status. Deleting the order would lose the audit trail, and a flag would duplicate the status. | AI proposed, accepted |
| A2 | "Background job updates PENDING orders every 5 minutes": does an order placed 1 second before the tick get promoted? That would leave almost no time to cancel. | Literal reading by default: every PENDING order is promoted on each tick. A `MinPendingAgeSeconds` setting (default 0) can guarantee a cancellation window without code changes. | AI raised it; literal default kept |
| A3 | Can staff move an order PENDING → PROCESSING by hand, or only the job? | Allowed through `PATCH /status`. The state machine is the only gatekeeper, and the job is just one caller of the same rule. | AI proposed, accepted |
| A4 | Can a PROCESSING order be cancelled? | **No**. The brief says "only if it's still PENDING". `409 Conflict`. | Requirement |
| A5 | Money: currency, rounding, who calculates the total? | Single currency; `numeric(18,2)`; prices with more than 2 decimals rejected (not silently rounded); the total is always calculated on the server. | AI proposed, accepted |
| A6 | Duplicate product lines in one order? | Rejected with 400 ("combine the quantities"), so totals and line semantics stay unambiguous. | AI proposed, accepted |
| A7 | "List all orders": unbounded? | Paged (`page`, `pageSize` ≤ 100), newest first. An optional `customerId` filter added so customers can track their own orders. | AI proposed, accepted |
| A8 | What if a cancel and the job hit the same order at the same moment? | Exactly one must win, never a lost update. → Optimistic concurrency (design in 02). | AI raised, accepted |

## 3. Scope decisions made by the developer

These came out of the clarifying questions the AI asked before writing any code:

| Question asked by AI | Options offered | **Developer's choice** |
|---|---|---|
| Language / stack | .NET 8 · Java 21 / Spring Boot | **.NET 8 / C#** |
| How far to split microservices | 2 services + gateway · full event-driven (RabbitMQ, outbox) · modular monolith | **2 services + gateway** |
| UI | Swagger only · add simple web UI | **Swagger only** |
| Evidence format | n/a | **Follow the course slides:** 01-planning … 06-reflection, each with an AI interaction log |

## 4. Acceptance criteria (the testable spec)

| Feature | Acceptance criteria | Verified by |
|---|---|---|
| Create | Valid request → `201`, `Location` header, status PENDING, server-calculated total. Invalid → `400` with field errors (no items, qty ≤ 0, price ≤ 0 or > 2 dp, duplicate product, missing customer). | `OrdersApiTests.Create_*`, `ValidatorTests`, smoke test |
| Get | Existing → `200` with items in submitted order; unknown → `404` problem+json | `Get_*`, `Items_are_returned_in_the_order_they_were_placed` |
| List | Filter by status and/or customer; paged with no overlap; invalid status or paging → `400` | `List_*` |
| Update status | Only PENDING→PROCESSING→SHIPPED→DELIVERED; anything else `409`; missing status `400` | `OrderStatusTransitionsTests` (all 25 pairs), `Update_status_*` |
| Cancel | PENDING → `200` CANCELLED; any other status → `409`; unknown → `404` | `Cancel_*` |
| Background job | Every 5 min (configurable); promotes only PENDING; idempotent; a failure doesn't stop later runs; never overwrites a concurrent cancel | `PendingOrderPromotionWorkerTests`, `Promote_pending_*`, `ConcurrencyTests` |
| Run & demo | `docker compose up --build` starts everything; Swagger at `:8080/swagger` | `smoke-test.sh` |

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| Pasted the assignment and the two SDLC slides; asked for a "complete working solution, maybe microservices, docker compose" | Asked 3 scoping questions (stack, service split, UI) with a recommended option for each, before planning | **Accept**: developer picked .NET 8, 2 services + gateway, Swagger only | Choices recorded above and reflected in the approved plan |
| Requirement "cancel only if PENDING" vs the status list | Add CANCELLED as a 5th status | **Accept** | State-machine tests cover CANCELLED as terminal |
| "Background job every 5 minutes" | Flagged the "promoted 1 s after creation" edge case; proposed a min-age setting | **Modify**: kept the literal behaviour as the default (0 s), setting is opt-in | `OrderServiceTests.Promote_respects_minimum_pending_age` |
| Concurrency between the cancel endpoint and the job | Identified the lost-update race as a risk at planning time | **Accept**: became a design requirement (02) | `ConcurrencyTests.Cancel_loses_if_background_job_promoted_the_order_after_it_was_read` |
| AI's first plan said "commit after each feature" | n/a | **Modified in practice**: create/get/list/update/cancel landed in one commit because they share one service class (see 03) | `git log` |
