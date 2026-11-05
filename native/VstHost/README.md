# Experimental VST3 instrument host

This host supports the experimental Drums VST preview in the existing lane editor
(Playback → Drums VST). Melodic keysounding and its piano roll remain separate future work.
The host does not run on editor startup unless explicitly loaded. The separate
`tools/Vst3Smoke` executable loads an explicitly chosen Windows x64 VST3 instrument,
opens its native editor, saves/restores opaque state and writes stereo test WAVs.

## Build

Use CMake 3.22+, Git and a Visual Studio 2022 C++ developer shell with the Windows SDK.
JUCE is pinned to 8.0.12, commit `29396c22c93392d6738e021b83196283d6e4d850`.
Its source is fetched at configure time. For an existing verified checkout, pass
`-DFETCHCONTENT_SOURCE_DIR_JUCE=<absolute-path>`.

```powershell
cmake -S native/VstHost -B artifacts/vst3-native -A x64
cmake --build artifacts/vst3-native --config Release
$hostDll = (Resolve-Path artifacts/vst3-native/Release/dawsu_vst3.dll).Path
dotnet build tools/Vst3Smoke/Vst3Smoke.csproj -c Release -p:Vst3HostDll="$hostDll"
tools/Vst3Smoke/bin/Release/net10.0/Vst3Smoke.exe "C:\Program Files\Common Files\VST3\YourInstrument.vst3" "E:\new-vst3-test-output" --editor
```

MSVC runtime prerequisites must also be available. The smoke executable targets .NET 10.
`--editor` waits for you to load an instrument and create an empty `render.ready` file
inside the output directory (ten-minute limit, then fails without rendering an empty rack).
`--play` plays the
rendered files at 25% gain. An output directory must be new. Default tests sweep MIDI
notes 24 through 83 on channel 10 at velocities 48 and 112; these are discovery notes,
not a claim about an instrument's drum map. Each render has a fixed five-second tail
for this test only. The editor drum exporter has a separate silence-based tail policy with a 15-second limit.
`--state=<absolute-path-to-instrument-state.bin>` reopens a saved local kit without
using the browser again. Use only trusted plug-ins and their trusted state files.

## Boundary and limitations

- C ABI in `host.cpp`; managed entry point `Vst3Instrument` in OsuHitsound.Audio.
- All instances share one STA owner thread for the process lifetime. Offline Process, state, editor and disposal
  belong to that thread; only the separately configured live processor has an audio consumer. The native runtime lives as long as any instance does.
- Processing is synchronous/offline. It allocates/marshals and pumps GUI events;
  use the separate IDrumInstrument live entry point for the real-time audio callback.
- Only stereo main output is supported. Other outputs are disabled through explicit
  bus negotiation. An incompatible/unready instrument fails instead of silently mixing buses.
- MIDI events carry offsets relative to each block. Channel voice messages are supported;
  SysEx, musical transport/tempo context, plugin latency compensation and MIDI keyboard input are pending.
- State is opaque, limited to 64 MiB, and is stored only in the chosen local test directory.
- Dispose explicitly on the owner thread. Native plugin crashes can terminate the process;
  this is not a sandbox or a compatibility guarantee for every VST3.
- GGD, its sample library, presets, activation data and other third-party plugins are never
  copied into the repo or included with dawsu!.

## Licensing

JUCE 8 modules are dual-licensed under AGPLv3 and the JUCE commercial licence:
https://github.com/juce-framework/JUCE/blob/29396c22c93392d6738e021b83196283d6e4d850/LICENSE.md

The local VST build is not included in the normal editor release package. Before distributing a
JUCE-linked dawsu! build, choose and meet the applicable licence terms and include
JUCE and its module dependency notices. The VST3 SDK's licence alone does not grant
a licence to JUCE or to any instrument/library. No repository-wide licence is changed here.

## Isolated discovery and state capture

The native build now produces both `dawsu_vst3.dll` and `dawsu_vst3_scan.exe`.
Keep the DLL, scanner and matching managed wrapper together; rebuilding either
validation tool with `Vst3HostDll` copies both native files. The descriptor-aware
open entry point is `dv_open_v2`, so an older DLL fails with a missing entry point
instead of interpreting the new argument layout incorrectly.

Discovery runs in the short-lived scanner process, with a 30-second timeout and
64 KiB descriptor bound. The render process loads the selected descriptor without
running JUCE's slow scan there. That scan can instantiate and discard a temporary
plug-in; avoiding it eliminated the tested delayed Kontakt message-dispatch crash.
This isolates discovery only. A crash in the actual rendering instrument can still
terminate its host process.

State saving captures once at the size query, then copies that same bounded snapshot.
It no longer asks the instrument to serialize twice for a single SaveState call.

## Live drum preview

Build the app with `-p:Vst3HostDll=<matching native DLL>` to copy the bridge and scanner
beside the app. Playback → Drums VST selects an installed instrument, opens its native
window and optionally loads/saves a trusted local instrument state. An empty rack stays
open for kit loading; Start live retries stereo preparation when the kit is ready.

`EnableRealtime` configures the instrument on its STA owner. `dv_process_realtime` runs
on a single audio consumer, without GUI dispatch or lazy preparation. The mixer splits
VST blocks at 512 frames and schedules channel-10 MIDI at block-relative sample offsets.
Detach under the mixer gate before state access, changing configuration or destruction;
detachment waits for an in-flight callback. The UI timer dispatches plugin messages.

Default skin sounds map normal/clap/whistle/finish to GM36/38/42/49. Explicit drum-map
MIDI assignments override defaults; a cleared MIDI assignment retains sample playback.
The first assigned alias wins so one shared sample lane plays once. Mapped lanes replace
sample preview only; other lanes and passive loops retain their samples. Hit volume
controls MIDI velocity. Seek/pause/cycle reset active VST voices; ordinary one-shots
receive note-off after 100 ms while natural release tails continue to process.

Render VST drums converts mapped hits into grouped WAV variants as one undoable edit.
Save/Generate then use those assigned samples. See docs/validation/drum-vst-export.md.
Melodic editing, hardware MIDI input and plugin crash isolation remain pending.
