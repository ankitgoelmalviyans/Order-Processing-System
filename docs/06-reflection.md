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

## 4. The AI suggestion I agreed with most, and what I'd do differently

**Agreed with most:** having the background worker call an idempotent API endpoint instead of touching the database, combined with a concurrency token. It solves the hardest edge case in the brief (a customer cancelling while the job runs) with one atomic `UPDATE`, and a deterministic test proves it. Removing the token made exactly that test fail.

**What I'd do differently:**
- **Update the SDLC files while building each feature**, as the slide asks, instead of writing them up at the end. The evidence is accurate, but it was compiled afterwards.
- **One commit per feature.** Create, get, list, update and cancel landed in one commit because they share one service class; smaller steps would make the history easier to review.
- **Set up repository hygiene on day one.** Working from both Windows (VS Code) and WSL caused line-ending and file-permission noise that a `.gitattributes` file and one git setting prevent from the start.

## 5. Where AI was wrong, and how it was caught

| AI mistake | Caught by | Lesson |
|---|---|---|
| Non-nullable enum in the PATCH body (`{}` → silently "PENDING") | Reading the code | Inspect model-binding defaults, not just logic |
| Greedy regex in its own smoke script; one check passed for the wrong reason | Running against the real stack | Test code needs review too; a passing check isn't proof |
| Too-broad log suppression as a "fix" | AI review of its own earlier change | Quick fixes to cross-cutting config deserve a second look |
| Item order assumed stable; `Location` behind a proxy; numeric overflow; `Accept` header edge case | AI review + reproduction | Green tests only cover what you thought of; run the real deployment shape |
| A verification grep that could never match (`' ERR '`) | Result was "too good" | Be suspicious of "0 problems"; check that the check works |
| Assumed `LogFileName={assembly}.trx` would expand in CI | Local dry run of the CI step before pushing | Don't trust a config option until you've seen its output |
| A CI reporter that "succeeded" but published nothing | Checking GitHub's API for the result instead of trusting the green step | A green step only means the step didn't fail; verify the output exists |
| Committed without checking the branch after VS Code switched it | Reading the `git log` output after the commit | Check the branch before every commit, especially when two tools share a repo |

## 6. Rework

1. Logging of handled exceptions: **3 iterations** (noisy → silenced → targeted filter).
2. Item ordering: added a `line_number` column with a **second migration**, rather than rewriting the first, as you would after a release.
3. Smoke-test parsing rewritten once, and its assertions strengthened.
4. Git history reorganised before publishing: the approved plan became the first commit, so the history reads plan → build → review → docs. Then repository hygiene for a Windows + WSL setup: `.gitattributes` and `core.filemode`.
5. CI test reporting: **3 iterations** (`.trx` artifact only → third-party reporter, which published nothing visible → own summary script).

## 7. What I'd do next

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

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| Writing this reflection | Draft it from the decisions, git history, stage logs and my own test results | **Modify**: AI draft, reviewed and adjusted by me before submission | Cross-checked against `git log`, docs 01–05 and my own test run |
