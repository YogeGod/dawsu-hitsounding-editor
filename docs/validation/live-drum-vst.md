Historical live-preview validation: explicit drum WAV export was subsequently implemented. See drum-vst-export.md for the current workflow and checks.

# Live drum VST preview — 2026-11-05

Approved scope: existing drum/hitsound lanes trigger a VST instrument live during
timeline playback and lane audition. Melodic keysounding remains a separate workflow.
No keyboard MIDI input, piano roll or VST export is included. Garstka stays installed as-is.

## Implementation

- Playback → Drums VST: installed-plugin picker, native instrument window, enable/unload,
  trusted state load/save and a link to MIDI assignments. Empty racks can load a kit before Start live.
- UI-owned plugin lifecycle/state/message dispatch, separate bounded 512-frame live processing
  on the existing output callback. Isolated discovery remains in the scanner process.
- GM36/38/42/49 for default skin normal/clap/whistle/finish across all banks; explicit row
  assignments override. Unmapped lanes retain samples, mapped lanes suppress their sample.
- MIDI velocity follows hit volume, live mute/solo gates triggers, transport resets voices.
- Save/Generate behavior remains unchanged and the panel explicitly states the export boundary.

## Validation

- 644 tests passed: App/audio 242, Format 52, Lanes/editing/corpus 350.
- Eight added regressions cover offsets, block boundaries, mute/solo and audition velocity,
  pause/seek cleanup, release, detachment, processing failure and speed/pitch behavior.
- Kontakt 7.10.9 with GGD PV/P5, explicitly GM-mapped saved state: separate wall-clock audio
  consumer and owner GUI thread rendered four distinct non-silent drum lanes and a real-map
  timeline section; pause/seek/detach passed. Evidence: live-check-2.log and live-results-2/.
- Native editor opened inside the actual MainWindow process with the GGD GM kit loaded.
- Native and managed builds succeed; one existing CS4014 warning remains at MainWindow line 239.
- Impeccable mechanical detector returned no findings for the changed UI files.
- Test map and song were copied under E:/Claude/live-vst-editor-20261105/test-map.
  No source mapset edits or Garstka installation changes were performed.

Actual-device callback metering passed in the real editor on WASAPI shared, with no fallback:
68 timeline note-ons, VST peak 0.7588634 and lane-audition peak 0.35989133.
Evidence: final-editor/actual-editor-playback.json. This verifies supplied audio buffers,
not the user's physical speakers or ASIO hardware.

## Correctness

Reviewed detach/lifetime ordering, state access while audio is stopped/detached, independent
UI and audio entry points, byte/frame and MIDI bounds, empty-rack readiness, failure propagation,
sample fallback, transport reset and unchanged non-VST scheduling. Fixed the sample-boundary
regression found by the existing tests and avoided clearing sample tails when no VST is active.
No unresolved correctness finding in the approved preview scope.

## Standards

Simplify performed inline without subagents: reused the existing mixer, scheduler, output,
storage pickers and drum-map metadata. No additional audio output, prerender cache, general
plugin graph or background host abstraction was introduced. No new package dependency.
No remaining standards finding.

## Spec

Approved drums-first live timeline/audition behavior is implemented. Melodic piano roll,
keyboard input and export remain deferred as agreed. Plugin latency compensation, host
transport synchronization and arbitrary-plugin compatibility are not claimed by this test.
No commit, push or release was requested.

Finding counts: Correctness 0; Standards 0; Spec 0 within approved scope.

## Security review

Checked explicit plugin selection, isolated scanner arguments/timeout, bounded state reads,
native block/event bounds, detach-before-free, DLL search policy and absence of network or
installation changes. No newly exploitable finding identified. User-selected plugins are
trusted native code and can still crash their host process. No licensed plugin, content,
activation file or instrument state is included in repository source.
