# osu!lazer editor internals

**Sources:**
- ppy/osu at tag `2026.1026.0` (commit `b94ee4d`) and ppy/osu-framework at tag `2026.1019.0` (commit `06e28ec`), cloned to `src/`. These are the exact versions installed in `%LOCALAPPDATA%\osulazer\current`.
- REA static inspection of the installed `osu.Game.dll` (assembly version 2026.1026.0.0, SHA-256 `4315a2e0…`). The editor types checked are all present, and the field layout of `EditorChangeHandler` matches the source exactly (REA evidence `ev_7ba1fc96…`). Nothing was executed.

This continues [osu-editor-and-hitsounding.md](osu-editor-and-hitsounding.md). That note covers how maps are **read**. This one covers how the editor **edits and writes** them.

## 1. Structure

| Screen (F1–F5 order) | Class | Notes |
|---|---|---|
| Song Setup | `Screens/Edit/Setup/SetupScreen` | Metadata, difficulty, colours, resources, design. A new beatmap opens here; an existing one opens in Compose. |
| Compose | `Screens/Edit/Compose` + the ruleset's `HitObjectComposer` | Placement, selection, snapping. Each ruleset has its own `*/Edit/` folder. |
| Design | `Screens/Edit/Design/DesignScreen` | **Empty.** The screen is just an "under construction" message; there is no storyboard editor. |
| Timing | `Screens/Edit/Timing` | Control point table, tap BPM, metronome, waveform comparison. |
| Verify | `Screens/Edit/Verify` + `Rulesets/Edit/Checks` | AiMod-style checks (see §7). |

The central objects are `Editor` (1,718 lines; it owns screens, menus and hotkeys), `EditorBeatmap` (the live playable beatmap plus selection), `EditorClock` and `BeatmapEditorChangeHandler`.

**Saving only works for legacy rulesets** (`canSave = ruleset is ILegacyRuleset`). The editor saves by writing a `.osu` file through `LegacyBeatmapEncoder`. That makes `.osu` the editor's real data model, not just an export format.

## 2. Undo/redo works on whole-file snapshots

`EditorChangeHandler` + `BeatmapEditorChangeHandler` + `LegacyEditorBeatmapPatcher`:

- On every change, the editor **encodes the entire beatmap to `.osu` text** and stores the bytes. If the bytes equal the previous snapshot, nothing is pushed.
- History is capped at **50 states** (`MAX_SAVED_STATES`); the oldest is dropped.
- To undo or redo, the editor **runs a line diff (DiffPlex) between two snapshots** and then:
  - removes and re-inserts only the `[HitObjects]` lines that changed, by index;
  - replaces only the control-point groups that differ;
  - patches breaks and bookmarks the same way;
  - copies SV and samples onto the existing objects, because those live on green lines in `.osu` but on the objects in lazer.
- "Unsaved changes" means the SHA-2 hash of the current snapshot differs from the hash taken at the last save. **Undoing back to the saved state clears the unsaved flag.**
- Consequence: **anything the `.osu` format can't express can't be undone either**, because it never makes it into a snapshot.

## 3. Clock and seeking (`EditorClock`)

- The clock is built on `FramedBeatmapClock(applyOffsets: true, requireDecoupling: true)`. "Decoupled" means the editor clock keeps running past the end of the track or before 0 without the audio driving it. Seeks are clamped to `[0, track length]`.
- The offsets apply in this order: **platform offset** (Windows **+15 ms**, plus −25 ms more with experimental WASAPI; 0 on other systems), then your **global offset**, then the **per-beatmap offset**. So the editor shows objects with the same offsets as gameplay.
- **Arrow-key seek while paused:** the step is `beatLength ÷ divisor`, then the result snaps to the grid (floor going forward, ceiling going back). Forward seeks never pass the next red line. If rounding would land on the current position, it moves one more step.
- **Arrow-key seek while playing:** the step is multiplied by `divisor × BPM ÷ 120`, which makes it constant in time regardless of BPM. It's then multiplied again by stable's legacy factor `1 + 250 ÷ (int)beatLength`. Shift multiplies by 4.
- **Smooth seek:** jumps under 5,000 ms animate over 300 ms (OutQuint easing) while paused. During playback, or for longer jumps, the seek is instant.
- **Hitsounds are muted while seeking or stopped** (`SeekingOrStopped`), so scrubbing never fires samples.

