# Context: osu!lazer editor and hitsounding research

Load this file at the start of a new session to pick the work back up. The detailed findings are in the two reports listed below. This file holds the setup, the decisions made, and the most important facts in short form.

## Who and why
- The user maps for osu!standard (their map: *Catalyst [Impetus]*, with stable maps in `F:\osu!\Songs`). They want to know exactly how the osu!lazer editor and its hitsounding behave.
- The workspace is `E:\Claude\REA`. REA 6.8.0 and Ghidra 12.1.4 are installed as an MCP server (see memory `rea-setup-checkpoint`).
- `E:\Claude\CLAUDE.md` has workflow rules: show a plan before features, run review steps before commits. This work has been research only: no code changes and no git repo of our own.

## Files
| Path | What it is |
|---|---|
| `E:\Claude\REA\osu\osu-editor-and-hitsounding.md` | Earlier note: how `.osu` maps are **read** (hitsound bitmask, bank inheritance, +5/+6 ms timing-point lookup, sample index fallback, snapping, distance snap, slider speed, stacking, and a survey of 13 maps). Based on ppy/osu commit `b77b028f`. |
| `E:\Claude\REA\osu\osu-lazer-editor-internals.md` | Main report: how the editor **edits and writes** maps. §1–10 cover the editor core, §11 sliders, §12 hitsounding in depth. |
| `E:\Claude\REA\osu\src\osu` | ppy/osu at tag **`2026.1026.0`** (commit `b94ee4d`), a shallow clone. |
| `E:\Claude\REA\osu\src\osu-framework` | ppy/osu-framework at tag **`2026.1019.0`** (commit `06e28ec`). |
| `%LOCALAPPDATA%\osulazer\current` | The installed lazer build, the same version as the clones. |

## Using REA on osu!lazer: what was found
- **The install:** .NET 10 that compiles at runtime (no NativeAOT or ReadyToRun), 356 managed assemblies, plus native SDL2/3, Veldrid, FFmpeg, Realm and SQLite.
- **`osu.Game.dll`:** not obfuscated; 7,333 types and 51,261 methods. REA confirmed the editor types exist and that `EditorChangeHandler`'s fields match the source exactly, so **the source clone is the shipped code**.
- **`osu!.dll`** (the desktop launcher) calls into Windows for:
  - the NVIDIA driver API (NVAPI), to set the graphics profile;
  - a low-level keyboard hook, to block the Windows key;
  - `SHChangeNotify`, for file associations.
- **`osu.Game.Auth.dll`:** closed source and **obfuscated** (single-letter names, control-character resource names, Reflection.Emit). It uses Windows key-storage crypto (`NCrypt*`) and loads native code by hand with `dlopen`/`dlsym`. It's most likely the client-verification / anti-cheat module. **We deliberately stopped at mapping its outline; it is not to be deobfuscated or analysed further.**
- **REA quirks:**
  - Windows paths work as-is.
  - `inspect_managed_members` on `osu.Game.dll` hits the 64 MB representation budget when given several types. Ask for **one type at a time**.
  - `inspect_analysis_view` doesn't accept a `fields` option for `managed` views.
- For questions about the open-source parts, read the source with normal tools; use REA only to confirm the source matches the shipped build.

## Key facts (in short; the report has the details and file references)

**Editor core**
- **Screens:** Setup, Compose, Design (**empty**, just "under construction"), Timing and Verify.
- **Saving needs a legacy ruleset;** `.osu` is the editor's real data model.
- **Undo:** every change encodes the whole `.osu` file into a 50-state history; undo works from a DiffPlex line diff.
  - "Unsaved changes" means the current snapshot's hash differs from the last save's.
  - Anything the `.osu` format can't express can't be undone.
- **Editor clock:** decoupled from the track; applies offsets in the order platform (Windows +15 ms, −25 ms more with WASAPI), then global, then per-beatmap.
- **Seeking:**
  - Paused: one `beatLength ÷ divisor` step, snapped to the grid.
  - Playing: ×`divisor × BPM ÷ 120`, times stable's factor `1 + 250 ÷ (int)beatLength`. Shift ×4.
  - Smooth seek: jumps under 5 s animate over 300 ms.
  - Hitsounds are muted while seeking or stopped.
- **Divisor:** presets {1,2,4,8,16} and {1,3,6,12}, or custom 1–64. Ctrl+wheel cycles within the current group.

**Compose / snapping**
- **Snap order:** nearby object (within 10 % of the radius ≈ 6.4 px; stacked objects snap to their unstacked position), then distance grid, then position grid (clamped to 512×384).
- **Distance grid anchor:** the last object at or before the current time; SV from the last slider before it. Distance is snapped by DistanceSpacing, rounded half up, never 0.
- **Input:** holding Shift temporarily flips grid snap. Right-click on empty space toggles new combo. J/K nudge by one `beatLength ÷ divisor`.

