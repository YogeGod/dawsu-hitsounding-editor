# Hitsound Difficulty Mode

## Problem Statement
How might we let a hitsounder build an osu!standard hitsound difficulty directly by placing sounds, with the necessary hit circles appearing immediately and normal Save writing the current difficulty?

## Recommended Direction
Add an explicit **HS mode** toggle, remembered per difficulty. Eligibility does not automatically enable it. Allow the toggle when the difficulty started with no gameplay objects, or its difficulty name (`Metadata.Version`) contains the standalone word `HS` or `hitsounds`, ignoring case. Examples: `HS`, `Hitsounds`, `Song HS`, and `Draft [Hitsounds]`. A substring inside a longer word, such as `HS2`, does not qualify. Remember that a difficulty started empty so creating and saving its first objects does not disable the feature on reload.

While enabled, placing a hitsound at a time without a suitable object creates a plain hit circle immediately. Multiple hitsounds at the same timestamp share one circle. Normal Save writes the objects and hitsounds into this difficulty, rather than creating a separate generated difficulty. Reuse Generate's mixing and priority controls when simultaneous layers cannot be represented directly on one circle; keep every input sound and its volume. A failed or cancelled mix must not leave a partially saved difficulty.

The mode manages the lifecycle of the circles it creates. Deleting the last hitsound attached to one removes that circle; deleting one layer from a shared circle keeps it. Moving notes, pasting, repeating, and MIDI import follow the same rule as drawing and painting. Reuse existing suitable objects without taking ownership of them. Existing objects must not be moved or deleted automatically. Each edit and its associated object changes form one Undo/Redo step.

## Alternatives Considered
- Manual per-difficulty toggle: chosen because the user controls when editing creates objects.
- Automatic activation by difficulty name: rejected; names establish eligibility only.
- A dedicated New HS difficulty command: potentially useful later, but not required for the toggle.
- Preview-only objects created on Save: rejected for the MVP because the requested objects should appear while editing.
- A shortcut to the existing Generate workflow: insufficient because Save must update this difficulty.

## Agreed Behavior
- Save into the current difficulty.
- Delete an automatically created circle when its last hitsound is deleted.
- Qualify initially empty difficulties or names containing standalone `HS`/`hitsounds`, case-insensitively.
- Keep an initially empty difficulty eligible after it acquires objects and is reopened.
- Apply object maintenance to all note-edit operations, not only drawing.
- Use Generate's mixing and priority controls when Save needs a mix.
- Protect preexisting objects and preserve the existing separate Generate workflow.

## Validated Behavior
- [x] Layers at the same time share a circle, including incompatible slots that require a rendered mix on Save.
- [x] Save uses the existing renderer and priorities while preserving unrelated sections and protected object geometry.
- [x] Exact-file-hash sidecar matching restores ownership and logical input layers after Save/reopen. External edits revoke old ownership; existing objects remain protected.
- [x] Object changes share Undo/Redo with backfill, drawing, moves, paste, repeat, quantize, range deletion and bulk note additions.

## MVP Scope
- [x] Add the per-difficulty HS toggle and explain why it is unavailable for an ineligible map.
- [x] Persist the toggle and initially-empty eligibility.
- [x] Create, reuse and remove owned circles as note edits require, keeping the object strip and conflict display current.
- [x] Preserve all layers at shared timestamps and show the existing mix controls only when needed for Save.
- [x] Make object and hitsound changes undoable together.
- [x] Preserve ordinary Save behavior when HS mode is off and the map has no managed HS state.
- [x] Add regressions for shared timestamps, last-layer deletion, moves between occupied/empty times, paste/repeat, bulk import, undo/redo, reload eligibility, protected existing objects, mix cancellation and failed Save.

Bulk import is covered through the shared edit-session note-addition API. This feature does not add a new MIDI importer to this checkout.

## Not Doing (and Why)
- Automatic enablement based only on a name: eligibility should not silently change editing behavior.
- General gameplay-object editing or conversion of arbitrary gameplay difficulties: this feature serves hitsound difficulty authoring.
- Automatic deletion or repositioning of preexisting objects: their layout belongs to the mapper.
- Removing or changing the separate Generate command: it remains useful for exporting a new difficulty.
- Writing files on each placement: object edits remain in memory until Save.

## Settled Details
- Enabling backfills already-unanchored notes as one undoable action.
- Off keeps owned circles. Re-enabling resumes maintenance, including removing owned circles whose notes were deleted while off.
- New circles are centred at `(256, 192)`.
- Ownership and logical layers are stored in the per-map editor project against the exact saved file hash. Protected objects can retain editable mixed input layers without becoming owned.
- File writes occur only on Save. Cancellation, failed rendering, stale edit plans and external map changes stop Save before map edits are written.

## Status
The user approved the direction and remaining lifecycle choices, then requested implementation before committing to online main. The feature is implemented and has model, editing, lossless-format, actual headless UI and rendered-audio-data checks. Physical listening, native file pickers and external osu!/Mapping Tools playback are not established by those checks.
