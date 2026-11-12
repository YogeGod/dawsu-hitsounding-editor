# dawsu! hitsounding editor

A Windows editor for osu! hitsounds. Each sample in a mapset becomes a lane you draw hits on, over the song's waveform, and the editor writes them back into the map.

Current release: **3.0.0**. Changes for every version are in [CHANGELOG.md](CHANGELOG.md). A written tour of the current screen is in [docs/TUTORIAL.md](docs/TUTORIAL.md).

## What it does

Features are grouped into **feature areas**, the same groups the F1 help uses. Hitsound and Keysound areas are named after their mode.

### Hitsound mode

- **Hitsound – Map files:** open a `.osu` or `.osz` (or drop one on the window), recent maps, **Import .osz…**, a new hitsound diff, and revert to the last save.
- **Hitsound – osu! connection:** **File → Open map from osu!** opens the map osu! has open and reloads it when that editor saves. After you save here, osu!'s editor can reload the same map (**File → Reload osu!'s editor after Save**).
- **Hitsound – Lanes:** each sample in the mapset is a lane, with numbered layers, filtering, and mute, solo and audition.
- **Hitsound – Hit editing:** draw, move and erase hits, plus quantize, repeat, edit range and scale by volume. **Insert volume** sets the volume of hits you draw next; clear it to follow the map volume. **Edit → Protect hitnormals from deletion** keeps base hitnormal sounds when you delete, cut, erase or clear a range. It is off until you turn it on.
- **Hitsound – Timeline navigation:** snap to the beat grid, or with **Objects** in the snap menu, to circles, slider nodes and spinner ends, including ones off the beat grid. Also zoom, bookmarks and the L/R locators.
- **Hitsound – Volume control:** set volumes per object in the controller, with ramps.
- **Hitsound – Map output:**
  - **Save .osu** writes the hitsounds into the difficulty, and a backup is kept outside the mapset. It uses osu!'s native sound combinations where it can and mixes samples only where it must.
  - **Generate hitsound diff…** makes a separate hitsound difficulty.
  - **Map → Copy hitsounds…** copies it into the other difficulties, like Mapping Tools' Hitsound Copier.
- **Hitsound – Sound sets:** reusable sound sets.
- **Hitsound – Drums:** drum maps, and a drum instrument (a VST3 or a SoundFont kit) for playback and WAV export.
- **Hitsound – Playback:** play at 25–150 % speed without changing pitch, with song volume and offset.

### Keysound mode (start screen → Keysounding)

- **Keysound – Project:** a project made from a reference beatmap, with instrument tracks.
- **Keysound – Note editing:** a piano roll with DAW-style editing.
- **Keysound – Note input:** draw notes, import MIDI, or play and record from a MIDI keyboard. You can also use your computer keyboard with **musical typing** (Alt+K).
- **Keysound – Instruments:** each track plays the bundled piano, a VST3 instrument or a SoundFont (.sf2) preset. The SoundFont's reverb and chorus can be on or off. Every plug-in runs in its own process, so a crash only silences its track.
- **Keysound – Gameplay sync:** **Sync to gameplay objects…** moves notes onto the map's circles, slider ends and spinner ends.
- **Keysound – Output:** **Generate keysounds…** renders every synced note to its own WAV and writes a playable `[Keysounds]` copy of the map. You can then export it as a `.osz`.
- **Keysound – Playback:** the same transport and cycle as Hitsound mode.

### Both modes

- **Start screen**, **Shortcuts** (DAW-like or osu! keys, each customisable), **Preferences**, and **Help and updates** (F1, Quick start, the interface tour, in-app updates).

## Getting started

1. Download `dawsu-win-Setup.exe` from the latest [release](https://github.com/YogeGod/dawsu-hitsounding-editor/releases) and run it. It installs dawsu! for your Windows user (no administrator rights needed) and adds it to the Start menu. Running a newer installer updates an existing install in place, keeping your settings; **File → Check for updates…** does the same from inside the app.
2. The first launch asks how you want to control the editors: **DAW-like keys** or **osu! editor controls**. You can switch later in **Preferences → Keys**.
3. Set your osu! Songs folder in **Preferences**, then open a map. A short **Quick start** follows (it's also under **File → Quick start**).
4. Press **F1** for every shortcut and mouse action. The house button next to the dawsu! name goes back to the start screen.

| | DAW-like keys | osu! editor controls |
|---|---|---|
| Mouse wheel | Scroll the lanes | Playhead one grid line (Shift: four) |
| Ctrl+wheel | Zoom in time | Change the snap |
| Alt+wheel | Change the snap | Zoom in time |
| Left / Right | Previous / next hit | Playhead one grid line (Shift: four) |
| Space | Play / stop | Play / stop (also X, C) |
| Z / V | First / last object | First / last object |
| 1, 2, 3, 4 | Select, Grab, Draw, Erase | Select, Draw, (none), Erase (Grab has no key) |

Both styles share J (snap), Ctrl+S (save), Ctrl+Z / Ctrl+Y (undo / redo) and Alt+K (musical typing in Keysound mode).

## Building from source

With Windows and the .NET 10 SDK:

```powershell
dotnet run --project src/OsuHitsound.App
dotnet test OsuHitsoundEditor.slnx
```

VST3 instruments in Keysound mode need `dawsu_vst3_host.exe` next to the app. Build it with the steps in [native/Vst3IsolatedHost/README.md](native/Vst3IsolatedHost/README.md).

| Folder | Purpose |
| --- | --- |
| `src/OsuHitsound.App` | The editor window (Hitsound and Keysound modes) |
| `src/OsuHitsound.Audio` | Playback, the bundled piano and the VST3 client |
| `src/OsuHitsound.Format` | Reading and writing `.osu` files |
| `src/OsuHitsound.Lanes` | Lanes, hitsound compiling, and the Keysound model |
| `native/` | The VST hosts (C++) |
| `tests/`, `tools/` | Automated tests and test helpers |
| `docs/` | Design notes and screenshots |

## Credits

- Default hitsounds from [osu!](https://github.com/ppy/osu-resources) by ppy and contributors ([CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)).
- Copy hitsounds follows [Mapping Tools](https://github.com/OliBomby/Mapping_Tools)' Hitsound Copier by OliBomby (MIT).
- Audio decoding by [BASS](https://www.un4seen.com) (free for non-commercial use); output through [NAudio](https://github.com/naudio/NAudio) (MIT).
- Waveform colours follow [osu-framework](https://github.com/ppy/osu-framework)'s Waveform (MIT).
- The bundled piano is the FreePats Upright Piano KW (CC0). VST3 hosting uses the Steinberg VST 3 SDK (MIT).
- Installing and updating by [Velopack](https://github.com/velopack/velopack) (MIT).
- SoundFont playback by [MeltySynth](https://github.com/sinshu/meltysynth) (MIT).
- Drum hit detection uses the [ADTOF](https://github.com/MZehren/ADTOF) drum model by Zehren, Alunno and Bientinesi ([CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)), converted to ONNX and run with [ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT). Its audio features and peak picking follow [madmom](https://github.com/CPJKU/madmom) (BSD).

Full licence texts are in [THIRD-PARTY-NOTICES.txt](src/OsuHitsound.App/THIRD-PARTY-NOTICES.txt).
