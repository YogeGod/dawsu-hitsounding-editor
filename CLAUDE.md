# CLAUDE.md

## Workflow checklist

Copied from `E:\Claude\CLAUDE.md`, which sessions in this checkout don't load. Change both when a rule changes.

### Keeping track
- Keep the current task list in `TASKS.md`: one line per open item, ticked when done. Read it after /clear.
- Subagents that only search or read files run on Sonnet or Haiku (`model: "sonnet"` or `"haiku"`). Use Opus only for hard logic.

### Before starting a feature
- Stress-test the plan with the `grilling` skill. In this repo, `/mattpocock-skills:grill-with-docs` runs the same interview and records the results in `CONTEXT.md` and `docs/adr/`. If neither is available, ask me the hard questions yourself until the plan has no open branches.
- For unfamiliar tech (audio APIs, chart formats), research first and summarize before coding.
- Show a short plan and wait for my OK.

### Before a PR or commit
- Diffs under ~300 lines: do the /simplify checks inline, without subagents. Larger diffs: run /simplify.
- Run /code-review at medium, inline. Use multi-agent review workflows only when I ask for one.
- Run /security-review.
- Run `dotnet test OsuHitsoundEditor.slnx` (decompiler/compiler, editing and the map corpus) and confirm it passes. `IsolatedVst3Tests.The_mixer_plays_several_instrument_parts_together` sometimes fails when all test projects run at once. If it does, rerun it alone before treating the failure as real.

### After PR comments
- Go through each comment, fix it or explain why not, and summarize what changed in 2-3 lines.

### Occasionally (every few days or after big features)
- Run an architecture pass with the `codebase-design` skill and /security-review.
- Re-read this CLAUDE.md and tell me if any rule is outdated.

## Agent skills

### Issue tracker

Issues are tracked in this repo's GitHub Issues, using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.
