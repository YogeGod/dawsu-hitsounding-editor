# V6.39 extra lanes and preset defaults — 2026-11-04

Implemented locally on feature/electron-lane-grouping, preserving the preceding uncommitted drum-map work. No commit/push performed.

## Reference behavior

Verified directly against `E:/Codex-releases/dawsu!/source-v6.39/src/sample-groups.js`: `addLayer` picks the next family index and `row` assigns `osu-default:` plus the unnumbered family WAV. No existing events are copied. `src/app.js:addSampleLayer` creates, reveals and activates the row without a picker. Its header Add lane uses the active standard family. The reference test `test/sample-groups.test.cjs` also verifies that numbered layers have bundled audio available for export.

## Changes

- Per-lane +, header + and Map → Add lane create the next numbered lane immediately when a standard family is selected. Custom nonstandard lanes retain the existing mapset-file picker.
- New lanes start empty and use exact bundled default family audio. Selecting a custom parent does not clone that custom audio. Channel can replace the new sample as before.
- Reuses SampleSlots.Next and BackingFile. Audio is installed with CreateNew into a protected fresh backing filename, avoiding overwrite and protecting another difficulty's missing-index skin fallback. Displayed editor slots remain independent of backing filenames.
- Preset import keeps matching own assignments; missing supported numbered samples are offered with family defaults. Acceptance installs their audio safely, deduplicated for aliases. Unassigned instruments remain unassigned. Cancellation writes no sample files.
- Drum Map Setup → Edit rows has a family selector and Add numbered layer. Every added layer is immediately available in every instrument's Assigned sample dropdown, across families. Pending defaults are materialized only after accepted setup/migration review.
- Setup numbering follows logical editor slots, even when a protected backing file has a larger index.
- Instrument lanes show instrument names only; the sample in parentheses was removed. Assignment details remain in Setup and Channel.
- Preset snapshots retain their requested slot when installed under a different safe backing name. Explicit hit migration resolves provisional destinations to installed lanes before moving.

## Verification

- Full solution: **597 tests passed**: 52 Format, 327 Lanes, 218 App. Log `artifacts/extra-lanes/tests.txt`. Final App rerun after the last dialog-numbering change: **218 passed**, log `app-final-tests.txt`.
- Expanded headless drum-map/extra-layer workflow: **69 passed**. Actual lane/header +, no picker, empty hits, exact default bytes, decoding, peer-index protection, Undo/Redo, deferred/cancelled import and Setup, aliases, free cross-family assignment, logical numbering, instrument-only labels, Generate planning and reopening. Log `artifacts/extra-lanes/ui-results.txt`.
- Existing feedback workflows: **58 passed**. Save, Generate, sound sets, copy/paste and Revert. Log `existing-ui-results.txt`.
- Existing Generate workflow: **16 passed**, including isolated real-map protection, optional slider audio, cancellation and planning failures. Log `generate-ui-results.txt`.
- Total workflow checks: **143**. Source .osu files and protected peer difficulty were unchanged by setup/import operations.
- Screenshots inspected: `artifacts/extra-lanes/extra-layer-assignments.png`, `instrument-labels-after.png`. Before/after add screenshots: `before-add-layer.png`, `after-add-layer.png`.

Commands use `-p:BaseOutputPath=bin/extra-lanes/`. The initial attempt to reuse bin/drum-maps failed with MSB3021/MSB3027 because the user's running editor (PID62808) locked its EXE. That editor was left open; all successful verification used the separate output folder.

Local executable: `src/OsuHitsound.App/bin/extra-lanes/Debug/net10.0/win-x64/OsuHitsoundEditor.exe`. It uses the installed .NET runtime; this is not a packaged release.

## Correctness

Reviewed deferred installation, cancellation, alias deduplication, source/target remapping for migration, existing file protection, slot persistence and no-hit lane creation. Fixed Setup numbering jumping to a protected backing-file index; the UI regression verifies slot3 after slot2 despite a larger actual filename. No outstanding concrete findings.

## Standards

Inline simplify/review. Reused the current numbered-slot allocation, layout Undo, Channel replacement, bundled audio and protected snapshot installer. Removed the previous per-lane picker path rather than maintaining two competing add-layer flows. No new packages, audio engine or routing controls. No outstanding findings.

## Spec

Compared with verified V6.39 source and the user's follow-ups: default population on import, freely assignable new layers and instrument-only labels are covered. Local adaptation installs protected audio when accepting a new layer rather than waiting for export. Files are retained conservatively after Undo, consistent with existing sample imports. No outstanding implementation gaps identified.

Review counts: Correctness 0 outstanding (1 fixed), Standards 0, Spec 0.

## Security

Checked imported preset validation before default installation, bounded existing import paths, trusted bundled resources, canonical generated filenames, FileMode.CreateNew and safe index allocation. No shell input, network access, new dependencies or overwriting of source samples. No concrete exploitable findings found.

Manual limits: hardware audio listening, native file pickers, screen-reader behavior and live osu! gameplay were not tested. Decoding and headless editing/export planning do not prove live playback. Imported/default audio files remain after Undo or a later failure; existing samples are protected.
