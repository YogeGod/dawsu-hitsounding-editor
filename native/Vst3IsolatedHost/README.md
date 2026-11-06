# Isolated VST3 instrument host

`dawsu_vst3_host.exe` hosts one VST3 instrument for one Keysound track, in its own process. A plug-in that crashes
takes down only its own track: dawsu! sees the process exit, silences the track and offers to reload it from its last
saved state. dawsu! starts and stops hosts itself (`OsuHitsound.Audio.IsolatedVst3`); nothing runs at editor startup.

It is built directly on the Steinberg VST 3 SDK, which is MIT-licensed since version 3.8 (no JUCE, unlike the
experimental Drums VST preview in `native/VstHost`).

## Build

Needs a VST 3 SDK 3.8+ checkout with the `base`, `pluginterfaces`, `public.sdk` and `cmake` submodules
(`git clone --branch v3.8.1_build_84 https://github.com/steinbergmedia/vst3sdk`, then
`git submodule update --init base pluginterfaces public.sdk cmake`), and MSVC with the Windows SDK. With a portable
toolchain folder (MSVC, Windows Kits, CMake and Ninja), `build.ps1` sets up the environment:

```powershell
native/Vst3IsolatedHost/build.ps1 -Tools <toolchain folder> -Sdk <vst3sdk folder> -Out artifacts/vst3-isolated
```

Copy `artifacts/vst3-isolated/bin/dawsu_vst3_host.exe` next to dawsu! (or build the app with
`-p:Vst3IsolatedHostExe=<path>`). Without it, Keysound tracks can only use the bundled piano.

## Protocol

- `--scan <plugin.vst3>` prints `CLASS \t <uid> \t <name> \t <vendor> \t <subcategories>` per instrument class.
- `--host <name> <parentPid> <plugin.vst3> [<uid>]` loads the instrument, prints `READY \t <name>` (or `ERROR`), and
  then:
  - **Audio.** The shared memory `Local\<name>` holds a header, up to 1024 MIDI events and 4096 stereo frames. Its
    layout is mirrored in `IsolatedVst3.cs`. dawsu! writes the block, bumps `requestSeq` and signals `<name>-req`.
    The host renders, sets `doneSeq` and signals `<name>-done`. dawsu! waits at most most of the block's duration,
    then plays silence and skips requests until the late block is done.
  - **MIDI.** Notes become VST3 note events. Sustain, modulation and pitch bend go through the plug-in's
    `IMidiMapping` as parameter changes. Edits made in the plug-in's own window are passed back to its processor.
  - **Control.** One line per command on stdin, one reply per line on stdout: `SHOW`, `HIDE` (the plug-in's own
    editor window, owned by the host), `GETSTATE` → `STATE \t <base64>`, `SETSTATE \t <base64>`, `QUIT`.
  - **Lifetime.** The host exits when dawsu! exits.

## Checked

- Serum 2: loaded in 264 ms, rendered audio, state round trip, no late blocks.
- Kontakt 7 (empty rack): loaded in 928 ms, state round trip.
- `tools/FakeVstHost` speaks the same protocol for the automated crash, stall and load-failure tests
  (`IsolatedVst3Tests`).

Plug-ins are native code chosen by the user. Isolation contains crashes; it isn't a security sandbox.
