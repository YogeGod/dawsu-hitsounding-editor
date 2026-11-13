<!-- GENERATED from .claude/rules/ at the repo root by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

# Rules for `src/OsuHitsound.App/`

Compiled from `.claude/rules/`. They apply to the paths named under each heading, inside this folder. Paths in the rule text are relative to the repo root.

## UI rules (Avalonia app)

**Applies to, in this folder:** `**/*.axaml`, `ArrangeView.cs`, `ArrangeModel*.cs`, `Paint.cs`, `ThemeResources.cs`, `Dialogs*.cs`, `MainWindow*.cs`, `KeysoundEditor*.cs`

**Source:** `.claude/rules/app-ui.md`. The same rule also covers: `tools/UiLayoutCheck/**`, `tools/Snapshot/**`

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

## Audio and VST3 host rules

**Applies to, in this folder:** `MainWindow.DrumVst.cs`, `MainWindow.DrumRender.cs`, `ArrangeModel.DrumRender.cs`

**Source:** `.claude/rules/audio-and-vst.md`. The same rule also covers: `src/OsuHitsound.Audio/**`, `native/**`, `tools/FakeVstHost/**`, `tools/Vst3Smoke/**`, `tools/Vst3MapRender/**`

- Never claim audio was heard, or that a real VST (Kontakt, GGD) loaded, unless a run in this session showed it. Tests with `FakeVstHost` prove the protocol, not the sound.
- Leave GGD Matt Garstka installed exactly as it is. Its stale VST3 repair was deferred by the user.
- Changes under `native/` mean the isolated host has to be rebuilt before release. Note that in TASKS.md.
- Renders write WAVs. They go into a copied map folder or a scratch folder, never into an original mapset.
