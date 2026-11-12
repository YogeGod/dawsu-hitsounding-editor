# Drum hit detection

Status: **built in** since 2.2.11. **Edit → Drum hit detection…** finds kicks and snares on the user's PC and works in every install. Toms, hi-hats and cymbals are ignored.

## In the editor

1. Open a map, then **Edit → Drum hit detection…** (called Drum Hits auto-fill up to 2.2.8).
2. Choose the kick and snare destination lanes, or Skip either category. Start with Conservative sensitivity and 30 ms object tolerance.
3. Analyse the whole song, or use the Range tool first and leave Selected range checked. An aligned external drum stem is optional.
4. Review the proposed hits. Uncheck any to exclude them; the Listen button plays the source on the left and diagnostic cues on the right.
5. **Apply selected hits**, then play the map in the editor to hear your chosen samples. **Ctrl+Z** undoes the complete addition. Save/Generate remain separate actions.

Existing hits and volumes are preserved. Duplicates are skipped. Unmatched hits are excluded unless explicitly enabled for Generate/HS mode. Destination lanes are remembered. If the map, edits, song offset or source audio changes before Apply, the analysis is rejected instead of applying stale results. No audio is uploaded.

## How it works

Code: `src/OsuHitsound.App/DrumDetection.cs`, called from `DrumAutoFillDetector` in `DrumAutoFill.cs`. Model: `src/OsuHitsound.App/DrumModel/` (see its README for the licence, credit and conversion notes).

1. The song (or stem) is decoded with BASS at 44.1 kHz, mixed to mono 16-bit and cut to the selected range.
2. A magnitude spectrogram is computed the way madmom frames audio: 2048-sample symmetric Hann windows centred on every 441st sample (100 fps), zero padded, with madmom's int16 scaling.
3. ONNX Runtime runs `adtof-frame-rnn.onnx`: madmom's logarithmic filterbank (84 bands), `log10(1 + x)`, then the official ADTOF Frame_RNN weights. Songs longer than 10 minutes run in overlapping windows, as ADTOF's `predict()` does.
4. Kick (BD) and snare (SD) activations go through madmom's note peak picking with ADTOF's settings: thresholds 0.22 and 0.24 (multiplied by the sensitivity), a moving average over 10 frames before and 1 after, a moving maximum over 2 before and 1 after, and 20 ms combining.

### Parity with ADTOF's own pipeline (2026-11-12)

The reference is the same audio through madmom's real `SignalProcessor`, `FramedSignalProcessor`, STFT, `LogarithmicFilteredSpectrogramProcessor` and `NotePeakPickingProcessor`, with the network built in TensorFlow from the official checkpoint.

| Check | Result |
|---|---|
| ONNX model vs TensorFlow, same input | max difference 2×10⁻⁷ |
| Filterbank features vs madmom | max difference 2×10⁻⁷ |
| C# magnitude spectrogram vs madmom | max difference 1.5×10⁻⁵ (values up to 204) |
| Roselia - MIIRO, 30–50 s | 28 kicks, 15 snares; identical onsets and times, scores within 10⁻⁶ |
| Roselia - MIIRO, full song (92 s) | 187 kicks, 143 snares; identical; 0.5 s in C# |

`tests/OsuHitsound.App.Tests/DrumDetectionTests.cs` keeps a synthetic drum file (`Fixtures/drums-synthetic.wav`, generated, no third-party audio) with its reference onsets, and madmom's peak picking on a fixed curve. `tools/DrumAutoFill/EditorCheck` drives the real editor window headlessly through detect, review, apply, undo and cancel.

Sensitivity matters less than with the old prototype: ADTOF's activations are confident, so on MIIRO thresholds × 0.75 to × 1.25 change the counts by at most 5.

## The earlier Python prototype

Everything below describes the Python prototype that the 2.2.8–2.2.10 test copy used. The prototype is kept in `tools/DrumAutoFill/` for evaluation against annotated MIDI. It used the unlicensed ADTOF-pytorch port and its librosa features, so its numbers differ slightly from the built-in detector.
### Confirmed design

Windows first. Detect actual kick and snare onsets from the map song or an optional drum stem, locally. A model download is acceptable; audio is not uploaded. Analyse the selected time range when present, otherwise the whole song. The user removed tom hits from the prototype after the initial evaluation.

The editor test chooses and remembers destination lanes, with each category optional. Preview proposed hits and choose sensitivity before analysing. Prefer fewer false detections. Snap to nearby gameplay objects within an adjustable tolerance. Show unmatched candidates separately, skipped by default, with an option to include them for Generate. Preserve existing hits and map volume, avoid duplicates, and apply in one undo step.

The CLI still only exports detections and a read-only object-matching preview. The editor test creates a separate plan against the current edit session and adds only the reviewed hits. Its 30 ms default matching tolerance is a trial value; it does not change the editor's existing 2 ms anchoring tolerance.

### Detector evaluated

