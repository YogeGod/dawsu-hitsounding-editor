# Generate sample-count diagnosis: Clinozoisite

2026-11-04, source b265af4 on feature/electron-lane-grouping.

## Reproduction

Read-only call through the current built MapDocument.PlanGenerate with PairSamples=true, current default-sound loading and the existing Generate manifest lookup. Source: F:/osu!/Songs/Ludicin - Clinozoisite (Castiello)/Ludicin - Clinozoisite (Castiello) [Iridescent Enigma].osu. No project sidecar or unsaved running-editor state was restored; this measures the difficulty as stored on disk. SHA256 compared all seven mapset files before/after: unchanged, no files added.

## Measured results

The source has 1840 hit objects and 3864 editable hits across the 12 default hitsound lanes. Passive audio contains 679 slider-slide entries, 35 slider ticks and one slider-whistle entry.

| Read-only plan | Generated circles | New sample files | Conflicts |
|---|---:|---:|---:|
| Current Generate | 3303 | 110 | 637 |
| Same source with passive slider sounds excluded | 2589 | 0 | 0 |

All 110 new files come from passive slider audio: 106 loop renders and four one-shot slider renders. No new ordinary hitsound pair copies or finish fusions are needed for this unchanged map. The alternate plan was made with a separate EditSession over the same decompiled map with Passive=[]; normal editable hits including slider heads/repeats/tails remain unchanged. This is a diagnostic comparison, not an implemented export option.

## Why

HitsoundDiff.Plan appends passive slider slide/whistle/tick entries to placed hits. The PairSamples path routes every slider-audio group through MixPlanner. Slider loops are rendered into one-shot WAVs and put on circles. Mix identity includes source, volume and exact floating-point duration. Examples include 342.85714285714494 and 342.8571428571413 ms as separate durations; these increase the number of distinct signatures. Do not promise those two exact WAVs are byte-identical without rendering and comparing, because frame length is calculated with Ceiling.

The dominant issue on this map is export scope, not ordinary hitnormal/finish fusion. A copier-oriented hitsound diff can plausibly exclude passive slider audio by default, retaining an explicit full-audio rendering option if wanted. This policy needs user confirmation. Preserve selected hits, slider-node sounds, volume/timing, sample-family rules and finish-only fusion. Do not silently change Save or Copy.

## Evidence and limits

Ignored harness: artifacts/generate-audit/Program.cs, Audit.csproj and results.json. References the current built DLLs from bin/sound-sets. No WAV rendering, hardware playback or actual Generate write was performed. The 637 conflicts are the current plan's reported count; their individual causes have not yet been categorized. Tests were not rerun because no application implementation changed.