## 4. Beat divisor (`BindableBeatDivisor`)

- Preset groups: common {1, 2, 4, 8, 16}, triplets {1, 3, 6, 12} and custom. The full allowed range is 1–64. `SetArbitraryDivisor` switches to whichever group contains the value.
- Ctrl + mouse wheel cycles to the previous or next divisor **within the current group only**.
- Tick colours: 1 white, 2 red, 4 blue, 8 yellow, 16 dark purple, 3 purple, 6 dark yellow, 12 darker yellow, 5/7/9 light green, anything else red.
- Each tick's colour is the smallest preset `d` where `(beatIndex × d) mod divisor == 0`.

## 5. Compose (osu!standard)

### Snap priority
Placing a circle, placing a slider, dragging a slider head and moving a selection all resolve snapping the same way (`HitCirclePlacementBlueprint`, `SliderPlacementBlueprint`, `OsuBlueprintContainer`, `PathControlPointVisualiser`):

1. **Nearby object:** snaps to any unselected object's snap point within **10 % of the circle radius** (≈ 6.4 osu!px). A stacked target snaps to its **unstacked** position, so dropping onto any object in a stack adds to that stack.
2. **Distance-snap grid** (if enabled and nothing nearby matched).
3. **Position grid** (rectangular, triangular or circular, if enabled), applied on top of the result of 1 or 2 and clamped to the 512×384 playfield.

If a nearby-object snap also lies on a distance-snap ring (within 1 px), the editor still uses the ring's time. That lets "stack on the previous note" and "time from distance snap" work together.

### Distance-snap grid
- **Anchor:** the grid starts from the last object at or before the current time (or before the first selected object). It runs toward the next object whose start is at or after the anchor's end time. Spinners never anchor a grid.
- **SV source:** the last slider at or before the anchor, which matches the "last slider before" rule in the earlier note.
- **Snapping:** the cursor distance is divided by DistanceSpacing, snapped to whole beat-snap steps (rounded half up, never 0), then multiplied by DistanceSpacing again.
- **Upper limit:** if the snapped time would pass the next object, the result is clamped to `MaxIntervals` steps.
- **"Limited distance snap":** the time is fixed to the current editor time and only the distance is derived from it.
- Results outside the playfield are rejected.

### Shortcuts and toggles
- **Holding Shift** temporarily flips grid snap; releasing flips it back.
- **Right-click on empty space** with nothing selected toggles new combo for the next placement. With a selection, right-click deletes.
- **J / K nudge:** moves the selection by one `beatLength ÷ divisor`, using the red line at the first selected object.

## 6. Hitsounding in the editor

### Placement state (`PlacementStateManager`)
A new object takes its state from the toolbar toggles, and its combo/sample state from the **previous object** (the last one that starts before the new object ends):

- **Volume, index (suffix) and "use beatmap samples"** are always copied from the previous object's `hitnormal`.
- **Auto bank (normal):** copies the previous object's `hitnormal` bank and marks it `EditorAutoBank`.
- **Auto bank (addition):** whistle, finish and clap use the object's own normal bank, falling back to soft.
- **Slider edges:** all node samples are reset to the slider's own samples when it's placed.

### Selection toggles (`HitObjectComposer.SelectionState`)
- Banks act like **radio buttons, not toggles**. You can't switch a bank off; you can only choose another one.
- "Auto" is ignored while objects are selected. It only affects new placements.
- Keys: Q new combo · W/E/R whistle/finish/clap · Shift+Q/W/E/R normal bank Auto/Normal/Soft/Drum · Alt+Q/W/E/R addition bank Auto/Normal/Soft/Drum.

### What saving writes (`LegacyBeatmapEncoder`)
**This differs most from stable.** Lazer stores bank, volume and index **on each object**, so on save it **generates green lines from the objects**:

| Object | Green line emitted |
|---|---|
| Circle (or any object without edges) | One sample point at the object's **end time** |
| Slider / repeating object | One at every edge, plus one at **edge + 6 ms** restoring the body's samples, if a span is longer than 6 ms |
| Nested objects (ticks etc.) | Collected the same way |

