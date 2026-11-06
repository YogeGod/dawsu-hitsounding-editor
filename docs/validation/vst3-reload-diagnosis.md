Current status: this is a historical investigation. Isolated discovery subsequently fixed the tested Kontakt reload failures (see vst3-reload-fix.md). The user declined Garstka repair; leave its installation unchanged.

# VST3 reload diagnosis - 2026-11-05

Conclusion: the Kontakt crash is intermittent and occurs inside Kontakt, but a
Kontakt-only defect has not been established. The requested Garstka control is
unusable in its present VST3 installation: it displays the same engine-load error
in both our host and REAPER. Its separate standalone application displays its kit.
A successful control render must wait until the Garstka VST3 can load independently.

## Controlled observations

All dawsu probes and full-map render used the unchanged native bridge SHA-256:
`12DDDAD20234D7B3393DA131FA3D7683433D782A171969DF3063C0D8BDE53B18`.
Plugin paths are the installed Kontakt 7.vst3 and GGD Matt Garstka Signature Drums.vst3
under C:\Program Files\Common Files\VST3. Garstka moduleinfo reports wrapper 1.1.5.

| Test | Observation |
|---|---|
| Garstka VST3, original host UI | Error: Matt Garstka Signature Drums cannot be loaded |
| Garstka VST3, minimal host, installation working directory | First processing block rejected |
| Same Garstka VST3, REAPER 7.35/x64 | Same cannot-be-loaded message; screenshot saved |
| Garstka standalone | Kit interface loads; no physical audio verification claimed |
| Earlier Kontakt GM state, minimal host | One warm-up crash, then cold and warm-up retries pass |
| Later Kontakt GM state, minimal host | Fresh-process restore and four-note render pass |
| Later Kontakt GM state, full map renderer | All 1356 triggers render; source unchanged |
| Earlier Kontakt GM state, REAPER | State loads, GM interface visible, offline four-note WAV is non-silent |

REAPER offline WAV: 8 seconds, 44.1 kHz stereo PCM24; peak -6.996974 dBFS,
RMS -37.899423 dBFS. The first realtime meter attempt returned zero; that attempt
is not counted as audio proof. The separate offline WAV is the cross-host audio proof.
The private JUCE state was losslessly unwrapped into standard VST3 component/controller
chunks for REAPER; no kit data or note assignments were edited in this conversion.

Four most recent Kontakt crash dumps contain access violation 0xC0000005 in
Kontakt 7.vst3, at offsets 0x23D9861, 0x23D9F0A, 0x23D9861 and 0x23D9F94.
A fault address identifies the crashing module, not whether a host call triggered
an instrument bug. Vendor symbols and a repeatable failing sequence are unavailable.

Three further bounded sequential fresh-process probes, including two 10-second
silent processing warm-ups, are recorded in repeat-summary.json. Each used the
exact earlier state file; these are repetitions, not a claimed fix.

## Other finding

One minimal run processed audio successfully but SaveState reported that the
serialized state grew between size query and copy. A subsequent save succeeds.
The existing bridge performs two captures; it should eventually capture once or
retry within its 64 MiB bound. This is a host robustness issue, distinct from the
Kontakt access violation. Production host code was not changed in this investigation.

## Evidence

Local root: E:\Claude\vst-diagnosis-20261105

- garstka-reaper-failure.jpg: same Garstka error in an independent host.
- garstka-cold-1.log: minimal-host first-block failure.
- kontakt-exact-state-1.log: warm-up crash after restore returned successfully.
- kontakt-exact-state-cold-2.log, kontakt-exact-afterprocess-2.log: successful retries.
- repeat-summary.json, repeat-*.log: final sequential repeatability checks.
- maprender-reload-1.log and maprender-reload-1/: successful fresh-process full-map render.
- reaper-control/control.log: Garstka independent-host setup.
- reaper-control/kontakt.log: state load accepted; realtime meter attempt zero.
- reaper-control/Kontakt-cross-host.wav: non-silent independent offline render.
- Probe.csproj and Program.cs: standalone diagnostic harness; no release integration.

REAPER ran with a disposable configuration, dummy audio device and generated test
project. No existing beatmap or project was opened for writing. Only diagnostic
REAPER processes started by this investigation were stopped. Proprietary plugin
states, crash dumps, presets and generated audio remain local and are not committed.

## Interpretation and next step

Do not dismiss this as just Kontakt, and do not cite Garstka as a passing control.
The evidence establishes an intermittent Kontakt failure plus an independently
failing Garstka VST3 installation. It does not isolate the Kontakt fault's root cause.
Repair/verify Garstka VST3 in an independent host before rerunning it as an audio
control. Keep fresh-process Kontakt state restore marked unreliable meanwhile.

References used to check host behavior: Steinberg's persistence FAQ describes
component/controller state restoration, and its processing FAQ permits offline
state and processing calls on the same thread. This checks basic lifecycle
expectations, not compatibility with every implementation.

- https://steinbergmedia.github.io/vst3_dev_portal/pages/FAQ/Persistence.html
- https://steinbergmedia.github.io/vst3_dev_portal/pages/FAQ/Processing.html
- https://www.reaper.fm/sdk/reascript/reascripthelp.html (TrackFX_SetPreset supports .vstpreset)

Final repeated failure: 05_11_2026 08_03_32 - Kontakt 7-mini.nicrash,
0xC0000005 in Kontakt 7.vst3 + 0x23D9861. The last three sequential runs
were PASS / PASS / crash-and-timeout; the failed run returned from state restore
successfully and faulted during the silent processing warm-up.
