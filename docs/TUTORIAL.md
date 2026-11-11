# Tutorial

A tour of dawsu! 2.2.7, in the same order as **File → Full interface tour**. Next and Back (or the arrow keys) step through that tour; Esc ends it.

The first launch asks whether you want **DAW-like keys** or **osu! editor controls**, then runs **Quick start** (also under **File → Quick start**). Quick start covers opening a difficulty, choosing a lane, assigning a sample, placing a hit, listening, and choosing Save, Generate or Copy. The house button next to the dawsu! name, or **File → Start screen**, goes back to the start screen and asks to save first.

## 1. Open, save and generate

- **Open a map** (Ctrl+O): pick a `.osu`, or drop a `.osu` or `.osz` on the window. Each sample file in the mapset becomes a lane. The File menu also has Recent maps, Import .osz… and Open map folder (Ctrl+Shift+O). Sample files you put in that folder show up as lanes when you come back.
- **Open map from osu!**: opens the map osu! has open. un!stable is asked directly. osu!stable is read from the open map. For osu!lazer, use **File → Edit externally** in lazer first, then open that folder. When you save in osu!'s editor, dawsu! reloads and keeps your place, lane layout and unsaved hitsound edits. A hit whose object was deleted in osu! stays, with no object. Slide, sound-set or bookmark edits that are also unsaved ask before the reload. **File → Reload osu!'s editor after Save** makes osu!'s editor pick up a save from dawsu!; anything unsaved there is lost when it reloads.
- **Save .osu** (Ctrl+S): writes hitsounds into the open difficulty. Hover the map title for its path. Only changed lines are rewritten, and a backup is kept outside the mapset.
- **Generate**: makes a separate hitsound difficulty from your hits, adding circles where a sound has no object. **Map → Copy hitsounds…** copies a difficulty into the others, like Mapping Tools' Hitsound Copier.

## 2. The top bar

- **Menus**: File, Edit, Playback, Map and View. Edit includes quantize, repeat, range and scale by volume, and **Protect hitnormals from deletion**. When that is on, deleting, cutting, erasing or clearing a range keeps base hitnormal sounds. It is off until you turn it on, and the choice is remembered.
- **The open map**: the name of the `.osu` you are editing. A long name ends with "…" instead of putting a scrollbar over the bar.
- **Preferences**: Songs folder, audio output and buffer, and key commands.
- **Keys / F1**: a searchable list of every shortcut and mouse action.
- **Undo and redo** (Ctrl+Z, Ctrl+Y). The optional History panel in the inspector lists edits by name.

## 3. Tools

The Draw tool uses a drumstick icon and cursor. Moving a hit onto another lane plays that lane's sample when Audition is on, whether you drag it or move it with the keyboard.

| Tool | Key | What it does |
|---|---|---|
| Select | 1 | Click a hit to select it, Ctrl/Shift+click to add more, drag a box around several, drag selected hits to move them. |
| Grab | 2 | Drag the canvas to pan time and lane rows. Space still plays or stops. View → Range selection tool keeps range editing available. |
| Draw | 3 | Click or drag along a lane to place hits. Starting on a hit erases instead. Holding Alt draws with any tool. |
| Erase | 4 | Drag across hits to remove them. Right-click deletes a hit with any tool. |
| Mute | 5 | Click a hit to silence it while you listen. osu! can't store a muted sound, so it is never saved. |
| Ramp | 6 | Drag in the volume controller to draw a smooth volume ramp. |

**Insert volume · %** in the toolbar presets the volume of hits you draw next. Clear the field to follow the map volume. Existing hits keep their volumes. The choice is remembered.

## 4. Snap, view and zoom

- **Snap** (J): when it is on, hits land on the grid.
- **Snap mode**: Grid puts dragged hits on grid lines. Grid relative moves them by whole steps and keeps their offset. **Objects** aligns hits with circles, slider nodes and spinner ends, including ones off the beat grid.
- **Beat division**: 1/4, 1/8… or 2/1, 3/1, 4/1 (every 2, 3 or 4 beats from each red timing point). Shift+1–9 still selects 1/n.
- **View toggles**: toolbar icons show or hide the inspector, sample indexes, unused lanes, gameplay-object lines and the volume controller. Hover an icon for its name.
- **Colour**: hits and volume bars show volume from blue through purple to red.
- **Zoom and Fit**: zoom in time (also Ctrl+wheel with DAW-like keys). Fit shows the whole map.

## 5. Selection bar and song

- **Selection bar**: how many hits are selected, where they start, and which circle or slider end they play on. Type a volume and press the checkmark. Relative adds to each hit; Set all gives them that value. The up arrow collapses these details and gives the space back, leaving a small expand handle. The same collapse is shared with the Keysound editor.
- **Song**: the song's volume, mute, and an offset in ms if it sounds early or late. None of this is saved into the map.

## 6. Inspector

Active lane and Bookmarks stay on the left. Bookmark labels show in the list and next to their flags on the ruler. A label that runs into the next flag ends with "…". Bookmarks without a label stay plain flags. View shows History or Sound sets when you need them. Preferences chooses the Classic browser inside the inspector or the Folder tree sidebar. Selecting a file previews it; the assignment button or a lane drop replaces that lane's sample.

## 7. Lanes and the grid

- **Sample lanes**: hitnormal, whistle, finish and clap, each with the default sound first and the map's own files under it. Filter to find one. Above the list, + adds a lane for a sample file already in the mapset, − deletes the highlighted lane (the one you touched last), and ‹ hides or shows the names.
- **A lane's buttons**: the icon shows the sound. + adds the next numbered layer with the default family sound; choose another sample through Active lane. ● plays the sample, M mutes the lane and S solos it. The Song audio row has M and S too.
- **The grid**: one row per sample, in time from left to right. Red-striped columns are sounds that can't be saved as drawn yet.
- **Song waveform**: coloured by frequency — blue for lows, purple for mids, yellow for highs.

## 8. Volume controller

One bar per circle or slider end. Drag a bar to change its volume, Alt-drag to paint, Shift-drag for a ramp. osu! has one volume per object, so every lane on that object shares it.

## 9. Transport and position

- **Transport**: L ◀ and R ◀ jump to the L and R markers. Type a time in ms in their boxes to move them. Then go to the start, stop, play (Space) and Cycle between L and R (L). Ctrl+click the ruler sets L; Alt+click sets R.
- **Position**: time, bar.beat.tick, tempo, and the time signature with the snap divisor.
- **Playback speed**: 25 % to 150 % without changing pitch. Return-on-stop and follow-playhead sit beside those buttons.
- **Global volume**: turns the song, hitsounds and previews up or down together. It never goes below 20 %.

File → Full interface tour shows this again. Press F1 for every shortcut.
