# Tutorial

A tour of dawsu! 3.1.1 in Hitsound mode, grouped by **feature area** (the same groups as the F1 help). **File → Full interface tour** walks through the same features in screen order. Next and Back (or the arrow keys) step through that tour, and Esc ends it.

The first launch asks whether you want **DAW-like keys** or **osu! editor controls**, then runs **Quick start** (also under **File → Quick start**). Quick start covers opening a difficulty, choosing a lane, assigning a sample, placing a hit, listening, and choosing Save, Generate or Copy. The house button next to the dawsu! name, or **File → Start screen**, goes back to the start screen and asks to save first.

The top bar has the menus (File, Edit, Playback, Map and View) and the name of the `.osu` you're editing. Hover the name for its path. A long name ends with "…" instead of putting a scrollbar over the bar.

## Hitsound – Map files

- **Open map…** (Ctrl+O): pick a `.osu`, or drop a `.osu` or `.osz` on the window. Each sample file in the mapset becomes a lane.
- **Difficulty** (the list next to Open, or Ctrl+O while a map is open): switch to another difficulty of the same mapset. It asks first if the open one has unsaved edits. With no map open, Ctrl+O opens a file as above.
- The File menu also has Recent maps, Import .osz… and Open map folder (Ctrl+Shift+O). Sample files you put in that folder show up as lanes when you come back.

## Hitsound – osu! connection

- **Open map from osu!** opens the map osu! has open:
  - un!stable is asked directly.
  - osu!stable is read from the map it has open.
  - For osu!lazer, use **File → Edit externally** in lazer first, then open that folder.
- When you save in osu!'s editor, dawsu! reloads the map and keeps your place, lane layout and unsaved hitsound edits.
  - A hit whose object was deleted in osu! stays, with no object.
  - If slide, sound-set or bookmark edits are also unsaved, dawsu! asks before the reload.
- **File → Reload osu!'s editor after Save** makes osu!'s editor pick up a save from dawsu!. Anything unsaved there is lost when it reloads.

## Hitsound – Lanes

- **Sample lanes:** hitnormal, whistle, finish and clap, each with the default sound first and the map's own files under it.
  - Filter to find one.
  - Above the list, + adds a lane for a sample file already in the mapset, and − deletes the highlighted lane (the one you touched last).
  - ‹ hides or shows the names.
- **A lane's buttons:** the icon shows the sound.
  - + adds the next numbered layer with the default family sound. Choose another sample through Active lane.
  - ● plays the sample, M mutes the lane and S solos it. The Song audio row has M and S too.
- **The grid:** one row per sample, in time from left to right. Red-striped columns are sounds that can't be saved as drawn yet.
- **Inspector:**
  - Active lane and Bookmarks stay on the left.
  - Preferences chooses between the Classic browser inside the inspector and the Folder tree sidebar.
  - Selecting a file previews it. The assignment button, or dropping the file on a lane, replaces that lane's sample.

## Hitsound – Hit editing

The Draw tool uses a drumstick icon and cursor. When Audition is on, moving a hit onto another lane plays that lane's sample, whether you drag it or move it with the keyboard.

| Tool | Key | What it does |
|---|---|---|
| Select | 1 | Click a hit to select it, Ctrl/Shift+click to add more, drag a box around several, drag selected hits to move them. |
| Grab | 2 | Drag the canvas to pan time and lane rows. Space still plays or stops. View → Range selection tool keeps range editing available. |
| Draw | 3 | Click or drag along a lane to place hits. Starting on a hit erases instead. Holding Alt draws with any tool. |
| Erase | 4 | Drag across hits to remove them. |
| Mute | 5 | Click a hit to silence it while you listen. osu! can't store a muted sound, so it is never saved. |
| Ramp | 6 | Drag in the volume controller to draw a smooth volume ramp. |

- **Insert volume · %** in the toolbar presets the volume of hits you draw next. Clear the field to follow the map volume. Existing hits keep their volumes, and the choice is remembered.
- The **Edit** menu has quantize, repeat, range and scale by volume.
- **Protect hitnormals from deletion** (Edit menu): when it's on, deleting, cutting, erasing or clearing a range keeps base hitnormal sounds. It's off until you turn it on, and the choice is remembered.
- **Drum hit detection…** (Edit menu) finds the kicks and snares in the song, or in a drum stem you choose, and adds them as hits. Pick a lane for kicks and one for snares (or Skip either), the sensitivity, and how close a hit must be to a circle or slider node to snap to it. Use the Range tool first to analyse only part of the song. The review window lists every proposed hit: uncheck any you don't want, and **Listen** plays the song on the left with short cues on the right. Apply is one Undo step and doesn't save. A **Beat grid** setting (1/4 by default) drops hits that are not on the map's beat divisions and snaps the rest onto them. It runs on your PC with signal processing, with no trained model, and no audio is uploaded. It works best on a drum stem; on a full mix, listen before applying.
- **Undo and redo** (Ctrl+Z, Ctrl+Y). The optional History panel in the inspector lists edits by name.

