# Reusable hitsound sets: implementation and verification

Implemented after 68cd020 on feature/electron-lane-grouping, following the owner's approval of the concrete reusable-set plan and Q41A/Q42A/Q43A/Q44A. Commit and push are authorized to that feature branch. No merge, release or portable package is included.

## Delivered workflow

- Sound sets is a foldable/reorderable inspector card. Create from selected simultaneous sounds or a setup dialog; choose a set and use the existing Draw tool. Single sample retains ordinary drawing. Creation from a selection also links that source occurrence as one undoable action.
- Add avoids duplicate sounds and adopts an independent match with its current volume. The adopted sound uses an unchanged frozen audio copy so later source-file changes cannot alter the set. Replace previews its affected sounds once per action/stroke and preserves unrelated sound types. Cancel makes no edit.
- Set edits update linked map-local placements as one action. Placements have overall gain, member overrides, Select whole set, Reset to set and Make independent. Local member deletion stays deleted until reset. First-set ownership protects shared components; shared sounds follow the first set's volume and edits. Ordinary single-hit selection still works, with linked companions highlighted.
- Whole-set move/copy/duplicate preserves links and overrides. Moving/copying a partly shared set to an empty time supplies its required sound at the new time without stealing the original owner's link. Cross-map copy/paste creates new map-local definitions and copies frozen audio, including local audio substitutions, as one undoable action.
- Set definitions, stable bindings, placement gain and overrides are additive project data. Save/reload and HS ownership retain original member layers. File hashes revoke stale bindings after external map edits. Revert discards loose placements without allowing a later definition update to resurrect them.
- Personal-library `.dawsuset` files embed audio and have recognizable filenames with stable IDs. Saving again updates the same file. Import makes a local copy; Update checks library identity and preserves local placement changes. Other maps do not update automatically.
- Snapshot allocation skips occupied/referenced indexes and never overwrites source/shared samples. Changed or missing snapshot files produce diagnostics and block export; editing a set can restore its saved audio through a fresh safe copy. Very quiet set gains, including zero, are baked into output audio to preserve the intended sound below osu!'s control-point minimum. Other layers retain existing minimum-volume behavior.
- Save, Generate and Copy retain the role-preserving pairing policy: finishes fuse into finish while the kick remains separate. Review output provides prepared-file audition/filename inspection; original layers and rendered output are not played together. Normal maps with set layer volumes retain the normal Save difficulty wording.

## Verification

Full solution: **568 passed, 0 failed, 0 skipped**: Format 52, Lanes 308, App 208. Existing format round trips cover **2,999 corpus files**, with decompiler/compiler/edit checks over the maps. The app suite was also rerun after the final readable-library-filename change; model/corpus code was unchanged by that last UI adjustment.

```powershell
dotnet test OsuHitsoundEditor.slnx --no-restore --verbosity quiet -p:NoWarn=CA1416 -p:BaseOutputPath=bin/sound-sets/
```

Twenty-five new model/integration cases cover normal/HS placement, adopted volumes and frozen lanes, Replace, ownership, gain/overrides, deletion/reset/detach, linked undo/redo, whole-set move/copy, shared-set moves, new-member collisions, JSON project bindings, external-hash revocation, self-contained library snapshots, changed sources, HS Save/reopen, discarded loose bindings, malformed paths/member names, custom-named audio, very quiet/zero gains and snapshot-tamper diagnostics.

The current-source Avalonia headless harness passed **58 checks**, including the prior 36 feedback/snap workflows and new set creation/Undo, actual mouse drawing, volume/set editing, readable stable library filenames, embedded audio, explicit library updates, copy/paste, drawn Replace confirmation/cancellation, Save/reload, Revert and cross-map paste. Generate and Copy outputs were BASS-decoded and compared to intended member sums within 0.0002 per float sample. Zero-volume output decoded to silence within 0.000001. Setup-dialog and inspector screenshots at 1600×1000 and 1280×820 were inspected; the new numeric inputs have readable foregrounds. Existing narrow-window toolbar crowding was not redesigned.

Evidence is under ignored `artifacts/sound-sets/`: `full-tests.txt`, `app-tests.txt`, `set-tests.txt`, `ui-results.txt`, `set-setup.png`, `sound-sets-1600.png`, `sound-sets-1280.png`, and `ui/Program.cs`. Builds/tests ran sequentially in separate `bin/sound-sets/` output, leaving the user's running editor intact. Native picker interaction, physical device playback and osu! gameplay/ranking-tool validation were not performed; decoder comparisons do not establish those checks. Existing CS4014 at MainWindow.axaml.cs:215 remains, plus obsolete headless-keypress warnings in the temporary harness.

## Correctness review

Reviewed metadata through equality/deduplication, HS reanchoring, move/copy/clipboard, volume edits, gesture undo, project save/reload, library updates and export. Fixed issues found during development: metadata lost on recreated hits; shared members disappearing on a whole-set copy/move; source linking or batch failures creating partial history; quiet/zero gains being raised on export; adopted hits still pointing to mutable original files; stale loose links reappearing after Revert; unrelated maps being coupled by clipboard definitions; and misleading HS wording for normal maps. No unresolved concrete correctness finding was identified within the approved scope.

## Standards and simplification

Used existing immutable EditState/history, partial window/dialog conventions, inspector card folding/reordering, safe writes, sample allocation, hashing, preview and pairing. Shared definition rendering and reconciliation keep local changes separate from explicit definition updates. Frozen-audio fingerprints are cached by immutable snapshot buffer identity, avoiding repeated full-audio hashing during linked updates. The import helper is shared by picker-driven import/update and tests. No dependencies or framework changes. Ordinary git diff remains unavailable with the prior work-tree error, so review compared against git-show HEAD; staged whitespace is checked before commit. Review was inline under root CLAUDE.md; no subagents were used.

## Spec review

The approved reusable-set plan is covered. Snapshot files are conservatively retained after Undo or a failed later action; the editable state rolls back, and no existing sample is overwritten. Library saves are explicit disk operations and do not imply cross-map filesystem Undo. Project links remain editor metadata; `.dawsuset` is the portable definition/audio format. Native platform checks are explicitly unverified. Original unreproduced crash/offset/unexpected-filename reports and rendered-audio trimming/effects remain outside scope.

Review findings: Correctness 0 unresolved concrete findings; Standards 0 unresolved violations; Spec 0 missing implementation requirements, with native verification limits above.

## Security review

No new exploitable finding identified. Checked plain/contained asset paths, validated sound roles and IDs, bounded name/member/audio/project sizes, file length checked on the opened library stream, CreateNew allocation, unchanged-copy reuse proofs, snapshot hashes, hash-bound restoration, duplicate binding rejection and rollback. Imported bytes are decoded before installation in the app. No new network access, command execution, credentials, dependencies or privilege changes were introduced. This is a review of the changed paths, not a security certification of the full editor.
