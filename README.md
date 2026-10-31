# dawsu! hitsounding editor

A Windows editor for dawsu! hitsounds, with sample lanes, MIDI import/export, a song waveform and a volume controller. Its interface and workflow are heavily inspired by Cubase’s MIDI Drum Editor.

## Download

Download the Windows application from [Releases](https://github.com/YogeGod/dawsu-hitsounding-editor/releases), extract the complete ZIP, and run the editor executable. Keep the extracted files together.

## Source code

This repository contains the complete application source. You can clone it or use **Code → Download ZIP**.

With Windows, Node.js 24 or newer, and npm:

```powershell
npm install
npm install --no-save electron@44.5.0 @electron/asar@3.4.1
npx electron .
```

See [SOURCE-README.md](SOURCE-README.md) for tests, native-audio compilation and portable Windows packaging instructions.

## Features

- Import MIDI, `.osu`, `.osz` and saved `.hseproj` projects.
- Normal, Soft and Drum sampleset folders with expandable numbered layers, sample icons and fixed export mappings.
- Select, Draw, Erase, snapping, copy/paste, undo/redo and volume ramps.
- Separate hitsound volume and MIDI velocity controls.
- Detailed song waveform, gameplay-object timeline, zoom sliders and cycle playback.
- Timing points, time signatures, playback speed, song offset and global volume.
- Collapsible, movable inspector panels and a compact, collapsible selection-info bar.
- Named bookmarks and a searchable shortcut overview through **Keys / F1**.
- Normal beatmap export or a separate HS diff rhythm guide generated from hitsound-event times.
- Browser audio and native Windows output options, including WASAPI and ASIO.

## Project layout

| Folder / file | Purpose |
| --- | --- |
| `main.cjs`, `preload.cjs` | Electron application and desktop bridge |
| `index.html`, `src/` | Editor interface, audio, MIDI and dawsu! logic |
| `samples/` | Bundled samples and their attribution/licence files |
| `native-audio/` | C# audio helper, dependencies and build instructions |
| `test/` | Automated checks |
| `scripts/` | Windows packaging and icon tools |
| `examples/` | Example editor projects |

The dependency cache and packaged application archives are excluded from the repository. Bundled third-party components and samples retain their included licence notices.

## Tutorial (C# editor)

A tour of the new C# osu! hitsound editor, from the [`csharp-rewrite`](https://github.com/YogeGod/dawsu-hitsounding-editor/tree/csharp-rewrite) branch, in the same order as its in-app tour. The app shows that tour on the first launch, and File → Tutorial shows it again (Next/Back or the arrow keys step through it; Esc ends it).

### 1. Open, save and generate

- **Open a map** (Ctrl+O): pick a `.osu` from your Songs folder. Each sample file in the mapset becomes a lane you can draw in. File → Recent maps and File → Import .osz… are in the File menu too. If the current map has unsaved edits, the editor asks before it opens another one.
- **Save .osu** (Ctrl+S): writes your hitsounds back into the map. Only the lines you changed are rewritten, and a backup of the old file is kept outside the mapset.
- **Generate**: makes a separate hitsound difficulty from your hits, adding circles where a sound has no object, ready for Mapping Tools' Hitsound Copier.

### 2. The top bar

- **Menus**: every command is also here — File (open, import .osz, new hitsound diff, the tutorial), Edit (quantize, repeat, range, scale by volume), Playback, Map and View.
- **The open map**: the name of the `.osu` you are editing.
- **Preferences**: your Songs folder, audio output and buffer, and the key commands (you can import your Cubase keys).
- **Keys / F1**: a searchable list of every shortcut and mouse action. Press F1 at any time.
- **Undo and redo** (Ctrl+Z, Ctrl+Y): step back and forward through your edits. The Recent edits card in the inspector lists them by name.

### 3. Tools

| Tool | Key | What it does |
|---|---|---|
| Select | 1 | Click a hit to select it, Ctrl/Shift+click to add more, drag a box around several, drag selected hits to move them. |
| Range | 2 | Drag a time range across the lanes you want, then Edit → Edit range… copies, moves or deletes everything inside it. |
| Draw | 3 | Click or drag along a lane to place hits. Starting on a hit erases instead. Holding Alt draws with any tool. |
| Erase | 4 | Drag across hits to remove them. Right-click deletes a hit with any tool. |
| Mute | 5 | Click a hit to silence it while you listen. Listening only: osu! can't store a muted sound, so it's never saved. |
| Ramp | 6 | Drag in the volume controller at the bottom to draw a smooth volume ramp. |

### 4. Snap, view and zoom

- **Snap** (J): turns snapping on or off. When it's on, hits land on the beat grid.
- **Snap mode and beat division**: *Grid* puts dragged hits on grid lines; *Grid relative* moves them by whole steps and keeps their offset. The divisor (1/4, 1/8…) sets how fine the grid is (Shift+1–9).
- **View toggles**: show or hide the inspector, each lane's file index (Info) and the volume controller.
- **Colour**: colour notes by lane, or by volume (quiet notes dark, loud notes bright, so odd volumes stand out).
- **Zoom and Fit**: zoom in and out of time (also Ctrl+wheel). Fit shows the whole map.

### 5. Selection bar and song

- **Selection bar**: how many hits are selected, where they start, and which circle or slider end they play on. Type a volume and press Apply to change the selected circles: *Relative* adds to each one, *Set all* gives them all that value.
- **Song**: the song's own volume, mute, and an offset in ms if it sounds early or late. None of this is saved into the map.

### 6. Inspector

Cards for the selection (shift time, set volume), the map's bookmarks, and Recent edits — click an edit to go back to it. Fold a card by clicking its title; drag ⠿ to reorder the cards.

### 7. Lanes and the grid

- **Sample lanes**: every sample you can use, grouped by sampleset. Type in the filter to find one, and press + to add a lane for another sample file.
- **A lane's buttons**: the icon shows the sound (normal, whistle, finish, clap). + adds a lane, ● plays the sample, M mutes the lane and S solos it while you listen.
- **The grid**: your hitsounds, one row per sample, in time from left to right. Red-striped columns are sounds that can't be saved as drawn yet.

### 8. Volume controller

One bar per circle or slider end. Drag a bar to change its volume, Alt-drag to paint, Shift-drag for a ramp. osu! has one volume per object, so all lanes on that object share it.

### 9. Transport and position

- **Transport**: go to the start, stop, play (Space), and Cycle between the L and R markers (L). Ctrl+click the ruler sets L; Alt+click sets R.
- **Position**: where the playhead is — time, bar.beat.tick, tempo, and the time signature with the snap divisor.
- **Global volume**: turns the song, hitsounds and previews up or down together. It never goes below 20 %.

That's everything: open a map and start drawing.
