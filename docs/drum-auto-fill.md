# Drum hit detection

Status: built in. **Edit → Drum hit detection…** finds kicks and snares on the user's PC and adds them as hits. Toms, hi-hats and cymbals are ignored. It now uses signal processing with no trained model; versions 2.2.11–3.0 used the ADTOF neural model (see the end of this page).

## Try it in the editor

1. Open a map, then **Edit → Drum hit detection…** (called Drum Hits auto-fill up to 2.2.8).
2. Choose the kick and snare destination lanes, or Skip either category. Start with Conservative sensitivity and 30 ms object tolerance.
3. Analyse the whole song, or use the Range tool first and leave Selected range checked. An aligned drum stem is optional and gives much cleaner results than a full mix.
4. Review the proposed hits. Uncheck any to exclude them; the Listen button plays the source on the left and synthesised cues on the right.
5. **Apply selected hits**, then play the map to hear your chosen samples. **Ctrl+Z** undoes the complete addition. Save/Generate remain separate actions.

Existing hits and volumes are preserved. Duplicates are skipped. Unmatched hits are excluded unless explicitly enabled for Generate/HS mode. Destination lanes are remembered. If the map, edits, song offset or source audio changes before Apply, the analysis is rejected instead of applying stale results.

## Detector: signal processing, no machine learning

`src/OsuHitsound.Audio/DrumOnsets.cs` runs in-process in C#. There is no model, Python runtime or download, and no audio leaves the machine. Steps:

1. Mono mix of the analysed range, peak-normalised so the result does not depend on the song's level.
2. STFT: 2048-sample Hann window, 10 ms hop, bins up to 11 kHz.
3. Harmonic/percussive separation: 17-point median filters across time (harmonic) and frequency (percussive), soft mask `P²/(P²+H²)`. This suppresses sustained guitar, bass and vocal partials.
4. Log spectral flux (`log(1 + 3·|X|)`, positive increase over a 3-bin maximum of the previous frame) in four bands: kick 30–120 Hz, snare body 150–400 Hz, snare wires 1.5–5 kHz, high noise 7–11 kHz.
5. Each curve is scaled so a typical strong hit is about 1. The snare curve is the geometric mean of body and wires, so both must rise together.
6. Peak picking: local maximum within ±30 ms, above `0.3·s` absolute and `0.12·s` over the local mean (−100/+30 ms), at least 60 ms between peaks of the same drum. `s` is the dialog's threshold scale: Conservative 2.0, Normal 1.5, Sensitive 1.0.
7. Kick bleed rule: a snare candidate is dropped when it is weaker than 0.75 × a coinciding kick (unless its own score is ≥ 1), or when its body is under 1.3 × the kick and its high noise under 0.5 × the kick.
8. Times get a +4 ms bias correction (measured), then the excerpt start and song offset.

### Beat grid

The dialog's **Beat grid** option defaults to 1/4. The other choices are 1/4 + 1/3, 1/8, 1/8 + 1/6 and Off. It works in `DrumAutoFillPlan.Create` using the map's red lines, through `ArrangeModel.Snap`:

1. **Lag.** A map's timing and its decoded audio often disagree by a constant 10–30 ms. For Roselia - MIIRO, detections run about 20 ms after the grid, and ADTOF's did too. `GridShift` measures this lag from the detections themselves: the median distance to the first division's lines, refined twice over the deltas within 25 ms. Up to ±50 ms is compensated. With fewer than 8 detections the lag is not measured.
2. **Filter.** After allowing for the lag, a detection is kept only if it is within 20 ms of a line of an allowed division. The window narrows to a third of the step for fast tempos or fine divisions. A kept hit moves exactly onto its line; others are counted as dropped off the grid.
3. **Review.** The dialog reports the number dropped and the measured lag. Where the map has no red line, nothing is dropped.

Without lag compensation, a 20 ms window would have dropped about half the real hits on MIIRO.

`Score` is the normalised peak height, a strength rather than a probability. `DrumAutoFillDetector.AuditionWav` writes the review audio: the analysed audio on the left, with a synthetic thump (kick) or noise burst (snare) on the right at each detected time.

## Measured results

`tools/DrumAutoFill/DspEval` scores the detector against drum-only GM MIDI annotations (kick 35/36, snare 38/40, one-to-one matching within 50 ms). The constants above were tuned on these same two clips, so these numbers are optimistic. They are not a held-out benchmark.

