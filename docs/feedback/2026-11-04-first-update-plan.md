# First update from Plaud feedback

Status: superseded in priority by the owner's later instruction to prioritize the hitsounder's answers. See 2026-11-04-priority-revision.md and the latest section of planning-decisions.md. Retained below as the earlier proposal; it is not implementation approval.

Baseline: db04c6c14ae6c0ce464c5aeeffe9f2161637bc6e, feature/electron-lane-grouping. Recheck the working tree and applicable instructions before implementation; preserve unrelated work. Original feedback intake and confirmed interview answers are in the adjacent plaud-review.md and planning-decisions.md documents dated 2026-11-04. Screenshots and the later questionnaire are excluded from this plan.

## Goal

Make both empty-map hitsound authoring and existing playable-map editing easier, while investigating reported output problems without promising unverified fixes.

## 1. Establish correctness evidence

Investigate F09 export crash, F10 exported object/sample/green-line discrepancies, F11 timing offset, F12 compatibility warnings and F13 unexpected sample files.

- Inspect and exercise normal Save, HS Save, Generate and Copy hitsounds separately using isolated mapsets. Keep source maps and samples unchanged.
- Compare intended sounds and volumes with the reopened exported map and actual sample assignments. Missing new green lines alone does not prove wrong output.
- Check per-map/global playback offsets, waveform alignment and exported timestamps separately. Do not change the default offset based only on the reported decimals; their units and test conditions are unconfirmed.
- Trace sample creation through import, replacement and generated mixes. Account for legitimate isolated backing filenames before treating a high index as an error.
- Classify missing objects separately from encoding problems. Determine whether warnings reflect the applicable save path, including its existing mixing capabilities.
- For each report, record reproduction conditions, evidence, result and any fix. Add meaningful regression coverage for confirmed bugs. Mark unreproduced reports open; they do not block the workflow update.
- If a confirmed bug requires a material export-policy change beyond these decisions, return that specific decision for clarification before implementing it.

Acceptance: every investigation has an explicit outcome and evidence limits. Confirmed fixes pass relevant regressions. No claim of physical audio-device or osu! playback verification unless actually performed.

## 2. Warning display (F01, F02)

- Provide independent visibility toggles for missing-object warnings and actual save/export conflicts.
- Initial defaults: missing-object warnings off, save/export conflicts on.
- Replace full-height stripes with small markers above the lanes. Hover text explains the cause; distinguish multiple causes at the same time.
- Hiding a marker never changes map data, conflict detection or save/export explanations.
- Proposed persistence: remember the two visibility choices globally in Preferences. Leave exact marker sizing and colors to restrained implementation within the current interface.

Acceptance: each toggle controls only its warning category; default and restored settings work; hover text identifies the problem; save/export still reports unwritable sounds with markers hidden; dense zoomed-out warnings do not obscure notes or grid lines.

## 3. Folder access (F03, F04)

- Add a main hitsound-library folder setting in Preferences.
- Add Open hitsound folder as a command with an assignable shortcut; retain Open map folder.
- Offer direct access to the configured library and active working mapset when browsing for samples. Keep the existing purposes of list-header + and lane +.
- Remember the last sample-browsing folder. Proposed default: remember it globally; use the active working mapset if it no longer exists. Do not open unrelated folders automatically when the configured library is missing; explain the missing location and let the user choose another.
- Folder operations refer to the active working mapset, not silently to its original source copy.

Acceptance: valid and missing library paths, direct access to both folders, remembered/fallback folder behavior and picker cancellation work; importing still protects existing shared sample files and stable editor slots. Native picker behavior requires actual Windows verification in addition to any headless checks.

## 4. First/last navigation (F05)

- Z seeks the earliest gameplay object or placed hitsound, whichever comes first. Include hitsounds on muted or collapsed lanes.
- Preserve the existing quick second press to seek song start.
- Apply the corresponding latest-time and second-press song-end behavior to the existing last-position command.
- Proposed empty-map behavior: first goes to song start, last goes to the existing song-end boundary.
- Preserve existing timeline click/drag behavior and key assignments.

Acceptance: cover objects only, loose hits only, both, muted/collapsed lanes, empty maps and repeated presses. Seeking changes no notes or sample assignments.

## Deferred

- F06: timeline click-navigation changes. User explicitly requested unchanged behavior.
- F07: longer beat snap, including 2/1, 3/1 and 4/1.
- F08: sample pairing. Its intended behavior remains open.
- Screenshot interpretations, questionnaire-derived assumptions, new merging policy and unrelated interface redesign.

## Verification and completion

Run targeted regressions and the required compiler/decompiler, editing and map-corpus suites. Exercise both empty-map HS authoring and existing playable-map editing using isolated fixtures. Check actual warning rendering, folder controls and first/last navigation. Preserve grouping, sample slots, undo/redo, original maps and existing exports. Report any unavailable corpus, manual-picker or audio checks explicitly.

Deliver the changes with a concise verification report and updated feedback statuses. Do not commit, merge, publish or create a release without separate authorization. A portable build is not a required deliverable under the current planning request.