## Hitsound – Timeline navigation

- **Snap** (J): when it's on, hits land on the grid.
- **Snap mode:**
  - Grid puts dragged hits on grid lines.
  - Grid relative moves them by whole steps and keeps their offset.
  - **Objects** aligns hits with circles, slider nodes and spinner ends, including ones off the beat grid.
- **Beat division:** 1/4, 1/8… or 2/1, 3/1, 4/1 (every 2, 3 or 4 beats from each red timing point). Shift+1–9 still selects 1/n.
- **View toggles** sit directly on the ribbon: Object lines beside the snap controls, Controller before Drums, and Inspector, Lane info and Used lanes in the View group.
- **Zoom and Fit:** like Cubase, each scrollbar ends in − slider +. The horizontal one zooms in time (also Ctrl+wheel with DAW-like keys); the vertical one sets the lane height (also Ctrl+Up/Down or Ctrl+Shift+wheel). Fit shows the whole map.
- **Menus:** every item shows an icon and its shortcut. On/off settings are switches, and Export style, Start playing from and Speed are rows of buttons inside the menu, with no submenus.
- **The ribbon** groups the buttons as Map, History, Tool, Grid, Drums and View, each with a label. In narrower windows the group names go first, then the less-used labels, then all labels except Save's.
- **Bookmarks:**
  - Labels show in the inspector's list and next to their flags on the ruler. A label that runs into the next flag ends with "…".
  - Bookmarks without a label stay plain flags.
- **Locators:**
  - **L/R** beside Cycle opens the locators: L and R jump to the markers, and you can type a time in ms in their boxes. It shows in windows 1000 px wide or more.
  - Ctrl+click the ruler to set L, and Alt+click to set R.
  - Cycle (L) loops between them.
- **Position:** time, bar.beat.tick, tempo, and the time signature with the snap divisor.

## Hitsound – Volume control

- **Volume controller:** one bar per circle or slider end.
  - Drag a bar to change its volume, Alt-drag to paint, and Shift-drag for a ramp.
  - osu! has one volume per object, so every lane on that object shares it.
- **Colour:** hits and volume bars show volume from blue through purple to red.
- **Selection card** (at the top of the inspector):
  - It shows how many hits are selected, where they start, and which circle or slider end they play on.
  - Type a start time and press Enter to move them, or type a volume and press Apply. Relative adds to each hit, and Set all gives them that value. Applies to chooses all lanes or only the selected ones.
  - Quantize…, Duplicate and Delete act on the selection. **New hits** sets the volume of hits you draw (empty follows the map).

## Hitsound – Map output

- **Save .osu** (Ctrl+S): writes hitsounds into the open difficulty. Only changed lines are rewritten, and a backup is kept outside the mapset.
  - A warning badge on the Save button counts hits with no gameplay object and moments that can't be saved as drawn. With problems, clicking Save lists them first: **Show first problem** jumps there, and **Continue to Save** runs the normal save. Ctrl+S and File → Save skip the list.
- **Generate hitsound diff…**: makes a separate hitsound difficulty from your hits, adding circles where a sound has no object.
- **Map → Copy hitsounds…** copies a difficulty into the others, like Mapping Tools' Hitsound Copier.

## Hitsound – Sound sets

- The Sound sets panel shows in the inspector when you choose it under View.

## Hitsound – Playback

- **Transport:** go to the start, stop, and play (Space).
- **Playback speed:** 25 % to 150 % without changing pitch. Return-on-stop and follow-playhead sit beside those buttons.
- **Song:** the song's volume, mute, and an offset in ms if it sounds early or late. None of this is saved into the map.
- **Song waveform:** coloured by frequency: blue for lows, purple for mids, yellow for highs.
- **Global volume:** turns the song, hitsounds and previews up or down together. It never goes below 20 %.

## Preferences, Shortcuts, and Help and updates

- **Preferences:** Songs folder, audio output and buffer, and key commands.
- **Keys / F1:** a searchable list of every shortcut and mouse action, grouped by feature area.

File → Full interface tour shows this again. Press F1 for every shortcut.
