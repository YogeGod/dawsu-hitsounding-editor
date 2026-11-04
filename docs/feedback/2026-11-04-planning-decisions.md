# Plaud feedback planning decisions

## Scope

Baseline: db04c6c14ae6c0ce464c5aeeffe9f2161637bc6e. Source intake: 2026-11-04-plaud-review.md in this folder. Planning uses the original Plaud feedback. The later screenshots, interpretations and hitsounder questionnaire are set aside at the user's request.

## Confirmed round 1

- Give equal attention to authoring a hitsound difficulty from an empty timed map and editing hitsounds on existing playable difficulties.
- Combine export/timing/correctness investigations with practical workflow fixes. Do not treat unconfirmed reports as reproduced bugs. The exact first-update work packages remain undecided.
- Defer sample pairing (F08) to a later update; do not let it block the other items.

## Confirmed round 2

- Provide separate visibility toggles for sounds without a gameplay object and actual save/export conflicts. Reduce marker prominence; save/export must still explain sounds it cannot write. Exact appearance and initial toggle states remain open.
- Let the user choose a main hitsound library folder in Preferences. Add Open hitsound folder with an assignable shortcut, retain Open map folder, offer quick access to both folders when adding samples, and remember the last folder used.
- Z goes to the earliest gameplay object or placed hitsound, including hitsounds on muted or collapsed lanes. Keep the existing quick second press to jump to song start. Apply the corresponding rule to the last-position command.

## Confirmed round 3

- Hide missing-object warnings by default and show actual save/export conflict warnings by default. Both visibility toggles remain available.
- Do not change timeline click behavior. The reported click-navigation issue (F06) is deferred from this update; this decision does not assert that it was reproduced or fixed.
- Defer longer-than-beat snap options (F07), including 2/1, 3/1 and 4/1, to a later update alongside pairing. Their placement rules remain undecided.

## Confirmed round 4

- Replace full-height warning stripes with small markers above the lanes, with explanations on hover.
- Investigate reported export/timing problems now, fix confirmed bugs, and document reports that cannot be reproduced. Unreproduced reports do not block the workflow update; do not claim the tester's exact issue was fixed without evidence.

## Open decisions

- Review the concrete first-update plan in 2026-11-04-first-update-plan.md and obtain implementation approval. Preference persistence and folder fallback defaults are proposals in that plan, not separately confirmed interview answers.

## Pairing exploration tailored to the editor owner

The user declined plan approval and requested further grilling on the topics previously posed to the hitsounder, tailored to their own workflow. This reopens pairing design discussion, but does not approve implementation or automatically add pairing to the first update. The screenshots and their earlier interpretations remain excluded unless the user explicitly brings them back.

Confirmed answers Q12-Q17:

- Handle simultaneous sounds automatically on save, with user overrides available.
- Pairing should help both encode separate hitsounds together without manual copying/renaming and prepare reusable sound sets for other placements.
- Retain access to both original layers and the finished mixed sample when reopening an editor project.
- A reusable set contains chosen sounds and their relative volumes, not a rhythm pattern.
- Placements are linked to the reusable set by default, with an option to make a placement independent. Editing the saved set updates its linked placements.
- Original-layer and mixed-sample views represent the same sounds. Editing original layers updates the mix; playback must not play both representations together.

Confirmed answers Q18-Q20:

- Create a set either from selected simultaneous hitsounds using Save sound set or by selecting samples and volumes in a dedicated setup window.
- Place a selected set directly, or apply it to an existing placed hit.
- Editing a placed occurrence affects that occurrence only, with local overrides while retaining its link. Editing the saved set affects linked placements. Keep an explicit Make independent command.

Confirmed answers Q21-Q23:

- Give a placed set one overall volume control that preserves its internal balance, while retaining individual sound adjustments. Export must preserve the intended balance within osu! volume limits.
- Support map-only sets and optional personal-library sets available across maps. Include required samples with the map/project for portability.
- Updating a set preserves local overrides and updates non-overridden properties. Provide Reset to set to remove local changes.

Confirmed answers Q24-Q26:

- Each map keeps its own copy of a library set; library updates are applied explicitly. Placements link to the map-local set, so changing the library does not silently change older maps.
- Applying a set offers Add and Replace, defaulting to Add. Replace must show which existing sounds it removes.
- The finished-mix view initially provides audition and output filename information; edits go through original layers. Direct mixed-audio editing (trimming/effects) is explicitly a later feature.

Confirmed answers Q27-Q32:

- Set constituents stay on the existing sample lanes; selecting one can highlight the whole set. Preserve the sample-family layout.
- Offer Review output on demand, and prompt when automatic handling needs a user decision. Do not require review before every routine save/export.
- Q29 was initially B, then explicitly corrected to A: reusable sets and pairing belong in a separate follow-up after the first workflow update. The correction supersedes the earlier answer.
- An export pairing/mixing override can apply to the current timestamp or every matching combination in the current map. Default to the current timestamp. Exact combination matching rules still require design.
- Saved sets retain their saved audio when an external source file changes. Replacing/updating the saved audio is explicit.
- A set change and all linked-placement updates form one Undo step. Local edits, library updates and Add/Replace actions are also undoable as complete actions. Cross-map disk mutation is not implied by Undo; maps retain their own set copies.

Confirmed Q34A: Add skips an identical sound already present at the timestamp, retaining its existing volume. Q33 was explained with examples but remains unanswered.

The user paused grilling to supply the hitsounder's reply. It is preserved and analyzed in 2026-11-04-hitsounder-response.md. Do not use the stakeholder's priorities to silently replace the owner's scope choices. Reusable set/library features are broader owner requests, distinct from the stakeholder's concrete numbered-file pairing example.

Still open: automatic export preference for keeping files separate versus mixing, reconciliation of the hitsounder's response with owner priorities, and review of the final follow-up specification. Exact file-allocation and combination matching algorithms have not been approved. No implementation approval has been given.

## Status

## Latest steering: prioritize the hitsounder's answers

The owner confirmed that the hitsounder's same-timeline examples mean the exact same timestamp. Green-line versus per-object field preference is undecided and depends on rankability. The owner explicitly instructed prioritizing the hitsounder's answers. This supersedes the old ordering that deferred basic pairing behind folder/navigation changes. It does not approve implementation or erase compatible earlier decisions.

Current priority: (1) basic sample-index pairing with optional same-timestamp fusion, (2) rankability/compatibility evidence and a deliberate green-line versus per-object export policy, (3) Revert shortcut Ctrl+L and verification of existing behavior. Related compatibility-warning/export investigations remain important. The broader linked reusable-set library stays future scope, with its decisions retained above.

Different sample indexes do not by themselves make two finish samples independently playable on one object. The stakeholder's desired coexistence is a requirement for editing/preview; output must encode it through an appropriate explicit fusion or other verified supported route, without silently dropping audio. Exact allocation and parent-hitnormal routing remain open.

Current rules research is recorded in 2026-11-04-priority-revision.md. A format-supported field is not by itself proof of map rankability. No automatic switch to per-object fields has been approved.

Planning is awaiting further decisions. No implementation plan is approved and no application code has been changed for this feedback task.

Confirmed Q35A: when two different finish sounds occur at the exact same timestamp, automatically mix them into a new finish sample, preserving both original editable layers in the project. Review output remains available for overrides. This resolves that specific fusion default; it does not establish every cross-bank destination policy or authorize implementation.

Confirmed Q36B: new pairing/fusing behavior applies to Save .osu, Generate and Copy hitsounds while preserving each command's purpose. Copy must preserve the intended sounds when transferring the hitsound difficulty to playable difficulties. Cross-bank destination policy remains to be clarified against the hitsounder's parent-hitnormal example.

Historical Q37B was explicitly superseded by the owner's later correction: do not fuse finishes into a hitnormal. Fuse finish layers into a new finish sample even across samplesets; keep the kick separate and pair it with the resulting finish. Preserve original project layers and Review output overrides.

Confirmed Q38A: a normal hitnormal kick, soft clap and drum whistle at the same timestamp stay separate through copies into compatible sample slots with a matching index. Copy audio without changing it. Cross-bank provenance alone does not require fusion when addition types occupy distinct playable slots.

The earlier broad Q33 is now refined by concrete cases rather than a single blanket preference: keep compatible distinct sound types separate through pairing; fuse simultaneous finish layers into finish even across samplesets; keep existing hitnormal separate and pair it with the resulting additions. Preserve project inputs and allow Review output overrides. Generalizations beyond these cases require compatibility evidence and the existing destination-priority design, not assumptions about the screenshots.

Confirmed Q39C: offer green-line and per-object export styles, defaulting to green lines. Per-object mode is conditional on verified supported behavior and ranking constraints. Preserve settings on untouched objects; slider behavior must use verified supported routes, not assumptions from circles.

The current concrete proposal is 2026-11-04-revised-implementation-plan.md. It requires explicit implementation approval. No application code has been changed.
