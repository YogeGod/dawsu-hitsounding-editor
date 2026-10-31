# dawsu! (old Electron editor): architecture notes

A reference for how the earlier Electron hitsound editor, **dawsu!**, is built. It was developed with Codex, not Claude, so this note collects what Codex recorded. Use it as prior art for the C#/Avalonia editor, not as a spec to copy.

**Provenance.** Compiled on 2026-10-30 from Codex's own files:

- `E:\Codex-releases\dawsu!\source-v6.39` (source, package version 1.0.39)
- `C:\Users\yogem\.codex\skills\dawsu-project\SKILL.md` (Codex's project skill, written against source-v6.36)
- `CHANGES-V6.38.md`, `POLISH-NOTES.md`, `native-audio\README.md` and `dawsu! V39 Electron TODO.md` in that tree
- Codex session "Work on dawsu v38 elektra" (`~/.codex/sessions/2026/10/28/`)

File names below were checked against `source-v6.39`. What each file *does* comes from Codex's skill and notes, and was **not** re-verified by reading the code. Where the note is inferring from a file name only, it says so.

## Where things live

| Item | Location |
|---|---|
| Release workspace | `E:\Codex-releases\dawsu!` |
| Current Electron source | `source-v6.39` (earlier: `source-v6.36` to `v6.38`; old release folders are archives, never edit them) |
| Browser port | `sites\dawsu-browser` (`app/` renderer, `browser/` bridges and shims, `build.mjs` copies `app/` to `dist/`) |
| Website and release policy | `drum-studio` and `AGENTS.md` (publishes to Vercel on every release) |
| Visual guides | `visual-user-guide`, `beginner-guide` |
| Open work | `dawsu! V39 Electron TODO.md` |

## Process model

Standard Electron split.

- **Main process:** `main.cjs`. It owns files, dialogs, native audio and project folders.
- **Bridge:** `preload.cjs`.
- **Renderer:** `src/app.js` plus the UI modules below. `package.json` declares `main.cjs`, runtime dependencies `midi-file` and `jszip` only, and the product name "dawsu!daw hitsounding editor".

Renderer and main talk over IPC. Project-folder state (folder path, save destination) is kept in the **main process**, outside the musical data and the undo history.

## Module map

| Area | Files |
|---|---|
| Project data and formats | `src/model.js`, `src/formats.cjs`, `src/file-io.cjs`, `src/project-assets.cjs`, `src/project-folder.cjs`, `src/preferences.cjs` |
| Samples and lanes | `src/sample-files.cjs`, `src/sample-groups.js`, `src/sample-render.js`, `src/mix-names.cjs`, `src/object-sounds.cjs` |
| Export | `src/export-samples.cjs`, `src/render-export.cjs`, `src/guide-greenlines.cjs`, `src/generate-objects.cjs` |
| Timeline UI | `src/layout-b.js`, `src/layout-b.css`, `src/waveform.js`, `src/inspector-panels.js`, `src/bookmarks.js`, `src/help.js`, `src/v39-ui.js` |
| Commands and keys | `src/shortcuts.js`, `src/commands.js` |
| Audio | `src/audio-decode.js`, `src/audio-bridge.js`, `src/audio-output-worklet.js`, `src/native-audio.cjs`, `native-audio\` |
| Window | `src/window-controls.cjs`, `src/macos-b.js` |

Entries in the "Export" row beyond `export-samples.cjs` and `render-export.cjs` (and in "Commands" beyond `shortcuts.js`) are described by **file name only**; their contents were not read.

## Formats and data

- **Project file:** `.hseproj`. It embeds the audio, samples and the original archive, so a saved project reopens without the extracted `.osz` files.
- **Imports:** MIDI (via `midi-file`) and osu! beatmaps (`.osu` / `.osz`, via `jszip`).
- **Exports:** normal `.osz`, or a separate HS rhythm-guide difficulty, which leaves the original difficulty untouched.
- **Bundled samples:** twelve default hitsounds (Normal, Soft, Drum, each with hitnormal, hitwhistle, hitfinish, hitclap) plus slider samples in `samples\default-skin`.

## Behaviors worth borrowing

These are in dawsu!'s notes and match problems the new editor also has.

1. **`.osz` import extracts the whole archive into a project folder.** The user picks a parent folder, dawsu! creates `<oszname>` there, and numbers a clash (`Song (2)`) instead of merging. Unsafe archive paths and Windows filename conflicts are rejected, and a failed import removes only the folder it created.
2. **Ctrl+S saves the project inside that folder.** Reopening a moved project saves at its real location. Appending a second import re-points the save folder.
3. **Lane view state is project data with Undo/Redo.** Folder order, lane visibility and collapse state are saved, and view changes must not alter note times, durations, velocities, volumes, sample identities, timing or gameplay objects.
4. **Collapsed numbered-layer objects stay visible as dim previews**, and clicking one reveals its lane.
5. **Export preserves original difficulty files and companion assets**, and distinguishes a normal export from an HS guide difficulty.
6. **Do not silently deduplicate coincident MIDI notes.**

## Native audio

`native-audio\AudioHost.exe` is a small .NET Framework 4.8 helper (C# in `AudioHost.cs`, pinned NAudio 2.2.1) that opens playback outputs only: ASIO, WASAPI shared/exclusive, DirectSound and MME.

- The renderer mixes in an `AudioWorklet` and sends interleaved float stereo blocks over Electron IPC to the helper's **stdin pipe**. While native routing is active the browser output is silent.
- **Wire protocol:** a one-byte kind (1 = JSON command, 2 = stereo float PCM), a four-byte little-endian payload length, then the payload. Commands are `devices`, `configure`, `stats`, `controlPanel`, `close`, with request IDs. There are no arbitrary file or process commands. The helper exits and releases the device when stdin closes.
- On a device or IPC error, native output is released and playback **falls back to browser output**. Underrun/overflow counters appear in the audio preferences.
- Codex's honesty rule: device enumeration, output initialization, silent PCM consumption and audible playback are four separate claims. A hardware buffer size is not end-to-end latency. ASIO/exclusive output counts as verified only when checked on the user's actual hardware.

This is a long way from the new editor's BASS plan, and the lesson transfers: report what was actually tested.

## Testing and packaging (as Codex did it)

- `npm test` runs `node --test test/*.test.cjs` (258 tests at V6.38 plus 15 `test/renderer-*-check.cjs` renderer scripts).
- Release scripts: `scripts/package-windows.py` (portable ZIP), `scripts/pack-runtime.cjs`, and the `verify-*.cjs` scripts, with `scripts/README.md` as the guide.
- A release counts as verified only after the ZIP's CRC passes, the app is **extracted from the ZIP and launched**, packaged files match source byte-for-byte, and import/export round-trips on representative fixtures. Passing the source suite alone was not treated as proof.

## Known gaps (V39 TODO, not implemented as of 2026-10-28)

- Snap-division colors matching osu!stable.
- **Z** / **V** jump-to-first/last-object keys.
- Custom-hitsound parity audit against the osu! wiki guide, including numbered names for merged samples (merging `soft-hitnormal2` and `soft-hitnormal3` should produce an unused index such as `soft-hitnormal4`).
- A Key Commands settings page with presets (dawsu! current, osu!stable).
- Solo/mute on sampleset groups and the song lane.
- A start screen with recent projects.
- An unfinished hitnormal requirement in the notes, which Codex flagged as needing clarification before it is implemented.

## Where this differs from the new editor

| | dawsu! (Electron) | osu! Hitsound Editor (this repo) |
|---|---|---|
| Stack | Electron, JS, native NAudio helper | C# (.NET 10), Avalonia, BASS |
| Export | Exports `.osz`, with merged/numbered samples and an optional separate HS difficulty | Minimal-diff patching with a byte-identical null round-trip |
| Lane model | Sampleset folders containing sample lanes | One lane per sample file, like a Cubase drum map |
| Source of truth | Not stated in the notes | osu!stable |

Treat dawsu!'s export behavior with care: its notes do not claim a null round-trip or minimal-diff patching, and this project's export rules in `docs/project-context.md` are stricter.
