"""Local kick/snare detection experiment. Exports a preview; never writes a beatmap."""
import argparse
from bisect import bisect_left
from collections import Counter
import hashlib
import json
from pathlib import Path
import time


CLASSES = {35: "kick", 38: "snare"}
GM_CLASSES = {35: "kick", 36: "kick", 38: "snare", 40: "snare"}


def events_from_peaks(peaks, activation, labels, duration_ms, offset):
    events = []
    for pitch, drum in CLASSES.items():
        for seconds in peaks[pitch]:
            if seconds * 1000 >= duration_ms:
                continue
            frame = round(seconds * 100)
            events.append(dict(drum=drum, timeMs=round(seconds * 1000 + offset, 4),
                               score=round(float(activation[0][frame][labels.index(pitch)]), 6)))
    return sorted(events, key=lambda e: (e["timeMs"], e["drum"]))


def match_moments(events, moments, tolerance_ms):
    """Nearest object/node per candidate; collapse same-class collisions by score."""
    ordered = sorted(moments, key=lambda m: (m["timeMs"], m["objectIndex"], m["node"]))
    times = [m["timeMs"] for m in ordered]
    used = set()
    for event in sorted(events, key=lambda e: (-e["score"], e["timeMs"], e["drum"])):
        index = bisect_left(times, event["timeMs"])
        nearby = ordered[index:index + 1]
        if index > 0:
            nearby += ordered[bisect_left(times, times[index - 1]):][:1]
        moment = min(nearby, key=lambda m: (abs(m["timeMs"] - event["timeMs"]),
                     m["timeMs"], m["objectIndex"], m["node"]), default=None)
        event["status"] = "unmatched"
        if moment is None or abs(moment["timeMs"] - event["timeMs"]) > tolerance_ms:
            continue
        key = (event["drum"], moment["objectIndex"], moment["node"])
        event["status"] = "duplicate" if key in used else "matched"
        event["moment"] = moment
        event["snapDeltaMs"] = round(moment["timeMs"] - event["timeMs"], 4)
        used.add(key)
    return events


def score_events(events, reference, tolerance_ms=50):
    """One-to-one onset matching, independently for each drum class."""
    result = {}
    for drum in CLASSES.values():
        predicted = sorted(e["timeMs"] for e in events if e["drum"] == drum)
        expected = sorted(t for t, kind in reference if kind == drum)
        i = j = 0
        errors = []
        while i < len(predicted) and j < len(expected):
            delta = predicted[i] - expected[j]
            if abs(delta) <= tolerance_ms:
                errors.append(abs(delta))
                i += 1
                j += 1
            elif delta < 0:
                i += 1
            else:
                j += 1
        tp = len(errors)
        precision = tp / len(predicted) if predicted else None
        recall = tp / len(expected) if expected else None
        result[drum] = dict(reference=len(expected), predicted=len(predicted), correct=tp,
                            precision=precision, recall=recall,
                            f1=2 * tp / (len(predicted) + len(expected))
                            if predicted or expected else None,
                            meanAbsoluteErrorMs=sum(errors) / tp if tp else None)
    return result


