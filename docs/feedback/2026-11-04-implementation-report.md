# Feedback implementation and verification

Implemented locally on `feature/electron-lane-grouping`, based on `db04c6c14ae6c0ce464c5aeeffe9f2161637bc6e`. This is the C# Avalonia editor despite the branch name. The owner subsequently authorized committing and pushing this update to the same feature branch. No merge, release or package is included.

## Delivered behavior

- Save .osu, Generate and Copy use a shared pairing planner. Different sound types remain separate through compatible copies. Simultaneous finishes fuse into hitfinish, retaining a separate kick/hitnormal. A copied singleton keeps its original bytes and extension. Source files and original editable layers are preserved.
- Allocation protects occupied filenames across extensions and indexes referenced by other difficulties/storyboards. Hash-bound reuse includes source content and rendered balance. Matching-combination overrides also distinguish relative balance while allowing overall gain changes.
- File → Review output shows filenames, previews individual outputs and original combinations, exposes existing priorities/presets, and supports timestamp or matching-combination sampleset choices. Explicit incompatible “Keep original files” choices block export rather than drop layers. Filenames refresh on the next review/save after choices change.
- Green lines are the default; Per-object is optional. Untouched fields remain untouched. Slider nodes retain green-line handling. Circles inside active slider audio can use object fields to avoid changing that slider; the review explains the exception.
- Ctrl+L invokes the existing confirmed Revert command, respecting custom bindings. Missing-object markers are initially hidden; actual export problems are initially shown. Small markers above the lanes provide hover reasons. Visibility never bypasses validation.
- Preferences contains a main hitsound folder. File and inspector commands expose library/map folders. Sample pickers remember the last folder globally with an active-mapset fallback. Z/V include loose hits, muted/folded lanes and the existing second-press song bounds. Timeline click gestures are unchanged.
- Rendering and validation precede transactional writes. Changed inputs, output difficulties, reused samples and late filename collisions stop export. Target failures roll back newly created samples. Copy can prepare unsaved source layers without saving the source difficulty.

## Verification

Final full solution run: **531 passed, 0 failed, 0 skipped**: Format 52, Lanes 283, App 196. This includes format round trips for **2,999 corpus files** (2,932 `.osu`, 67 `.osb`), decompilation and compiler/edit checks over the `.osu` corpus. Command:

```powershell
dotnet test OsuHitsoundEditor.slnx --no-restore --verbosity quiet -p:NoWarn=CA1416
```

New regressions cover role separation, same/cross-bank finish fusion, extensions and occupied indexes, different gains, balance-sensitive matching, scoped overrides, unchanged low-volume/custom-filename objects, source changes, stale output/reused files, filename collisions, rollback, styles, overlapping slider preservation, reopening original layers, keybinding customization and warning/folder defaults.

The current-source Avalonia headless harness passed **26 checks**. It operated on unique synthetic mapsets under ignored `artifacts/feedback-validation/`, exercising normal Save and cancellation, Review settings and preview errors, actual Ctrl+L/Revert and cancellation, Generate confirmation/follow-up, unsaved-source Copy, marker hover reasons, navigation, and library/map folder buttons. BASS decoded the saved normal plus finish samples; their summed waveform and gain matched all original layers within 0.0002 per float sample. This verifies decoded sample data, not physical device playback. Review and editor screenshots at 1600×1000 and 1280×820 were inspected.

Evidence: `artifacts/feedback-validation/full-tests.txt`, `ui-results.txt`, `review-output.png`, `editor-1600.png`, `editor-1280.png`; harness `ui/Program.cs`. Build/test runs were sequential to avoid competing Avalonia-generated reference assemblies. The existing CS4014 warning at `MainWindow.axaml.cs:215` remains; the test harness also uses an obsolete headless keypress overload.

## Correctness review

Reviewed planning, compiler effects, manifest reuse, file writes and UI command paths inline. Fixed issues found during implementation: whole-combination fusion losing sound roles; normalized-gain reuse returning the wrong object volume; unchanged custom filenames being unnecessarily prepared; slider audio affected by circle green lines; incomplete reused-file checks; stale Generate plans; and intermediate Copy assets being written unused. Added regression coverage for these cases. No remaining concrete correctness finding was identified within the implemented scope.

## Standards review

Used the approved plan, existing partial-window/dialog conventions, preferences/keymap infrastructure, compiler validation and transaction helpers. Simplification removed an unused constructor argument/local, reused settings deep-copy and folder-selection helpers, and avoided a reverse-lookup scan for each projected moment. Existing legacy API defaults remain for compatibility; app entry points explicitly select pairing and the configured style. No new dependencies. Review was inline in accordance with the current root CLAUDE.md; initially started parallel reviewers were stopped when that rule was found.

Git status/show worked, but `git diff HEAD` failed with “this operation must be run in a work tree.” Source/test comparisons were therefore generated from `git show HEAD:<path>` using `artifacts/feedback-validation/make-diff.py`; no successful git-diff check is claimed. Unrelated `.claude/`, `NOTES.md` and `workflows/` were preserved.

## Spec review

The approved local implementation scope is covered. External acceptance remains incomplete: native osu! playback/reopen/ranking-tool checks and native file-picker interaction were not performed. The official criteria/file-format research is documented in `2026-11-04-priority-revision.md`; documented fields and automated preservation tests are not a rankability guarantee. Original issue fixtures were unavailable; the remaining reports below are explicitly open.

Review finding count: Correctness 0 unresolved concrete findings; Standards 0 unresolved violations; Spec 2 outstanding verification categories (native compatibility and original-report reproductions).

## Security review

No new exploitable security finding was identified in the changed paths. Checked source/output filename containment, plain output sample names, occupied-file checks/CreateNew, reused/input hashes, rollback, bounded preset dictionaries/JSON, and Explorer invocation through ArgumentList. No new network calls, credentials, dependencies, archive extraction or privilege changes were introduced. This is a review of the changed code, not a security certification of the whole application.

## Remaining feedback

- F06: click/navigation behavior was intentionally left unchanged by owner instruction. The reported missed-click case is not reproduced.
- F07: longer snap options and their phase policy are deferred, along with reusable linked sound sets and rendered-audio editing.
- F09: the original export crash is unconfirmed without its map, samples and exact steps. Controlled Save/Generate/Copy and failure/rollback checks pass; they do not prove that report fixed.
- F11: the 0.785 versus 0.4805 offset example lacks confirmed units and test conditions. The global -17 ms default was not changed.
- F13: the literal unexpected filename “softer whistle 47” and creation path remain unconfirmed. New allocation/reuse checks do not identify that original file.
- Older generated files are conservatively retained in pairing mode to avoid deleting samples used elsewhere. Unused-file cleanup is not implemented; check a submission mapset separately.
