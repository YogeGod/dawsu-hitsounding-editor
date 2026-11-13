<!-- GENERATED from CLAUDE.md and .claude/ in this folder by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

# CLAUDE.md

## Agent skills

### Issue tracker

Issues are tracked in this repo's GitHub Issues, using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Area rules (compiled from .claude/rules/)

Each rule applies only while you work on the paths listed under it. Claude loads them automatically by path; you have to check the paths yourself.

### UI rules (Avalonia app)

**Applies to:** `src/OsuHitsound.App/**/*.axaml`, `src/OsuHitsound.App/ArrangeView.cs`, `src/OsuHitsound.App/ArrangeModel*.cs`, `src/OsuHitsound.App/Paint.cs`, `src/OsuHitsound.App/ThemeResources.cs`, `src/OsuHitsound.App/Dialogs*.cs`, `src/OsuHitsound.App/MainWindow*.cs`, `src/OsuHitsound.App/KeysoundEditor*.cs`, `tools/UiLayoutCheck/**`, `tools/Snapshot/**`

- Colours, sizes, radii and the Slate scheme come from `docs/design-system.md` and `DesignSystem.axaml` / `ThemeResources.cs`. Don't hard-code a value the design system already names.
- Most dawsu! users are osu! mappers with no DAW experience. Controls need visible surfaces and text labels, and on/off states must be obvious. Don't make things subtle. Keep the V3.0 ribbon and menu icons and the diamond hit markers.
- Within each sample set, the hitnormal lane comes first; additions (whistle, finish, clap) sit under it.
- Drum map view must not add lanes. Resolve conflicts in the model (`ArrangeModel.DrumMaps.cs`), and turning drum map off must show the real lanes.
- The Hitsound and Keysound editors share the `.ribbon` styles. A style change in one shows in the other, so check both.

#### Before you say it works
1. `dotnet build src/OsuHitsound.App -v q`
2. `dotnet run --project tools/UiLayoutCheck -- <scratch dir>` and report the check count.
3. For visible changes: `dotnet run --project tools/Snapshot -- "<copy of a map.osu>" <scratch dir>`, then look at the PNG.
4. `dotnet test tests/OsuHitsound.App.Tests`. If VST-host tests fail, build `tools/FakeVstHost` first. A missing build has caused those failures before; it is not your change.
5. Mouse feel, sound and native file pickers can't be checked from a screenshot. Say so instead of claiming them.

### Audio and VST3 host rules

**Applies to:** `src/OsuHitsound.Audio/**`, `native/**`, `src/OsuHitsound.App/MainWindow.DrumVst.cs`, `src/OsuHitsound.App/MainWindow.DrumRender.cs`, `src/OsuHitsound.App/ArrangeModel.DrumRender.cs`, `tools/FakeVstHost/**`, `tools/Vst3Smoke/**`, `tools/Vst3MapRender/**`

- Never claim audio was heard, or that a real VST (Kontakt, GGD) loaded, unless a run in this session showed it. Tests with `FakeVstHost` prove the protocol, not the sound.
- Leave GGD Matt Garstka installed exactly as it is. Its stale VST3 repair was deferred by the user.
- Changes under `native/` mean the isolated host has to be rebuilt before release. Note that in TASKS.md.
- Renders write WAVs. They go into a copied map folder or a scratch folder, never into an original mapset.

### Format and lane rules (osu! file reading, writing, compile and decompile)

**Applies to:** `src/OsuHitsound.Format/**`, `src/OsuHitsound.Lanes/**`, `tests/OsuHitsound.Format.Tests/**`, `tests/OsuHitsound.Lanes.Tests/**`, `tools/RoundTrip/**`

Mappers don't trust hitsound tools with their maps, so this code has the strictest rules in the repo.

- **Unedited lines stay byte-identical** on every write path: comments, number spelling, line endings, BOM, unknown sections and keys, `[Events]`, `[Colours]`. Lazer quirks are kept, not "fixed". Read `docs/adr/0002-saves-leave-unedited-lines-byte-identical.md` before changing any writer.
- How osu! actually resolves hitsounds (sample set, index, additions, green lines) is in `docs/osu-hitsound-rules.md`. Check it before you trust intuition.
- Use the words from `CONTEXT.md` (Lane, Folder, Lane layout). A Folder is a sample-set group in the UI, never a directory on disk.
- Never change UI code to work around a format problem. Fix it here, then let the UI follow.
- Never open or write maps in `F:\osu!\Songs` from tests or tools. Use copies, `testmaps/` or `corpus/`.

#### Before you say it works
1. Make sure `corpus/` exists. The corpus tests inside the two test projects skip silently without it, and new worktrees don't have it. Step 0 of the `verify-dawsu` skill shows how to copy it in. Copy it; never link it.
2. `dotnet test tests/OsuHitsound.Format.Tests` and `dotnet test tests/OsuHitsound.Lanes.Tests`.
3. Report the pass, fail and **skipped** counts you actually saw. A skipped corpus test means the change is not proven on real maps.

## Claude agents and skills in this project

Claude runs these as subagents or skills. You can't, so when a rule or the user names one, open its file and follow it yourself as a checklist. Read-only agents (reviewers) stay read-only when you play them.

- Agent `dawsu-ui-tweak` (`.claude/agents/dawsu-ui-tweak.md`): Makes one small dawsu! UI change at a time while the user tries the app (layout, colours, sizes, labels, shortcuts, mouse behaviour), then rebuilds, checks a screenshot and relaunches. Not for audio, VST, osu! file format or lane compile/decompile work.
- Agent `format-guardian` (`.claude/agents/format-guardian.md`): Read-only reviewer for any change that touches how dawsu! reads, writes, compiles or decompiles .osu files. Checks the diff against ADR 0002 (byte-identical unedited lines) and the osu! hitsound rules, and runs the format and lane tests. Use before committing such a change.
- Agent `scope-reviewer` (`.claude/agents/scope-reviewer.md`): Read-only semantic review of a diff or a document change. Checks that it does what the plan or issue asked and no more, that it doesn't contradict CONTEXT.md, the ADRs or docs/design-system.md, and that docs and CHANGELOG still describe what the code does. Complements /code-review, which looks for bugs.
- Skill `ship-feature` (`.claude/skills/ship-feature/SKILL.md`): The dawsu! feature workflow in three phases (plan, build, check), each in a fresh session that hands over through a plan file, with the user's approval gates. Run only when the user types /ship-feature.
- Skill `verify-dawsu` (`.claude/skills/verify-dawsu/SKILL.md`): Run dawsu!'s full check suite (build, the three test projects with the map corpus, UiLayoutCheck, WorkflowSafety) and report real pass/fail counts. Use before a commit, a PR or a release, or when asked "does everything still pass?".
