# Workflow safety validation

The five workflow changes cover explicit Save/Discard/Cancel on close, Save and Help from text fields, sample-replacement scope, shared volume scope, and navigation/explanations for output problems.

## Validation

- Full solution: 613 tests passed (226 app, 52 format, 335 lanes), including the local 2,999-file map corpus.
- Disposable Avalonia headless fixtures: 49 checks for close choices, repeated close, Escape and dialog cancellation, successful/failed/cancelled saves, external file changes, discard and reopen, text-field shortcuts, sample assignment versus preview, output explanations, and problem navigation.
- Previous workflow regression harness: 61 checks passed.
- Compact layout regression harness: 69 checks passed.
- Rendered the editor at 900×500, 1366×768 and 1600×900. Checked the visible shared-volume label, problem action and footer bounds; inspected the compact screenshot.

Run the retained safety harness on Windows with:

```powershell
dotnet run --project tools/WorkflowSafety -- artifacts/workflow-safety
```

It creates unique disposable maps under the supplied directory and uses the editor's normal AppData project and backup stores. It starts no audio device and does not open the user's maps. This validation does not establish live audio or hardware behavior.

## Simplification

Reused existing compile conflicts, lane reveal/layout, selection, seeking, project persistence and save dialogs. Extracted project capture once to retain the loaded baseline for discard. Kept ordinary sample assignment and preview gestures.

## Correctness

No outstanding findings. Save now reports success explicitly. A failed or cancelled save cannot authorize closing. Discard replaces autosaved loose-hit, HS and sound-set state with the loaded baseline while retaining view preferences. Dialog X and Escape cancel; repeated close cannot implicitly discard.

## Standards

No outstanding findings. Reviewed inline against the workspace instructions. Changes use the existing partial-window and dialog patterns. No production dependencies were added; the optional headless harness uses Avalonia packages already used by the snapshot tooling.

## Spec

No outstanding findings against the five approved critique recommendations. Sample assignment remains undoable, volume semantics are unchanged, and navigation changes no hit or volume data.

Finding counts: correctness 0; standards 0; spec 0.

## Security review

No exploitable findings in the changed paths. Checked displayed filenames and lane names, existing sample activation paths, project persistence, dependencies and process execution. The changes add no shell commands, network requests, archive extraction or credential handling to the app.

## Release state

V1.4 remains paused. This change does not publish a release.
