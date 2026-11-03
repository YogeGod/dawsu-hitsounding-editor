# Lane and folder dragging

Decisions confirmed during GrillMe on 2026-11-03. The agreed behavior is implemented in the lane model and grip gestures, with shared edit history.

- Dragging the dotted grip on a lane reorders that lane within its current folder. It cannot move into a different sample-set folder.
- Dragging a folder's grip moves the folder and all its contained lanes together, preserving their internal order.
- Each map remembers its own lane layout. Reopening that map restores both folder order and lane order.
- Ctrl+Z undoes lane and folder moves alongside note edits. Redo restores them. A completed drag is one history step; a click or a drop that changes nothing adds none.
- A drop indicator shows where the lane or folder will land.
- Reordering changes the visual layout. Sample files, sample-set assignments and note times remain unchanged.
- Folder collapse, preview, mute/solo, note selection/dragging, playhead snapping and hover highlighting remain available.

## Verification

Check individual and folder grip drags, folder boundaries, collapsed and filtered views, project save/reopen, and interleaved layout/note undo and redo. Include all contained lanes when moving a folder, even when some are hidden by a filter. Confirm the existing controls and note gestures still work.

## Implementation context

ArrangeView.cs handles grip gestures and drop feedback. ArrangeModel.cs applies lane and whole-folder moves. EditSession.cs keeps layout moves in chronological undo/redo, and ProjectStore.cs preserves folder and lane order in the map's project data.
