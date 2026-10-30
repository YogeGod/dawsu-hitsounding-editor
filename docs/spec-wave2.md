# Spec: V6.39 port, wave 2

Status: approved by the user on 2026-10-30. The decisions behind it are in `docs/merge-decisions.md`, and wave 1 is in `docs/spec-wave1.md`. V6.39's behaviour comes from `src/app.js`, `src/model.js` and `src/help.js` on tag `V6.39`.

## Already in the C# editor (not part of this wave)

Playback start mode, return on stop, playback speed, song volume, song offset, and lane rename/reorder/add/hide (the lane menu). The feature list on the merge dashboard said they were missing; it is corrected.

## Dropped (the user's choice)

Assign to gameplay objects, and the Objects and Cursor snap modes.

## Ground rules (unchanged)

- Gameplay objects never move. A sound placed where there is no object is a loose (red) hit: a conflict in Apply mode, a new circle in Generate mode.
- Volume belongs to a circle or slider end, not to one sound.
- Each completed edit is one undo step. Export rules (byte-exact null round-trip, minimal diff, stay inside the mapset folder) hold for everything below.

## 1. Quantize (Q)

- **Q** quantizes the selection at once, at the last strength used (100 % at first). **Edit → Quantize…** opens a dialog to change the strength (1–100 %), then applies it.
- Only **loose** selected hits move: hits on a circle or slider end stay, because circles never move. If nothing selected is loose, the status bar says so.
- A hit moves `strength × (grid line − time)` toward the nearest grid line at the current snap (as V6.39's `snapSelected`). With Grid relative snap (part 3), Quantize still targets the plain grid.
- A loose hit that lands within `EditSession.AnchorTolerance` of an object plays on it, through the existing `HitAt`.
- The strength is kept in Preferences.

## 2. Repeat (Ctrl+R)

- **Edit → Repeat…** (Ctrl+R) opens a dialog with **Copies** (1–64, default 1) and **Gap** in snap steps at the current snap.
- The gap's default is the selection's length rounded up to whole snap steps, the same as Duplicate. Copy *n* goes *n × gap* steps after the original, measured on the grid, so it follows tempo changes.
- The copies are one undo step and are selected afterwards. The clipboard is unchanged.

## 3. Snap mode: Grid relative

- A **Snap mode** box next to the Snap divisor: **Grid** (as now) or **Grid relative**.
- Grid relative applies to dragging a selection and to nudging by a snap step: the grabbed hit keeps its offset from its own grid line, so a hit 10 ms early lands 10 ms early on the new line. Drawing, pasting and the Range tool keep plain grid snap.
- The mode is kept in Preferences and shown in the status bar ("snap 1/4 relative").

## 4. Time signatures (editor-only)

- **View → Time signatures…** opens a list of changes: start bar and numerator/denominator (1–32 / 1, 2, 4, 8, 16). Add, edit and remove entries.
- They change only how bar and beat lines are drawn, and the LCD's bar.beat. They are saved in the editor's project file (`ProjectData`, a new `TimeSignatures` list) and **never** in the `.osu`.
- With no entries, bars follow the map's red lines as now (each red line starts a new bar with its own meter).
- An entry takes effect at the start of its bar, counted from the red line in force there. A later red line still starts a new bar.

## 5. Colour modes

- **View → Colour by → Lane** (as now) or **Volume**.
- In Volume mode a note's fill goes from dark (1 %) to bright (100 %) by its moment's volume, on one ramp for every lane. Loose hits stay white with a red ring in both modes.
- The mode is kept in Preferences.

## 6. Inspector panel (left)

- A collapsible panel at the far left of the window, toggled by a **VIEW → Inspector** button (like Lane details) and **View → Inspector**. Its open/closed state and width are kept in Preferences.
- It holds cards that fold (click the title) and reorder (drag the grip). Their order and fold state are kept in Preferences.
- **Selection card:**
  - Nothing selected: "Nothing selected".
  - One hit: its time (ms and bar.beat), lane and volume.
  - Several: a count, the time span and the volume range.
  - **Shift time (ms)** moves the selection by a typed offset (through `Move`), and **Volume** sets the selected moments' volume (through `SetVolume`), as in V6.39.
- **Bookmarks card:**
  - The map's bookmarks as a list (bar.beat and time). Click one to jump there.
  - **+ At playhead** and **Remove**, the same as Ctrl+B and Ctrl+Shift+B.
- **Recent edits card:** see part 7.
- The UI inspector (Ctrl+Shift+I, the theme editor) is a different window and stays as it is.

## 7. Recent edits

- Each undo step gets a name: Draw, Erase, Move, Paste, Duplicate, Delete, Range copy/move/delete, Quantize, Repeat, Volume, Ramp, Scale by volume, Bookmark, and so on. `EditSession` stores a label with each step.
- The card lists the last 50 steps, newest at the top, with the current state marked. Clicking an entry undoes or redoes to just after that step. Redo still works afterwards.
- Undo and Redo (Ctrl+Z, Ctrl+Y) are unchanged.

## 8. .osz import

- **File → Import .osz…** picks an archive and unpacks it into a new folder in the Songs folder from Preferences, named after the archive. A clash becomes "Name (2)", "Name (3)" and so on: it never merges into an existing folder.
- **Safety:** every entry must resolve inside the new folder. Absolute paths, `..`, drive letters and names Windows can't store are refused, and the import stops with a message. Nothing is written outside the new folder. A failed import deletes only the folder it created.
- Afterwards, if the archive has several difficulties, a list to pick one opens. The status bar reminds the user to press F5 in stable's song select.
- If Preferences has no Songs folder, the user is asked to set one first.

## 9. New hitsound diff (Ctrl+N)

- **File → New hitsound diff…** (Ctrl+N), with a map open, asks for a difficulty name (default "Hitsounds").
- It writes a new `.osu` in the same folder. The text is copied from the open file, with these changes:
  - `[Metadata] Version:` is the new name, and `BeatmapID:` is 0;
  - `[TimingPoints]` keeps only the red (uninherited) lines;
  - `[HitObjects]` is empty;
  - bookmarks are kept.
- Encoding, BOM and line endings follow the open file.
- The filename is `Artist - Title (Creator) [Name].osu`, with Windows-invalid characters replaced the way osu! does. If the name, or the file, already exists in the folder, it is refused with a message.
- The new diff then opens, ready for drawing and Generate.

## Keys

| Command | Built-in | Cubase command (the user's file) | osu! preset |
|---|---|---|---|
| Quantize | Q | `MIDI|Quantize` (Q) | Q |
| Quantize… dialog | none | none | none |
| Repeat… | Ctrl+R | `Edit|Repeat` (Ctrl+K in Cubase 15) | Ctrl+R |
| New hitsound diff… | Ctrl+N | `File|New` (Ctrl+N) | Ctrl+N |
| Import .osz…, Time signatures…, Colour by, Inspector | none | none | none |

Commands with no built-in key join the menu-only list in `PreferencesAndHelpTests`.

## Tests

- **Lanes:**
  - Quantize moves only loose hits, by the strength, onto or toward the grid, and a hit landing on an object anchors.
  - Repeat makes N copies at grid-step gaps across a tempo change.
  - Undo labels and jumping to a step, both forward and back.
  - The new-diff writer keeps every line except the changed fields, red lines only, and the same bytes for encoding, BOM and line endings.
  - .osz import refuses `..`, absolute paths and drive letters, never writes outside, numbers clashes, and cleans up after a failure.
- **App:**
  - Grid relative keeps offsets when dragging and nudging.
  - Time signatures change bar lines but not the `.osu`, and they round-trip through the project file.
  - Volume colour mode picks the colour by volume.
  - The keys in each preset.
- **Corpus:** a no-edit round-trip is still byte-identical.

## Build order

1. Undo labels and Recent edits, because the rest gets named steps.
2. Quantize, Repeat and Grid relative.
3. Time signatures and colour modes.
4. The inspector panel and its cards.
5. .osz import and New hitsound diff, with the file safety tests first.

Each step ends with the tests passing. The CLAUDE.md checks (/simplify, /code-review, security pass) and the user trying it in the app come before the commit.
