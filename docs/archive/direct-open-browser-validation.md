# Direct beatmap opening, left browser and Recent projects

Base: aef0850875ce7262b141518a2df8c1da7dd9df0c (V1.5).

- Opening .osu edits the selected file directly. Removed MapsetWorkspace and its four tests for automatic copying. No Songs folder is needed for ordinary .osu Open; IDs and source bytes are unchanged by opening. Existing copies are not deleted. Save-time backups and .osz extraction remain.
- Browser moved to the left of the inspector and timeline. Folder expansion, audition, assignment, dragging, resizing and hide/show remain; layout column references and guidance were updated.
- Reproduced an empty first-click Recent popup in Windows. Entries are now prepared before the flyout opens, and unchanged recent paths keep their existing menu items.

## Validation
- 632 unit/corpus tests passed: 234 App, 52 Format, 346 Lanes. Four tests for the removed copier were deleted.
- WorkflowSafety: 89 checks passed. StartScreenSafety: 42 checks passed, covering direct path/metadata/no-folder behavior, Recent popup, OSZ import and browser placement at 1920x1030, 1040x720 and 900x500.
- Native Windows: reproduced the old empty Recent popup; fixed build displays entries on first click and opens the chosen test difficulty. Inspected browser on left. Opened a disposable .osu with normal preferences enabled: original E: path shown, original IDs retained, Songs directory listing unchanged.
- Windows self-contained Release publish succeeded. Existing platform audio warnings and existing unawaited Save callback warning remain. Physical audio/ASIO and live osu! gameplay were not independently verified.

## Simplify
Deleted the unused copier rather than retaining a toggle; reused MapDocument.Load, current Recent events and browser controls. Updated stale copy/right-sidebar guidance.

## Correctness
No remaining findings after checking direct-path loading, project restoration paths, save destination, Recent menu initialization and layout column references.

## Standards
No blocking findings; changes are scoped to the requested removals, layout move and confirmed Recent bug.

## Spec
All three user requests are implemented. Save-time backups remain separate from automatic folders on Open.

Findings: Correctness 0; Standards 0; Spec 0.

## Security review
No exploitable findings. Checked removed copy/extraction behavior, original-file save paths, stale recent paths, menu callbacks and file-browser assignment. Existing save conflict detection/backups and safe OSZ extraction remain. No new production dependencies, commands or network behavior.