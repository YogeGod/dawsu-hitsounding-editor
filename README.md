# dawsu! hitsounding editor

A Windows editor for osu! hitsounds, with sample lanes, MIDI import/export, a song waveform and a volume controller. Its interface and workflow are heavily inspired by Cubase’s MIDI Drum Editor.

## Download

Download the Windows application from [Releases](https://github.com/YogeGod/osu-daw-hitsound-editor/releases), extract the complete ZIP, and run the editor executable. Keep the extracted files together.

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
| `index.html`, `src/` | Editor interface, audio, MIDI and osu! logic |
| `samples/` | Bundled samples and their attribution/licence files |
| `native-audio/` | C# audio helper, dependencies and build instructions |
| `test/` | Automated checks |
| `scripts/` | Windows packaging and icon tools |
| `examples/` | Example editor projects |

The dependency cache and packaged application archives are excluded from the repository. Bundled third-party components and samples retain their included licence notices.
