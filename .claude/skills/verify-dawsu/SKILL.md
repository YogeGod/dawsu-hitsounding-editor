---
name: verify-dawsu
description: Run dawsu!'s full check suite (build, the three test projects with the map corpus, UiLayoutCheck, WorkflowSafety) and report real pass/fail counts. Use before a commit, a PR or a release, or when asked "does everything still pass?".
---

# Verify dawsu!

Run these from the repo root, in order. Run long ones in the background. Put tool output in a scratch folder, not in the repo.

## 0. Corpus first
The corpus tests (round trip, compile, decompile, green-line clean-up over thousands of real maps) live inside the Format and Lanes test projects. They **skip silently** when `corpus/` is missing. `corpus/` is git-ignored, so every new worktree lacks it. In a worktree, **copy** the main checkout's corpus. Never link it with a junction or symlink:

```powershell
robocopy E:\GitHub\dawsu-hitsounding-editor\corpus corpus /E /NFL /NDL /NJH /NJS
```

Why: on 2026-11-13 the main checkout's corpus (4,062 files) was emptied while it was linked into a worktree with a junction during a check run. The cause wasn't found, but a copy means nothing in a worktree can reach the main corpus. Before removing a worktree, delete its `corpus/` copy normally.

If no copy exists anywhere, `tools/build-corpus.ps1` builds one from the local lazer library. Report any skipped tests by name; never count them as passes.

## 1. Checks
| # | Check | Command |
|---|---|---|
| 1 | Build | `dotnet build OsuHitsoundEditor.slnx -v q` |
| 2 | Fake VST host (App tests need it) | `dotnet build tools/FakeVstHost -v q` |
| 3 | Format tests | `dotnet test tests/OsuHitsound.Format.Tests --no-build` |
| 4 | Lane tests | `dotnet test tests/OsuHitsound.Lanes.Tests --no-build` |
| 5 | App tests | `dotnet test tests/OsuHitsound.App.Tests --no-build` |
| 6 | Layout | `dotnet run --project tools/UiLayoutCheck -v q -- <scratch>/layout` |
| 7 | Workflow safety | `dotnet run --project tools/WorkflowSafety -v q -- <scratch>/workflow` |

Steps 6 and 7 build their own `Snapshot.exe`, so don't pass `--no-build` to them. They matter only when `src/OsuHitsound.App` changed; otherwise say they were skipped.

## Baseline
V3.1 (`2018657`, 2026-11-13) with corpus (4,062 files): Format 108, Lanes 555, App 432 (1,095 tests, 0 failed, 0 skipped); UiLayoutCheck 699; WorkflowSafety 155. Fewer tests than this without a matching code change means something was skipped or removed. Say so.

## Report
One table: check, result (pass / fail / skipped), counts, and for failures the first failing test name and message. Then one line: is it safe to commit?

If something fails, also run the same check on `origin/main`, in a separate worktree or on a stash, so the report says whether the failure is new or already there. Never call a failure "pre-existing" without that run.
