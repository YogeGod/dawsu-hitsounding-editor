---
paths:
  - "src/OsuHitsound.Audio/**"
  - "native/**"
  - "src/OsuHitsound.App/MainWindow.DrumVst.cs"
  - "src/OsuHitsound.App/MainWindow.DrumRender.cs"
  - "src/OsuHitsound.App/ArrangeModel.DrumRender.cs"
  - "tools/FakeVstHost/**"
  - "tools/Vst3Smoke/**"
  - "tools/Vst3MapRender/**"
---
# Audio and VST3 host rules

- Never claim audio was heard, or that a real VST (Kontakt, GGD) loaded, unless a run in this session showed it. Tests with `FakeVstHost` prove the protocol, not the sound.
- Leave GGD Matt Garstka installed exactly as it is. Its stale VST3 repair was deferred by the user.
- Changes under `native/` mean the isolated host has to be rebuilt before release. Note that in TASKS.md.
- Renders write WAVs. They go into a copied map folder or a scratch folder, never into an original mapset.
