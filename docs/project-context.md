# osu! Hitsound Editor: project context

A summary of the planning chat from 2026-10-29, updated at the end of the 2026-10-30 build session. Load this file at the start of a new session to pick up where it left off.

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
| Roadmap dashboard (Now/Next/Later, milestone status) | https://claude.ai/artifact/WjSNkbCWpSu7vMKrKM9aqY |

## Decisions made

- **Stack:** C# (.NET 10) with **BASS**, the audio library osu! uses, so playback timing matches the game. C++ would only be worth it for live MIDI hitsounding, which is not a priority. The UI is Avalonia.
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

## osu!stable behaviour: confirmed by ear (2026-10-30)

The user played the test maps in stable. Details are in `docs/osu-hitsound-rules.md`.

- **sampleSet 0 ("auto") on a timing point plays Soft**, whatever the map default (tested with Drum and Normal defaults). Neither the wiki (map default) nor lazer (Normal) is right. A timing-point line with no set field at all still uses the map default (untested).
- **A green line inside a slider changes the ticks after it** (index and volume): each tick reads the timing point in force at its own time. The slide and whistle are still evaluated at slider start (untested).
- **A slider's own index and volume are ignored** for the head, end and body. The timing point wins.
- **A filename override plays only that file:** no hitnormal and no additions.

## Milestones

1. **Stable test maps:** listen in stable and record the answers in the research note.
2. **Null round-trip:** lossless parser and writer, no UI.
3. **Decompiler:** .osu → lanes plus per-moment velocity.
4. **Compiler (Apply mode):** lanes → fields, indices and green lines; small edits change only the lines they touch.
5. **Lane grid UI:** drum-editor UI, audio sync, Cubase keybinds, conflict display.
6. **Generate mode:** an empty diff becomes a hitsound diff that Mapping Tools' Hitsound Copier copies without warnings.


## Current state

Repo: `E:\Claude\osu-hitsound-editor`, branch `main`. It is pushed to the **`csharp-rewrite` branch** of the remote `dawsu` (https://github.com/YogeGod/dawsu-hitsounding-editor). That remote's `main` is a different project (the Electron dawsu! app, releases up to V6.39) with no shared history, so **never push to its `main`**: use `git push dawsu main:refs/heads/csharp-rewrite`. `/security-review` looks for `origin/HEAD` and fails here, so the security pass is done by hand.

**Milestones:** M1–M4 and M6 are done. M5 (lane grid UI) is built and the audio check passes; its one open item is **importing the user's Cubase Key Commands XML**. Some keybind code exists in `ArrangeModel.cs`/`ArrangeView.cs`, but how far it goes hasn't been checked.

| Path | What |
|---|---|
| `src/OsuHitsound.Format/` | Lossless format library: `OsuFile`/`OsuLine` keep original bytes per line; field-level get/set on `TimingPoint`/`HitObject`; `HitSample`; `HitsoundRules`. |
| `src/OsuHitsound.Lanes/` | `Decompiler` (.osu → lanes; `PointState` holds the stable sampleset rules), `Compiler` (Apply mode, green lines, conflicts, notes about slider body/tick changes), Generate mode, `MixPlanner`, project file. |
| `src/OsuHitsound.Audio/` | BASS playback (`AudioEngine`, `HitScheduler`). Default playback buffer is `AudioEngine.DefaultBufferMs` = 30 ms: hitsounds lag the song by about the buffer, and 100 ms was audibly late. |
| `src/OsuHitsound.App/` | Avalonia UI: lane grid, waveform, object strip, tools, cycle/locators, bookmarks, preferences, F1 help (`EditorCommands.cs` is the one command list). |
| `tools/make-test-maps.cs` | Generates `testmaps/hitsound-rules-test` and `.osz`: ticks, slider body, sampleSet 0 (v2 Drum default, v3 Normal default) and filename-override diffs. Needs ffmpeg. |
| `tests/` | Format (48), Lanes (105) and App (61) tests: 214 total, all passing. |

**Running the app:** `src/OsuHitsound.App/bin/Debug/net10.0/win-x64/OsuHitsoundEditor.exe`. While it runs, it locks the App build, so close it before `dotnet test`.

**User's machine:**
- osu!stable is installed at `F:\osu!` (Songs: `F:\osu!\Songs`), **not** in AppData. To get a new test map into stable, copy the files into a folder there and press F5 in song select. Double-clicking an `.osz` opens it in lazer.
- The user's saved editor preferences may still hold a 100 ms buffer. Saved values override the new default, so check Preferences if hitsounds sound late.

## dawsu! V6.39 (Electron app) vs this editor

V6.39's features are documented in its `README.md` (Features section) and `src/help.js` (~96 control and tool descriptions). It has these, which this editor lacks: MIDI import/export with velocity and note/channel per lane, Quantize (Q), Repeat (Ctrl+R), time signatures, select/scale by volume, a Range tool, event lengths and colour modes, manual assignment of sounds to objects, `.osz` import, New empty project, WASAPI/ASIO output, a recent-edits list, screenshots, playback start mode and return-on-stop. The likely next ports for daily hitsounding are Quantize/Repeat, select-by-volume and the Range tool.

## Next steps

1. Start the next feature with `/mattpocock-skills:grill-with-docs`: either the Cubase keybind import (last v1 item) or porting V6.39 features. For several features: `/to-spec` → `/to-tickets` → `/implement`.
2. Before each commit, follow CLAUDE.md: `/simplify`, `/code-review`, security pass by hand, then `dotnet test`. Push only when asked, to `csharp-rewrite`.
3. CLAUDE.md's periodic `codebase-design` architecture pass is due after the 2026-10-30 build.
4. Still open in the research note: questions 5–10 (missing-file fallback, spinner samples, volume over 100, filename case, field history, line endings).

## Working with this user

- **Plain language.** Explain osu!/audio terms simply and avoid jargon.
- **Don't** ask them to "answer by number". Use clickable choices or one plain question at a time.
- **When they need to test something,** say exactly where (full path, diff name, which group) and what to listen for. Ask for their answer in plain words ("the 4th group"), not codes.
- **Ask before closing their running apps.**
- **Follow `E:\Claude\CLAUDE.md`:**
  - Before a feature: grill the plan, research unfamiliar tech, then show a short plan and wait for OK.
  - Before a commit: `/simplify`, `/code-review`, the security review, and the tests.
- **Ask before long runs.**
