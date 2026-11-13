# Changelog

Public releases of the C# dawsu! editor, newest first. Each version's notes are also in [docs/releases](docs/releases). Earlier same-day preview builds (`csharp-preview-2026.10.30` through `csharp-preview-2026.11.03.5`) stay on the [GitHub releases](https://github.com/YogeGod/dawsu-hitsounding-editor/releases) page. The Electron app's last release was dawsu! V6.39.

## 3.1.3

Saves no longer write volumes below 5 %.

- **Volume floor on save:** a hit the save changes is written at 5 % volume or more, on the object or on its green line. osu! plays lower volumes, 0 % included, at its own 5 %/8 % floor anyway, so they now save as 5 %. Hits the save leaves alone keep their volume, even below 5 %.

## 3.1.2

Menu items work with one click again.

- **Menus:** one click on a menu item runs it again. Since 3.1, the first click on an item (Open map…, Preferences… and the rest) could be lost, so it took two clicks, and the window could maximise or restore as well.

## 3.1.1

Switch between a mapset's difficulties from the ribbon.

- **Difficulty list** on the ribbon, next to Open: switches to another difficulty of the open mapset, by its osu! difficulty name, and asks first if the open one has unsaved edits. While a map is open, Ctrl+O drops the list down; with no map open (or in the Keysound editor), Ctrl+O still opens a file.

## 3.1

A refreshed interface for both editors, a live map preview, and drum hit detection without the neural model.

- **Interface refresh** for both editors:
  - **One bar on top:** the menu bar is also the window's title bar. It carries Save (Save .osu, or Save project in the Keysound editor) and the minimise, maximise and close buttons.
  - **Ribbon tiles:** every ribbon button shows its icon over its name. Switches that are on are tinted with an underline; the current tool is solid. Files and Inspector sit alone at the right end.
  - **Panels:** the file browser is at the far left, with the inspector beside the lanes.
  - **Controls moved next to what they affect:** Fit beside the time zoom, "Used only" and lane info beside the lane filter, and the bottom panel (now "Volumes" / "Velocities") folds to its header from an arrow.
  - **Sample-set folders:** lanes sit under a header per sample set (Normal, Soft, Drum) with the set's M and S. Hitnormal comes first with its additions indented under it, lane names drop the set prefix, and a header can be dragged to reorder the sets. A lane now moves only within its own set.
  - **Slate colour scheme:** a fifth scheme in Preferences → Colours and the new default, with blue-slate panels that differ in shade by area, rounder controls and every other bar shaded in the grid. A scheme you already picked stays.
  - **Icons and font:** hit-circle style icons on the ribbon (not the tools), and the transport (the lane sound icons are redrawn and sit before the lane name; the row buttons are larger and further apart); the interface font is Nunito, semibold. Sliders and scrollbars are thin and flat, and scrollbars no longer grow under the pointer. The lane rows lose their ● preview button. In wide windows Cycle, Return and Follow are labelled; the title shows the file as a chip with its mode; Snap to and Spacing are captioned; object lines are gold.
  - **Menus:** rounded, and switches that are on use the accent colour.
  - **Smaller touches:** Play is always green, the time display is divided, Global and Song share a line in wide windows, the inspector's two-way choices and the file browser's mode are buttons instead of drop-downs, the lane header has a two-way Samples | Drum map switch, and the bottom panel's hint shows its keys as chips.
