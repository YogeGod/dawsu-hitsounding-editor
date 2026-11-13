# CLAUDE.md

## Agent skills

### Issue tracker

Issues are tracked in this repo's GitHub Issues, using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`. `docs/README.md` says which docs are current and which are historical; check it before trusting a doc.

## Learned rules

- No multi-agent reviews unless the user asks. In `/ship-feature check`, work through `.claude/agents/format-guardian.md` and `.claude/agents/scope-reviewer.md` yourself as checklists.
- `.claude/hooks/guard.ps1` (a PreToolUse hook in `.claude/settings.json`) blocks writes into `F:\osu!\Songs`, editing, deleting or linking the main checkout's `corpus/`, file changes by the read-only reviewer agents, and file changes by `dawsu-ui-tweak` outside its folders (Edit, Write and shell writes; its shell may write only in the temp folder). If it blocks you, change the approach; don't work around it. After changing the guard, run `pwsh -NoProfile -File tools/guard-test.ps1` and add a case for the change. It checks command text, so it is a safety net, not permission for anything it misses. Codex doesn't run this hook: follow these limits by hand.