| Annotated recording | Conservative (2.0) kick correct / proposed / reference | Conservative snare | Normal (1.5) kick | Normal snare |
|---|---|---|---|---|
| MusicDelta 80sRock, 36.92 s | 63 / 63 / 64 | 21 / 21 / 35 | 64 / 64 / 64 | 23 / 24 / 35 |
| MusicDelta Punk, 28.76 s | 81 / 81 / 84 | 30 / 31 / 44 | 83 / 93 / 84 | 33 / 38 / 44 |

For comparison, the earlier ADTOF neural detector scored 63/63/64 and 35/35/35 on 80sRock, and 82/84/84 and 32/32/44 on Punk. Kick quality is about the same. Snare recall is lower: some quieter snares and snares played together with kicks are missed.

Mean timing error is within ±2 ms. Analysis takes roughly 0.2–0.5 s per 30 s of audio.

**Dense full mixes are the weak point.** On the Roselia - MIIRO 30–50 s excerpt (no ground truth), Conservative proposes about 42 kicks and 40 snares. ADTOF proposed 28 and 14 there, and all of those are among the DSP detections. Some of the extras may be real hits that ADTOF skipped, but many are likely guitar or bass attacks.

The beat grid barely changes this. Measured with the editor's own BASS decode of the map's MP3, after lag compensation:
- at Conservative with the 1/4 grid, kicks go from 37 to 34 and snares stay at 38, while 27 of 28 ADTOF kicks and all 14 ADTOF snares are kept
- at Sensitive, kicks go from 74 to 61 and snares from 69 to 64

The extras sit on the beat, so they can't be removed by timing alone. Use a drum stem where possible, keep Conservative, and listen before applying.

Run the scorer:

```powershell
dotnet run --project tools/DrumAutoFill/DspEval -c Release -- --scale 2 artifacts/drum-autofill/benchmark/80sRock.wav artifacts/drum-autofill/benchmark/80sRock.mid artifacts/drum-autofill/benchmark/Punk.wav artifacts/drum-autofill/benchmark/Punk.mid
```

Set `DSP_DIAG=1` to see which reference instrument each false detection lands on. Add `--osu map.osu --start-ms <excerpt start> --grid 4` to apply a plain grid filter. That filter does not compensate for lag, so use excerpts decoded the way the editor decodes them. The annotated recordings come from [MDB Drums ++](https://github.com/xavriley/MDBDrumsPlusPlus) (CC BY-NC-SA 4.0) and stay local evaluation artifacts.

## Verification

- `tests/OsuHitsound.App.Tests/DrumOnsetsTests.cs`: a synthetic 120 BPM kick/snare/hi-hat pattern. All kicks and snares are found; hi-hats and kicks are never labelled snare; results are identical at 1/20 the level; silence gives nothing; cancellation and argument checks work; the audition WAV is correct.
- `tests/OsuHitsound.App.Tests/DrumAutoFillTests.cs` covers:
  - plan creation, one-step Undo/Redo, preserving hits and volumes, deduplication, skipped categories, unmatched handling and range boundaries
  - the grid: snapping, dropping off-grid hits, triplets, keeping the stronger of two hits on one line, narrowing the window for fine divisions, unmapped time, and measuring a constant lag (including the case with too few hits).
- `tools/DrumAutoFill/EditorCheck`: `dotnet run --project tools/DrumAutoFill/EditorCheck -- <output folder> <song.wav>` drives the real dialogs headlessly through analysis, deselection, Apply, Undo/Redo, cancellation and stale-analysis rejection.

Detection quality on arbitrary songs still needs listening; passing these checks does not establish it.

## Earlier detectors (ADTOF, no longer used by the app)

**2.2.11–3.0: ADTOF in the app.** `DrumDetection.cs` ran the official ADTOF Frame_RNN weights (CC BY-NC-SA 4.0), converted to ONNX, through ONNX Runtime, with madmom-style features and peak picking. It matched ADTOF's own pipeline exactly. On Roselia - MIIRO 30–50 s it found 28 kicks and 15 snares. This change removes the model, `DrumDetection.cs`, its tests and fixture, the ONNX Runtime package and their third-party notices. They remain in git history at release 3.0 (`284044b`).

**2.2.8–2.2.10: Python prototype.** `tools/DrumAutoFill/prototype.py` ran [ADTOF-pytorch](https://github.com/xavriley/ADTOF-pytorch) (commit `85c192e7`) in a separate Python environment. The app called it through a `drum-autofill.json` configuration. The script and its tests remain only for comparison runs against annotated MIDI.
