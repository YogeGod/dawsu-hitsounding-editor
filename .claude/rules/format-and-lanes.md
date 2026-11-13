---
paths:
  - "src/OsuHitsound.Format/**"
  - "src/OsuHitsound.Lanes/**"
  - "tests/OsuHitsound.Format.Tests/**"
  - "tests/OsuHitsound.Lanes.Tests/**"
  - "tools/RoundTrip/**"
---
# Format and lane rules (osu! file reading, writing, compile and decompile)

Mappers don't trust hitsound tools with their maps, so this code has the strictest rules in the repo.

- **Unedited lines stay byte-identical** on every write path: comments, number spelling, line endings, BOM, unknown sections and keys, `[Events]`, `[Colours]`. Lazer quirks are kept, not "fixed". Read `docs/adr/0002-saves-leave-unedited-lines-byte-identical.md` before changing any writer.
- How osu! actually resolves hitsounds (sample set, index, additions, green lines) is in `docs/osu-hitsound-rules.md`. Check it before you trust intuition.
- Use the words from `CONTEXT.md` (Lane, Folder, Lane layout). A Folder is a sample-set group in the UI, never a directory on disk.
- Never change UI code to work around a format problem. Fix it here, then let the UI follow.
- Never open or write maps in `F:\osu!\Songs` from tests or tools. Use copies, `testmaps/` or `corpus/`.

## Before you say it works
1. `dotnet test tests/OsuHitsound.Format.Tests` and `dotnet test tests/OsuHitsound.Lanes.Tests`.
2. `dotnet run --project tools/RoundTrip -- corpus` if `corpus/` has maps (it is git-ignored; `tools/build-corpus.ps1` fills it from the local lazer library). If it is empty, say the corpus round trip was not run.
3. Report the pass and fail counts you actually saw.
