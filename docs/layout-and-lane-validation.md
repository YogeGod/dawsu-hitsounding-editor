# Layout, lane selection and native export validation

The approved scope retains the dark DAW editor and the pending workflow safety fixes. Secondary settings now have stable Grid, View, Song and Playback entry points at every width. Toolbar controls use consistent sizing and quieter states; the inspector uses flat, collapsible sections with fold indicators. Existing inspector preferences are retained; fresh preferences expand Active lane only. The supplied skin contributes transport, Help and Settings icons. Note diamonds use up to half the row height instead of 40%, producing approximately 20% larger diamonds with the shipped theme while retaining their centres and hit testing.

Lane selection is separate from selected hits and the active sample editor. Plain click selects one lane; Ctrl toggles; Shift selects a visible range; Ctrl+Shift adds a range. Selected lanes share a highlight and count. Mute/Solo follows the clicked lane's next state for the selected group. Plain selection and Escape end grouping without clearing mixer states. Hidden/deleted lanes leave the group. Same-map view restoration retains selection; project files do not persist temporary groups.

The shared pairing planner checks exact native encoding before saved merge overrides. Native combinations are absent from merge preparation and retain their original files and volumes. Incompatible indexes/banks may still use byte-for-byte pairing copies. Same-role overlaps or distinct layer gains that native fields cannot represent retain their existing preparation path. No gameplay objects are added to avoid a mix. The native encoding model was checked against the official [osu! file-format reference](https://github.com/ppy/osu-wiki/blob/master/wiki/Client/File_formats/osu_%28file_format%29/en.md).

## Validation

- Full solution: **627 passed**, no failures or skips (233 app, 52 format, 342 lanes), including the existing 2,999-file local map corpus.
- Seven new lane-selection tests cover modifier behavior, forward/reverse/shrinking ranges, hidden lanes, group mixer states, deselection, data preservation and view restoration.
- Seven new native-export tests include all 72 normal-bank/addition-bank/addition-flag combinations, plus both output styles across Save, Generate and Copy. Render callbacks throw if called for native combinations; sample filenames and bytes remain unchanged.
- Retained workflow safety harness: **69 passed**, including actual pointer-driven Ctrl/Shift selection and grouped M/S checks and availability of all four settings entry points at three sizes.
- Prior workflow harness: **61 passed**. Compact harness: **73 passed**.
- Rendered at 900×500, 1366×768 and 1600×900, including settings popovers and selected lanes. Reviewed the wide/compact editor, grouped selection and Playback/View popovers. The final Playback popover has readable headings, aligned 28px controls and a bounded scroll area.
- Impeccable's layout detector returned `[]`; its native Avalonia coverage is limited, so rendered checks provide the layout evidence.

The Windows test build is framework-dependent and requires .NET 10. Audio-device playback and live osu! import/playback were not verified. Build warnings remain in existing audio platform annotations and the existing unawaited call; the headless harness uses existing deprecated input overloads.

## Simplification

Reused controls, event handlers, compiler capability checks and existing mixer sets. Replaced width-dependent reparenting with one-time placement into settings popovers. Removed the redundant native-encoding branch after making native handling unconditional before overrides. Kept the skin bitmap unchanged and used cached alpha masks to tint it.

## Correctness

No outstanding findings. Reviewed selected-lane versus active-lane scope, mixed M/S states, range anchors, view restoration, sample roles, shared volumes and native-first handling. Corrected inspector header styling so keyboard focus retains the shared focus border.

## Standards

No outstanding findings. Reviewed inline against workspace instructions; no new production dependencies or background services. New tests cover behavior and output rather than duplicating the rendering implementation.

## Spec

No outstanding findings against the approved layout, larger diamonds, multi-lane behavior and native-first export plans. Existing output-safety changes remain present. Finding counts: correctness 0; standards 0; spec 0.

## Security

No exploitable findings in the changed paths. Checked asset loading, file provenance, native output planning, test file paths, dependency changes and process execution. Asset references and glyph coordinates are fixed local resources; changes add no network execution, archive extraction, credential handling or shell commands to the app.

Integrated around the concurrent 20980f2 release-preparation commit, retaining its 1.4.0 version metadata and release notes. This change publishes no release; the local package is for testing.
