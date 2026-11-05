# Random default-hitsound map audio proof

Build with .NET 10 and the experimental native VST3 DLL (see `native/VstHost/README.md`):

```powershell
dotnet build tools/Vst3MapRender -c Release -p:Vst3HostDll="<absolute-path-to-dawsu_vst3.dll>"
tools/Vst3MapRender/bin/Release/net10.0/Vst3MapRender.exe "F:\osu!\Songs" "C:\Program Files\Common Files\VST3\Kontakt 7.vst3" "<trusted-P5-instrument-state.bin>" "<new-output-directory>"
```

The tool initializes Kontakt on its STA thread before scanning maps, restores a trusted
locally saved P5 kit and randomly orders real map candidates. It accepts a standard
difficulty with 100–2500 moments, 30–240 seconds, no parse warnings, no passive sounds,
only resolved default hitsounds, and at least 20 each of clap, whistle and finish.
It copies the `.osu` into the output directory and verifies the original's hash afterward.
All real mapset access is read-only. It does not create a playable modified difficulty.

Approved conversion across Normal/Soft/Drum: hitnormal to Kick (GM36), hitclap to Snare
(GM38), hitwhistle to Closed Hi-Hat (GM42), hitfinish to Right Crash (GM49).
The note numbers come from `DrumMapDefaults`; object/node times come from `Decompiler`.
osu!stable played volume is scaled to MIDI velocity. MIDI note-offs occur after 100 ms.
These drum triggers are a deliberate conversion, not an audio-identical osu! recreation.

Outputs: `GGD-PV-drums.wav`, `Song-with-GGD-PV.wav` when the song can be decoded, and
`render-report.json` with the full trigger list, counts, peaks and source hash.
WAVs are stereo float at 44.1 kHz. The optional song mix applies one 0.5 master gain.
The render waits for two seconds below -90 dBFS after the last event/song, with a
30-second tail limit. Reaching that limit reports an incomplete tail and exits with failure.
The tested P5 preset and sample library must already be installed locally; they are not bundled.

This is an offline validation tool. It does not add live MIDI or VST controls to the editor.

For a repeat of a particular map, pass its `.osu` path as the first argument.
Append `--editor` to configure a fresh live instance instead of restoring state.
In GGD, select Settings > Mappings > Load Preset > GM and verify the GM label;
then create `<new-output-directory>.ready`. Setup processes silent audio so Kontakt
scripts initialize. The saved state argument must still be a trusted local file.
The exact live instance renders the map and saves its current state with outputs.
Four separate `Check-*.wav` files and `gm-checks.json` validate notes 36/38/42/49.
Earlier fresh-process GM state restores crashed Kontakt. The matching isolated-scanner
bridge fixed the tested reload failures; see docs/validation/vst3-reload-fix.md.
