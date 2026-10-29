# osu! Hitsound Editor: project context

A summary of the planning chat from 2026-10-29. Load this file at the start of a new session to pick up where it left off.

## What we're building

A standalone hitsound editor for **osu!standard** maps that works like **Cubase's drum editor**: same workflow, same keybinds, same painting feel. It has to be **100% compatible with the osu! editor**, especially when exporting back into a map.

- **Lanes are sample files.** Each lane is one sound (kick, snare, crash), like a row in a Cubase drum map. Clicking in a lane places only that lane's sound, so you can see which sound is on which object.
- **Two jobs, used about equally:**
  1. Import an empty diff (timing only), hitsound everything, and export. Hit objects are generated automatically and every sample comes out correct.
  2. Open maps other people made and hitsounded (their hitsound diffs *or* playable diffs), change some hitsounds, and export with only those changes.
- **Pipeline:** mapset folder → **decompiler** (.osu → lanes) → **lane grid** → **compiler** (lanes → hitsound fields, custom indices, green lines, renamed sample files) → export.
- **Export modes:** *Apply* keeps the objects as they are and shows sounds that can't be encoded as red conflicts. *Generate* is Apply plus creating circles wherever a sound has no object to land on.
- **Why it exists:** the osu! editor and Mapping Tools' Hitsound Studio are slow and tedious. The user has used Hitsound Studio and dislikes it.

## Documents

