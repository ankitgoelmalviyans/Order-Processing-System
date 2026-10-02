# 06 · Reflect

**Stage goal:** explain the human decisions, the rework and the lessons.

## 1. Decisions I made, and why

| Decision | Why |
|---|---|
| **.NET 8 / C#** | ASP.NET Core, EF Core, hosted background services and xUnit cover every requirement with first-party tooling, and the language makes the design patterns (aggregate, repository, options, DI) explicit, which suits the design-pattern round. .NET 8 is a long-term-support release. |
| **Two services + an API gateway**, not a full event-driven system | It shows real service boundaries (a public gateway, a service that owns its data, an independently scheduled worker) without a message broker, an outbox and eventual consistency. Those would have added more to explain than to evaluate for this brief. |
| **The worker calls the API instead of writing to the database** | One writer per database keeps the business rules and the concurrency handling in one place. A shared database between services is a known anti-pattern. |
| **Swagger only, no UI** | The assignment assesses the backend. Swagger, a smoke-test script and CI demonstrate every endpoint. |
| **Follow the course SDLC slides for the evidence** | The reviewers asked *how* AI was used. A log per stage with Accept / Modify / Reject and how each was verified answers that directly. |
| **Verify every AI review finding before acting on it** | AI reviews can sound right and still be wrong. Reproducing each finding showed which were real (6 failed live), and each reproduction became a regression test. |

## 2. How I worked with the AI

1. **Plan before code.** The AI started in a read-only planning mode, asked me to choose the stack, the service split and the UI, and wrote a plan that I approved before any code existed ([00-approved-plan.md](00-approved-plan.md)).
2. **Build in layers, test each one.** Domain first (no dependencies), then use cases, database, API, worker, gateway and Docker. Each step was committed only with its tests passing.
3. **Distrust green results.** All tests passing on the first run was treated as a warning sign: the code was deliberately broken to prove the tests catch it, and the real Docker stack was run end to end.
4. **Review, then verify.** An AI code review produced 10 findings. Each was reproduced or confirmed before being fixed or rejected.
5. **Check it myself.** I ran the full test suite (136 passed) and the smoke test (23/23) on my machine, and re-checked review findings F2 and F9 against the running system.

## 3. What surprised me

- **A fully green test suite still hid six real bugs.** All 126 tests passed when the AI review found, among others, the internal hostname leaking in the `Location` header, a 500 error for very large page numbers, and items coming back in random order. Tests only cover what you think of; running the real deployment and an independent review found the rest.
- **The AI's own test code had bugs.** The smoke-test script read the wrong id from the JSON, and one check passed for the wrong reason. Test code needs the same scrutiny as product code.
- **"Success" doesn't mean it worked.** A CI reporting step finished green but published nothing, and a placeholder in a config value was written literally instead of being expanded. Both were caught only by checking the actual output.

## 4. What I'd do next

| Next improvement | Why |
|---|---|
| Authentication + authorization (customer can only see and cancel own orders; staff role for status changes) | Biggest functional gap |
| `Idempotency-Key` header on `POST /api/orders` | A client retry after a timeout currently creates a duplicate order |
| Publish domain events (`OrderCreated`, `OrderCancelled`, `OrderStatusChanged`) via an outbox + broker | Lets inventory, payment and notification services react without coupling |
| OpenTelemetry traces and metrics (W3C trace context instead of a custom correlation header) | End-to-end tracing across gateway → api → db |
| Shared secret or mTLS for `/internal/**` | Defence in depth beyond network isolation |
| Rate limiting at the gateway | Protect the API from abusive clients |
| Batch the promote `UPDATE` (e.g. 1,000 rows per statement) | Bounded transaction size for very large backlogs |
| Keyset pagination | `OFFSET` gets slow on large tables |
