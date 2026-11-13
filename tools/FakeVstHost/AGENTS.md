<!-- GENERATED from .claude/rules/ at the repo root by ~/.claude/hooks/sync-codex-instructions.ps1. Edit the source, not this file. -->

# Rules for `tools/FakeVstHost/`

Compiled from `.claude/rules/`. They apply to the paths named under each heading, inside this folder. Paths in the rule text are relative to the repo root.

## Audio and VST3 host rules

**Applies to:** everything in this folder

**Source:** `.claude/rules/audio-and-vst.md`. The same rule also covers: `src/OsuHitsound.Audio/**`, `native/**`, `src/OsuHitsound.App/MainWindow.DrumVst.cs`, `src/OsuHitsound.App/MainWindow.DrumRender.cs`, `src/OsuHitsound.App/ArrangeModel.DrumRender.cs`, `tools/Vst3Smoke/**`, `tools/Vst3MapRender/**`

- Never claim audio was heard, or that a real VST (Kontakt, GGD) loaded, unless a run in this session showed it. Tests with `FakeVstHost` prove the protocol, not the sound.
- Leave GGD Matt Garstka installed exactly as it is. Its stale VST3 repair was deferred by the user.
- Changes under `native/` mean the isolated host has to be rebuilt before release. Note that in TASKS.md.
- Renders write WAVs. They go into a copied map folder or a scratch folder, never into an original mapset.
