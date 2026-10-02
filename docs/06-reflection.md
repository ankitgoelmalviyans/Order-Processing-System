# 06 · Reflect

**Stage goal:** explain the human decisions, the rework and the lessons.

> ✍️ **Note to self before submitting:** this file was drafted with AI from the build history. Rewrite it in your own words, and add or adjust anything you decided differently or would defend differently in the walkthrough. Delete this note when done.

## 1. Decisions I made (and why)

The first three decisions were mine, picked from the options the AI offered. The rationale shown is the one attached to the option I picked. _Add your own reasons._

| Decision | Why |
|---|---|
| .NET 8 / C# | _(your reason)_ · Option rationale: ASP.NET Core, EF Core, hosted services and xUnit fit a design-pattern discussion. |
| 2 services + a gateway, not full event-driven | Shows real service boundaries (gateway, data-owning service, independent scheduler) without RabbitMQ, an outbox and eventual consistency, which would add more to explain than to evaluate. |
| Swagger only, no UI | The assignment assesses the backend; Swagger plus a smoke script demonstrate everything. |
| Follow the course SDLC slides for evidence | The reviewers asked *how* AI was used. A per-stage log with Accept/Modify/Reject and verification answers that directly. |
| Accept/modify/reject each review finding only after reproducing it | AI reviews produce plausible-sounding findings. Reproducing them showed 6 were real failures, and the reproductions became regression tests. |

## 2. Where AI helped most

- **Asking before building.** The clarifying questions at the start fixed the scope in one exchange.
- **Spotting edge cases in the brief:** the missing CANCELLED state, job timing, the money rules, and the cancel-vs-job race, all before writing code.
- **Volume work:** project scaffolding, Dockerfiles, compose, 136 tests, the smoke script and these docs.
- **Review:** it found 6 real defects that a fully green test suite missed.

## 3. Where AI was wrong, and how it was caught

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

## 4. Rework

1. Logging of handled exceptions: **3 iterations** (noisy → silenced → targeted filter).
2. Item ordering: added a `line_number` column with a **second migration**, rather than rewriting the first, as you would after a release.
3. Smoke-test parsing rewritten once, and its assertions strengthened.
4. Git history reorganised before publishing: the approved plan became the first commit, so the history reads plan → build → review → docs. Then repository hygiene for a Windows + WSL setup: `.gitattributes` and `core.filemode`.
5. CI test reporting: **3 iterations** (`.trx` artifact only → third-party reporter, which published nothing visible → own summary script).

## 5. What I'd do next

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
| Drafting this reflection | Reconstruct decisions and rework from the git history and the stage logs | **Modify**: drafted by AI, to be rewritten in my own words (note at top) | Cross-checked against `git log` and docs 01–05 |
