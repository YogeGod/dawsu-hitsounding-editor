# Compact editor workflow (P1)

The approved change keeps Open, Save, Generate, tools, snap and transport on screen at smaller window sizes. Existing Grid, View, Song and Playback controls move into labeled popovers as space narrows; the same controls and handlers are reused. Popovers return their controls to the original toolbar positions when the window grows.

The inspector starts closed below 1100 logical pixels wide, and the controller starts closed below 650 logical pixels high. Each remembers the user's explicit open/closed choice separately from its larger layout during the session. Automatic inspector collapse does not overwrite the saved wide-layout inspector preference. A manually opened controller retains a body area with scrolling rather than being squeezed to its header.

Validation uses the isolated source at E:\Claude\compact-layout-20261104. All 613 solution tests passed (App 226, Format 52, Lanes 335), including compiler/decompiler, editing and the existing 2,999-file map corpus. The final App suite passed again after popover changes. Local CompactCheck exercises 69 checks: geometry at 900x500, 1366x768 and 1600x900, panel restoration, all four compact popovers, unchanged snap spacing, retained grid/song values, usable manual controller height, valid timeline height and unchanged fixture bytes. Logs and native headless captures are under compact-checks.log, final-tests.log, app-final-tests.log and artifacts/compact.

Live Windows inspection used the new editor and an exact-size native fixture with audio and preferences disabled. It confirmed the compact layout and Controller reopening. The first View popover clipped Fit; its final layout groups visibility, colour and zoom into separate rows, confirmed in the actual native popover. No real map was opened or written by these live checks. Native fixtures were closed after inspection. Hardware playback was outside this layout change.

Inline simplification reused existing controls and kept reparenting confined to four concrete groups. Correctness review checked original parent/index restoration and panel preference handling. Standards and approved-plan review found no outstanding issues. Security review found no new parsing, network, filesystem or process-launch paths in the application change. No new dependency was introduced. Existing audio platform and unawaited-call warnings remain.

## Correctness

0 outstanding findings. Control parent/index restoration, compact panel memory, source parity and selected-hit bounds were checked. All 613 tests and 69 compact checks pass on the final source.

## Standards

0 outstanding findings. Inline simplification reuses existing controls and handlers; no new dependency or unrelated change. TASKS.md records completion locally (the repository ignores that file).

## Spec

0 outstanding findings against the approved P1 plan. Main editing/file/transport controls stay visible; secondary controls use labeled popovers; compact panels are reopenable and larger-layout choices return on resize. Audio/export behavior is unchanged.

Security: no concrete findings in the application diff. No parsing, network, shell or sample-file write paths were added.

No release was published for these changes.
