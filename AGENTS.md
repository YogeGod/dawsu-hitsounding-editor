<!-- GENERATED from CLAUDE.md and .claude/ in this folder by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

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

## Area rules (in nested AGENTS.md files)

Claude's path-scoped rules from `.claude/rules/` are compiled into an AGENTS.md inside each folder they cover. **Before you edit a file, read every AGENTS.md between the repo root and that file's folder.** Codex loads them on its own only for the folder it started in.

| Folder | Rules |
|---|---|
| `native/AGENTS.md` | Audio and VST3 host rules |
| `src/OsuHitsound.App/AGENTS.md` | UI rules (Avalonia app); Audio and VST3 host rules |
| `src/OsuHitsound.Audio/AGENTS.md` | Audio and VST3 host rules |
| `src/OsuHitsound.Format/AGENTS.md` | Format and lane rules (osu! file reading, writing, compile and decompile) |
| `src/OsuHitsound.Lanes/AGENTS.md` | Format and lane rules (osu! file reading, writing, compile and decompile) |
| `tests/OsuHitsound.Format.Tests/AGENTS.md` | Format and lane rules (osu! file reading, writing, compile and decompile) |
| `tests/OsuHitsound.Lanes.Tests/AGENTS.md` | Format and lane rules (osu! file reading, writing, compile and decompile) |
| `tools/FakeVstHost/AGENTS.md` | Audio and VST3 host rules |
| `tools/RoundTrip/AGENTS.md` | Format and lane rules (osu! file reading, writing, compile and decompile) |
| `tools/Snapshot/AGENTS.md` | UI rules (Avalonia app) |
| `tools/UiLayoutCheck/AGENTS.md` | UI rules (Avalonia app) |
| `tools/Vst3MapRender/AGENTS.md` | Audio and VST3 host rules |
| `tools/Vst3Smoke/AGENTS.md` | Audio and VST3 host rules |

## Claude agents and skills in this project

Claude runs these as subagents or skills. You can't, so when a rule or the user names one, open its file and follow it yourself as a checklist. Read-only agents (reviewers) stay read-only when you play them.

- Agent `dawsu-ui-tweak` (`.claude/agents/dawsu-ui-tweak.md`): Makes one small dawsu! UI change at a time while the user tries the app (layout, colours, sizes, labels, shortcuts, mouse behaviour), then rebuilds, checks a screenshot and relaunches. Not for audio, VST, osu! file format or lane compile/decompile work.
- Agent `format-guardian` (`.claude/agents/format-guardian.md`): Read-only reviewer for any change that touches how dawsu! reads, writes, compiles or decompiles .osu files. Checks the diff against ADR 0002 (byte-identical unedited lines) and the osu! hitsound rules, and runs the format and lane tests. Use before committing such a change.
- Agent `scope-reviewer` (`.claude/agents/scope-reviewer.md`): Read-only semantic review of a diff or a document change. Checks that it does what the plan or issue asked and no more, that it doesn't contradict CONTEXT.md, the ADRs or docs/design-system.md, and that docs and CHANGELOG still describe what the code does. Complements /code-review, which looks for bugs.
- Skill `release-dawsu` (`.claude/skills/release-dawsu/SKILL.md`): Build and publish a dawsu! release (version bump, release commit, self-contained publish, Velopack pack, checksums, tag and GitHub release) the way V3.1.1 and V3.1.2 were shipped. Run only when the user types /release-dawsu.
- Skill `ship-feature` (`.claude/skills/ship-feature/SKILL.md`): The dawsu! feature workflow in three phases (plan, build, check), each in a fresh session that hands over through a plan file, with the user's approval gates. Run only when the user types /ship-feature.
- Skill `verify-dawsu` (`.claude/skills/verify-dawsu/SKILL.md`): Run dawsu!'s full check suite (build, the three test projects with the map corpus, UiLayoutCheck, WorkflowSafety) and report real pass/fail counts. Use before a commit, a PR or a release, or when asked "does everything still pass?".
