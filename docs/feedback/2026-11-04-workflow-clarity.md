# Remaining workflow priorities (findings 2, 3 and 4)

These changes build on the uncommitted compact-window P1 work. Audio, note editing, sample replacement semantics and export policies remain unchanged.

- **Active lane** replaces the Channel card title, while retaining its stable preference key. It distinguishes active lane from selected hits and explains that assigning a sample affects every hit in that lane. A selection containing hits from one lane offers an explicit **Use selected hit's lane** action; selections across multiple lanes do not guess a target. Activation keeps notes and selection unchanged. Lane and sample filenames use separate labels when their identities differ. The tutorial, status prompts and README use the same terminology.
- **Copy hitsounds** starts with no targets selected. Select all and Clear selection preserve an efficient batch path. The action is disabled until targets are chosen, then names its scope (for example, Replace hitsounds in 2 difficulties or Copy into 1 difficulty). Source changes retain deliberate choices. Existing replacement modes, advanced options, backups and execution logic are preserved.
- **Output guide** identifies the explanatory toggle. Loose-hit warnings name Save .osu and point to HS mode or Generate. Copy results put completed/attempted counts and not-written counts on their first line. At short window heights, outcome and warning each get one line, with full text in tooltips and Details. Operation feedback precedes longer warnings.

## Validation

The isolated source is E:\Claude\workflow-clarity-20261104. The full solution passes 613 tests (App 226, Format 52, Lanes 335), covering compiler/decompiler, editing and the existing 2,999-file corpus. Logs are tests.log and final-tests.log; the final App suite also passed in final-app-tests.log after review corrections.

The local WorkflowCheck harness passes **61 native headless checks**, including explicit activation without note or selection changes, multi-lane ambiguity, empty Copy targets and disabled Enter, Select all/Clear selection, source retention, dynamic mode/count labels, Advanced, cancellation, persistent results, two-line compact feedback, Details, geometry and unchanged fixture bytes. The P1 CompactCheck harness still passes **69 checks**. Rendered captures are under artifacts/workflow and artifacts/compact. Helpers are local validation artifacts, not application dependencies.

Live Windows inspection showed the new Active lane title/instructions and explicit activation control in a disposable fixture. The user began using the first helper instance with a real map and Save dialog; it was left untouched. A second fixture used a distinct executable path. Ongoing user input prevented reliable further automated clicks, so no Copy or Save operation was submitted through the native UI. Pointer interactions above were checked headlessly. Only the separate disposable fixture process was stopped. No hardware playback or OS screen-reader behavior was tested.

## Correctness

0 outstanding findings. Checked that lane activation does not invoke sample replacement, that Copy requires explicit targets, and that changing source/mode keeps target scope and labels consistent. Feedback retains its full underlying text when compact display truncates it. Review also corrected Copy success counting: unchanged successful targets count as completed even when they need no backup.

## Standards

0 outstanding findings. Inline simplification reuses the existing SetActive action, stable card ID, dialog configureOk hook, target-choice map and operation result path. No new dependency or alternate exporter was added.

## Spec

0 outstanding findings against the three remaining numbered critique recommendations. P1 remains intact. Scope is interface clarity and deliberate Copy targets; replacement modes and note/audio/export behavior are preserved.

Security review found no concrete exploitable changes: plain text labels/results remain plain text, targets come from the existing difficulty list, and no new shell, network, parser or file-write paths were added.

No release was published for these changes.