def run(args):
    import numpy as np
    import pretty_midi
    import soundfile as sf
    import torch
    from adtof_pytorch import ADTOFFrameRNN, calculate_n_bins, get_default_weights_path
    from adtof_pytorch.model import load_audio_for_model
    from adtof_pytorch.post_processing import PeakPicker, LABELS_5, FRAME_RNN_THRESHOLDS

    started = time.perf_counter()
    output = Path(args.output)
    # Require a fresh directory so an input or previous result cannot be overwritten.
    output.mkdir(parents=True, exist_ok=False)
    torch.set_num_threads(args.threads)
    torch.manual_seed(0)
    weights = Path(get_default_weights_path())
    if not weights.is_file():
        raise FileNotFoundError("Pretrained weights are required; random initialization is forbidden.")
    checkpoint = torch.load(weights, map_location="cpu", weights_only=True)
    model = ADTOFFrameRNN(n_bins=calculate_n_bins(), use_keras_gru=False)
    model.load_state_dict(checkpoint.get("model_weights", checkpoint), strict=True)
    model.eval()
    load_seconds = time.perf_counter() - started
    before_features = time.perf_counter()
    x = load_audio_for_model(str(args.audio))
    feature_seconds = time.perf_counter() - before_features
    before_inference = time.perf_counter()
    with torch.inference_mode():
        activation = model(x).numpy()
    inference_seconds = time.perf_counter() - before_inference
    active_indices = [LABELS_5.index(pitch) for pitch in CLASSES]
    np.save(output / "activations.npy", activation[:, :, active_indices])
    peaks = PeakPicker([t * args.threshold_scale for t in FRAME_RNN_THRESHOLDS]).pick(activation)[0]
    audio, sr = sf.read(args.audio, dtype="float32", always_2d=True)
    mono = audio.mean(axis=1)
    duration_ms = len(mono) * 1000 / sr
    offset = args.audio_start_ms + args.song_offset_ms
    events = events_from_peaks(peaks, activation, LABELS_5, duration_ms, offset)
    map_data = None
    if args.moments:
        map_data = json.loads(Path(args.moments).read_text(encoding="utf-8-sig"))
        match_moments(events, map_data["moments"], args.tolerance_ms)
    metrics = None
    if args.reference_midi:
        ref = pretty_midi.PrettyMIDI(str(args.reference_midi))
        # Curated drum annotation files may use MIDI channel 1 rather than channel 10.
        reference = [(n.start * 1000 + offset, GM_CLASSES[n.pitch])
                     for instrument in ref.instruments
                     for n in instrument.notes if n.pitch in GM_CLASSES
                     and 0 <= n.start * 1000 < duration_ms]
        if not reference:
            raise ValueError("Reference MIDI contains no supported drum annotations in this excerpt")
        metrics = score_events(events, reference)
    midi = pretty_midi.PrettyMIDI()
    instrument = pretty_midi.Instrument(program=0, is_drum=True)
    ticks = np.zeros(len(mono), dtype=np.float32)
    rng = np.random.default_rng(0)
    for event in events:
        seconds = (event["timeMs"] - offset) / 1000
        pitch = next(p for p, drum in CLASSES.items() if drum == event["drum"])
        instrument.notes.append(pretty_midi.Note(100, pitch, seconds, seconds + .05))
        start = round(seconds * sr)
        length = min(round(.08 * sr), len(ticks) - start)
        t = np.arange(length) / sr
        tone = rng.uniform(-1, 1, length) if pitch == 38 else np.sin(2 * np.pi * 75 * t)
        ticks[start:start + length] += .4 * tone * np.exp(-t * 60)
    midi.instruments.append(instrument)
    midi.write(str(output / "detected.mid"))
    # Original left; diagnostic drum cues right. These are not production samples.
    sf.write(output / "audition.wav", np.column_stack((mono, np.clip(ticks, -1, 1))), sr)
    report = dict(schemaVersion=2, detector="ADTOF-pytorch", device="cpu",
                  enabledDrums=list(CLASSES.values()), activationLabels=list(CLASSES.values()),
                  audioSha256=hashlib.sha256(Path(args.audio).read_bytes()).hexdigest(),
                  weightsSha256=hashlib.sha256(weights.read_bytes()).hexdigest(),
                  audioStartMs=args.audio_start_ms, songOffsetMs=args.song_offset_ms,
                  durationMs=duration_ms, thresholdScale=args.threshold_scale,
                  toleranceMs=args.tolerance_ms, scoreMeaning="raw model activation, not calibrated confidence",
                  timingSeconds=dict(modelLoad=load_seconds, features=feature_seconds,
                                     inference=inference_seconds, total=time.perf_counter() - started),
                  counts=dict(Counter(e["drum"] for e in events)),
                  statuses=dict(Counter(e.get("status", "not-matched") for e in events)),
                  mapSha256=map_data["mapSha256"] if map_data else None,
                  accuracyAt50Ms=metrics, events=events)
    (output / "candidates.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "events"}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audio", type=Path, required=True, help="Staged WAV excerpt; never uploaded")
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    parser.add_argument("--audio-start-ms", type=float, default=0, help="Excerpt start in original song")
    parser.add_argument("--song-offset-ms", type=float, default=0, help="Editor song offset added to song time")
    parser.add_argument("--moments", type=Path, help="Read-only MapMoments JSON export")
    parser.add_argument("--reference-midi", type=Path, help="Aligned GM drum annotations for this excerpt")
    parser.add_argument("--threshold-scale", type=float, default=1.25, help="Higher keeps fewer candidates")
    parser.add_argument("--tolerance-ms", type=float, default=30)
    parser.add_argument("--threads", type=int, default=4)
    args = parser.parse_args()
    import math
    for name in ("audio_start_ms", "song_offset_ms", "threshold_scale", "tolerance_ms"):
        if not math.isfinite(getattr(args, name)):
            parser.error(f"{name} must be finite")
    if args.audio_start_ms < 0 or args.tolerance_ms < 0 or args.threshold_scale <= 0 or args.threads < 1:
        parser.error("Start/tolerance must be nonnegative; threshold scale and threads must be positive")
    run(args)


if __name__ == "__main__":
    main()
