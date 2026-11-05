# Drum VST WAV export validation

Date: 2026-11-05 (Europe/Amsterdam).

Implemented explicit Playback > Drums VST > Render VST drums. The review lists lanes,
notes, velocities, hit counts, render count and cache reuse. Save/Generate never invokes
VST rendering. Rendered hits use WAV playback, with project metadata retaining their
original parent lane, MIDI note and velocity for an explicit rerender.

Rendering captures the live kit state into a separate owner-thread instance, processes
44.1 kHz stereo in 512-frame blocks with conservative wall-clock pacing, and sends
note-off at 100 ms. It flushes between notes, retains leading silence and release tails,
and stops with a warning rather than accepting an active tail at 15 seconds. A one-second
quiet window after at least two seconds ends a completed sound. This is a silence-based
tail heuristic; arbitrary instruments with long delayed echoes are not exhaustively tested.

Output is staged before installation. Content-hash filenames never overwrite existing
files; rollback removes only files created by that attempt. Cache reuse validates WAV
hashes and fingerprints the plugin binary/bundle plus captured state and render settings.
Each distinct note/velocity is rendered once; different source lanes may receive identical
byte copies so their grouping and edit identity stay independent.

The conversion is one history step, preserves timing/anchors and unrelated layer gain,
and sets newly baked layers to 100 percent. Later gain changes survive rerenders.
Custom parents and velocity children persist in editor project data; parent mute/solo
applies to their children. Existing sample pairing/mixing handles Save/Generate.

## Automated checks

- 658 tests pass: app/audio 254, Format 52, Lanes/editing/corpus 352.
- New regressions cover conversion, unrelated volumes, undo/redo, repeat no-op,
  grouping in both views, parent mute/solo, metadata persistence, custom parents,
  post-render gain, stale assignments, cache tampering, rollback, note-off timing,
  leading silence, cancellation, invalid audio and the 15-second tail boundary.
- Logs: E:\Claude\drum-vst-export-20261105\final-tests.log.
- Build retains existing platform warnings and CS4014. NuGet vulnerability lookup was
  unavailable in this environment (NU1900); no dependencies were added.

## Kontakt/GGD check

Kontakt 7 with the trusted P5 Matt Halpern GM state rendered distinct non-silent
kick, snare, closed hat and crash files. Peaks: 0.43340915, 0.6129741, 0.09325034,
0.056221634. On a copied real map, 387 hits converted to four WAV assignments;
cancellation, undo/redo, repeat no-op, WAV routing and actual Save/reopen passed.
Evidence: E:\Claude\drum-vst-export-20261105\test-editor\export-results.json,
export-check.log and renders/.

Native UI checks on a second copy confirmed the review list, progress/cancel,
restored controls after cancellation and zero installed files after cancellation.
A complete render then converted all 1156 mapped hits into five WAV assignments.
No original mapset was edited. The original .osu SHA-256 still matches:
4359D7E5565DC698A4E772D4D2CC00EBCE13D7CE37D01FB055FAF13ADFB111F3.
These checks do not establish physical-speaker or ASIO playback.

## Correctness review

Reviewed the complete feature diff against the live-VST snapshot and checked target
repository hashes before transfer. Fixed stale-edit handling, close/shortcut blocking,
custom-parent persistence, undo layout restoration and drum-row dragging of children.
No remaining correctness findings in reviewed paths.

## Standards review

Performed simplify inline: removed the unused filename-based velocity helper, reused
existing dialog/layer/history/export infrastructure, simplified metadata merging and
avoided duplicate grouping work. No multi-agent workflow, commit, push or release.
No remaining standards findings.

## Spec review

The approved drum export workflow and follow-up WAV-playback decision are implemented.
Melodic piano roll and hardware MIDI remain deferred. No changes to Garstka installation.
No remaining spec findings within the approved drum export scope.

Review totals: correctness 0 open; standards 0 open; spec 0 open.

## Security review

Checked path construction, metadata parsing, staged-file cleanup, cache integrity,
existing-file collisions, plugin state handling and dependency changes. Filenames are
constructed from validated integers and hashes, metadata rejects traversal/invalid parent
links, cached file content is rehashed, and plugin state stays in memory. No credentials,
kit state, sample libraries or test recordings are included in source changes. No new
exploitable security findings. Native plugin crashes remain in-process, as before.
The manual repeat review requested fresh renders for Kontakt rather than finding an
exact cache match. Cache reuse is deliberately conditional on exact serialized plugin
state and binary hashes; volatile plugin state may therefore cause a cache miss even
without an intentional kit edit. Exact cache reuse and identical-assignment no-op are
covered by tests, but cross-invocation Kontakt cache hits are not claimed.


The native UI Save path also completed on the 1156-hit copy, using three new paired/fused samples. The final build reopened that saved copy successfully. Final editing/corpus log: final-lanes-tests.log (352 passed).

Pre-commit review (2026-11-05): simplify, correctness, standards/spec and security passes repeated inline for the full VST/MIDI integration. No additional code changes required. Updated stale workflow/repair documentation; proprietary binaries, state and audio excluded.
Pre-commit full repository test run passed: 254 app/audio + 52 Format + 352 Lanes/editing/corpus = 658 tests. Log: E:\Claude\drum-vst-export-20261105\precommit-tests.log.