**Hitsounding: the model**
- **Per-sample fields:** Name, Bank, Suffix, UseBeatmapSamples, Volume, EditorAutoBank. Index 0 and index 1 are both stored with no suffix; only UseBeatmapSamples separates them.
- **New objects copy from the previous object:** volume, index and beatmap-samples flag always; the normal bank only with Auto bank on.
- **Bank toggles act like radio buttons.** Auto bank applies only to new placements, never to a selection.
- **Keys:** Q new combo · W/E/R whistle/finish/clap · Shift+QWER normal bank Auto/N/S/D · Alt+QWER addition bank Auto/N/S/D.
- **Volume floor:** playback is never below **5 %**. True silence needs a blank custom sample of at least 25 ms.
- **Sounds derived from the object:**
  - `sliderslide`, `slidertick` come from the body hitnormal; `sliderwhistle` loops only if the body has whistle.
  - Body samples are fixed **once** from the green line at start + 6 ms; edges read edge + 5 ms. Green lines inside a slider body don't change ticks or slide in lazer (stable not compared).
  - `spinnerspin` uses the spinner's first sample.
- **Timeline popover** (label like `S:2 70`):
  - A selected object means editing the whole selection, including every slider edge.
  - Edge pieces edit a single edge only when one object is selected.
  - Volume and sample set apply to the normal sound and additions together.
  - Changing the normal bank also changes auto-bank additions.
- **Custom sample sets:** found from file names `{bank}-{hit*}{N}.{wav|ogg|mp3}` (no number = set 1). Adding a file in Setup → Resources renames it to that pattern.

**Hitsounding: the save path** (`LegacyBeatmapEncoder`) — the biggest difference from stable
- **Green lines are generated from the objects:** one at each object's end time, and for sliders one at every edge plus edge + 6 ms to restore the body. Volume = the highest; bank = hitnormal's; index = the highest. Redundant points are dropped.
- **Per-object field (non-mania):** `normal:addition:0:0:filename`, so index and volume are **always written as 0**. An auto addition bank is written as 0.
- **Kiai** is never put on red lines.
- **Result:** saving a stable map in lazer rewrites `[TimingPoints]`.

**Timing screen**
- **Tap BPM:** ignores the first 4 taps, averages up to 128, rounds to a whole BPM ÷ playback rate.
- **"Adjust existing objects on timing changes" is on by default:** moving an offset shifts that section's objects; changing BPM rescales their beat positions.

**Breaks:** generated automatically when the gap is ≥ 1,300 ms. Start = previous end + 200 ms; end = next start − preempt; shorter than 650 ms is dropped. The object after a break is forced to start a new combo.

**Sliders**
- **Placing:** clicks add control points; clicking the last point again or pressing S starts a new segment; right-click finishes.
- **Automatic curve type:** 1–2 points Linear, 3 Perfect, 4+ Bézier. Tab or Alt+1–4 overrides.
- **Freehand:** drag right after the first click. Fitted as a degree-4 B-spline; sections that fit a circle become Perfect arcs (defaults: tolerance 90, corner 40, circle 30).
- **Length always rounds down** to the last snap step that fits the path.
- **Editing:**
  - Ctrl+click adds a control point.
  - Perfect with more than 3 points, or an arc ≥ 640×480, becomes Bézier.
  - Split offsets each piece by 100 ms.
  - Convert to stream (Ctrl+Shift+F) gives every circle the head's samples.
- **Dragging the end:**
  - On the playfield: changes length (Shift = change SV instead).
  - On the timeline: changes the repeat count (Shift = change SV).
- **SV toolbox:** 0.1–10×. Follows the selection, otherwise the last slider before the current time.
- **Saving and export:**
  - Lazer saves mixed path types and `B4|`, **which stable can't read**.
  - Export → legacy converts them to Bézier, rounds red lines and object times down to whole ms (moving each section so it stays snapped), rounds positions, and snaps grid size to 4/8/16/32.

**Verify thresholds**
- **Unsnapped:** ≥ 2 ms from the nearest tick of **any** standard divisor is a problem; 1–2 ms is minor.
- **Few hitsounds:** > 24 s and > 16 objects is a problem; > 8 s and > 4 objects a warning; > 4 s a note.
- **Muted:** ≤ 5 % muted; ≤ 20 % low (heads only).
- **Delayed hitsound files:** ≥ 5 ms is a warning, ≥ 1 ms negligible.
- **Formats:** hitsounds must be WAV or OGG; any audio under 25 ms is flagged.
- **Spacing and timing:** concurrent within 2 ms (10 ms "almost"); spacing warns at 10 px and 15 % off (problem at 20 px and 30 %); too-short slider span < 125 ms.
- **Offscreen bounds:** x −67…579, y −60…428.

## Open questions and possible next steps
- **Was *Catalyst* last saved in lazer?** Signs to look for: a green line next to every hitsounded object, `:0:0:` index/volume per object, `B4|` or mixed slider types. The user declined this for now ("Not now").
- **Stable comparison:** do ticks and slider-slide in stable follow green lines inside the body? (osu!stable is fully obfuscated and was not analysed.)
- **Not traced:** the taiko, catch and mania composers; the clipboard and timestamp format (`EditorTimestampParser`, `mm:ss:mmm (1,2,3)`).
