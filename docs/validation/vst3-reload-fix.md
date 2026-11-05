Current status: Kontakt reload correction and the live/export integration are implemented. The user declined Garstka repair; the pending-approval wording in the historical investigation below is superseded. See drum-vst-export.md for current validation.

# VST3 reload fix - 2026-11-05

## Result

Implemented the Kontakt reload fix in the experimental host. Plug-in discovery
now runs in `dawsu_vst3_scan.exe`, outside the process that restores and renders
the instrument. The real renderer consumes the resulting JUCE PluginDescription.
No plugin names or machine paths are hardcoded into the fix.

The comparison implicates in-process scanning/temporary-instance teardown as the
trigger: JUCE's slow scan creates, initializes, terminates and releases an IComponent.
The previous host did this immediately before creating the real Kontakt instance.
Later failures occurred in Kontakt code during GUI message dispatch. Stale callback
state after temporary-instance teardown is the working explanation; it is an inference,
not a vendor-symbol-level diagnosis of Kontakt's internal code.

The repair removes that temporary instance from the rendering process. Actual
rendering still runs in-process, so this is not a general crash sandbox.

## Controlled checks

| Sequence | Passed | Failed |
|---|---:|---:|
| Original repeated reload | 2 | 1 |
| Deactivate during restore | 5 | 3 |
| Dedicated audio worker, final batch | 4 | 4 |
| Initialize editor before restore | 4 | 2 |
| Skip in-process scan using diagnostic metadata (excluding priming scan) | 8 | 0 |
| Generic isolated scanner, fresh processes | 8 | 0 |

Rejected startup/thread candidates are absent from repository source.
The generic implementation has no hardcoded diagnostic cache.

Final matching build also passed:

- Kontakt state restore, capture, restore into a second instance and both velocity sweeps.
- Kontakt native editor opened AFTER a fresh state restore; GM mapping remained selected.
  Evidence: `scan-fixed-editor-GM.jpg` and `scan-fixed-editor-reopen.log`.
- 32 consecutive state captures with the final ABI build.
- Serum 2 state round-trip, both velocity sweeps and six buffer/MIDI/thread/state guards.
- Same real beatmap: 1356 triggers, 138.704399 s, drums peak 0.795638, complete tail,
  original source SHA-256 unchanged. Separate GM36/38/42/49 checks remain non-silent.
- 636 unit/compiler/editing/map-corpus tests: App 234, Format 52, Lanes 350.

The local native artifacts are in `E:\Claude\vst3-proof-20261105\msvc-build`.
The final matching smoke executable is in `E:\Claude\vst-diagnosis-20261105\release-fix-smoke`.

- dawsu_vst3.dll SHA-256:
  6D99056BD09000A828134E26CA369CA8D08A4AD64034E9B84E24D69BBC3F4CF8
- dawsu_vst3_scan.exe SHA-256:
  C75DEEB397C655DED1563D6E4EF95615462C5B368FC37CC83B9A36FB4BD3DAB9

## State capture correction

The old size query and copy serialized two different instrument snapshots. One
observed run grew between those calls and failed to save. The native bridge now
captures once on query, copies that same snapshot, then releases it. Empty/oversize
states fail within the existing 64 MiB bound.

The new descriptor-aware open entry point is `dv_open_v2`. The helper, DLL and
managed wrapper must be built together. Changing the entry-point name ensures an
old DLL cannot silently misinterpret the changed ABI argument list.

## Garstka cause and pending repair

The installed VST3 binary runs version **1.0.5.0, engine 0.9.5**, according to its
runtime log. The working standalone runs **1.1.5.0, engine 0.10.2**. The VST3's
moduleinfo file says 1.1.5 but does not match its binary.

The 1.1.5 VST3 installer log explains the mismatch: replacing the existing binary
failed with DeleteFile code 5 (in use/access denied), then Ignore was selected.
The old engine subsequently reports a Lua getArticulationIndex nil-value error.
That is the independent failure seen in both dawsu and REAPER.

Cradle Hub reports Installed and offers Uninstall plus View Content Installation
Path, with no VST3-only repair control. The uninstall/reinstall approval request
is pending. No uninstall, installation or activation change has been performed.

Rollback material is prepared locally:

- Garstka-VST3-before-repair/: existing VST3 binary and moduleinfo.
- Garstka-content-backup/: all 81 content files, 7,146,523,009 bytes.
- garstka-content-manifest.json: source/backup SHA-256 equality verified for every file.

After approval: use Cradle's supported reinstall flow, preserve the content and
activation state, verify the actual VST3 runtime version, then repeat both REAPER
and dawsu renders. Stop for any authentication/UAC interaction requiring the user.
Garstka is not yet claimed as a passing audio control.

## Correctness

Reviewed scanner lifetime, parent timeout/cleanup, descriptor/path/type validation,
ABI versioning, single-owner access, capture bounds and helper packaging. Fixed the
observed two-capture state race and prevented silent old/new ABI mixing. No remaining
code finding in this fix. External installation repair remains pending approval.

## Standards

Applied simplify inline: reused JUCE descriptors and discovery, standard process
arguments and the existing bridge. Removed all diagnostic paths, logging, audio-worker
and editor-prewarm experiments from production source. No normal editor startup or
release packaging change. No remaining standards finding.

## Spec

Kontakt reload correction is implemented and validated. Garstka's stale binary is
identified; its reinstall is prepared but awaits the action-time approval required by
the Computer Use skill. No commit, push or new release was requested or performed.

Finding counts: Correctness 0 remaining; Standards 0; Spec 1 pending Garstka repair.

## Security review

Checked explicit helper path, shell-free ArgumentList use, per-run temporary output,
30-second scanner timeout, metadata and state bounds, descriptor-to-plugin matching,
DLL search policy and repository contents. No new exploitable finding identified.
Selected plugins remain trusted native code. Scan isolation does not sandbox rendering.
No private states, licensed samples, activation data or crash dumps are copied into source.

Follow-up decision 2026-11-05: the user declined Garstka repair and confirmed leave installed as-is. The installation is unchanged; backups retained. Live Drums VST editor validation is documented in live-drum-vst.md.
