---
name: ship-feature
description: The dawsu! feature workflow from idea to ready-to-commit, with the user's approval gates. Run only when the user types /ship-feature.
disable-model-invocation: true
---

# Ship a dawsu! feature

Arguments: what to build, or an issue number or a TASKS.md line.

Do the phases in order. Each **Gate** means stop and wait for the user's reply in chat.

## 1. Load context (only what this task needs)
Read `TASKS.md`, `CONTEXT.md`, `.handoff/HANDOFF.md` if present, and the doc for the area (`docs/design-system.md` for UI, `docs/osu-hitsound-rules.md` and the ADRs for format work). Check `git status` and the branch. Don't read the whole `docs/` folder.

## 2. Plan
- For unfamiliar tech, research first and summarize it in 5 lines or fewer.
- Stress-test the plan with the `grilling` skill until no open questions are left.
- Write a short plan: the files to change, what "done" looks like, and which checks will prove it.
- **Gate:** show the plan and wait for an OK.

## 3. Build
- Work on a branch or in a worktree, not on `main`.
- Hand small UI tweaks to the `dawsu-ui-tweak` agent; keep model and format logic in the main session.
- Add a TASKS.md line for the work and keep `.handoff/HANDOFF.md` current.

## 4. Check
- Run the `verify-dawsu` skill.
- If Format or Lanes changed, work through the checklist in `.claude/agents/format-guardian.md` yourself.
- Work through `.claude/agents/scope-reviewer.md` yourself, with the approved plan as the goal.
- Run those two as subagents only if the user asks for a multi-agent review (CLAUDE.md: no multi-agent reviews unless asked).
- Diff under about 300 lines: do the /simplify checks inline; otherwise run /simplify. Then /code-review at medium, inline, and /security-review.
- Fix what they find, then run `verify-dawsu` again.

## 5. Hand back
- Tick the TASKS.md line, and add an Unreleased entry to CHANGELOG.md when users would notice the change.
- **Gate:** summarize the change, the real check results and anything not verified (sound, mouse feel, native dialogs). Commit, push or open a PR only when the user asks.
- After the user's OK, log it in the vault: a line in today's daily note and, for lasting decisions, `Decisions.md` (see the vault's CLAUDE.md).
