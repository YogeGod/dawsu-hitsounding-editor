# Reusable hitsound sets

Status: approved by the owner's ok and implemented locally, with commit/push authorized to feature/electron-lane-grouping. Baseline: 68cd020. Verification is recorded in [implementation report](2026-11-04-reusable-sets-verification.md). Retains confirmed Q12-Q34 and Q41-Q44 decisions in planning-decisions.md. This feature is distinct from osu!'s Normal/Soft/Drum samplesets and from the already implemented export pairing rules.

## User workflow

- Add a Sound sets inspector card with map sets, explicit personal-library import/update, and a Single sample option that keeps ordinary drawing available. Existing sample-family lanes, layout controls and click gestures stay intact.
- Create a set from selected simultaneous hits using Save sound set, or choose samples and relative volumes in a setup dialog. A set contains sounds and balance, never a rhythm. Reject multi-timestamp creation with a clear explanation instead of flattening a rhythm into one sound.
- Choose a saved set, then use the existing Draw tool at the current snap. Also allow applying the set to selected hit positions. Add is the default. Replace previews the affected sounds before committing, once per command/stroke; cancellation changes nothing.
- Replace affects only sound types supplied by the set, leaving unrelated layers intact. Kick + clap over kick + whistle retains whistle. Preserve the corrected finish-only export fusion policy; saved sets do not choose a different export policy.
- Add skips a duplicate of the same sound. An existing independent duplicate joins the new linked placement with its current volume as a local override. If another placement already controls it, the first placement keeps that link and the later placement supplies only its other components. Show this shared-component relationship; no automatic takeover or extra playback.

## Editing and links

- Placements link to their map-local set by default. A set edit updates all linked placements in that map as one undoable action, while preserving local member changes and placement gain.
- The card provides overall placement volume, individual member adjustments, Reset to set and Make independent. Overall volume preserves internal balance within existing osu! gain handling. Edits on a placement affect that placement; editing the saved set is explicit.
- Removing one member locally remains a local change so an unrelated set update does not silently restore it. A component controlled by an earlier placement is excluded from the later placement's updates until the user explicitly reapplies/relinks it.
- Keep member hits on existing sample lanes. Selecting a member can visually highlight its set companions without silently changing ordinary single-hit selection/deletion. Provide Select whole set for whole-placement actions. Whole-set move/copy/duplicate retains links and overrides; isolated member changes do not modify the saved set.
- New-set creation, application, Replace, local changes, resetting/detaching and set updates are complete undoable actions. Disk/library changes do not cause hidden mutations to other maps or imply cross-map Undo.

## Persistence and audio

- Store set definitions, stable component/placement references, gain and overrides in the existing immutable edit-state/project workflow. Restore links alongside original editable layers after Save/Revert/reopen, including generated HS objects. Validate stale bindings so externally edited .osu files cannot attach metadata to unrelated hits.
- Snapshot required sample bytes when saving a set, including built-in sounds. Later source-file changes do not silently replace the saved audio. Preserve sampleset/sound-role metadata, gain and content identity.
- Keep required map samples in the mapset using safe allocation and unchanged copies; never overwrite existing/shared sounds. Include definitions/assets in explicit set save/import operations so reuse does not depend on the original external sample paths.
- Personal-library sets are explicit saved copies containing required audio. Each map imports its own definition/audio copy. Library updates affect a map only through an explicit Update action; preserve local overrides on that update. Use the configured hitsound-library folder for library storage and explain a missing/unwritable location.
- Reuse Review output for prepared-file audition and filenames. Playback/export plays effective member layers once, never a mixture of original layers plus their finished rendered representation. Preserve existing Save, Generate and Copy behavior.

## Implementation areas

Add reusable-set domain records and operations beside EditSession; extend EditState/ProjectData additively, preserving old projects. Integrate set application with HitEditor/ArrangeView's existing drawing gestures, and add a focused inspector/dialog partial for setup and set/placement commands. Use existing file safety, hashing, safe sample allocation, project persistence, preview and export facilities. Avoid changes to chart timing, unrelated audio or the family layout.

## Verification and delivery

Test create/apply/Add/Replace, preserved whistle example, duplicate adoption, first-link ownership, overlapping sets, missing/changed samples, snapshot portability, per-member/overall gain, local deletion/reset/detach, map/library updates, linked edits with one-step undo/redo, whole-placement move/copy/duplicate and project round trips. Cover Save/Generate/Copy and reloaded effective layer/audio behavior.

Run unit/compiler/decompiler/map-corpus tests and current-source headless UI checks sequentially in isolated build output, leaving the user's running editor intact. Inspect the new card/dialogs and ordinary drawing at 1600x1000 and 1280x820. Complete inline simplify, correctness/standards/spec and security review. Report native playback and external ranking evidence separately.

The owner's approval includes implementation, commit and push on feature/electron-lane-grouping. No merge or release is implied. Rendered-audio trimming/effects and original unreproduced crash/offset/filename reports remain outside this feature.
