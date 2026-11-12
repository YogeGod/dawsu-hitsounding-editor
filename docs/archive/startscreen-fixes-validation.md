# Start-screen fixes and flat sample lanes

Base: main a69e4d16b1e3eec19edc887de3eaedce6a770a26.

## Changes
- Start-screen open/import feedback is visible in the overlay, and its content scrolls down to Preferences/Shortcuts at 900 x 500.
- Open accepts .osu and .osz; archives use the existing safe importer.
- Enter applies numeric volume using the same action and scope as Apply.
- Edit samples updates Active lane and enables default-sample audition without a beatmap. Replacement clearly requires opening a map.
- Sample view has no Normal/Soft/Drum/Own-files folder rows. Lane families can move across banks; numbered layers stay with their parent. Old collapsed sample-set flags cannot hide lanes.
- Saved lane order, Undo/Redo, filtering and family collapse remain supported.

## Validation
- Unit/corpus suite: 636 passed, 0 failed (234 App, 52 Format, 350 Lanes); real-map corpus is read through the existing 2,932-map corpus fixture.
- WorkflowSafety: 89 checks passed.
- StartScreenSafety: 29 checks passed, including layout at 1920x1030, 1040x720 and 900x500; failed open, invalid/valid OSZ, recent open, no-map lane selection, Enter volume and scope/Undo.
- Windows self-contained Release publish succeeded. Existing platform audio warnings and pre-existing unawaited Save handler warning remain.
- Native Windows checks on this build: start screen entry, Edit samples, lane selection/inspector update, flat lanes, native picker filter *.osu;*.osz, disposable-map open, cross-bank lane drag and Undo, Enter volume and Undo.
- Native audio output was not independently listened to. Small-window reachability and OSZ extraction were verified by headless application checks; native picker acceptance was inspected.
- Only disposable test maps and their generated working copies were edited.

## Simplify
Removed unused test variable/debug captures and updated stale lane-folder instructions. Reused existing volume, import and preview actions; retained legacy folder-state readers for project compatibility.

## Correctness
No remaining findings in changed code after checking native/import routing, null document preview state, family boundary moves, saved order and Undo.

## Standards
No blocking findings. Scoped production edits and regression tools; no unrelated application features changed.

## Spec
Five confirmed audit bugs fixed, plus removal of sample-set lane folders and cross-bank family ordering. No remaining requirements identified. Numbered-layer families remain intact.

Findings: Correctness 0; Standards 0; Spec 0.

## Security review
No exploitable findings in the changed paths. Checked archive routing against existing path/size/entry limits and fresh-folder extraction, no-map editing guards, file assignment, dependencies, command construction and secrets. No new production dependencies or network behavior.