- Each generated point's **volume = the highest volume among the object's samples**. Its **bank = the `hitnormal` bank**. Its **index = the highest index** (a numbered suffix, or 1 if "use beatmap samples" is on, else -1, meaning inherit).
- Points that don't change anything are dropped. Red lines never carry kiai; kiai is moved to a green line at the same time.
- **Each object's own field (non-mania)** is written as `normalBank:additionBank:0:0:filename`. **Index and volume are always written as 0** because the green line already carries them. Auto-assigned addition banks are written as 0 (inherit).
- In osu!taiko and osu!mania, scroll speed is written as SV on every effect point.

Practical consequence:
- Opening a stable map in lazer and saving it can **rewrite the whole `[TimingPoints]` section**. Green lines appear next to every hitsounded object, and any per-object index or volume is folded into them.
- This fits the earlier survey, which found no map using per-object index or volume. A map saved in lazer can't contain them.
- Whether that explains *Catalyst*'s 1,029 green lines depends on which client last saved it, which I didn't check.

## 7. Timing screen

- **Tap BPM:** ignores the first **4 taps** and averages up to the last **128**. The result is `round(60000 ÷ average interval ÷ playback rate)` and is written straight to the selected red line.
- **"Adjust existing objects on timing changes"** is **on by default**:
  - Moving a red line's offset shifts every object in its section (up to the next red line) by the same amount.
  - Changing BPM rescales each object's beat position, so objects stay on the same beats. Durations of non-repeating objects (spinners, holds) scale too.
  - The first red line's section reaches back to −∞.

## 8. Breaks (`EditorBeatmapProcessor`)

Breaks are **regenerated automatically** whenever object timing changes:
- **When a break is added:** the gap between one object's end and the next object's start is at least **1,300 ms** (200 + 650 + 450).
- **Break start:** previous end **+ 200 ms**.
- **Break end:** the next object's start **minus its approach time (preempt)**. For objects without preempt, it's next start − max(450 ms, 2 beats).
- **Too short:** a break under **650 ms** is dropped.
- **Manual breaks** are kept unless they overlap objects or fall outside the map.
- **New combo:** the object after a break is forced to start a new combo.

## 9. Verify thresholds (from `Rulesets/Edit/Checks`)

| Check | Rule |
|---|---|
| Unsnapped | Measured against the nearest tick of **any** standard divisor (1–9, 12, 16), not just the current one. ≥ 2 ms off → problem. 1–2 ms → minor. Under 1 ms is ignored (SV rounding). Slider repeats and ends are checked too. |
| Few hitsounds | Time without hitsounds, excluding breaks: > 24 s **and** > 16 objects → problem; > 8 s and > 4 objects → warning; > 4 s and > 4 objects → negligible. Samples count at object **end** times, including slider edges. |
| Muted objects | Volume ≤ 5 % → muted. Volume < 20 % → low volume. |
| Delayed hitsounds | Audio onset > 5 ms into the file → warning; > 1 ms → negligible. |
| Concurrent objects | Within 2 ms → concurrent. Within 10 ms → almost concurrent. |
| Time/distance equality | Compares spacing over a 4 s window. Warns at 10 px **and** 15 % off; problem at 20 px and 30 % off. Ignores objects within 12 px (stacks). |
| Low-diff overlaps | Should overlap below 150 ms (1/2 at 200 BPM). Should not overlap above 250 ms. |
| Too-short sliders | Span under 125 ms (1/2 at 240 BPM). |
| Offscreen | Bounds −67 to 579 on x and −60 to 428 on y; slider paths are sampled every 5 px. |

## 10. Default editor keys (`GlobalActionContainer`)

- **Seeking:** ←/→ seek (Shift ×4) · ↑/↓ previous/next red line · Ctrl+←/→ previous/next object · Ctrl+Shift+←/→ previous/next sample point · Alt+←/→ bookmarks.
- **Playback:** Z start · X play from start · Space/C pause · V end.
- **Bookmarks:** Ctrl+B add · Ctrl+Shift+B remove nearest.
- **Editing:** Ctrl+D clone · Ctrl+H/J flip · Ctrl+R/E rotate/scale · G/Shift+G grid spacing/type · J/K nudge · Ctrl+Alt+wheel DistanceSpacing.
- **Test play:** F5 test · Tab autoplay · Ctrl+P quick pause · F1/F2 exit to start/current time.
- **File:** Ctrl+L discard changes · Ctrl+Shift+O edit externally · Ctrl+Shift+U submit.

## 11. Sliders

Source: `osu.Game.Rulesets.Osu/Edit/Blueprints/Sliders/`, `Rulesets/Edit/ComposerDistanceSnapProvider`, `Database/LegacyBeatmapExporter`.

