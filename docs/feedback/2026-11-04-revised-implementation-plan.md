# Revised implementation plan from hitsounder feedback

Status: approved implementation completed locally. Automated verification and inline review are recorded in [implementation report](2026-11-04-implementation-report.md). Original reports without reproducible fixtures and native osu! playback/ranking checks remain open. The owner subsequently authorized commit and push to feature/electron-lane-grouping. Merge and release remain outside scope.

Baseline: db04c6c14ae6c0ce464c5aeeffe9f2161637bc6e. Recheck local and remote state before starting and preserve unrelated work. The owner's latest instruction prioritizes the hitsounder's concrete requirements. This replaces the earlier workflow-first ordering. Follow planning-decisions.md for the decision history; latest corrections prevail over earlier answers.

## First priority: pairing and finish fusion

- Apply the same pairing/fusing rules to Save .osu, Generate and Copy hitsounds, retaining each command's purpose.
- Prepare compatible sample-bank/index assignments through unchanged audio copies when distinct sound types can remain separate. Example: normal-hitnormal.wav can be copied to a safe normal-hitnormal2.wav to accompany soft-hitclap2, when that index/file is safe to use. Never overwrite an occupied or shared sample to force the example index.
- Different-type additions such as soft clap and drum whistle remain separate through compatible sample copies, even when their source samplesets differ.
- Fuse simultaneous finish layers into a new finish sample, including finish inputs from different samplesets. Keep the original hitnormal/kick separate and pair it with the resulting finish. The earlier proposal to fuse finishes into hitnormal is explicitly rejected.
- Retain every intended input and its relative volume; preserve original editable project layers while exposing the finished output for audition and filename inspection. Review output is available on demand, with prompts only for unresolved decisions.
- Allow export overrides for one timestamp or matching combinations in the current map, defaulting to one timestamp. Define matching using actual input identity and intended balance; do not reuse audio solely because filenames look related. Avoid unnecessary file generation and protect other difficulties' playback.
- Reuse the current slot/destination priority machinery where compatible with the confirmed finish policy. Do not silently broaden the finish decision into a new policy for every other sound family. Any unresolved output behavior must preserve inputs or report a clear problem.

Acceptance: paired kick/addition example, distinct additions across banks, same-bank and cross-bank finish fusion, repeated combinations with changed gains, occupied indexes/extensions, shared-difficulty files, missing/undecodable audio, cancellation, reopen and all three export commands. Confirm exported sounds through actual sample resolution, not filenames alone. Keep source mapsets untouched during verification.

## Second priority: export settings

- Offer Green lines and Per-object export styles; default to Green lines.
- Preserve untouched objects and timing data. Do not rewrite the whole map merely to normalize its export style.
- Implement the optional per-object style only where compatibility is verified; evaluate circles and slider-related sounds separately. Use verified green-line handling where a slider sound requires it and explain the exception in the output review.
- Preserve slider velocity, timing, unrelated samples/volumes and other difficulties. Do not create conflicting timing points.
- Inspect current ranking criteria and use isolated exported examples to check supported-client playback and reopen/save behavior. Rules inspection found documented per-object index/volume fields and no blanket ban, but this is not a guarantee that arbitrary outputs are rankable.
- Reproduce export/timing reports where possible, fix confirmed bugs, and keep unreproduced reports explicitly open. Do not change the global offset based on an unverified decimal example.

Acceptance: identical intended sound/balance in each supported mode, unchanged unrelated data, valid slider behavior, safe inherited settings and explicit evidence limits. A native playback or ranking-tool check may be reported only if actually performed. Return to the owner if verification requires changing the agreed policy.

## Third priority: Revert and warning behavior

- Add Ctrl+L as a default binding for the existing Revert to last saved command. Preserve its existing confirmation and reload behavior. Respect custom bindings and avoid silently replacing user choices.
- Classify compatibility warnings using the actual selected export path, including resolvable pairing and fusion. Do not mark a combination irrecoverably invalid simply because a generated paired/mixed sample is needed.
- Keep the agreed separate visibility toggles: missing-object warnings initially hidden, genuine save/export problems initially shown. Replace full-height stripes with small markers above the lanes and hover explanations. Hidden markers must not suppress save/export diagnostics.

Acceptance: Ctrl+L routes to existing Revert, cancellation changes nothing, saved map reloads correctly, custom keybindings remain intact; warning categories, defaults, hover explanations and unresolved export messages behave correctly.

## Following workflow work

Retain, at lower priority, the agreed main hitsound-folder preference, direct map/library folder access and remembered sample browsing; earliest/latest navigation including placed hits, with existing quick second-press start/end behavior. Both empty-map HS authoring and playable-map editing remain equally important.

## Deferred

- Personal/map reusable sound-set library, linked placements, local overrides, set creation/application tools and original/mixed view switching as a full set-management feature. Keep their confirmed design decisions for a later plan. Basic pairing and preservation of original project inputs must not depend on building this library.
- Direct editing of rendered audio, including trimming and effects.
- Longer-than-beat snap options; their requested beat lengths are understood but placement phase remains unsettled.
- Timeline click changes, per the owner's explicit instruction.

## Verification and delivery boundaries

Use meaningful targeted regressions plus the required compiler/decompiler, editing and map-corpus checks. Verify actual warning rendering and interaction as appropriate. Separate automated file/package checks from native picker checks and physical audio/gameplay verification. Report unavailable checks and unconfirmed issues plainly. Keep the handoff and feedback statuses current.

No commit, merge, release, remote publication or portable package is implied. The owner's approval was obtained before application changes, as required by project instructions.
