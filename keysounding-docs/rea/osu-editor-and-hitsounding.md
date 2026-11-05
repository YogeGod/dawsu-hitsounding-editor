# osu! editor and hitsounding: confirmed rules

**Sources:**
- `.osu` files: your *Catalyst [Impetus]* plus 12 osu!standard maps sampled from `F:\osu!\Songs`.
- osu!lazer source, ppy/osu at commit `b77b028f` (MIT licence). Lazer deliberately reproduces stable's behaviour when it reads legacy maps; the code comments mark these places "for compatibility with stable".

**Not used:** the osu!stable client. `osu!.exe` is a .NET assembly with every name obfuscated, so I didn't analyse its code.

## Hitsounds

| Rule | Where in the source |
|---|---|
| The `hitSound` field is a bitmask: 2 whistle, 4 finish, 8 clap. `hitnormal` is always added. | `ConvertHitObjectParser.convertSoundType` |
| When additions are present and the Normal bit is not set, `hitnormal` is a **layered** sample, which a skin can mute with `LayeredHitSounds: 0`. | same |
| A custom `filename` **replaces `hitnormal` only**; finish, whistle and clap still play. | same |
| Object sample sets: 0 = inherit, and unknown values become Normal. The additions set inherits the object's normal set; if both are 0, both come from the timing point. | `readCustomSampleBanks`, `LegacySampleControlPoint.ApplyTo` |
| An object's index, volume or bank wins when it's non-zero; otherwise the timing point's value applies. A timing point with set 0 ("None") means Normal. | `LegacySampleControlPoint.ApplyTo`, `handleTimingPoint` |
| **Which timing point applies:** the one active at object start **+ 6 ms**, each slider edge **+ 5 ms**, or the end of an object with duration **+ 5 ms**. A green line up to about 5 ms late still takes effect. | `LegacyBeatmapDecoder.applySamples`, `CONTROL_POINT_LENIENCY = 5` |
| **Sample index:** 0 uses skin samples only. 1 uses the map's un-numbered `soft-hitclap.wav`. N ≥ 2 uses `soft-hitclapN.wav`. | `LegacyHitSampleInfo`, `LegacyBeatmapSkin.GetSample` |
| **No fallback inside the map folder:** with index ≥ 2, if the numbered file is missing, the map's un-numbered file is **not** used and the skin plays its plain sample instead. Skins never use numbered samples. | `LegacySkin.getLegacyLookupNames` |
| Sliders: per-edge flags (`8`) and sets (`9`) override the slider's defaults for each edge. | `ConvertHitObjectParser` |

## Editor timing and snapping

**Snapping:**
- The editor snaps to the nearest `redLineTime + k × beatLength ÷ divisor`, with halfway cases rounded away from zero.
- If the next red line is closer than the snapped time, it snaps to that red line instead. (`ControlPointInfo.GetClosestSnappedTime`)

**Red and green lines:**
- A red line creates a new beat grid. A green line only changes slider velocity, sound and kiai. (`handleTimingPoint`)
- Kiai is effects bit 1. "Omit first barline" is bit 8.

**Distance snap:**
- Distance per snap step = `100 × base SV × green-line SV ÷ divisor` osu!px, multiplied by DistanceSpacing. (`ComposerDistanceSnapProvider.GetBeatSnapDistance`, `CircularDistanceSnapGrid`)
- The spacing the editor reads between two objects is `actual distance ÷ expected distance`. The expected distance uses the SV of the **last slider before** the second object, and pairs closer than 3 px are ignored. (`OsuDistanceSnapProvider.ReadCurrentDistanceSnap`)

## Sliders

- **Speed:** `100 × base SV ÷ beat length` osu!px per ms. Green-line SV is clamped to **0.1–10×**. (`Slider.ApplyDefaultsToSelf`, `GetPrecisionAdjustedBeatLength`)
- **Duration:** `repeats × length ÷ speed`. Duration isn't stored in the file; it's always derived.
- **Tick spacing:** `100 × base SV × SV ÷ tick rate`. Writing NaN as the beat length disables ticks.
- **Tail judgement:** at `max(start + duration ÷ 2, end − 36 ms)`. (`SliderEventGenerator.TAIL_LENIENCY`)

## Stacking

- Objects within **3 px** of each other, or of a slider's end, stack if they come within `(int)preempt × StackLeniency` ms of each other.
- Each stack level shifts the object **6.4 × scale px** up and to the left. (`OsuBeatmapProcessor`, `OsuHitObject.StackOffset`)
- Preempt by AR: 1800 ms at AR0, 1200 ms at AR5, 450 ms at AR10.
- Your map: AR 9.6 gives a 510 ms preempt; × 0.2 leniency = a **102 ms** stack window. The usual 0.7 would give 357 ms, so only near-simultaneous objects stack in your map.

## Survey highlights (13 maps)

- **Snapping:** almost every object sits on 1/1, 1/2 or 1/4. Only 5 of about 18,700 objects in the sample were off-grid.
- **Where the sound choices live:** some mappers put all sample decisions on timing points (*Magician*, *Insomnia*). Others override banks on individual objects (*Catalyst*, *ItsWinter*, *Eiya*). Index switching shows up in *Burn This Right* and *Baby Sweet Berry Love*.
- **Unused features:** no map in the sample used per-object index or volume, custom filenames, or combo colour skips.
- **Your map:** *Catalyst* has the most green lines (1,029), the highest base SV (2.2), the highest DistanceSpacing (2.3), the lowest StackLeniency (0.2) and the shortest combos (3.4 objects).