| What | Where |
|---|---|
| One-pager (Claude Doc) | https://claude.ai/artifact/LSbRfSzohZAhKmfV6P2GB2 |
| osu! hitsound rules research note (from primary sources: lazer source and osu-wiki) | `docs/osu-hitsound-rules.md` |
| Main-screen UI design: chosen direction, colours, layout | `docs/ui-design.md` (design canvas: https://claude.ai/artifact/C3ChZkS3d9onU2eF5bHW41) |
| Stable listening test instructions | `testmaps/README.md` |

## Decisions made

- **Stack:** C# (.NET 10) with **BASS**, the audio library osu! uses, so playback timing matches the game. C++ would only be worth it for live MIDI hitsounding, which is not a priority. The UI framework (WPF or Avalonia) is not chosen yet.
- **Source of truth:** osu!**stable**. Where stable and lazer disagree, stable decides. The compiler only writes patterns both clients play the same way.
- **License:** free and open source, so BASS stays free under its non-commercial terms.
- **Speed goal:** no measured number. "10× faster" describes the spirit; success is judged by feel after hitsounding a real map.
- **Keybinds:** import the user's own Cubase Key Commands XML file.
- **Hitsound copying to other diffs:** keep using Mapping Tools' Hitsound Copier in v1.

### Export rules (non-negotiable)

1. **Null round-trip:** import then export with no edits gives byte-identical `.osu` and `.osb` files, including encoding, BOM, line endings and the final newline.
2. **Minimal diff:** keep every original line and patch only changed fields. Never re-serialize the whole file. lazer's encoder can't be the reference because it normalizes everything.
3. **Shared files:** sample files are shared across all diffs in the mapset folder. Reuse existing indices when they already have the right sounds, and never overwrite a file another diff uses.
4. **Lookup rules:**
   - Timing points are looked up 5 ms after each object's time.
   - Index 0 means the skin only, index 1 means no number suffix, index N≥2 adds the number.
   - Extensions are tried in the order wav, mp3, ogg.
   - A missing file falls back to the player's skin.
   - Never write the hitSample filename field.
5. **Stay inside the mapset folder:** a hitSample filename or storyboard sample path in someone else's map can contain `..\` or an absolute path. Never read, copy or write a sample file outside the mapset folder.
6. **Ranking criteria:** no unused or 0-byte files, hitsound files at least 25 ms long, the official 44-byte `blank.wav` for silence, and no automatic hitsound generation from audio.

### v1 scope

- **In:**
  - osu!standard
  - circles, slider heads, repeats and tails, and spinner ends as hitsound positions
  - one velocity per moment, written as green lines
  - Apply and Generate export
  - storyboard samples shown as a read-only lane
  - mouse plus imported Cubase keybinds
- **Out:**
  - taiko, catch and mania
  - editing slider body and tick sounds (shown read-only)
  - per-note velocity
  - a built-in hitsound copier
  - MIDI import or live recording

## osu!stable behaviour: the user's answers from experience

- **sampleSet 0 on a timing point:** falls back to the map's default sampleset (`[General] SampleSet`). lazer's parser reads it as Normal.
- **Slider's own index and volume:** the head and end ignore them and use the timing point's, the same as lazer. Unsure about the body.
- **Slider ticks:** probably follow the slider body's sampleset. **Still open:** does a green line in the middle of a slider change the ticks after it?
- **Filename override:** stable plays only that file, with no hitnormal and no additions. lazer plays them all.

The milestone 1 test mapset is meant to confirm all of these by ear.

## Milestones

1. **Stable test maps:** listen in stable and record the answers in the research note.
2. **Null round-trip:** lossless parser and writer, no UI.
3. **Decompiler:** .osu → lanes plus per-moment velocity.
4. **Compiler (Apply mode):** lanes → fields, indices and green lines; small edits change only the lines they touch.
5. **Lane grid UI:** drum-editor UI, audio sync, Cubase keybinds, conflict display.
6. **Generate mode:** an empty diff becomes a hitsound diff that Mapping Tools' Hitsound Copier copies without warnings.

## Current state

Repo: `E:\Claude\osu-hitsound-editor`, branch `main`. There is no remote yet. `/security-review` needs one (it compares against `origin/HEAD`), so until then the security pass is done by hand.

| Path | What |
|---|---|
| `src/OsuHitsound.Format/` | Lossless format library. `OsuFile`/`OsuLine` keep original bytes per line. `TimingPoint`/`HitObject` provide field-level get/set, and setting a field to its current value changes nothing. Also `HitSample`, plus `HitsoundRules` (5 ms lookup, sample file names). |
| `tools/RoundTrip/` | `dotnet run --project tools/RoundTrip -- <folder>` checks every .osu/.osb file under the folder. |
| `tools/make-test-maps.cs` | `dotnet run tools/make-test-maps.cs` generates `testmaps/hitsound-rules-test.osz` (3 diffs: ticks, slider body, sampleSet 0). Needs ffmpeg. |
| `tools/build-corpus.ps1` | Copies every .osu/.osb file from the local lazer library into `corpus/`, which should be git-ignored. |
| `tests/OsuHitsound.Format.Tests/` | 29 xUnit tests (format edge cases, lookup rules, corpus round-trip), all passing. |

- **Corpus:** 2,932 `.osu` (format v6, v7, v12–v14) and 67 `.osb` files from the user's lazer library. **All 2,999 pass the round-trip check.** v5 and v8–v11 aren't covered.
- **Test mapset:** generated, but **not yet played** by the user in stable.

## Next steps

1. Before each commit, follow CLAUDE.md: `/simplify`, `/code-review`, `/security-review`, then `dotnet test`. The round-trip suite plus the lookup-rule tests stand in for CLAUDE.md's "timing/judgement tests".
2. The user plays `testmaps/hitsound-rules-test.osz` in stable (see `testmaps/README.md`), and the answers go into the research note.
3. Milestone 3: the decompiler.

## Working with this user

- **Plain language.** Explain osu!/audio terms simply and avoid jargon.
- **Don't** ask them to "answer by number". Use clickable choices (AskUserQuestion) or one plain question at a time.
- **Follow `E:\Claude\CLAUDE.md`:**
  - Before a feature: grill the plan, research unfamiliar tech, then show a short plan and wait for OK.
  - Before a commit: `/simplify`, `/code-review`, `/security-review`, and the tests.
- **Ask before long runs.**
