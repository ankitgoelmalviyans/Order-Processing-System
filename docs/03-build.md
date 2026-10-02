# 03 · Build

**Stage goal:** implement in small steps, inspect AI changes, debug.

## 1. Build sequence (from `git log`)

| Commit | Step | Built and checked before committing |
|---|---|---|
| `996b13a` | Scaffold + domain | Central package management, `Order` aggregate, state machine. **67 unit tests green.** |
| `f2130d7` | Orders service | Application (use cases, validators), Infrastructure (EF Core, migration), API (controllers, ProblemDetails, Swagger, health). **37 integration tests green on real Postgres.** |
| `0bcf052` | Status worker | `BackgroundService` + typed client + resilience; application-layer unit tests. Mutation checks run (04). |
| `e8e0d95` | Gateway + Docker | YARP, Dockerfiles, compose, smoke test. **Ran the full stack**; fixed 2 issues found there (below). |
| `d118b51` | Review fixes | 9 verified review findings fixed, with regression tests (05). |

Before the build: `bc542e3`, the approved plan. After it: documentation and repository improvements (`4de1c03` … latest), listed file by file in [ai-sdlc-process.md](ai-sdlc-process.md) §3.

The plan called for one commit per feature. In practice create/get/list/update/cancel went into one commit (`f2130d7`): they share `OrderService`, the repository and the exception handler, and splitting them would have meant committing half-wired code. Each feature still has its own tests.

## 2. Environment issues hit while building

| Problem | Cause | Fix |
|---|---|---|
| Commands in WSL hung forever | `bash -lc` (login shell) stalled on something in the profile | Use non-login `bash -c`/`sh -c` |
| `dotnet` not found | Windows had no .NET SDK; WSL had none either | Installed the .NET 8 SDK user-locally (`~/.dotnet`, via `dotnet-install.sh`, no sudo) |
| Nested quotes garbled (`Build: command not found`) | PowerShell → `wsl` → bash quoting | Put commands in a script file and ran `wsl bash script.sh` |
| `./scripts/smoke-test.sh: Permission denied` | Editing the file over the `\\wsl.localhost` path dropped the exec bit | `chmod +x` and `git update-index --chmod=+x` so the bit is in the repo |

**Hit after the build, while publishing and using the repo from both Windows (VS Code) and WSL:**

| Problem | Cause | Fix |
|---|---|---|
| Six docs suddenly "modified", with no visible change | The branch was switched in VS Code, whose Windows Git checks files out with CRLF line endings | Restored the LF versions; added `.gitattributes` to pin LF for every text file. This also protects `smoke-test.sh` for reviewers who clone on Windows. |
| A commit landed on the backup branch instead of `main` | After that branch switch, the AI committed without first checking which branch was active | Noticed in the `git log` output; moved the commit to `main` with `cherry-pick` and restored the backup branch. Branch is now checked before every commit. |
| VS Code showed `smoke-test.sh` as modified (0 lines changed) | Windows Git can't see Linux's executable bit over the `\\wsl.localhost` path | `core.filemode=false` for this clone. Committing it would have removed the exec bit and broken the script and CI. |
| CI test results: only one `.trx` file instead of three | The AI assumed `LogFileName={assembly}.trx` would expand; this SDK writes the name literally, so the projects overwrote each other | Caught by a local dry run before pushing; CI now runs each test project with its own file name |
| CI test report never appeared | `dorny/test-reporter@v1` succeeded on GitHub but created no visible check | Replaced with `scripts/test-summary.py` (tested locally, including a failing case), which writes the report to the run's Summary page |

## 3. Issues in AI-generated code, and corrections

**Caught by inspecting the code before running anything:**

| # | AI wrote | Problem | Correction |
|---|---|---|---|
| B1 | `record UpdateOrderStatusRequest(OrderStatus Status)` | A body of `{}` binds `Status` to the enum default **`Pending`**. That's not a 400, and is semantically "move to PENDING". | Made it `OrderStatus?` + `NotNull()` rule. Test: `Update_status_with_missing_or_invalid_status_returns_400("{}")` |
| B2 | `services.AddSingleton(TimeProvider.System)` in the Application DI | Would override a fake clock registered by tests or other hosts | `TryAddSingleton` |
| B3 | `RuleForEach(r => r.Items).SetValidator(...)` | An `items: [null]` element would skip validation and NRE in the domain | `.NotNull()` before `SetValidator`. Test: `items:[null]` → 400 `Items[0]` |
| B4 | YARP destination named `orders-api-1` | The hyphen ends up in the env-var override key `…Destinations__orders-api-1__Address` | Renamed to `primary` |

**Found by running the real Docker stack** (all tests were already green at this point):

| # | Symptom | Root cause | Correction |
|---|---|---|---|
| B5 | Smoke test: create → 201, but then **get by id → 404** for the same order | Bug in the **AI-written test script**, not the API: `sed 's/.*"id":…/'` is greedy, so it picked the **last** `"id"` in the JSON (an item's id). One check ("list contains the order") **passed for the wrong reason**, comparing the wrong id with itself. | `grep -o … \| head -1` for the first match; the list assertion now searches for the exact order id, and a negative check was added |
| B6 | orders-api logged **every expected 404/409 at ERROR with a full stack trace** | .NET 8 `ExceptionHandlerMiddleware` logs *every* exception at Error **before** calling `IExceptionHandler` | First fix: raised that category to `Fatal`. **Later judged too broad in review (F5)**: it also hid real failures. Replaced with a filter that drops only *expected* exception types. |

## 4. Key implementation notes for the walkthrough

- `Order.Create` repeats the important invariants even though FluentValidation runs first (defence in depth): the domain must be valid no matter which use case calls it.
- `Order.ChangeStatus` is the **only** way to change status; `Cancel()` is just `ChangeStatus(Cancelled)`. The transition table is in `OrderStatusTransitions`.
- `OrderRepository.PromotePendingAsync` uses EF Core 7+ `ExecuteUpdateAsync`: one SQL statement, no entities loaded.
- `CorrelationIdMiddleware` rejects header values that aren't simple tokens (prevents log forging) and writes the id back on the request, so YARP forwards it.

## AI interaction log

| Context | AI suggestion | Decision | Verification |
|---|---|---|---|
| Scaffolding | Central package management (`Directory.Packages.props`), `TreatWarningsAsErrors`, file-scoped namespaces | **Accept** | `dotnet build`: 0 warnings |
| Status update contract | Non-nullable enum in the request record | **Modify** (B1): nullable + `NotNull` | Integration test `{}` → 400 |
| DI registration of the clock | `AddSingleton(TimeProvider.System)` | **Modify** (B2): `TryAddSingleton` | Unit tests use `FakeTimeProvider` |
| Noisy error logs found in the running stack | Silence the `ExceptionHandlerMiddleware` category at `Fatal` | **Accept → later Reject**: replaced after review F5 with a targeted filter | Live check: 4xx produce 0 ERR lines; DB-down request produces ERR with stack trace (05) |
| Smoke-test JSON parsing | Greedy `sed` extraction | **Reject**: replaced (B5) | Smoke test 24/24 with the real worker wait |
| Worker scheduling | `PeriodicTimer(interval, TimeProvider)` + per-run scope | **Accept** | `Calls_the_api_once_every_five_minutes` (fake clock) and the live worker log |
| CI test reporting | `LogFileName={assembly}.trx` + `dorny/test-reporter` | **Reject** both: placeholder not supported; reporter published nothing visible → per-project loop + own summary script | Local dry run: 3 `.trx` files, report renders, failure path exits 1 with the error message |
