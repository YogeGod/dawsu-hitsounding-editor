# Hitsound Editor

A standalone editor for arranging osu! hitsounds in sample lanes.

## Language

**Lane**:
A row representing one sample sound, together with its hits on the timeline.

**Lane audio**:
The sound a lane plays, as bytes: read from the mapset folder for file lanes, from the default skin for skin lanes. A file that is absent, unreadable as content, or empty counts as missing.
_Avoid_: Sample source

**Editor project**:
What the editor keeps for one map that a .osu can't hold: lane layout, hits with no object, sound-set links, HS-mode state, locators, export settings, song offset and time signatures. Stored in the editor's app data, never in the mapset.

**Folder**:
A sample-set group containing its lanes in the editing window. It is a visual group, not a directory on disk.
_Avoid_: Disk folder

**Lane layout**:
The visual order of folders and the lanes within them for one map.
_Avoid_: Note timing, sample assignment

**Keysounding**:
A separate editor mode for arranging pitched musical notes and turning their instrument sounds into samples assigned to an osu! beatmap. Editable notes and instrument settings remain part of the project.

### Keys

**Key preset**:
One of the editor's built-in starting keymaps, DAW-like (read from the user's Cubase key commands) or osu!. Each keeps its own key changes. There are no user-named presets.
_Avoid_: Keymap, profile, scheme

**Key change**:
A user's edit to a command's keys within one key preset. It covers assigning, removing or clearing a key, and the key a command loses when another command takes it.
_Avoid_: Override, binding

**Customized command**:
A command whose keys differ from what its key preset alone would give it, whoever caused the difference.