### Placing with clicks (`SliderPlacementBlueprint`)
- **First click** sets the head, which goes through the usual snap order (nearby object → distance grid → position grid). The slider's SV comes from the velocity toolbox (§11.5).
- **Each later left click** adds a control point. Clicking the last point again (or within 1 px of it) starts a **new segment** there, the equivalent of stable's red anchor.
- **S** places a point and starts a new segment in one step.
- **Right-click** finishes the slider.
- **Control points snap only to the position grid** while you place them. Nearby-object and distance snapping apply to the head alone.
- **Automatic curve type** for each segment, by point count including the cursor: 1–2 → Linear, 3 → Perfect circle, 4+ → Bézier.
- **Choosing the type by hand:** Tab / Shift+Tab cycles Linear → Bézier → Perfect → B-spline(4). Alt+1…4 picks one directly. Once you choose a type by hand, that segment stops changing automatically.
- Once placement has started you can keep drawing outside the compose area.

### Freehand drawing
- **To start:** drag right after the first click, before placing any other point. The stroke is fitted live with an incremental **degree-4 B-spline**.
- **Toolbox settings** (internal value, then the 0–100 number shown in the UI):
  - Tolerance 1.8 (shown 90)
  - Corner threshold 0.4 (shown 40)
  - Circle threshold 0.0015 (shown 30)
- **Circle replacement:** each fitted segment becomes a **3-point Perfect curve** when all of these hold:
  - its arc is under 1,000 px;
  - it bends one way only (no S-shape) and turns less than a full circle;
  - the fit error is within the circle threshold.
  - A circle threshold of 0 turns replacement off.
- **Length:** the slider keeps its full drawn length while you drag. When you release, the length is snapped.

### Length snapping
- **Rule:** the slider's length is always reduced to the **last whole snap step that fits inside its path** (`FindSnappedDistance`). If rounding to the nearest step would make it longer by more than 1 ms, it drops back one step.
- **Effect:** sliders get **shorter**, never longer, than the shape you drew. The unused part of the path is still stored as control points.
- **Step size:** one step = `100 × SV × base SV ÷ divisor` osu!px, which equals one `beatLength ÷ divisor` of time.

### Editing an existing slider
- **Ctrl+left-click on a selected slider** adds a control point there and starts dragging it. "Add control point" is also in the right-click menu.
- **Dragging control points:**
  - Moving the **head point** moves the whole slider and re-snaps its time (nearby object → distance grid → grid).
  - **Other points** snap to nearby objects and the grid. The exception is points inside an inherited B-spline segment, which snap to the grid only.
  - After every drag step the length is re-snapped. If that would leave the length at 0, the drag is undone.
- **Path type rules** (`EnsureValidPathTypes`):
  - A Perfect segment with more than 3 points becomes Bézier.
  - A 3-point Perfect arc whose bounding box is ≥ 640×480 becomes Bézier.
  - Setting Perfect on a point in a longer segment splits it into a 3-point arc plus the rest.
  - With exactly one point selected, Tab cycles its type, including "inherit" (except on the head). Alt+1…5 picks Linear, Bézier, Perfect, B-spline or inherit.
- **Changing a type keeps the slider length** unless the new path is shorter, in which case the length is re-snapped.
- **Split** (right-click menu on red anchors): cuts the slider into separate sliders at each anchor. Each piece is pushed **100 ms** later than the one before, so you'll need to re-time them.
- **Convert to stream** (Ctrl+Shift+F):
  - Places circles every `beatLength ÷ divisor` from the start to the end, following repeats back and forth.
  - **Every circle gets the head's hitsounds**; edge sounds are dropped.
  - The first circle keeps the slider's new combo.

### Length, repeats and SV
| Gesture | Effect |
|---|---|
| Drag the **slider end** on the playfield | Changes length along the existing path, snapped (adding 1 px before rounding so the full length is easy to reach). The minimum is one snap step. Control points past the new end are removed when you release. |
| Shift + drag the end on the playfield | Keeps the duration and changes **SV** instead (length limited to 0.1–10× the old duration). Shift can be pressed or released mid-drag. |
| Drag the **end on the timeline** | Changes the **repeat count**: `round(new duration ÷ one span) − 1`, minimum 0. |
| Shift + drag the end on the timeline | Changes SV so the slider ends at the dragged time. |

