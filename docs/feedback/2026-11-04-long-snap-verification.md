# Longer snap verification

Implemented the approved F07 follow-up after 33b0acb on feature/electron-lane-grouping. The owner's Q40A anchors long snaps to each red timing point; the subsequent OK authorizes implementation and committing to this branch.

## Behavior

The selector now offers 2/1, 3/1 and 4/1 alongside every existing 1/n choice. The new spacing represents whole osu! beats, independently of measure length. Green lines and editor-only time-signature changes do not reset it. Red-line offsets, including the first and negative offsets, define the grid. A nearer upcoming red line is a valid snap boundary even when it cuts a long step short.

Drawing, relative dragging, quantize, nudge, duplicate and repeat share the explicit positive SnapSpacing value. Repeats and long-grid duplicates reach a new red line before continuing on its new grid; previous/next-grid nudges handle both directions. Existing fractional snapping, step sizes, keyboard shortcuts and click gestures retain their behavior. Copy's slider-end snap-filter denominators were not changed.

Active long-snap positions use purple grid lines over the existing beat/bar references. The selector, LCD, status, Repeat dialog, tooltips and tutorial identify the selected spacing. Straight Alt+wheel adds 4/1 and 2/1; Triplets adds 3/1. Shift+1–9 still selects 1/n. No file-format, export, audio or dependency changes were made.

## Tests and evidence

Full solution: **543 passed, 0 failed, 0 skipped**: Format 52, Lanes 283, App 208. This includes the existing 2,999-file format corpus plus map decompilation/compiler/edit checks. Twelve new app test cases cover 2/1–4/1, input-map preservation, first/negative red offsets, green lines, time signatures, BPM/red boundaries, nearest truncated steps, wheel order, editing operations, backward nudges, repeat/duplicate and undo.

```powershell
dotnet test OsuHitsoundEditor.slnx --no-restore --verbosity quiet -p:NoWarn=CA1416 -p:BaseOutputPath=bin/long-snap/
```

The current-source headless UI harness passed **36 checks**: all 26 previous feedback workflows plus selector/LCD changes, actual mouse placement at 1640 ms landing on the 1500 ms 3/1 grid, Repeat wording, straight/triplet Alt+wheel and Shift+4 returning to 1/4. Screenshots at 1600×1000 and 1280×820 were inspected; active long-grid lines and the 3/1 LCD label are visible. Existing narrow-window toolbar crowding was not redesigned.

Evidence is under ignored `artifacts/long-snap/`: `full-tests.txt`, `ui-results.txt`, `long-snap-1600.png`, `long-snap-1280.png`, and `ui/Program.cs`. The first build attempt failed because the user's running editor locked the normal output DLLs; subsequent builds used isolated `bin/long-snap/` directories, leaving that editor open. Test and harness builds ran sequentially. Existing CS4014 at MainWindow.axaml.cs:215 and obsolete keypress-overload warnings in the harness remain. Hardware audio/native osu! gameplay was not tested for this editor-only change.

## Correctness

Reviewed first/negative offsets, red-line truncation, signature splits, prior fractional behavior, wheel ordering and edit call sites. Corrected a nearest-boundary case where rounding to the old grid could miss a closer red line, removed a zero-time sentinel that would invent a boundary for negative red offsets, and made backward grid nudges/duplicates honor red boundaries. Regression coverage includes those cases. No unresolved concrete correctness finding identified.

## Standards and simplification

Used an explicit positive spacing type instead of overloading negative divisors. The same spacing reaches all editing operations, with small existing int overloads retained for fractional model callers/tests. Shared red-boundary lookup avoids scanning earlier map segments on each repeat step. Kept existing UI layout and fractional rendering. Updated help and feedback status. Review was inline under root CLAUDE.md; no subagents were used. The ordinary git diff command still failed with the existing work-tree error, so source/test review used git-show baseline comparisons; staged whitespace checks are run before committing.

## Spec

The approved long-snap plan is covered. Reusable sound sets and original unreproduced feedback reports remain separate. Existing map timing, volumes, output rules and timeline click gestures were not changed.

Review findings: Correctness 0 unresolved; Standards 0 unresolved; Spec 0 missing requirements within this follow-up.

## Security

No new exploitable finding identified. Checked spacing construction, red-line lookup and grid iteration against the existing timing bounds, repeat bounds and viewport range. No new deserialization, filesystem writes, external processes, network calls, secrets, permissions or dependencies were added to the application.
