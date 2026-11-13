<!-- GENERATED from .claude/rules/ at the repo root by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

# Rules for `src/OsuHitsound.Format/`

Compiled from `.claude/rules/`. They apply to the paths named under each heading, inside this folder. Paths in the rule text are relative to the repo root.

## Format and lane rules (osu! file reading, writing, compile and decompile)

**Applies to:** everything in this folder

**Source:** `.claude/rules/format-and-lanes.md`. The same rule also covers: `src/OsuHitsound.Lanes/**`, `tests/OsuHitsound.Format.Tests/**`, `tests/OsuHitsound.Lanes.Tests/**`, `tools/RoundTrip/**`

Mappers don't trust hitsound tools with their maps, so this code has the strictest rules in the repo.

- **Unedited lines stay byte-identical** on every write path: comments, number spelling, line endings, BOM, unknown sections and keys, `[Events]`, `[Colours]`. Lazer quirks are kept, not "fixed". Read `docs/adr/0002-saves-leave-unedited-lines-byte-identical.md` before changing any writer.
- How osu! actually resolves hitsounds (sample set, index, additions, green lines) is in `docs/osu-hitsound-rules.md`. Check it before you trust intuition.
- Use the words from `CONTEXT.md` (Lane, Folder, Lane layout). A Folder is a sample-set group in the UI, never a directory on disk.
- Never change UI code to work around a format problem. Fix it here, then let the UI follow.
- Never open or write maps in `F:\osu!\Songs` from tests or tools. Use copies, `testmaps/` or `corpus/`.

### Before you say it works
1. Make sure `corpus/` exists. The corpus tests inside the two test projects skip silently without it, and new worktrees don't have it. Step 0 of the `verify-dawsu` skill shows how to copy it in. Copy it; never link it.
2. `dotnet test tests/OsuHitsound.Format.Tests` and `dotnet test tests/OsuHitsound.Lanes.Tests`.
3. Report the pass, fail and **skipped** counts you actually saw. A skipped corpus test means the change is not proven on real maps.
