# Windows x64 VST3 host proof - 2026-11-05

Implemented a JUCE 8.0.12 native instrument host with a C# bridge in OsuHitsound.Audio
and a separate Vst3Smoke executable. Existing editor startup, Hitsound mode and release
packaging do not load the experimental DLL. Build instructions and limits are in
`native/VstHost/README.md`.

## Confirmed on this PC

Final regression suite: 632 passed, 0 failed, 0 skipped (App 234, Format 52, Lanes 346), including the copied map corpus. Six additional live host guard checks passed.

- MSVC 19.44 and Windows SDK 10.0.26100.0 compiled the native DLL successfully.
- The managed harness builds against the existing .NET 10 audio project.
- Native editor for installed Kontakt 7 opened inside the proof host.
- Loaded GGD P5 Matt Halpern Signature Pack, preset `00 Startup`, through Kontakt's
  library browser. The user referred to this kit as GGD PV.
- Saved 1,678,881 bytes of instrument state and restored it into a new Kontakt instance.
- Reopened that state from disk in two subsequent clean host processes and rendered it.
- MIDI notes 24..83, channel 10, note-on velocities 48 and 112, note-offs after 100 ms.
  Each sweep is 20 seconds of stereo float WAV at 44,100 Hz, with a five-second test tail.
- Both renders are finite and non-silent; no per-render normalization is applied.
- Six managed guards pass: undersized output, oversized block, out-of-block MIDI,
  unsupported MIDI status, empty state and wrong-thread access.

| GGD P5 sweep | Peak absolute sample | RMS | Samples above 0.000001 |
|---|---:|---:|---:|
| Velocity 48 | 0.059604 | 0.003510 | 1,694,333 |
| Velocity 112 | 0.656781 | 0.019449 | 1,758,052 |

The clean-process test exposed and fixed an initialization-order defect: restoring
Kontakt state before configuring its audio processor could trigger Kontakt's crash
handler. The host now negotiates the stereo main bus and prepares processing before
state restoration. Both clean-process reruns passed after that change.

Serum 2 also passed as an independent control instrument: editor-independent loading,
state saving/restoration and non-silent renders. This does not establish universal
VST3 compatibility or a particular velocity response for its preset.

## GGD Matt Garstka standalone-platform test

The installed GGD Matt Garstka VST3 wrapper opens, but its own editor reports
`Error: Matt Garstka Signature Drums cannot be loaded`; it exposes no usable audio
channels in that state. A prior installer log records failure to replace its VST3
binary followed by Ignore, and the expected core artifact folder is empty. These
are installation concerns, not proof of a host defect or a license diagnosis.
No activation/account settings were changed. The user switched the requested test
to Kontakt plus GGD PV, which passed as described above.

The separately installed older Kontakt rejected P5 as requiring a newer Kontakt;
Kontakt 7 was used for the successful test.

## Verification boundaries

These checks prove the native/managed boundary, MIDI-triggered stereo rendering and
state reopening for the tested local kit. They do not prove live keyboard latency,
real-time mixer safety, controller/tempo accuracy, full release-tail detection, osu!
assignment/export, plugin crash isolation or Cubase piano-roll editing behavior.
The WAVs have a fixed test tail; they do not implement the planned full-tail exporter.
The script/resources warnings emitted by Kontakt are retained in local logs; the kit
nevertheless loaded and rendered. Plugin UI styling is controlled by the plugin.

## Inline review

### Correctness

Fixed runtime lifetime/owner-thread handling, explicit stereo bus negotiation,
UTF-8 error decoding and the Kontakt processor-before-state initialization defect.
Bounds and finite-sample checks protect the managed/native audio-buffer boundary.
No remaining correctness finding within the documented offline proof scope.

### Standards

Reused JUCE hosting and existing NAudio WAV/output facilities. Consolidated preparation
into one native function. No generic DAW framework, graph engine or new UI shell added.
The approved host-first plan was followed; no commit, push or release was requested.

### Spec

This completes the instrument-host proof step. The Keysound project, piano roll,
live MIDI/audio integration and synced-note exporter remain unimplemented.

Finding counts: Correctness 0 remaining; Standards 0; Spec 0 within the host-proof
step, with full Keysound-mode implementation explicitly pending.

### Security

Checked plugin/state paths, DLL search, buffer/event/state limits, dependency pinning,
and repository contents. No exploitable finding in the exposed proof workflow.
Plugins are explicitly chosen local native code and run in-process; they are trusted,
not sandboxed. State is bounded to 64 MiB. No plugin binaries, library samples, local
state blobs, activation data or crash dumps are included in the source changes.
JUCE licensing/distribution remains a prerequisite before packaging this into a release.

## Local evidence

Workspace: `E:\Claude\vst3-proof-20261105`.

- `kontakt7-p5-2.log`: initial browser-loaded P5 render.
- `kontakt7-p5-reopened-2.log`, `kontakt7-p5-reopened-3.log`: corrected clean-process reopens.
- `artifacts/kontakt7-p5-reopened-3/results.json`: machine-readable render measurements.
- `artifacts/kontakt7-p5-reopened-3/midi-sweep-v48.wav` and `midi-sweep-v112.wav`: listenable evidence.
- `tests-final.log`: existing unit/compiler/editing/map-corpus suite.
- `native-final-build.log`, `native-restore-fix-build.log`, `managed-restore-fix-build.log`: build logs.

Generated audio and private plugin state remain local test artifacts.

## Follow-up diagnosis

The later GM-state investigation exposed intermittent Kontakt reload crashes,
so the earlier successful reopens do not establish general reload reliability.
Garstka's VST3 also displays its load error in REAPER; its standalone kit UI loads.
See `vst3-reload-diagnosis.md` for the controlled comparison and limits. Neither a
Kontakt-only root cause nor a successful Garstka audio control has been established.

## Reload correction verified

Follow-up fix: isolated discovery removes the temporary scan instance from the
rendering process. Eight generic fresh-process reloads passed, plus native editor
reopen, state round-trips and full-map audio. State capture now uses one snapshot.
See `vst3-reload-fix.md`. The Garstka VST3 update was skipped by its installer and
left binary 1.0.5 beside metadata 1.1.5; the user declined repair and requested leaving the installation as-is.
