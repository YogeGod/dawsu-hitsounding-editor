# General MIDI assignments and GGD PV map export - 2026-11-05

All 25 Expanded Kit and 18 Simple Kit starter rows now have editable MIDI notes in
Drum Map Setup. Changes/clears persist in mapset layouts and portable `.dawsudrum`
presets. Samples and beatmap hit placements are unchanged by this metadata.

The General MIDI percussion layout uses channel 10. Reference:
https://www.cs.cmu.edu/~music/cmp/archives/cmsip/readings/GMSpecs_PercMap.htm

| Row | MIDI note |
|---|---:|
| Kick | 36 |
| Snare / Ghost Notes | 38 |
| T1 / T2 / T3 / T4 | 50 / 48 / 45 / 41 |
| Tight Tip / Tight Edge / Closed Tip / Closed Edge | 42 |
| Hi-Hat Open 1 / 2 / 3 | 46 |
| Ride Bell | 53 |
| Bow Ride (Low) | 51 |
| Bow Ride (High) / Ride Crash | 59 |
| Right Crash / Left Crash | 49 / 57 |
| China / Stack | 52 |
| Mini Stack Left / Mini Stack Right / Splash | 55 |

GM has no dedicated ghost snare, hat tip/edge/open-stage or stack articulations. Those
rows deliberately share the nearest GM sound. Ghost-note velocity is not forced by
the assignment. Unknown custom instrument names start unassigned. Supported edits are
whole notes 0–127 or empty; explicit empty values remain empty on reload. Missing note
fields in older presets acquire the known starter mapping from the original row name.

## Map export

Random eligible selection: Various Artist - Oriental's Long Stream Practice & Training
Maps 2 (Oriental), **BPM188 Claiomh Solais**. Eligibility requires no custom sounds or
passive sounds and at least 20 each of clap, whistle and finish; it is not an all-normal test.

| Resolved default lane | Layers | Requested instrument / note |
|---|---:|---|
| soft-hitnormal | 1028 | Kick / 36 |
| soft-hitclap | 128 | Snare / 38 |
| soft-hitwhistle | 132 | Closed hi-hat / 42 |
| soft-hitfinish | 68 | Crash / 49 |

The initial `random-map-4` export is superseded: outgoing GM note numbers were checked,
but GGD's internal mapping was still PV. Its audio must not be treated as a verified
GM conversion. The user reported hearing only hi-hat.

Corrected export: `E:\Claude\gm-notes-20261105\artifacts\corrected-GM-live`.
In Kontakt 7, loaded P5 `00 Startup`, opened Settings > Mappings > Load Preset,
and explicitly changed **PV to GM**. `GGD-GM-selected.jpg` captures this selection
in the exact instance used for export. Silent processing during setup was necessary
for Kontakt's instrument script and performance panel to initialize.

Rendered the same difficulty and 1356 triggers from that configured live instance.
Duration 138.704399 s, stereo 44.1 kHz float WAV; drums peak 0.795638, mix peak
0.950828. Tail completed; source SHA-256 unchanged. No added preview gain.
Separate velocity-100 checks for notes 36/38/42/49 produced non-silent distinct files;
peaks 0.340745 / 0.376821 / 0.063745 / 0.029790. These are measured audio checks
plus visible mapping confirmation, not a claim of human listening verification.
The short preview plays kick, snare, closed hat, crash in that order.

A headless restore of the corrected state crashed Kontakt in a fresh map-render
process. The successful export used `--editor` and kept the visibly configured
instance alive throughout. State reload reliability remains unresolved. Live
MIDI/editor VST integration is still pending.

## Checks and review

- 636 unit/compiler/editing/map-corpus tests passed (App 234, Format 52, Lanes 350).
- 20 focused drum-map tests passed again after strengthening the legacy JSON test.
- Headless UI check: all 25 note fields, kick/snare defaults, fractional value rejection,
  changed note persistence, cleared note persistence and unchanged sample assignment.
- Both editor and render tool build successfully. Visual inspection caught clipped
  numeric fields; the MIDI column was widened to fit three-digit notes and spin buttons.

### Correctness

Added note bounds/whole-number validation and preserved explicit nulls. Old presets
remain readable; renamed rows retain saved assignments. Export uses the existing
decompiler, including layer times and stable played-volume semantics. No remaining
finding in the approved metadata and offline-render scope.

### Standards

Inline simplify reused the existing preset JSON/persistence, UI controls, decompiler,
sample decoder and tested VST3 bridge. Removed duplicate progress output. No existing
sample assignment or beatmap-writing logic was changed.

### Spec

Implemented the approved default row assignments and editable setup column. Exported
the requested GGD PV sound files using a copied real map with substantial additions.
Existing editor MIDI routing remains outside this change, as requested earlier.

Findings: Correctness 0 remaining; Standards 0; Spec 0 within approved scope.

### Security

Checked note/state bounds, JSON compatibility, source/output paths, mapset audio lookup,
symlink skipping and DLL dependency use. No new exploitable finding identified. Plugin
state and sample libraries remain local; no credentials, proprietary kit state, samples,
crash dumps or rendered song audio are included in repository source changes.
