# Electron sample grouping in the C# editor

This ports the grouping behavior from the shipped V6.39 Parity Electron build into dawsu! hitsound studio 1.1. The local `V6.39` Git tag points to older code; the reference is the source shipped with the public V6.39 Parity archive.

- Sampleset folders default to Normal, Soft, Drum. Hitnormal stays first; addition families are indented beneath it.
- Each family has one parent: file index 1 when present, otherwise skin index 0, otherwise the first layer. Skin sounds remain separate from file index 1 sounds.
- Collapsed folders and families show dim note previews. Clicking a preview reveals its source lane without placing or deleting a note. Filtering shows matching layers with their parent context.
- Dragging between addition families moves the entire family. Dragging within a family reorders child layers. Parents remain first, and hitnormal remains above additions. Folder order remains movable.
- Adding a layer uses the next index in its own sampleset and sound family. Assigning different audio preserves the lane's editor slot, name, family and position. Both actions support undo and redo.

The editor stores slot identity in optional `LaneSlots` project metadata, separately from the actual sample filename. Importing audio never overwrites existing files. If a preferred filename or index could affect another map, it uses an isolated backing index while retaining the requested editor slot. The inspector shows both identities when they differ. Existing export logic uses the actual backing file to play the assigned audio correctly.

Saving and reopening retain slot metadata. Reopening or reverting unsaved map edits restores the saved map's audio; an unused replacement for an occupied slot stays hidden. Imported files are retained when an add or assignment is undone.

Verification covers grouping, skin/file separation, parent selection, filtering, family reordering, undo/redo, metadata reload, per-family numbering, extension collisions, exhausted indexes and shared-map fallbacks. Headless window checks exercise actual preview clicks and grip dragging, the sample import paths, saving and reopening, shared-file preservation, and the existing HS toggle and mixed-audio regressions. Physical audio-device playback remains a separate manual check.
