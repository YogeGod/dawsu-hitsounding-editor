<!-- GENERATED from .claude/rules/ at the repo root by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

# Rules for `tools/UiLayoutCheck/`

Compiled from `.claude/rules/`. They apply to the paths named under each heading, inside this folder. Paths in the rule text are relative to the repo root.

## UI rules (Avalonia app)

**Applies to:** everything in this folder

**Source:** `.claude/rules/app-ui.md`. The same rule also covers: `src/OsuHitsound.App/**/*.axaml`, `src/OsuHitsound.App/ArrangeView.cs`, `src/OsuHitsound.App/ArrangeModel*.cs`, `src/OsuHitsound.App/Paint.cs`, `src/OsuHitsound.App/ThemeResources.cs`, `src/OsuHitsound.App/Dialogs*.cs`, `src/OsuHitsound.App/MainWindow*.cs`, `src/OsuHitsound.App/KeysoundEditor*.cs`, `tools/Snapshot/**`

- Colours, sizes, radii and the Slate scheme come from `docs/design-system.md` and `DesignSystem.axaml` / `ThemeResources.cs`. Don't hard-code a value the design system already names.
- Most dawsu! users are osu! mappers with no DAW experience. Controls need visible surfaces and text labels, and on/off states must be obvious. Don't make things subtle. Keep the V3.0 ribbon and menu icons and the diamond hit markers.
- Within each sample set, the hitnormal lane comes first; additions (whistle, finish, clap) sit under it.
- Drum map view must not add lanes. Resolve conflicts in the model (`ArrangeModel.DrumMaps.cs`), and turning drum map off must show the real lanes.
- The Hitsound and Keysound editors share the `.ribbon` styles. A style change in one shows in the other, so check both.

### Before you say it works
1. `dotnet build src/OsuHitsound.App -v q`
2. `dotnet run --project tools/UiLayoutCheck -- <scratch dir>` and report the check count.
3. For visible changes: `dotnet run --project tools/Snapshot -- "<copy of a map.osu>" <scratch dir>`, then look at the PNG.
4. `dotnet test tests/OsuHitsound.App.Tests`. If VST-host tests fail, build `tools/FakeVstHost` first. A missing build has caused those failures before; it is not your change.
5. Mouse feel, sound and native file pickers can't be checked from a screenshot. Say so instead of claiming them.