**SV toolbox** (`OsuSliderVelocityToolboxGroup`, range 0.1–10×, step 0.01):
- **With sliders selected:** it shows and edits their SV.
- **With nothing selected:** it follows the **last slider at or before the current time**, unless you set a value by hand. "Use last slider's velocity" goes back to following.
- New sliders take the toolbox value.

### What gets saved
- **Each control point's type is written as-is:**
  - `L|`, `P|`, `C|`, `B|`, or `B4|` for degree-4 B-splines;
  - Bézier anchors are written as repeated points;
  - mixed types in one slider are allowed.
- **The length written is the snapped length**, not the full path. The edge hitsounds and banks come after it.
- **Stable can't read these files.** Mixed types and `B4` only work in lazer. The stable-compatible route is **Export → legacy (`LegacyBeatmapExporter`)**, which:
  - rounds every red line **down** to a whole ms and moves the green lines and objects in its section by the same amount, so they stay snapped;
  - rounds object times **down** and positions to the **nearest** whole number;
  - converts any slider with more than one segment, or a B-spline, to plain Bézier (`BezierConverter.ConvertToModernBezier`);
  - snaps grid size to one of 4, 8, 16 or 32.

## 12. Hitsounding in depth

Extends §6. Sources: `Audio/HitSampleInfo`, `Screens/Edit/Compose/Components/Timeline/SamplePointPiece`, `Screens/Edit/EditorBeatmapSkin`, `Screens/Edit/Components/FormSampleSet`, `Rulesets/Objects/HitObject`, `osu.Game.Rulesets.Osu/Objects/{Slider,Spinner}`, `Skinning/SkinnableSound`, `Rulesets/Edit/Checks`.

### What lazer stores for each sound (`HitSampleInfo`)
Each object carries a list of samples. Each sample has six fields:

| Field | Meaning | `.osu` equivalent |
|---|---|---|
| `Name` | hitnormal / hitwhistle / hitfinish / hitclap (taiko adds hitflourish) | hitSound bits |
| `Bank` | normal / soft / drum (taiko adds strong) | sampleSet / additionSet |
| `Suffix` | `"2"`, `"3"`, … or none | custom index ≥ 2 |
| `UseBeatmapSamples` | look in the map folder first | custom index ≥ 1 |
| `Volume` | 0–100 | volume |
| `EditorAutoBank` | addition bank follows the normal bank | additionSet = 0 |

- **Lookup order:** `Gameplay/{bank}-{name}{suffix}`, then `Gameplay/{bank}-{name}`, then `Gameplay/{name}`.
- **Two samples count as equal when name, bank, suffix and UseBeatmapSamples match.** Volume and auto-bank don't count.
- **"Index 1" and "index 0" are both stored with no suffix;** only the UseBeatmapSamples flag tells them apart. So index 1 means the map's un-numbered `soft-hitclap.wav`, and 0 means skin only (as in the earlier note).

### Volume floor
- Gameplay and editor playback both use **at least 5 % volume** (`MINIMUM_SAMPLE_VOLUME = 5`, applied as `max(volume, 5)`).
- A volume of 0 to 5 in the map plays as 5 %, so there are no fully silent hitsounds. Muting needs a silent custom sample.
- The editor's volume slider also starts at 5.

### Sounds that come from the object's own samples
| Sound | Derived from | Notes |
|---|---|---|
| `sliderslide` | the slider's body `hitnormal` (same bank, index and volume) | loops while held |
| `sliderwhistle` | the body's `hitwhistle`, **only if the body has whistle** | loops alongside the slide |
| `slidertick` | the body's `hitnormal` (or the first sample if there is no hitnormal) | every tick uses the same sample |
| Head / repeat / tail | the edge's own samples (`NodeSamples[i]`) | tail samples are played by the slider itself at its end time |
| `spinnerspin` | the spinner's **first** sample | looping |
| `spinnerbonus` | a new sample built from the spinner's `hitnormal` settings | `spinnerbonus-max` at full bonus |

- **When a map is loaded, the body samples are fixed once,** from the green line active at **slider start + 6 ms**. Each edge reads the green line at **edge + 5 ms** (`LegacyBeatmapDecoder.applySamples`).
- **There is no lookup at each tick's own time.** So in lazer, a green line placed partway through a slider body does **not** change the volume or bank of its ticks or slide.
- I didn't check whether stable behaves differently, so treat that comparison as open.
- **Slider body whistle:** whistle on the body makes `sliderwhistle` loop the whole time the slider is held.
- **Hitsounds play at an object's end time:** a circle plays at its start, a spinner plays only at its end.

