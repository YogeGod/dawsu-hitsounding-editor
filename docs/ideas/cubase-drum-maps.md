# Cubase-style Drum Maps

Confirmed concept, 2026-11-04. Implemented locally; see [verification](../feedback/2026-11-04-drum-maps-verification.md). Not committed or pushed.

## Problem Statement

How might we let osu! hitsounders edit by instrument name and freely arranged drum rows, while preserving the underlying sample assignments, existing hits and regular sample-family layout?

## Recommended Direction

Use Cubase's Drum Editor drum-sound list and Drum Map Setup as the functional reference. Add a toggle between the existing Sample view and a separate Drum-map view. The latter is a flat list without folders: every row has an editable instrument name, an assigned osu! sample lane and a freely movable position. Named drum maps are shared across difficulties within a mapset and can be saved and reused in other mapsets. Creating a new drum map means a new editing preset, not a new .osu difficulty.

Drum Map Setup provides New Map, New Copy, Rename, Remove, Save and Load. On loading a preset, ask: **Use the samples saved with this preset?** Use preset samples loads its actual saved audio and instrument layout. Use my own samples loads the names/order, keeps matching assignments from the current mapset and asks about missing assignments. Matching assignments do not prove audio identity; the user can check and change them. Existing sample files must be protected. Switching views or presets never moves existing hits.

Example rows: Kick (drum-hitnormal), Snare (soft-hitclap), T1–T4 (drum-hitclap2–5), Closed hi-hat (drum-hitwhistle2), Open hi-hat (drum-hitwhistle3), Crash L (soft-hitfinish), Crash R (normal-hitfinish). Chosen instrument rows come first; other samples remain underneath, also without folders. The normal sample-family layout retains its own names, order and grouping.

## Key Assumptions to Validate

- [ ] Instrument names and free ordering reduce navigation work. Compare editing the same short pattern in Sample view and Drum-map view.
- [ ] Users understand that aliases share one underlying lane. Show two differently named rows assigned to one sample, edit either, and verify that mirrored hits are understood and play only once.
- [ ] Reusing a layout with the current mapset's samples is predictable. Test a preset with matching, missing and differently sounding sample assignments; missing assignments must be explicit rather than guessed silently.
- [ ] View changes preserve the song. Compare hit timing, volumes, sample assignments and generated output before/after toggling, renaming and reordering.

## Editing Behavior: Preserve the Existing Editor

This feature is primarily an alternate UI over the same editor, not a new note-editing system. Select, Draw, Erase, Mute, Range, volume editing, snapping, dragging/moving hits, copy/paste, shortcuts, audition, Undo/Redo and export continue to use the existing behavior. Do not change these workflows just to resemble Cubase; Cubase's drum-sound list and map setup inform the presentation.

The UI changes are the Sample/Drum-map toggle, a flat folder-free instrument list, editable instrument names and sample assignments, freely reordered rows and drum-map presets. A row's position or alias does not create a new sound or alter the hit data. Mirrored rows address the same underlying sample lane. Existing editing actions must resolve visible rows to their actual assigned sample lanes, and operate on each underlying hit only once.

The previously agreed explicit reassignment action (assignment only versus moving existing hits, with preview/overlap choices/Undo) remains part of preset setup. It does not introduce different everyday note-editing semantics.

## MVP Scope

- Toggle Sample view / Drum-map view over the same editable hits and timeline.
- Create, duplicate, rename, remove, save and load named drum-map presets; share them across a mapset and reuse them in other mapsets.
- Edit instrument names and assigned samples; freely reorder all rows across Normal/Soft/Drum families with no folders.
- Show instrument names alone on drum-map lanes, per the later user clarification. Keep actual sample assignments visible in Drum Map Setup and Channel.
- Permit multiple instrument rows to reference the same sample lane. Their hits mirror; displaying or editing aliases must not create duplicate audio or duplicate edits.
- Load bundled actual audio or use the current mapset's samples, with the explicit choice described above.
- On reassignment, offer **change assignment only** or **move existing hits**. The latter affects only the currently open difficulty, previews affected hits and supports Undo.
- If the destination already has a hit at the same time, show the overlaps and require a choice before moving; do not silently merge or discard hits.
- Explain that moving source-lane hits affects what all aliases of that lane display: hits are owned by the sample lane, not by the alias through which they were entered.
- Preserve timing, volumes, existing export/pairing rules and the regular view. Renaming/reordering a drum row is editing convenience, not a sample-file rename or audio edit.

## Not Doing (and Why)

- **MIDI routing** — explicitly excluded by the user; do not add MIDI input/output notes, MIDI channels, devices or routing controls.
- Creating new .osu difficulties through Drum Map Setup — New Map creates a drum-map preset only.
- One instrument row triggering a multi-sample sound set — the first version maps each row to one sample lane; multiple aliases of that same lane are supported.
- Automatically moving existing hits when loading or switching a preset — migration is a separate explicit action.
- Moving hits in other difficulties — migration is limited to the current difficulty.
- Per-instrument quantize, notation and copying every Cubase column — outside the confirmed first version.
- The earlier general UI simplification project — cancelled by the user; no related app code was changed.

## Implementation Decisions

The user approved building after the concept interview and toggle-location screenshot. Presets persist in mapset-scoped app data, separate from per-difficulty note projects. Portable `.dawsudrum` presets contain optional actual audio snapshots. Assignment matching uses an exact existing lane first, then a unique matching sample slot; missing numbered assignments use the bundled family default for review. Unknown/ambiguous nonstandard assignments remain unassigned. Completely unassigned instruments are not guessed. Bundled audio is validated before review and only installed after acceptance, using protected new filenames.

Migration previews affected hit count and destination overlap times/sample lanes. Assignment-only is the default; moving requires choosing Keep both, Keep destination or Replace destination when overlaps exist. Multiple aliases continue to address one underlying sample lane. Channel sample replacement updates preset references to that replaced lane with the existing note-change Undo step.

The two Yoge starter kits use the exact screenshot names/order, with all samples initially unassigned as explicitly requested. Later clarification adds family-based numbered-layer creation in Setup, freely assignable to every instrument, default audio for imported missing numbered samples, and instrument-only lane labels. Pending new defaults are installed only after acceptance. No MIDI routing was added.

## Local Cubase Reference and Evidence Limits

- [Cubase 15 Drum Editor analysis](../../keysounding-docs/rea/cubase/cubase15-feature-analysis.md), section4: drum-sound list, instrument/sample-mapping concept, visibility options and setup actions.
- [Extracted Drum Map Setup UI](../../keysounding-docs/rea/cubase/srf/skin/skin.xml), template DrumMapSetup near line1509: newMap, copyMap, deleteMap, saveMap, loadMap, drumMapList and drumMapView.
- [Extracted editor layout](../../keysounding-docs/rea/cubase/srf/skin/editors.xml), DrumEditorMainView and DrumMainView: editor zones and timeline container.

The reference establishes the available controls and structure. The local analysis explicitly leaves native grid interactions and exact I-Note/O-Note playback behavior unverified. Our osu! sample assignments are an adaptation; no claim of an exact native Cubase remapping reproduction is made, and MIDI routing is excluded.
