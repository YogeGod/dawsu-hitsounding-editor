# Changelog

Public releases of the C# dawsu! editor, newest first. Each version's notes are also in [docs/releases](docs/releases). Earlier same-day preview builds (`csharp-preview-2026.10.30` through `csharp-preview-2026.11.03.5`) stay on the [GitHub releases](https://github.com/YogeGod/dawsu-hitsounding-editor/releases) page. The Electron app's last release was dawsu! V6.39.

## 2.2.11

- **Edit → Drum hit detection…** now works in every install. It finds kicks and snares on your PC with the [ADTOF](https://github.com/MZehren/ADTOF) drum model, with no Python or extra download. Before, it only worked on a PC with a local Python detector and said "not configured" everywhere else. It is no longer labelled "test build only".
- The installer is about 8 MB bigger, for the drum model and ONNX Runtime.

## 2.2.10

- **Drum hit detection** shows times with a decimal point (`0.543 s`) on every PC. Before, PCs set to a comma locale showed `0,543 s` in the review list and the selected-range label.

## 2.2.9

- **F1** and **Preferences → Keys** group commands by feature area, such as "Hitsound – Hit editing" and "Keysound – Note editing". In F1, a command both editors run (Undo, Play, zoom…) is listed under each mode's area. In Preferences → Keys it is listed once, its details name the Keysound area too, and searching for that area finds it.
- Menu renames:
  - **File → Open…** is now **Open map…**.
  - **File → Save** is now **Save .osu**.
  - **Edit → Drum Hits auto-fill (test build only)…** is now **Drum hit detection (test build only)…**.
- The README and the tutorial are organised by the same feature areas.
- Fixes:
  - Saving a map with older slider lines keeps the whistle, finish and clap on every slider node. Before, nodes with no edge sounds of their own were saved as plain hits.
  - Saving refuses hits that fail its own "came out as" check, the same way it refuses other conflicts. Before, some of them could be written after **Copy hitsounds**.
  - Preferences and projects are written to disk before they replace the old file. A file that can't be read is kept as a `.unreadable` copy instead of being reset to defaults.
  - VST instruments no longer leave notes hanging after playback stalls.
  - Adding circles to very long maps is faster.

## 2.2.8

- **Preferences → Keys** has a filter next to search: All, Assigned, Customized or Unassigned, with counts. Commands whose keys differ from the preset are marked, and hovering one shows the preset's key.
- **Import…** and **Export…** in **Preferences → Keys** save a preset's keys to a `.dawsukeys` file and load them back, on any PC, with or without Cubase.
- **Edit → Drum Hits auto-fill (test build only)…** is a kick and snare detection prototype. It needs a local detector and does not work in this release.

## 2.2.7

- Save and Generate check all sample files used in their plan before writing, so changed files are caught.
- Sound-set restore warnings stay visible after opening a map. A failed open no longer leaves the previous map's project active.
- Cancelling Generate clears the planning message. Switching maps during Generate no longer reports changed edits on the new map.
- osu!'s editor still reloads a saved map if reopening it in dawsu! fails.
- Lane audio, project settings, and Save/Generate now use shared internal modules.

## 2.2.6

- A circle that plays one sample file by name can be changed back to other sounds. Before, Save reported a conflict and kept the file.
- A hit on a stacked circle stays on its own object when you move it a little.
- Live reload puts unsaved hits back within the same 2 ms window that editing uses.
- Deleting a lane keeps protected hitnormals, and lane previews play at playback volume.
- Adding a circle or bookmarks works in maps with an unreadable object line, or that end at `[Editor]` without a newline.

## 2.2.5

- The public tutorial and changelog now match the current editor. Screenshots of older editor versions were removed from the repository.
- No change to editing, playback, or export from 2.2.4.

## 2.2.4

- Draw tool uses a drumstick icon and cursor.
- Collapsing the selection details gives that space back, leaving a small expand handle in both editors.
- Moving hits to another lane plays that lane's sample, with the mouse or the keyboard, when Audition is on.
- **Objects** in the snap menu aligns hits with circles, slider nodes and spinner ends, including ones off the beat grid.
- **Insert volume** presets the volume of newly drawn hits. Clear it to follow the map volume.
- **Edit → Protect hitnormals from deletion** keeps base hitnormals when deleting, cutting, erasing or clearing a range. Off by default.

## 2.2.3

- **File → Open map from osu!** opens the map osu! has open (un!stable directly, osu!stable from the open map, osu!lazer after Edit externally).
- Live reload when osu!'s editor saves: zoom, scroll, playhead, lane layout and unsaved hitsound edits stay. A hit whose object was deleted stays, with no object.
- **File → Reload osu!'s editor after Save** makes osu!'s editor show the save. Unsaved work in osu!'s editor is lost when it reloads.

## 2.2.2

- Drum maps can be built from a drum VST's own mapping, including GGD exports and kits that report note names. Chokes are optional. Lanes can play through the VST or be rendered to WAVs.
- Typing in dialog lists jumps to the next matching name.
- The start screen and the editor top bar can download an update and restart.
- Bookmark labels show beside their flags on the ruler.
- A long map name ends with "…" instead of adding a scrollbar.

## 2.2.1

- Older slider lines that stop early are written in full when edge hitsounds are added, so Mapset Verifier can open the mapset.
- Silent sounds dawsu! writes are the 44-byte `blank.wav`.
- A sample file with no audio counts as silence instead of failing Save.

## 2.2.0

- Keysound tracks and the hitsound drum instrument can use a SoundFont (`.sf2`) preset, including the SoundFont's reverb and chorus. Generate and drum WAV export use the same preset. The `.sf2` stays where it is on this computer.

## 2.1.0

- Installer (`dawsu-win-Setup.exe`) for the current Windows user, with in-app updates. The app is `dawsu!.exe`. Unzipped V2.0 folders are not upgraded in place.
- First launch asks for DAW-like keys or osu! editor controls.
- Keysound editor matches the Hitsound editor's toolbar, selection bar, bottom bar and themes.
- Musical typing (Alt+K) plays and records the selected track from the computer keyboard.
- House button, or File → Start screen, returns to the start screen.

## 2.0.1

- The lane-list splitter no longer steals clicks at the start of the grid, on the ruler or on ‹. Width is saved, and double-click restores the default.

## 2.0.0

- Keysound mode: a key editor with the bundled piano or a VST3 per track, each plug-in in its own process. Import MIDI, record from a MIDI keyboard, sync notes to gameplay objects, generate a `[Keysounds]` difficulty and export `.osz`.
- Hitsound editor: the V2 window, volume-coloured hits, gameplay objects on the waveform, Cubase-style editing, slider-body sound editing, and live Drums VST playback with WAV export.
- Save no longer writes hidden copies of hitnormals. A green line within 5 ms after an object is the one that changes.

## 1.5.0

- Preferences chooses the Classic file browser or the Folder tree sidebar.
- Opening a beatmap edits that file directly. The file browser is on the left. The empty first-click Recent projects menu is fixed.
- Start screen with Open, Recent projects and sample auditioning.
- Flat sample lanes (no sample-set folders), multi-lane mute/solo, native osu! sample combinations before mixing, a right-side sample browser, the Grab tool, optional panels and editor-only bookmark labels.

## 1.4.0

- Unsaved close offers Save, Discard or Cancel.
- Ctrl+S and F1 work while a text field is focused.
- Clearer sample browser, shared-volume control, Next problem, and compact-window popovers.

## 1.3.0

- Drum-map view with Yoge Expanded Kit and Yoge Simple Kit, portable `.dawsudrum` presets, and extra numbered layers.
- New numbered lanes start empty with the bundled family sound.
- Hitsounds-only Generate by default, reusable sound sets, long 2/1–4/1 snapping, and role-preserving hitsound pairing.

## 1.2.0

Portable package of commit `5b3dc95`. The GitHub release notes do not list separate changes; 1.3.0 is the notes for that work.

## 1.1.0

- HS difficulty mode for empty difficulties and difficulties named HS or hitsounds.
- Layer volumes survive mixing, undo and save. Sample replacement, revert and simultaneous copy fixes.

## 1.0.0

First public dawsu! hitsound studio build (`csharp-preview-2026.11.03.6`), 2026-11-03. The same-day previews before it are on GitHub and are not repeated here.