- **Map preview** (the ribbon's Map preview button, or View → Map preview): a window that shows the map's circles, sliders and spinners at the playhead, as osu!lazer's editor shows them with its default (Argon) skin, with approach circles, slider balls, ticks, reverse arrows and stacking. With lazer installed it uses lazer's own approach circle and number font; without it, plain stand-ins. A **Skin** list in the preview window switches between lazer's Argon, lazer's Classic, and every skin in osu!'s Skins folder (found beside your Songs folder; **Skins folder…** picks another). User skins are drawn as lazer draws legacy skins, with their skin.ini colours, number font and slider settings, and fall back to Classic for images they leave out. It follows playback and every move of the playhead, can be resized or moved to another screen, and opens again where you left it. osu!standard maps only; circles created in HS mode are not shown.
- **Drum hit detection** now uses signal processing instead of the ADTOF neural model. It no longer needs a model file or ONNX Runtime, so the installer is about 8 MB smaller. Kicks are found about as well as before; fewer snares are found, especially snares played together with kicks, and dense full mixes can give extra hits, so listen before applying.
- **Beat grid** setting for drum hit detection (1/4 by default; also 1/4 + 1/3, 1/8, 1/8 + 1/6 or Off). Hits off the map's beat divisions are dropped and the rest snap onto them. A steady offset between the song and the map's timing is measured and allowed for.
- **Keysound editor** now looks and works like the Hitsound editor: ribbon, inspector Selection and Bookmarks cards, overview strip, scrollbars with zoom ends, velocity panel, L/R locator button and the same menus. It gains the Range tool with Edit range, and Scale by velocity. Keysound projects keep their timeline bookmarks.
- **Drum VST host:** dragging a plug-in's window, or opening its menus and file dialogs, no longer leaves the host unresponsive. A plug-in that outputs invalid audio is silenced instead of spoiling the mix, and a plug-in state that cannot be read is reported instead of saved empty.

## 3.0

A clearer Hitsound workspace, with controls on the ribbon, selection editing in the inspector, and colours you can customise.

- **Compact labelled ribbon:** Open, HS mode, History, tools, Grid, Drums and View stay in one row. Labels shorten as the window narrows. Inspector, Lane info, Used lanes, Object lines and Controller are directly accessible.
- **Selection card:** edit the selected hits' start time and volume from the inspector, with Quantize, Duplicate and Delete beside them. The old selection bar gives its height back to the lanes.
- **Clearer menus:** icons and shortcut chips, switches for on/off settings, and inline buttons for Export style, Start playing from and Speed.
- **Scrollbar zoom:** time zoom at the end of the horizontal scrollbar, lane height at the bottom of the vertical scrollbar. L/R beside Cycle opens the locator controls in windows at least 1000 px wide.
- **Save warning badge:** when hits have no gameplay object or cannot be saved as drawn, clicking the ribbon's Save button lists the problems first. Ctrl+S and File → Save keep saving directly through the normal checks.
- **Preferences → Colours:** four default schemes (Grey, Dark, Blue-grey dark and Teal dark), plus accent, grid and ruler colours, with live preview, Apply, Cancel and Defaults. These replace the old dark-mode button.
- **Cubase-style Hitsound editing:**
  - Right-click opens a tool picker; it no longer deletes hits. Right-click deletion is also removed from the Keysound piano roll.
  - With Select, double-click empty space to insert a hit.
  - Up/Down moves selected hits between lanes. Shift+Up/Down still extends the selection; Left/Right selects the previous/next hit across lanes.
  - Volume bars follow the selected hits' lanes, or the active lane when nothing is selected. The All/Selected lanes setting still controls numeric volume changes.
- **Safer writes:** Save, Generate and green-line cleanup check that the difficulty still contains the bytes they reviewed when replacing it, protecting edits made by another program.
- **Start screen:** the Songs folder has its own line, so the F1 button no longer covers its path.

**Windows x64:** run `dawsu-win-Setup.exe` to install or update. Or extract `dawsu-win-Portable.zip` and run `dawsu!.exe`. Existing installs can use **File → Check for updates…**. Version: **3.0.0**.

## 2.2.12

Saving is safer: dawsu! never changes a line of your .osu that you didn't edit, and it shows you what a write will change before it happens.

- **Copy hitsounds** works out every difficulty first and writes nothing until you confirm:
  - A summary shows each difficulty with what will change. Expand a row to see the changed lines.
  - A difficulty with conflicts starts unticked ("Write anyway: these moments keep their old sounds").
  - The ticked difficulties are written together. If one can't be written, the others are put back as they were and any new samples are removed.
  - New option **Never create silent hitnormals**: a hit point keeps its own sound instead of getting a silent custom hitnormal, including points the source has nothing for.
- **Save .osu** saves straight away when only hitsounds change, and the status bar says what changed ("Saved this difficulty (3 objects, 1 green line added)"). If a save would change anything else in the file, it lists those lines and asks first.
- **Undo and redo keep working after you save.** Undoing past a save brings those edits back as unsaved changes.
- **Generate** and **File → New hitsound diff** keep the source difficulty's storyboard, colours, green lines (kiai and slider velocity), line endings and file encoding. Generate also shows what changes in an existing [Hitsounds] diff.
- **Save** removes the sample files it made in an earlier save once nothing in the mapset uses them, now with paired samples too. It lists them first. A file you changed, or that another difficulty uses, is kept.
- The mix files dawsu! makes itself (such as `normal-hitnormal3.wav`) no longer show up as empty lanes.
- **Map → Clean up green lines…** removes green lines that change nothing (same sample set, index, volume, slider velocity and kiai as already in force). It lists them first and keeps a backup. Nothing plays differently.
- **Alt+Space** moves the playhead to the selected note, or to the first one of a selection.
- Backups keep the newest 20 per difficulty. **Map → Open backups folder** opens them.
- Fixes:
  - Copy hitsounds' green lines for slider ticks no longer switch to the Soft sample set after a line without a sample set, and no longer carry the "omit first bar line" flag.
  - Two backups made in the same millisecond no longer overwrite each other.

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
