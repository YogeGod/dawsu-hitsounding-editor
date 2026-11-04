# Generate scope change: implementation and verification

2026-11-04. Branch feature/electron-lane-grouping; baseline b265af4. User approved hitsounds-only by default, with optional slider-audio export. UI simplification remains deferred.

## Behavior

Generate now includes normal/whistle/finish/clap hitsounds at circles, slider heads/repeats/tails and spinner ends by default. Passive slider-slide/slider-whistle/tick audio and explicitly placed slider-audio lanes are excluded unless Include slider-body sounds and ticks is checked. Loose ordinary hits remain included. The option defaults off for each export.

The Generate confirmation replans read-only when the option changes and updates counts before allowing export. Generate is disabled while planning, after planning errors, or for plans with conflicts. Cancel remains available during planning. Late completion cannot export or reopen a cancelled dialog. Switching back recovers from errors/conflicts. Legacy merge-priority preset saving remains available through the replacement dialog.

Save behavior is unchanged. Copy's temporary Generate path and Review output explicitly opt into the prior slider-audio policy so this change does not silently alter those workflows. No shared samples are overwritten or automatically cleaned up by this change. Optional full-audio rendering retains its existing limitations; it is not a fix for slider-audio timing conflicts or redundant duration signatures.

## Real-map result

Used an isolated copy of Ludicin - Clinozoisite (Castiello) [Iridescent Enigma].osu and its sibling files. The native-app Generate command/dialog and actual writer were exercised headlessly.

| Scope | Circles | New WAVs | Conflicts |
|---|---:|---:|---:|
| Hitsounds (new default) | 2589 | 0 | 0 |
| Including slider audio | 3303 | 110 | 637 |

The default output was written and reopened. Every generated moment retained the source sample lanes, rounded export timestamp and volume. A repeat plan also needed zero new files. All seven original mapset files were SHA256 checked before/after and were byte-identical. The optional conflicted output was blocked, not written.

## Verification

- Full solution: 571 tests passed (Format52, Lanes311, App208). This includes corpus round-trip, decompile and compile/edit checks.
- App208 rerun passed after the final dialog cleanup.
- Added three regression cases covering both pairing policies, slider heads/repeats/tails, circle/spinner sounds, volume preservation, no default slider renders, explicitly placed slider-audio exclusion and loose-hit retention.
- Existing slider-render tests now explicitly opt in, retaining their assertions.
- Focused real-map Generate UI harness:16 checks passed, including async pending/cancel/late completion, error/recovery, conflict blocking, default export and original-file preservation.
- Existing feedback workflow harness:58 checks passed, including actual Save/Generate/Copy, decoded finish fusion and silent gains, reusable sets, Undo/Revert and cross-map paste.
- Inspected default and slider-audio confirmation captures in one batch. Copy wraps, counts are visible and blocked Generate is visually disabled.
- No manual listening, screen reader or real osu! gameplay test was performed. Existing CS4014 warning in MainWindow.axaml.cs:215 remains.

## Inline review

### Correctness
No outstanding findings. Reviewed default/opt-in filtering, all PlanGenerate callers, replacing legacy confirmation, async lifecycle/readiness, state validation after confirmation, and writer refusal of conflicted plans. The two initial test failures were fixture issues: an old full-audio test needed explicit opt-in; the new pairing test needed default-sample bytes for fingerprints. Both corrected and full suite passed.

### Standards
No outstanding findings. Reused the existing dialog builder and MergeText/preset helpers. Removed the obsolete ConfirmMergeGenerate dialog. Added only an optional planner flag and an optional primary-button configuration hook; no new dependencies or framework abstraction. Simplification and review were performed inline, without subagents, per CLAUDE.md.

### Spec
No outstanding findings. The approved default and optional export are implemented. Existing Save/Copy behavior, sample roles, finish-only fusion, source files and open editor are preserved. Broader UI audit and slider-render optimization remain outside this task.

### Security
No new exploitable findings. Checked changed paths and file operations, read-only replanning, cancellation, malformed/missing audio error handling, output/overwrite guards, and absence of new shell execution/network/dependencies in product code. Existing GenerateWriter containment/atomic-write checks remain in force.

## Evidence and build

Ignored artifacts/generate-audit/: full-tests.txt, app-tests.txt, ui-results.txt, workflow-results.txt, generate-default.png, generate-slider-audio.png and ui/ harness. The original diagnostic results.json records the old behavior. New executable: src/OsuHitsound.App/bin/generate-scope/Debug/net10.0/win-x64/OsuHitsoundEditor.exe. Existing desktop editor was not closed or replaced. No commit, push or release was performed.