[ADTOF-pytorch](https://github.com/xavriley/ADTOF-pytorch), commit `85c192e78f716ea0b111cc8a5ee4a8f6a3a4f8a9`, supplies a five-class pretrained model. The runner retains only kick and snare output; toms, hats and cymbals are excluded from candidates, MIDI, audition cues, exported activations and evaluation metrics. The underlying pretrained model is unchanged. Weights SHA256: `1bc986e596ec47ba0b44916f87cd4a39f0b2bec23596df3fb5d0e87749217320` (3,617,805 bytes).

The runner requires pretrained weights, uses strict loading, and refuses a partial/random model. Loading explicitly uses PyTorch's `weights_only=True`. Its output score is a raw activation, not a calibrated probability that a candidate is correct.

Third-party code and weights are installed in an ignored environment, not copied into application sources or a release. [Original ADTOF](https://github.com/MZehren/ADTOF) declares CC BY-NC-SA 4.0; distribution of the port and weights needs a separate provenance/licence review before bundling. The annotated evaluation files come from [MDB Drums ++](https://github.com/xavriley/MDBDrumsPlusPlus), attributed there to the original MDB Drums / MedleyDB recordings and published under CC BY-NC-SA 4.0. They remain local evaluation artifacts.

### Run the prototype

From the repository root, create an environment and install the pinned detector:

```powershell
python -m venv artifacts/drum-autofill/venv
artifacts/drum-autofill/venv/Scripts/python.exe -m pip install -r tools/DrumAutoFill/requirements.txt
```

Stage a WAV excerpt yourself, or use ffmpeg. Originals are only read. Put the staged map in its own folder; the export tool needs the map's timing and object lines, not audio/sample copies.

```powershell
ffmpeg -i path/to/song.mp3 -ss 30 -t 20 -ar 44100 -ac 1 artifacts/drum-autofill/excerpt.wav
dotnet run --project tools/DrumAutoFill/MapMoments -- path/to/staged.osu artifacts/drum-autofill/moments.json
artifacts/drum-autofill/venv/Scripts/python.exe tools/DrumAutoFill/prototype.py --audio artifacts/drum-autofill/excerpt.wav --audio-start-ms 30000 --moments artifacts/drum-autofill/moments.json --output artifacts/drum-autofill/run-01
```

Both outputs must be new: the exporter refuses to overwrite a JSON file, and the detector requires a new result directory. Use WAV input for this experiment. `--audio-start-ms` is the excerpt's position in the original song; `--song-offset-ms` is the editor's song offset, added to song time. For an external stem, alignment to the original song is the caller's responsibility. Defaults: CPU, four threads, 30 ms object tolerance, threshold scale 1.25. Scale 1 uses the upstream class thresholds; larger values keep fewer candidates.

Each result directory contains:

- `candidates.json`: absolute timeline times, raw scores, optional nearest object/node and its volume, snap delta, collision/unmatched status, hashes, counts and timings. If multiple same-class candidates target one node, the strongest wins in the preview. This is not existing-lane deduplication.
- `activations.npy`: kick/snare activations with shape `[1, frames, 2]`. JSON schema version 2 records `enabledDrums` and `activationLabels` as `["kick", "snare"]`.
- `detected.mid`: all detected candidates at excerpt-relative times, including unmatched/duplicate preview candidates. Diagnostic only; it is not the proposed set to apply.
- `audition.wav`: original audio on the left, synthesized kick/snare cues on the right, at detected unsnapped times. These cues are not the user's destination samples. Any toms in the original recording remain audible on the left, but no tom cues are generated.

For annotated evaluation, add `--reference-midi path/to/annotation.mid`. This must be a drum-only GM annotation file aligned to the WAV excerpt, even if it uses MIDI channel 1 rather than channel 10. Scoring groups GM kick 35/36 and snare 38/40, ignoring other instruments, with chronological one-to-one matching within 50 ms. It is a small functional check, not a representative held-out benchmark or ranking against other detectors.

### Measured local results

Current evidence: `artifacts/drum-autofill/kick-snare/`. Thresholds were not trained or tuned on these recordings. Both annotated examples were rerun with kick/snare-only output at conservative threshold scale 1.25.

| Annotated recording | Kick correct / proposed / reference | Snare correct / proposed / reference |
|---|---|---|
| MusicDelta 80sRock, 36.92 s | 63 / 63 / 64 | 35 / 35 / 35 |
| MusicDelta Punk, 28.76 s | 82 / 84 / 84 | 32 / 32 / 44 |

The earlier three-class experiment found both missed and false tom hits. Its artifacts remain under `artifacts/drum-autofill/benchmark/` for historical reference; they are not the current prototype outputs. The current runner intentionally excludes toms rather than attempting to correct them.

A staged excerpt of Roselia - MIIRO, 30–50 s, produced 28 kick and 14 snare candidates. At 30 ms tolerance, 41 matched gameplay moments and 1 was unmatched. It took approximately 0.71 s inside the runner (0.44 s features, 0.22 s neural inference), excluding Python/import startup and environment installation. The source map's volume is retained in the preview. There are no manually verified drum annotations for this song; these numbers demonstrate execution and alignment, not accuracy.

Listen to `artifacts/drum-autofill/kick-snare/map/audition.wav` for the current kick/snare-only preview. The retained kick/snare events match the corresponding events in the original three-class run.

### Verification

Run `python -m unittest discover -s tools/DrumAutoFill -p test_prototype.py -v`. Five tests cover tolerance boundaries, stable stacked-object choice, same-class collisions, simultaneous different drums, empty moments, one-to-one accuracy scoring and exclusion of strong tom peaks. Export checks also inspect JSON, MIDI, activation channels and synthesized audition cues. The exporter is built independently of the desktop/Android solution.

Editor integration tests live in `tests/OsuHitsound.App.Tests/DrumAutoFillTests.cs`. They cover one-step Undo/Redo, preserving hits/volumes, deduplication, skipped categories, empty hitsound difficulties, unmatched handling and range boundaries. `tools/DrumAutoFill/EditorCheck <work folder> <song.wav>` exercises the built-in detector through both dialogs, deselection, Apply, Undo/Redo, cancellation and rejection after edits change. It also confirms that Apply alone leaves `.osu` bytes unchanged.

Continue auditioning representative acoustic/electronic songs to judge whether the cleanup effort is worthwhile. Physical output and detection correctness on arbitrary songs need user listening; passing an automated editor-flow check does not establish either.
