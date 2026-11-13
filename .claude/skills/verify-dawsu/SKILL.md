---
name: verify-dawsu
description: Run dawsu!'s full check suite (build, the three test projects, corpus round trip, UiLayoutCheck, WorkflowSafety) and report real pass/fail counts. Use before a commit, a PR or a release, or when asked "does everything still pass?".
---

# Verify dawsu!

Run these from the repo root, in order. Run long ones in the background. Put tool output in a scratch folder, not in the repo.

| # | Check | Command |
|---|---|---|
| 1 | Build | `dotnet build OsuHitsoundEditor.slnx -v q` |
| 2 | Fake VST host (App tests need it) | `dotnet build tools/FakeVstHost -v q` |
| 3 | Format tests | `dotnet test tests/OsuHitsound.Format.Tests` |
| 4 | Lane tests | `dotnet test tests/OsuHitsound.Lanes.Tests` |
| 5 | App tests | `dotnet test tests/OsuHitsound.App.Tests` |
| 6 | Corpus round trip | `dotnet run --project tools/RoundTrip -- corpus` (skip and say so if `corpus/` is empty) |
| 7 | Layout | `dotnet run --project tools/UiLayoutCheck -- <scratch>/layout` |
| 8 | Workflow safety | `dotnet run --project tools/WorkflowSafety -- <scratch>/workflow` |

Steps 7 and 8 matter only when `src/OsuHitsound.App` changed; otherwise say they were skipped.

## Report
One table: check, result (pass / fail / skipped), counts, and for failures the first failing test name and message. Then one line: is it safe to commit?

If something fails, also run the same check on `origin/main`, in a separate worktree or on a stash, so the report says whether the failure is new or already there. Never call a failure "pre-existing" without that run.
