# Classic / Folder tree browser preference

Base: main 7ba81dcd4fc47f5a9ed8f0ac3df7ab9178d5f7f9.

Preferences > General > File browser offers Folder tree (sidebar) and Classic (inside inspector). The saved choice applies immediately. Existing preferences retain Folder tree. Classic uses a compact current-folder list with Up navigation; Folder tree retains expandable parents and the full left sidebar. Both reuse one browser instance, preview/assignment callbacks, drag data, file filters and read-only guards. View > File browser controls the chosen browser; Browse samples and the start screen reveal it.

## Validation
- 632 unit/corpus tests passed: 234 App, 52 Format, 346 Lanes. Preference roundtrip now covers ClassicBrowser.
- WorkflowSafety: 89 checks passed. StartScreenSafety: 42 checks passed. BrowserChoiceSafety: 23 checks passed.
- New checks cover the Preferences choice, persistence, current-folder listing, keyboard folder navigation, Up, preview versus explicit assignment, hide/show, repeated reparenting, current-folder retention, compact layouts and a new window restoring the saved choice.
- Native Windows: changed Folder tree to Classic in Preferences, applied, inspected compact browser inside inspector; reopened Preferences and verified Classic persisted, then switched back and inspected full sidebar returning immediately. Original Folder tree preference restored.
- Self-contained Windows Release publish succeeded. Existing audio/platform warnings remain; physical audio/ASIO and live osu! gameplay were not independently verified.

## Simplify
Kept one file-browser control and shared actions/scan code. Classic changes its presentation/navigation and host; no duplicated legacy browser implementation or new packages.

## Correctness
No remaining findings after checking control detach/reattach, persisted choice, asynchronous folder scans, mode switches, panel visibility and compact layout.

## Standards
No blocking findings. User confirmed Classic versus Folder tree and requested push plus replacement of V1.5. Changes remain scoped to this setting and regression coverage.

## Spec
Classic inside inspector and expandable Folder tree sidebar are selectable and remembered. Sample assignment stays explicit in both modes. No outstanding requested behavior identified.

Findings: Correctness 0; Standards 0; Spec 0.

## Security review
No exploitable findings. Checked directory navigation, stale scan cancellation, path filters, sample assignment guards, persistence and test fixture writes. No new production dependencies, process commands, network requests or changes to beatmap-open/save behavior.