### The timeline sample popover
Clicking the small label above an object on the timeline opens the popover. The label reads like `S:2 70`, meaning soft bank, index 2, volume 70.

- **Which objects it edits:**
  - If the object is **selected**, the popover edits **all selected objects**; otherwise only that one.
  - Each slider edge has its own piece (`NodeSamplePointPiece`) that edits just that edge, **unless several objects are selected**.
  - When several objects are selected, an edit applies to every one of their sample lists: body plus every edge.
- **What it contains:**
  - W/E/R toggles, a normal-bank and addition-bank dropdown (with Auto), sample-set buttons, and volume (5–100).
  - A field that differs across the selection shows "(multiple)".
  - Every change plays a preview through the beatmap skin.
- **Changing the normal bank** also changes any additions marked auto-bank, so they keep following it.
- **Adding an addition** (e.g. clap) copies bank, index and volume from an existing addition if there is one, otherwise from hitnormal.
- **Removing an addition** deletes that sample everywhere in the selection.
- **Addition bank "Auto"** sets the addition to the normal bank and marks it auto-bank. On save it's written as additionSet `0`.
- **Volume and sample set apply to every sample** in the list, normal and additions alike. You can't give one object different volumes for its normal sound and its additions.

### Custom sample sets (`EditorBeatmapSkin`, Setup → Resources)
- **Discovery:** the editor scans the map folder for `{normal|soft|drum}-{hitnormal|hitwhistle|hitfinish|hitclap}{N}.{wav|ogg|mp3}`.
  - No number means set 1.
  - A number of 2 or more means that set.
  - Set 1 always appears, even if empty.
  - Other files (sliderslide, slidertick, spinner sounds) aren't counted as sample sets.
- **Choices in the popover:** "User skin" (index 0), then "Custom #1", "Custom #2", and so on. With 20 or more sets the buttons become a dropdown.
- **Adding samples:** in Setup → Resources, pick a set (or "Add new…" with a chosen index). Each bank-and-sound slot then accepts an audio file.
  - The file is copied into the map set **renamed to `{bank}-{sound}{N}{ext}`**, with no number for set 1.
  - Removing it deletes the file from the map set.
  - New files are picked up without reloading.

### Hitsound checks in Verify
| Check | Rule |
|---|---|
| Few hitsounds | See §9. Samples count at object end times, including slider edges. |
| Muted objects | Only edge sounds count (ticks and slide are skipped). Maximum volume ≤ 5 % → **muted**. On a head or circle that's an "active" problem; on a tail or repeat it's "passive". Volume ≤ 20 % on a head → **low volume**. |
| Delayed hitsounds | Applies to files named exactly `{bank}-{hit…}` (slider and spinner sounds are skipped). The audio is read at 1 ms resolution. Silence counts as average amplitude < 0.001 after a 0.95-per-ms decay. **≥ 5 ms of leading silence**, or ≥ 5 ms before the peak → warning. ≥ 1 ms → negligible. |
| Hitsound format | Every audio file except the song itself must be **WAV or OGG**. MP3 hitsounds are flagged; a format BASS can't decode is also flagged. |
| Too-short audio | Any audio file shorter than **25 ms** is flagged, because some sound cards don't play it. |

### Practical takeaways for hitsounding in lazer
- **Volume is per object.** Lazer writes it to the green lines on save (§6), so a lazer-saved map gets a green line next to almost every hitsounded object. Expect big timing-point diffs when comparing against stable.
- **Use the popover on one slider edge** for edge-specific sounds. With several objects selected the popover edits every edge, so select a single slider first.
- **Slider ticks and slide are set at the slider's start.** To change a body's soft/normal or volume, change the slider's body sample, not a green line inside it.
- **Silence:** 0 % still plays at 5 %. For a truly silent note, use a custom set containing a blank (but not under-25 ms) file.

## Open questions

- Which client last saved *Catalyst*? This decides whether its green-line count comes from lazer's save behaviour (§6).
- The composers for taiko, catch and mania weren't traced.
- Clipboard and timestamp format: `EditorTimestampParser` accepts `mm:ss:mmm (1,2,3)`. Not checked in detail.
