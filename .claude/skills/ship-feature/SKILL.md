---
name: ship-feature
description: The dawsu! feature workflow in three phases (plan, build, check), each in a fresh session that hands over through a plan file, with the user's approval gates. Run only when the user types /ship-feature.
disable-model-invocation: true
---

# Ship a dawsu! feature

Usage:
- `/ship-feature plan <what to build | issue # | TASKS.md line>`
- `/ship-feature build`
- `/ship-feature check`

Each phase runs in its **own fresh session**: a new Claude Code session in the feature's worktree, or `/clear`. A phase reads only the plan file and what the plan names, not the earlier conversation. That keeps each phase's context small and makes the check phase look at the work with fresh eyes. With no phase given, read `.handoff/PLAN.md` in the current folder: no file means `plan`; otherwise use the phase in its **Status** line.

**Gate** means: stop and wait for the user's reply in chat.

## The plan file
`.handoff/PLAN.md` in the feature's worktree (`.handoff/` is git-ignored). It is the only thing passed between phases, so write it for a reader who saw none of the conversation:

```markdown
# <feature>
Status: plan approved, ready for build | built, ready for check | checked, ready for the user
Branch: <branch> · Worktree: <path> · Base: <commit>

## Goal
## Plan (approved <date>)
- Files to change, and what each change is
- Done when: <observable result>
- Checks that prove it: <verify-dawsu rows, format-guardian if Format/Lanes, manual checks>
## Decisions (from the grilling, with the reason)
## Build log (one line per step, newest last)
## Check results
## Not verified
```

Also make `.handoff/HANDOFF.md` point at `PLAN.md`, so the Codex fallback finds it.

## Phase 1: plan
1. Read `TASKS.md`, `CONTEXT.md`, and the doc for the area (`docs/design-system.md` for UI; `docs/osu-hitsound-rules.md` and `docs/adr/` for format work). Check `git status`. Don't read the whole `docs/` folder.
2. For unfamiliar tech, research first and summarize it in 5 lines or fewer.
3. Stress-test the idea with the `grilling` skill until no open questions are left.
4. **Gate:** show a short plan (files, done-when, checks) and wait for an OK.
5. After the OK: create a worktree from `origin/main` (`git worktree add -b <type>/<slug> E:\GitHub\dawsu-wt-<slug> origin/main`), copy the corpus into it (`verify-dawsu` step 0), write `.handoff/PLAN.md` there with Status "plan approved, ready for build", and add a TASKS.md line.
6. End with exactly what to do next: "Start a new Claude Code session in `<worktree>` (or run `/clear`), then run `/ship-feature build`." Do no building in this session.

## Phase 2: build
1. Read `.handoff/PLAN.md` and only the files it names. If the plan is unclear or wrong, stop and say so; don't re-plan on your own.
2. Build it. Hand small UI tweaks to the `dawsu-ui-tweak` agent; keep model and format logic in this session. Add one Build log line per step.
3. Run the quick checks for the area (the rule file's "Before you say it works"). Leave the full checks for phase 3.
4. Set Status to "built, ready for check" and end with: "Start a new session in this folder (or `/clear`), then run `/ship-feature check`."

## Phase 3: check
Work from `.handoff/PLAN.md` and `git diff <Base>...HEAD` plus `git diff`, not from memory of the build.
1. Run the `verify-dawsu` skill.
2. If Format or Lanes changed, work through the checklist in `.claude/agents/format-guardian.md` yourself.
3. Work through `.claude/agents/scope-reviewer.md` yourself, with the plan's Goal and Plan as the goal.
4. Run those two as subagents only if the user asks for a multi-agent review (CLAUDE.md: no multi-agent reviews unless asked).
5. Diff under about 300 lines: do the /simplify checks inline; otherwise run /simplify. Then /code-review at medium, inline, and /security-review.
6. Fix small findings here and log them. If a finding needs a design change, write it into the plan and go back to phase 2 in a new session.
7. Run `verify-dawsu` again, fill in Check results and Not verified, and set Status to "checked, ready for the user".
8. Tick the TASKS.md line, and add an Unreleased entry to CHANGELOG.md when users would notice the change. If `CLAUDE.md` or anything in `.claude/` changed, regenerate Codex's copy with `pwsh -NoProfile -File ~/.claude/hooks/sync-codex-instructions.ps1 -Project .` and commit `AGENTS.md` with it.
9. **Gate:** summarize the change, the real check results and anything not verified (sound, mouse feel, native dialogs). Commit, push or open a PR only when the user asks.
10. After the user's OK, log it in the vault: a line in today's daily note, and lasting decisions in `Decisions.md` (see the vault's CLAUDE.md).
