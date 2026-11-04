# Hitsounding trust coverage matrix

Status: initial evidence inventory and proposed release gate, not a claim of complete coverage or a test report.

Prepared 2026-11-04. Product snapshot: [`9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548`](https://github.com/YogeGod/dawsu-hitsounding-editor/commit/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548). Official wiki snapshot: [`fe4c485e83007b421361dc895da2df4e06631d85`](https://github.com/ppy/osu-wiki/commit/fe4c485e83007b421361dc895da2df4e06631d85).

## Purpose and agreed decisions

Expose rules and combinations that may have been overlooked while developing dawsu. Cover importing, editing, preview, Save, Generate, copying to playable difficulties, and final verification in osu!stable.

Every relevant scenario has three independent acceptance gates:

1. Sound: every intended sound survives with its intended source, time and volume.
2. Preservation: unrelated sounds, gameplay, difficulties and shared assets stay unchanged.
3. Ranking: applicable mandatory criteria are checked; recommendations and exceptions are recorded separately.

An event scheduled by the code is not necessarily audible against the song. Likewise, audible output does not establish correct timing, preservation or rankability. General map-ranking eligibility includes matters outside this hitsounding matrix; this is not a certificate that an entire map is rankable.

Settled: normal Save blocks by default when any intended edit cannot be represented, before writing, and retains unsaved work. No partial-save override has been approved. Generate/Copy blocking, handling passive slider side effects and cross-target rollback are still open decisions, explicitly listed below.

## How to read and maintain this file

A row is a **scenario family**, not one executable test. Expand its listed values into individually identified instances such as S04.a before claiming that family covered. The initial inventory has 76 families, not 76 completed tests. It is deliberately extensible.

Stages: **I** import/reopen; **E** edit; **P** dawsu preview; **S** Save into an existing map; **G** Generate helper difficulty/mixes; **C** Copy into playable difficulties; **V** verify final playable output in stable.

Evidence states are separate:
- Automated evidence: a related test exists in source, but was **not run in this inspection**. The evidence index describes its limits. Test presence does not imply every variant in a row is covered.
- Stable evidence **NT**: no stable verification established for this snapshot. **H-...**: a historical listening result is documented, with limitations below.
- **UNVERIFIED**: expected outcome not established end-to-end.
- **UNKNOWN**: the expected stable behaviour itself needs investigation.
- **GAP:source**: source inspection contradicts the selected behaviour; not a newly executed failure.
- **RISK:source**: a source-inspected path may omit a layer or result; reproduce it before calling it a demonstrated runtime bug.
- **POLICY-OPEN**: the desired product behaviour has not been decided. It cannot be counted as passing.

Release selection:
- **CORE**: always include appropriate automated checks and the mapped core manual fixture groups.
- **CHANGE:...**: required when the named area or a dependency changes. Select all implicated rows, not just the file directly edited.
- A selected unknown, failed, blocked or missing-fixture case cannot be counted as a pass. Document unresolved cases and the decision to restrict affected release scope; do not quietly mark them deferred.
- Do not carry a historic pass forward as a fresh run. Retain its app/fixture/stable versions and date; record why the case was or was not selected again.

No automated tests, stable playback, hardware output tests or new fixture generation were performed while preparing this matrix.

## Source hierarchy and ranking checks

Use current official ranking criteria for ranking requirements. Use the official format documentation for documented encoding. For stable-specific disputed behaviour, retain a reproducible stable observation with exact client build and fixture hash. Lazer source is a comparator and source of hypotheses, not proof of stable behaviour. Internal tests show that code implements its assumptions, not that those assumptions match stable.

| Check | Requirement or review | Type and source |
|---|---|---|
| PRES | Original bytes preserved on no-op; only intended fields change; shared files protected; failed/blocked Save retains work | Product contract; [trusted exports proposal](../ideas/trusted-exports.md), [SAVE-SRC], [COMP-SRC] |
| RC1 | Active clicked parts need audible, distinct feedback. Separately check survival of every intentionally authored sound, including decorative layers | Mandatory feedback rule: [RC-G] Audio and [RC-O] Overall/General. Every-intended-sound survival is the product goal |
| RC2 | Each clicked part needs at least one feedback sample with clear impact/peak no later than 5 ms, using WAV or Ogg Vorbis; hitsound files have minimum length 25 ms; silence uses official blank.wav | Mandatory, with stated default hitfinish exception: [RC-G] Audio. Verify true encoding, not extension alone |
| RC3 | Storyboard audio cannot substitute for active feedback; avoid confusing non-input sounds with click feedback | Mandatory replacement rule and separate guideline: [RC-G] Audio/Storyboarding |
| RC4 | No unused or zero-byte mapset files (thumbs.db exception); silence must use the official 44-byte blank.wav | Mandatory: [RC-G] General/Audio. Treat canonical blank.wav as a dedicated asset case, not an arbitrary too-short sample |
| RC5 | No duplicate same-type timing points; no green before first red; first red cannot toggle kiai; preserve correct timing and consistent red-line BPM/offset across difficulties | Mandatory: [RC-G] Timing. A red and green point at the same instant is not a forbidden duplicate |
| RC6 | Listen for unwanted clipping, muffling and crackling | Guideline: [RC-G] Audio. Automated amplitude checks cannot replace listening |
| RC7 | Preserve gameplay object geometry/rhythm; validate applicable snapping and final playable output; do not submit a helper as a normal playable difficulty without assessment | Preservation contract plus [RC-G] Timing and [RC-O] difficulty-specific rules. There are stated snapping exceptions |
| RC8 | Review end/reverse feedback, natural loop samples and tick loudness; review frequent soft/normal finish replacement under skin fallback | Guidelines with exceptions: [RC-O] General and Hard/Insane/Expert; [RC-G] Audio. Not automatic mandatory failures |
| RC9 | Hit objects, hitsounds and timing must be authored by direct human input, without generative tooling | Mandatory: [RC-G] AI policy. Reassess features that infer creative choices; deterministic encoding of user choices is the proposed product boundary |

Do not reject all MP3 decorative layers merely because one feedback layer must meet RC2. Do not count a sound as compliant merely because its volume field is nonzero. Test audibility in musical context under a recorded baseline with audible master/effect volumes; deliberate user mute or disabled beatmap hitsounds changes expected playback.

## Variable inventory

These are dimensions to enumerate when expanding families. Lists marked unresolved need research; they are not established support claims.

| Dimension | Values or distinctions to retain | Related families |
|---|---|---|
| Object/event | Circle; slider head, repeat, tail, tick, slide, whistle; spinner end, spin/bonus; storyboard sample; simultaneous objects | I03, L01–L08, T10 |
| Format | Supported .osu versions; absent/short fields; decimal times; malformed input; non-standard mode; .osb; BOM, encoding, line endings | I01–I04, I09 |
| Sound flags/banks | Normal bit unset/set; each addition and combinations; auto versus Normal/Soft/Drum; explicit versus inherited normal/addition banks | S01–S03, S08 |
| Index/source | Object inherit versus effective index 0; unsuffixed 1; 2 and high N; custom file, skin, built-in fallback; explicit filename override | S04–S07 |
| Sample files | WAV/MP3/OGG; actual encoding; mixed extensions/case; same basename; missing, replaced, undecodable; mono/stereo and sample rates | I06–I08, S05/S06, G03, P03 |
| Timing | Red/green; same-time order and legal pair; first point; BPM, SV, meter, effects; lookup boundary -6 through +6 ms, including exact ±5 and fractional values | T04–T09, L03/L04 |
| Volume | 0 inheritance versus attempted silence; 1/5/8/100; malformed >100; object versus timing point; shared moment; per-layer mix gain | T01–T03, G02, C05 |
| Edit operation | Draw, erase, move, range-copy, repeat, quantize, volume/ramp, undo/redo; anchored versus loose hit; snap mode and BPM boundary | E02, E07, E10 |
| Lane/project | Sample import/index allocation; display hide/reorder; lane delete; reopen preferences/project; dirty session | E08, E11/E12 |
| Export | Save existing objects; Generate helper; fresh versus reused mix; ownership manifest; fresh directory rescan; write failure | E01–E06, G01–G06 |
| Copier | Overwrite everything/defined; leniency and ties; sounds, bodies, banks/indexes, volume toggles; keep 5%; tick/slide copying; storyboard skip toggles; mute-end divisor/length filters | C01–C08 |
| Shared state | Sibling difficulties and shared .osb; file in use elsewhere; changed files since open/plan; multiple targets and partial failure | E04, G05/G06, C07/C08 |
| Preview/client | Beatmap hitsounds setting; layered hitsounds setting; skin present/absent; muted/soloed lanes; offset, speed, seek, cycle; audio output and latency | S08/S09, E08, P01–P05 |
| Ranking context | Mandatory rules versus guidelines; active versus passive sound; masking and attack; final playable mapset versus helper; unrelated existing issues | R01–R08, E09 |

## Scenario matrix

The first table in each group defines its checks; the second records evidence. Evidence keys refer to the source and automated-test indexes below. Default sound expectations come from the cited source set; UNKNOWN rows explicitly require establishing an independent expected answer before creating an assertion.

### Import and file preservation

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| I01 | No-edit round trip: BOM, LF/CRLF/mixed endings, final newline, comments, decimal fields, unknown sections | I→S→I | Exact original bytes; repeat once with .osb present | All original .osu/.osb bytes and other difficulties | PRES | CORE |
| I02 | Old .osu versions and omitted/truncated optional fields | I→E→S | Preserve original form; unsupported edits must be identified rather than guessed | Version, object geometry and unrelated fields | PRES | CHANGE:format |
| I03 | Circle, slider head/repeat/tail, spinner end in one existing map | I→P→S | Every supported event resolves at the correct position; inspect saved result in stable | Object count, coordinates, durations and rhythm | RC1/RC7 | CORE |
| I04 | Storyboard samples in .osu and shared .osb; variables and relative paths | I→P→S→C | Read supported samples accurately; clearly identify unsupported constructs | Raw storyboard text, layers, variables and shared scope | RC3 | CHANGE:storyboard |
| I05 | Import .osz with sibling difficulties and shared samples | I→E→S→G→C | Resolve from imported mapset; do not detach or overwrite sibling assets | Archive contents and sibling sample references | RC4 | CHANGE:import |
| I06 | Missing, undecodable or unsupported audio on import | I→P→E | Make missing/unreadable sources visible; do not imply that playback proves completeness | Original references; edits remain recoverable | RC1/RC2 | CHANGE:import,audio |
| I07 | Path traversal, absolute paths, mixed separators and quoted paths | I→P→G→C | Read or write only allowed mapset paths; unsafe input is rejected visibly | Files outside mapset and unrelated in-mapset files | PRES | CHANGE:paths |
| I08 | Samples added, removed or renamed while a map stays open | I→E→P→S→G | Refresh resolution consistently; do not save from an invisible stale sample inventory | Other difficulties and current unsaved edits | RC4 | CHANGE:rescan |
| I09 | Non-standard mode, malformed object line or unsupported format construct | I→E→S | Expose unsupported scope; no silent apparent-success export of unmodelled edits | Original unsupported content | PRES | CHANGE:format,import |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| I01 | [FORMAT], [HIST], [FIXTURES] | FMT | NT | UNVERIFIED |
| I02 | [FORMAT], [HIST], [FIXTURES] | FMT | NT | UNVERIFIED |
| I03 | [FORMAT], [HIST], [FIXTURES] | DEC | NT | UNVERIFIED |
| I04 | [FORMAT], [HIST], [FIXTURES] | DEC/SB | NT | UNVERIFIED |
| I05 | [FORMAT], [HIST], [FIXTURES] | OSZ | NT | UNVERIFIED |
| I06 | [FORMAT], [HIST], [FIXTURES] | DEC/AUDIO | NT | UNVERIFIED |
| I07 | [FORMAT], [HIST], [FIXTURES] | SAFE | NT | UNVERIFIED |
| I08 | [FORMAT], [HIST], [FIXTURES] | DOC | NT | UNVERIFIED |
| I09 | [FORMAT], [HIST], [FIXTURES] | DEC/FMT | NT | UNVERIFIED |

### Sample selection and inheritance

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| S01 | Normal/Soft/Drum × hitnormal/whistle/finish/clap; individual and combined flags | I→P→S→C | Correct sources and additions at intended time and shared volume | Unedited events and original flag forms | RC1 | CORE |
| S02 | Object normalSet=0 and additionSet=0 versus explicit banks | I→E→S | Resolve inherited normal and addition banks correctly; addition auto follows resolved normal bank | Unrelated bank fields and events | RC1 | CHANGE:resolution |
| S03 | Timing-point sampleSet=0 versus missing field; change map default | I→P→S | Treat explicit auto and missing field as separate cases; recheck disputed stable behaviour | Do not normalize these two forms into one | RC1 | CORE |
| S04 | Object index=0 inheritance versus timing-point index=0; indices 1, 2 and high index | I→E→S→G→C | Distinguish inheritance from effective skin-only index; index 1 uses unsuffixed filename | Existing numbered files and sibling references | RC1/RC4 | CORE |
| S05 | Custom file absent; skin sample present/absent; bare hitclap file present | I→P→S | Establish exact stable fallback chain independently; mark unresolved fallback variants | Missing references and skin fallback semantics | RC1 | CHANGE:resolution |
| S06 | Same basename with WAV/MP3/OGG and mixed-case extensions | I→P→S→G | Verify exact extension precedence and case handling in stable; no claim from internal decoder alone | Duplicate files are not silently overwritten | RC1/RC2 | CHANGE:resolution |
| S07 | Filename override with and without addition flags; missing override file | I→P→E→S | Existing override preserved; stable replacement behaviour verified; unsupported editing visibly blocked | Raw filename field; no accidental additions | RC1 | CORE |
| S08 | Normal flag 0 versus 1 with additions; LayeredHitSounds enabled/disabled | I→P→S→C | Match expected skin layering; preserve original flag distinction | Do not force extra or missing normals silently | RC1 | CHANGE:resolution |
| S09 | Beatmap hitsounds enabled/disabled and two contrasting skins | P→S→C→V | Match declared client settings; distinguish deliberate client fallback from export loss | Saved sample assignments across settings | RC1/RC8 | CHANGE:skin |
| S10 | Same moment requests two normals, duplicate addition types, incompatible banks or indices | E→S | Block normal Save; show exact conflicting sounds and retain all edits | Entire original .osu; no partial write or state reset | PRES/RC1 | CORE |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| S01 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | NT | UNVERIFIED |
| S02 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | NT | UNVERIFIED |
| S03 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | H-AUTO | UNKNOWN |
| S04 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | NT | UNVERIFIED |
| S05 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | NT | UNKNOWN |
| S06 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/FOLDER | NT | UNKNOWN |
| S07 | [FORMAT], [CUSTOM], [HIST], [LAZER] | DEC/COMP | H-FILE | UNVERIFIED |
| S08 | [FORMAT], [CUSTOM], [HIST], [LAZER] | RULE/DEC | NT | UNVERIFIED |
| S09 | [FORMAT], [CUSTOM], [HIST], [LAZER] | DEC/AUDIO | NT | UNVERIFIED |
| S10 | [FORMAT], [CUSTOM], [HIST], [LAZER] | COMP/SAVE | NT | GAP:source |

### Timing and volume interactions

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| T01 | Object volume=0 versus explicit volume; timing volume changes | I→P→E→S | Zero object volume inherits; explicit value overrides rather than multiplies | Other moments and lanes share intended object volume | RC1 | CORE |
| T02 | Low volumes 0/1/5/8, 100 and malformed >100 values | I→P→E→S→V | Measure stable floors/clamps; do not treat zero as guaranteed silence | No invalid values introduced; original raw values preserved until edited | RC1 | CHANGE:volume |
| T03 | Volume change on one lane of a multi-sound object | E→P→S→C | Communicate and apply the one shared moment volume to all its sounds | Unrelated moments and their volumes | RC1 | CHANGE:volume |
| T04 | Timing change at event time and ±1/4/5/6 ms; fractional times | I→E→S→V | Verify effective lookup boundary in stable for each supported event kind | Adjacent events outside intended change | RC1/RC5 | CORE |
| T05 | Circle near slider node with incompatible index/volume; <5 ms separation | E→S | Find a safe encoding or block; never silently alter nearby event | Earlier/later event sounds and volumes | RC1/RC5 | CHANGE:compiler |
| T06 | Slider-node edit inserts green line and restores following events | E→S→V | Desired node changes; all non-target active sounds remain correct | SV, BPM, meter, effects, surrounding sounds | RC5 | CORE |
| T07 | Existing red and green point at same time; existing malformed duplicate same-type points | I→E→S | Keep legal red+green pair; never add duplicate red/red or green/green; report inherited problems separately | Original rhythm, point ordering and unrelated fields | RC5 | CHANGE:timing |
| T08 | First timing point, event before first red point, kiai and omit-bar-line flags | I→E→S | No illegal green-before-red or first-red kiai introduced; flag unsupported edit | Timing origin, SV and effect flags | RC5 | CHANGE:timing |
| T09 | BPM/SV changes, inherited SV and unusual meter with volume/index editing | I→E→S→C | Only intended sound settings change | Slider duration, tick/repeat positions, BPM, meter and SV | RC5/RC7 | CHANGE:timing |
| T10 | Two objects at same time; different requested volumes or samples | E→S→G→C | Represent each supported object correctly or report conflict; audit final playable target separately | No unintended extra playable objects | RC1/RC7 | CHANGE:compiler |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| T01 | [FORMAT], [HIST], [COMP-SRC] | RULE/DEC/COMP | NT | UNVERIFIED |
| T02 | [FORMAT], [HIST], [COMP-SRC] | RULE/DEC | NT | UNKNOWN |
| T03 | [FORMAT], [HIST], [COMP-SRC] | EDIT/COMP | NT | UNVERIFIED |
| T04 | [FORMAT], [HIST], [COMP-SRC] | RULE/COMP | NT | UNKNOWN |
| T05 | [FORMAT], [HIST], [COMP-SRC] | COMP | NT | UNVERIFIED |
| T06 | [FORMAT], [HIST], [COMP-SRC] | COMP | NT | UNVERIFIED |
| T07 | [FORMAT], [HIST], [COMP-SRC] | RULE/COMP | NT | UNVERIFIED |
| T08 | [FORMAT], [HIST], [COMP-SRC] | COMP | NT | UNVERIFIED |
| T09 | [FORMAT], [HIST], [COMP-SRC] | COMP/SLIDER | NT | UNVERIFIED |
| T10 | [FORMAT], [HIST], [COMP-SRC] | COMP/GEN/COPY | NT | UNVERIFIED |

### Sliders and spinners

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| L01 | Distinct head/repeat/tail sounds and banks; one and multiple spans | I→P→E→S→C | Correct node count, sound, bank and time at every node | Curve, length, repeat count and object geometry | RC1/RC7/RC8 | CORE |
| L02 | Slider-level index/volume override versus timing-point settings | I→P→S | Reverify which fields stable ignores for nodes and body | Retain raw ignored fields unless intentionally changed | RC1 | CHANGE:slider |
| L03 | Green line inside slider changes tick index and volume | I→P→E→S→V | Tick transitions match stable at their own evaluated times | Slider nodes and surrounding objects | RC1/RC8 | CORE |
| L04 | Green line inside slider changes slide/whistle loop bank, index and volume | I→P→E→S→V | Verify loop transition timing, continuation and phase; do not generalize tick observations | Other passive sounds and slider geometry | RC8 | CHANGE:slider,audio |
| L05 | Node edit changes passive slider sounds as a side effect | E→S→V | Detect and identify changed ticks/loops; policy for allowing such changes remains open | Unselected passive sounds are reviewed, not assumed preserved | RC1/RC8 | CORE |
| L06 | Short/zero-length sliders, reverses near red line, tick-rate extremes | I→P→S→C | Supported edge cases do not add/drop ticks or shift nodes; unsupported input is identified | Object shape, duration and existing content | RC7/RC8 | CHANGE:slider |
| L07 | Spinner end hitnormal/additions and per-object index/volume | I→P→E→S→C | Play intended end sounds at end time with correct settings | Spinner start/end and unrelated events | RC1/RC8 | CHANGE:spinner |
| L08 | Spinner spin/bonus source naming, bank and custom index | I→P→S→V | Establish supported stable resolution and preview limitations; track as unknown until checked | Preserve uneditable spinner-loop data | RC8 | CHANGE:spinner,audio |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| L01 | [FORMAT], [HIST], [COMP-SRC] | DEC/COMP/SLIDER | NT | UNVERIFIED |
| L02 | [FORMAT], [HIST], [COMP-SRC] | DEC | H-SLIDER | UNVERIFIED |
| L03 | [FORMAT], [HIST], [COMP-SRC] | DEC/COMP | H-TICK | UNVERIFIED |
| L04 | [FORMAT], [HIST], [COMP-SRC] | DEC/AUDIO | NT | UNKNOWN |
| L05 | [FORMAT], [HIST], [COMP-SRC] | COMP | NT | POLICY-OPEN |
| L06 | [FORMAT], [HIST], [COMP-SRC] | SLIDER/DEC | NT | UNVERIFIED |
| L07 | [FORMAT], [HIST], [COMP-SRC] | RULE/DEC/COMP | NT | UNVERIFIED |
| L08 | [FORMAT], [HIST], [COMP-SRC] | DEC/AUDIO | NT | UNKNOWN |

### Editing and safe saving

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| E01 | One valid edit plus one anchored compiler conflict | E→S | Normal Save blocks before writing and keeps both unsaved edits | Original bytes and undo history | PRES | CORE |
| E02 | One valid edit plus one unanchored drawn sound | E→S | Normal Save blocks if it cannot include every intended edit; offer navigation to unsupported placement | Original bytes and every unsaved sound | PRES/RC1 | CORE |
| E03 | Unsupported filename-override edit, missing lane or invalid volume | E→S | Actionable reason and location; no destructive normalization or partial success | Original map and unresolved edits | PRES | CHANGE:compiler,save |
| E04 | External editor changes map between open and save | I→E→S | Refuse stale overwrite; keep edits available for recovery | External edit and local unsaved state | PRES | CORE |
| E05 | Single valid sound edit with bookmarks, metadata, storyboard and untouched objects | E→S→I | Patch only intended fields and explicitly edited bookmarks | All unrelated bytes; object count and geometry | PRES/RC5 | CORE |
| E06 | Backup failure, permission failure or interrupted file replacement | E→S | Report actual outcome; preserve original or restore it; never falsely report success | Original and recoverable backup; no stranded partial output | PRES | CHANGE:filesystem |
| E07 | Undo/redo after draw, erase, move, volume change and failed save | E→S→E | Restore intended sounds and shared volumes; blocked save does not reset history | Unsaved edits and original file | PRES | CHANGE:edit,save |
| E08 | Temporary hit/lane mute, solo, song offset/volume and playback speed | E→P→S→I | Listening-only settings do not erase or retime saved hitsounds | All intended exported sounds, times and map audio settings | RC1 | CHANGE:edit,audio |
| E09 | Existing rankability issue in imported map; unrelated sound edit | I→E→S | Separate pre-existing issue from a new regression; do not silently repair content | Unrelated user-authored content | RC1–RC8 | CHANGE:validation |
| E10 | Draw/erase/move/range-copy/repeat/quantize across snap and BPM changes | E→P→S→G | Every intended edit reaches correct moment or remains visibly unanchored; no duplicated/omitted hits | Gameplay objects and unrelated selection | RC1/RC7 | CHANGE:tools,snap |
| E11 | Import sample as new lane; delete/hide/reorder lane; index already used by sibling map | E→P→S→G→C | File import allocates safely; presentation changes do not silently alter sound assignments | Sample bytes, sibling files and saved hits | RC1/RC4 | CHANGE:lanes,files |
| E12 | Close/reopen project, lane layout and preferences after unsaved or blocked edits | E→I→P | State persistence is explicit; sound assignments remain correct; recovery guarantees need defining | Saved map; identify any editor-only state loss | PRES | CHANGE:project,save |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| E01 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | COMP/SAVE/APP | NT | GAP:source |
| E02 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | DOC/SAVE/APP | NT | GAP:source |
| E03 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | COMP/SAVE | NT | GAP:source |
| E04 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | SAVE | NT | UNVERIFIED |
| E05 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | FMT/COMP/SAVE | NT | UNVERIFIED |
| E06 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | SAVE/SAFE | NT | UNVERIFIED |
| E07 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | EDIT/APP | NT | UNVERIFIED |
| E08 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | EDIT/AUDIO | NT | UNVERIFIED |
| E09 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | GAP | NT | POLICY-OPEN |
| E10 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | EDIT/APP | NT | UNVERIFIED |
| E11 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | FOLDER/EDIT/APP | NT | UNVERIFIED |
| E12 | Product contract; [SAVE-SRC], [DOC-SRC], [APP-SAVE] | DOC/EDIT/APP | NT | POLICY-OPEN |

### Generation and shared files

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| G01 | Timing-only difficulty → draw sounds → Generate → copy to playable targets | E→G→C→V | Every intended sound survives full workflow at intended time/volume | Source map, targets' gameplay and shared assets | RC1–RC7 | CORE |
| G02 | Incompatible simultaneous lanes require mixed sample | E→G→C→V | Every layer present; output volume/gain and onset correct; inspect distortion | Original samples; unrelated shared assets | RC1/RC2/RC6 | CORE |
| G03 | One layer missing/undecodable in a multi-layer mix; contrast all layers missing | E→G→C | Surface incomplete render and prevent misleading complete-success result; decide blocking policy explicitly | No silently incomplete mix; original files unchanged on abort | RC1/RC2 | CORE |
| G04 | Generate plan has unresolved conflicts | E→G | Display every unresolved edit; default blocking beyond normal Save is a proposed policy, not settled | Source map and intended session data | PRES/RC1 | CHANGE:generate |
| G05 | Regenerate with reused mixes; edited mix file; index collision with sibling difficulty or .osb | G→G→C | Reuse only matching owned content; allocate safely; never overwrite another difficulty's sample | All shared file references and content hashes | RC4 | CHANGE:generate,files |
| G06 | Failed render/write/cleanup; stale manifest; orphaned generated samples | G→C | Rollback failures accurately; audit actual final files and retained references | Original diff and shared assets; report leftovers | RC4 | CHANGE:generate,filesystem |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| G01 | Product contract; [APP-MAP], [GENWRITE-SRC] | GEN/GENWRITE/COPY | NT | UNVERIFIED |
| G02 | Product contract; [APP-MAP], [GENWRITE-SRC] | GEN/MIX/AUDIO | NT | UNVERIFIED |
| G03 | Product contract; [APP-MAP], [GENWRITE-SRC] | APP/GENWRITE | NT | RISK:source |
| G04 | Product contract; [APP-MAP], [GENWRITE-SRC] | GEN/APP | NT | POLICY-OPEN |
| G05 | Product contract; [APP-MAP], [GENWRITE-SRC] | GEN/GENWRITE/MIX | NT | UNVERIFIED |
| G06 | Product contract; [APP-MAP], [GENWRITE-SRC] | GENWRITE | NT | UNVERIFIED |

### Copying across difficulties

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| C01 | Source hits match target circles and slider nodes at exact times | G→C→V | Correct sound/bank/index/volume at each matched target | Target geometry, timing rhythm and nonselected difficulties | RC1/RC5/RC7 | CORE |
| C02 | Temporal leniency: just inside/outside threshold; multiple candidate matches | C→V | Deterministic matching; no missing/doubled/wrong-neighbour sound | Unmatched target sounds according to chosen overwrite mode | RC1 | CHANGE:copy |
| C03 | Overwrite everything versus only defined; copy sound/bank/index/volume toggles | C→V | Each selected option has a separate expected outcome; no implied all-options pass | Fields excluded by options | RC1 | CHANGE:copy |
| C04 | Unmatched source hit copied to target tick/slide with custom mix | G→C→V | Correct target bank/index, volume and layer mix at intended target event | Slider body/tick transitions elsewhere | RC1/RC2/RC8 | CHANGE:copy,slider |
| C05 | Keep 5% volume and mute slider ends with divisor/minimum-length filters | C→V | Respect filters and volume policy; low value alone is not proof of silence | Nonmatching ends and active click feedback | RC1/RC8 | CHANGE:copy,volume |
| C06 | Storyboard copy and skip-at-hit settings; .osu versus shared .osb | C→V | No unintended double play; active sound still provides required feedback | Original shared storyboard and untargeted difficulties | RC1/RC3 | CHANGE:copy,storyboard |
| C07 | Open source has unresolved edits; one target fails or conflicts while others succeed | E→C | Expose incomplete source and each target outcome; batch blocking/rollback policy is open | Source edits, failed targets and unrelated mapset files | PRES/RC1 | CORE |
| C08 | Source and target use different green lines, SV and volumes; target changed externally | C→V | Respect chosen copy options; preserve target timing geometry; refuse stale destructive overwrite | Target BPM/SV/kiai, slider timings and external edits | RC1/RC5 | CHANGE:copy,timing |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| C01 | Product contract; [APP-MAP]; COPY tests | COPY | NT | UNVERIFIED |
| C02 | Product contract; [APP-MAP]; COPY tests | COPY | NT | UNVERIFIED |
| C03 | Product contract; [APP-MAP]; COPY tests | COPY | NT | UNVERIFIED |
| C04 | Product contract; [APP-MAP]; COPY tests | COPY/MIX | NT | UNVERIFIED |
| C05 | Product contract; [APP-MAP]; COPY tests | COPY | NT | UNVERIFIED |
| C06 | Product contract; [APP-MAP]; COPY tests | COPY/SB | NT | UNVERIFIED |
| C07 | Product contract; [APP-MAP]; COPY tests | APP/COPY/SAVE | NT | POLICY-OPEN |
| C08 | Product contract; [APP-MAP]; COPY tests | COPY/SAVE | NT | UNVERIFIED |

### Preview and audio output

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| P01 | Song plus identifiable simultaneous hits and long slider loops | I→P→S→V | Baseline preview matches intended event times and source selection; compare saved result in stable | No preview-only edits leak into map | RC1/RC8 | CORE |
| P02 | Seek into loop, stop/restart and cycle across marker boundaries | P | No stuck loop, doubled trigger or omitted first hit after seek/cycle | Saved data and edit history | RC1 | CHANGE:audio |
| P03 | 25/100/150% playback, song offset, mixed sample rates and channel layouts | P→S→V | Preview transformations consistent; saved timing remains original unless explicitly edited | Map timing, sample files and object volumes | RC1/RC2 | CHANGE:audio |
| P04 | WASAPI shared/exclusive, ASIO and fallback; device change under load | P | Supported outputs keep song/hits together; fallback and failures visible | No saved-data changes | RC1 | CHANGE:outputs |
| P05 | Dense map, polyphony, clipping and overlapping sample tails | P→G→C→V | No voice loss; no unintended distortion; layering and tails remain audible in context | Original sample content and intended gain | RC1/RC6 | CHANGE:audio,mixing |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| P01 | Product contract; AUDIO tests; [HIST] | AUDIO | NT | UNVERIFIED |
| P02 | Product contract; AUDIO tests; [HIST] | AUDIO | NT | UNVERIFIED |
| P03 | Product contract; AUDIO tests; [HIST] | AUDIO | NT | UNVERIFIED |
| P04 | Product contract; AUDIO tests; [HIST] | AUDIO | NT | UNVERIFIED |
| P05 | Product contract; AUDIO tests; [HIST] | AUDIO/MIX | NT | UNVERIFIED |

### Ranking and final mapset checks

| ID | Scenario and interacting variables | Stages | Expected sound or command result | Preservation check | Ranking / contract | Release |
|---|---|---|---|---|---|---|
| R01 | Active clicks across final playable difficulties with full song audible | C→V | Each active click has distinct feedback; every intended sound survives; assess masking by ear | Check all target difficulties, not helper alone | RC1 | CORE |
| R02 | Clicked event uses MP3, late-attack or slow-peak sample | E→G→C→V | At least one feedback sample per click meets impact/onset and WAV/OGG requirements; assess others in context | Do not incorrectly require every decorative layer to satisfy click-feedback rule | RC2 | CORE |
| R03 | Hitsound lengths below/exactly/above 25 ms; canonical silence asset | G→C→V | Detect too-short normal samples; recognize official blank.wav separately and apply silence requirement | No arbitrary short or zero-byte silence substitution | RC2/RC4 | CORE |
| R04 | Unused/zero-byte assets and helper difficulty in final submission folder | G→C→V | Audit final upload folder; remove or relocate helper artifacts only after confirming no references | Shared files still used by any difficulty/storyboard | RC4 | CORE |
| R05 | Inserted timing points and final target object timing | S→G→C→V | No same-type duplicate points or green-before-red; preserve BPM/offset; verify applicable snapping | Legal red+green pairs and intended exceptions | RC5/RC7 | CORE |
| R06 | Slider ends/reverses/spinner ends, natural loops and hard+ tick loudness | S→C→V | Assess guideline compliance and documented exceptions separately from mandatory rules | Intended held-sound cases and musical choices | RC8 | CHANGE:slider,spinner,volume |
| R07 | Human-authored placement versus generated creative choices | E→G→C | Document that deterministic encoding/mixing of user edits differs from generative authorship; recheck policy if adding inference | User-authored event timing and sound choices | RC9 | CHANGE:feature-policy |
| R08 | Representative real mapsets covering all stages and previously reported bugs | I→E→P→S→G→C→V | Run both principal workflows on available representative maps; record all outcomes and regressions | Before/after hashes for unrelated files and target gameplay | RC1–RC8 | CHANGE:full-regression |

| ID | Expected-result basis | Related automated evidence (not run) | Stable evidence | Coverage status |
|---|---|---|---|---|
| R01 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R02 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R03 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R04 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R05 | [RC-G], [RC-O]; product preservation contract | COMP/COPY | NT | UNVERIFIED |
| R06 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R07 | [RC-G], [RC-O]; product preservation contract | GAP | NT | UNVERIFIED |
| R08 | [RC-G], [RC-O]; product preservation contract | REAL | NT | UNVERIFIED |

## Interaction coverage ledger

Single-variable tests are necessary but insufficient. Explicitly seed these high-risk combinations when creating executable instances. They are not covered merely because each variable has an individual test.

| Interaction | Families | Why it needs a combined case | Current status |
|---|---|---|---|
| Auto timing bank × auto object/addition bank × custom index × missing sample | S02–S05 | Two inheritance layers plus fallback can select a different source | Needs independent expected result and fixture |
| Slider repeat volume/index edit × nearby circle × 5 ms lookup boundary | T04–T06, L01 | A seemingly local green line may alter an earlier event | Related compiler tests; combined stable check not established |
| Slider-node edit × ticks/loops inside the slider × restoration at next node | T06, L03–L05 | Active-node preservation alone misses passive sound changes | Historical tick observation; policy and full check open |
| Filename override × addition flags × missing override × skin settings | S07–S09 | Existing override semantics differ from ordinary bank resolution | Present-file case historically reported; other combinations unknown |
| Mix generation × one undecodable layer × copied target volume options | G02/G03, C03/C04 | Render can succeed while an intended layer is absent | Source-inspected risk; reproduction needed |
| Copy-to-tick × target slider bank × fractional tick time × nearby green point | C04/C08, T04 | Copy matching, sample lookup and target timing all interact | Related copier tests; stable not verified |
| Reused generated mix × shared index in sibling difficulty × changed file contents | G05/G06, C08 | Safe cleanup/reuse depends on ownership and live references | Related generation tests; full workflow not verified |
| Unanchored edit × valid anchored edit × normal Save × reload/undo | E01/E02/E07 | A partial write followed by reload can lose the intended session state | Source-inspected gap |
| Open-source unsaved conflict × Copy × one successful and one failed target | C07, E01 | Save's future gate alone would not protect Copy | Policy open; source path identified |
| Low-volume mixed layers × stable minimum volume × song masking × skin fallback | T02, G02, S09, R01 | Correct numeric fields do not prove audibility or gain equivalence | Independent listening needed |

After these seeds, enumerate remaining values and apply pairwise selection across compatible dimensions within each family. Retain the explicit higher-order cases above. Pairwise selection is a proposed expansion method, not a claim that all pairs have been generated or covered. Record invalid combinations with a reason; do not let them disappear from a coverage percentage.

## Existing automated evidence

All entries below were inspected in source, not executed. The keys in scenario rows are **related evidence**, often partial. A test method's name may assert a stable assumption without independently proving it.

| Key | File | Relevant families | Representative exact methods | Limitation |
|---|---|---|---|---|
| FMT | [Format.Tests/OsuFileTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Format.Tests/OsuFileTests.cs) | I01,I02,E05 | `Unedited_file_writes_back_identical_bytes`; `Editing_one_field_changes_only_that_field`; `Editing_a_line_that_is_not_utf8_is_refused` | Byte preservation and field patching; not sound correctness or full old-version support |
| RULE | [Format.Tests/HitsoundRulesTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Format.Tests/HitsoundRulesTests.cs) | S01,S04,T04,T07 | `Objects_read_the_timing_point_5ms_ahead`; `Among_points_at_the_same_time_the_later_line_wins`; `Beatmap_sample_names_follow_the_index_rules` | Internal lookup assumptions; stable boundary still needs independent check |
| DEC | [Lanes.Tests/DecompilerTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/DecompilerTests.cs) | I03,I04,I06,I09,S01–S09,T01,T02,L01–L04,L06–L08 | `Object_inherits_set_index_and_volume_from_the_timing_point`; `Timing_point_sample_set_0_plays_soft_whatever_the_maps_default`; `Timing_point_without_a_sample_set_field_uses_the_maps_default_set`; `Custom_filename_plays_only_that_file`; `Slider_ignores_its_own_index_and_volume`; `A_green_line_inside_a_slider_changes_the_ticks_after_it`; `Slider_body_starts_with_the_6ms_lookup_and_previews_later_changes`; `Non_standard_modes_and_broken_lines_are_warnings_not_errors` | Many expectations encoded; shared implementation cannot independently establish stable behaviour |
| COMP | [Lanes.Tests/CompilerTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/CompilerTests.cs) | S07,S10,T03–T10,L01,L03,L05,E01,E03,E05,R05 | `Nothing_wanted_changes_nothing`; `Impossible_moments_are_conflicts_and_keep_their_old_sounds`; `A_green_line_that_would_change_a_sound_less_than_5ms_earlier_is_a_conflict`; `Slider_nodes_get_a_green_line_and_the_next_node_gets_the_old_settings_back`; `A_green_line_keeps_the_slider_velocity_and_kiai_in_force`; `A_green_line_at_a_repeat_that_changes_the_ticks_after_it_is_reported`; `The_filename_field_is_never_written` | Conflict detection does not prove command-level blocking; passive changes are notes |
| SAVE | [Lanes.Tests/MapSaverTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/MapSaverTests.cs) | S10,E01–E06,C07,C08 | `Saves_the_osu_and_keeps_the_original_as_a_backup_outside_the_mapset`; `Nothing_wanted_writes_nothing`; `A_file_changed_on_disk_since_opening_is_not_overwritten`; `Bookmarks_are_saved_into_the_osu` | No test here for conflict-blocked writes or preservation of dirty editor state |
| DOC | [Lanes.Tests/MapDocumentTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/MapDocumentTests.cs) | I08,E02,E04,E12 | `An_edit_is_saved_and_the_reopened_map_starts_from_it`; `Save_refuses_a_map_changed_on_disk_since_it_was_opened`; `Compiling_works_on_a_copy_and_never_changes_the_map_as_opened`; `A_sample_added_to_the_folder_is_found_and_a_hit_on_it_saves_with_its_index` | Added sample covered; removed/renamed/changed-source and blocked-state recovery need explicit cases |
| EDIT | [Lanes.Tests/EditSessionTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/EditSessionTests.cs) | T03,E02,E07–E12 | `A_hit_near_a_moment_plays_on_it_and_one_elsewhere_has_no_object`; `Moving_off_every_object_leaves_a_hit_with_no_object_that_is_not_compiled`; `Edits_become_compiler_wishes_and_undo_redo_step_through_them`; `Moving_a_range_moves_only_its_hits_and_never_the_objects` | Unanchored hit exclusion is tested, not command-level rejection |
| GEN | [Lanes.Tests/GenerateTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/GenerateTests.cs) | T10,G01,G02,G04,G05 | `Every_sound_becomes_a_circle_in_a_hitsounds_diff_with_the_red_lines_only`; `Sounds_that_cannot_play_together_are_mixed_on_a_fresh_index_and_identical_mixes_share_a_file`; `Different_volumes_at_one_time_are_mixed`; `The_fresh_index_is_above_every_index_any_difficulty_or_file_uses`; `A_mix_file_changed_since_is_never_reused_or_removed`; `An_unneeded_mix_whose_index_another_difficulty_uses_is_kept` | No stable end-to-end run established; conflict policy at command level remains open |
| GENWRITE | [Lanes.Tests/GenerateWriterTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/GenerateWriterTests.cs) | G01,G03,G05,G06 | `Writes_the_mixes_and_the_diff_and_backs_up_a_replaced_diff`; `A_mix_that_cannot_be_rendered_stops_everything_before_any_file_is_written`; `An_existing_sample_file_is_never_overwritten`; `A_failure_puts_back_an_overwritten_mix_and_removes_nothing` | Null render tested; loss of one layer from otherwise renderable mix is a separate gap |
| MIX | [Lanes.Tests/MixPlannerTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/MixPlannerTests.cs) | G02,G05,C04,P05 | `Identical_layers_share_a_file_and_volumes_below_8_percent_play_at_8`; `The_file_being_replaced_does_not_hold_up_indexes_but_other_difficulties_do` | Volume floor is a model assumption; no whole-map rankability proof |
| COPY | [Lanes.Tests/HitsoundCopierTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/HitsoundCopierTests.cs) | T10,C01–C08,R05 | `A_hit_is_copied_only_within_the_leniency`; `Overwrite_everything_makes_the_target_play_what_the_source_plays`; `Overwrite_only_defined_leaves_target_hits_the_source_has_nothing_for`; `A_five_percent_target_volume_stays_only_when_asked`; `A_hit_near_a_slider_tick_goes_onto_the_tick_and_the_old_settings_come_back_after_lookup`; `Copied_ticks_resolve_the_generated_sample_at_integer_and_fractional_times`; `Copying_a_tick_keeps_the_sliders_bank_and_earlier_sounds`; `Slider_mix_timing_respects_volume_options`; `A_storyboard_sample_a_hitsound_already_plays_is_skipped`; `Copying_into_a_mapset_writes_each_target_with_a_backup` | Options and model-level playback covered in selected cases; not independent stable results or multi-target transaction guarantees |
| FOLDER | [Lanes.Tests/MapFolderTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/MapFolderTests.cs) | S06,E11 | `Only_paths_inside_the_folder_are_allowed`; `Lookups_are_case_insensitive_and_return_the_name_on_disk` | Internal case/path behaviour; not client fallback proof |
| OSZ | [Lanes.Tests/NewDiffAndOszTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/NewDiffAndOszTests.cs) | I05,I07 | `An_osz_unpacks_into_a_new_folder_named_after_it_and_a_clash_is_numbered`; `An_unsafe_entry_stops_the_import_and_nothing_is_left_behind`; `A_new_diff_never_takes_a_name_or_file_already_there` | Import checks do not establish full workflow preservation |
| REAL | [Lanes.Tests/RealMapTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/RealMapTests.cs) | R08 | `Every_corpus_map_decompiles`; `Catalyst_spot_check`; `Catalyst_generates_a_hitsound_diff_that_plays_every_sound`; `Catalyst_compiles_unchanged_and_takes_slider_head_volumes_through_green_lines` | Corpus needed; Catalyst cases can silently return when author-specific path absent |
| AUDIO | [App.Tests/PlaybackMixerTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.App.Tests/PlaybackMixerTests.cs) | S09,L04,P01–P05 | `A_hit_starts_on_its_exact_sample`; `Slider_samples_resume_after_seeking_but_stop_on_pause_and_respect_mutes`; `Slider_sample_changes_replace_the_loop_and_cycles_restart_it`; `The_playhead_is_what_is_heard_the_rendered_audio_less_the_latency`; `A_driver_that_pulls_faster_than_real_time_doesnt_run_the_playhead_ahead`; `Volumes_scale_the_hit_and_the_master_volume_scales_everything` | Buffer/mixer behaviour; hardware outputs, decoding errors and actual stable parity remain separate checks |
| AUDIO | [App.Tests/PlaybackModelTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.App.Tests/PlaybackModelTests.cs) | S09,L04,P01,P02 | `Copied_slide_samples_are_decompiled_and_scheduled_after_reopening`; `Scheduler_plays_ticks_and_looped_slides_at_stables_minimum_volume`; `Default_skin_sounds_load_from_the_lazer_install` | Default skin install test can silently return; scheduled minimum volume is not independent measurement |
| AUDIO | [App.Tests/MixerAndLaneTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.App.Tests/MixerAndLaneTests.cs) | G02,G03,P03,P05,R03 | `Mixing_sums_layers_at_their_gains_into_a_16_bit_wav`; `A_mix_is_never_shorter_than_25_ms`; `Mono_samples_become_stereo_at_44_1_khz` | 25 ms assertion covers generated mixes, not arbitrary imported source samples |
| APP | [App.Tests/HitEditorTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.App.Tests/HitEditorTests.cs) | E07,E10 | `A_fine_nudge_cannot_pull_a_hit_off_its_object`; `Quantize_moves_only_loose_hits_by_the_strength`; `A_quantized_hit_that_lands_on_a_circle_plays_on_it`; `Repeat_counts_its_gap_on_the_grid_across_a_tempo_change`; `Repeat_restarts_on_a_red_lines_grid_when_a_step_would_cross_it` | Tool model tests; not complete UI save/conflict lifecycle |
| APP | [Lanes.Tests/ProjectStoreTests.cs](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/ProjectStoreTests.cs) | E11,E12 | `A_project_saves_and_loads_by_map_path`; `Lane_keys_round_trip` | Project persistence only; crash/blocked-edit recovery not established |

Additional keys: **SB** points to the storyboard import/copy tests under DEC/COPY; **SLIDER** points to slider cases under DEC and timing cases under COMP; **SAFE** points to path/archive checks under FOLDER/OSZ and write checks under SAVE/GENWRITE. These are not claims of dedicated complete suites. **GAP** means no specific direct automated assertion was established in this inventory. **APP** also identifies inspected command source where relevant; the listed app-model tests do not exercise every command's UI lifecycle.

Known execution and evidence limitations:

- [CorpusTests](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Format.Tests/CorpusTests.cs), the corpus case in [CompilerTests](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/CompilerTests.cs), and the corpus case in [RealMapTests](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/tests/OsuHitsound.Lanes.Tests/RealMapTests.cs) require a locally generated corpus. Missing corpus must be recorded as blocked setup, not a pass.
- The real-map corpus contains .osu files without custom sample assets. It cannot prove real custom-audio resolution or audibility.
- Three Catalyst tests in RealMapTests return without assertions when an author-specific Windows file is absent. Record whether these actually executed and what fixture was used.
- `Default_skin_sounds_load_from_the_lazer_install` returns if local lazer resources are unavailable. It is not portable evidence of successful default-audio loading.
- Most compiler/copy expectations are checked with the product's own decompiler. Keep independent stable verification even if all those tests pass.
- Generated-mix minimum-length coverage does not validate arbitrary imported samples or an entire mapset against ranking criteria.
- This inspection did not establish a complete census of missing assertions. Treat unmapped cases as a test-design queue, not proof that a test does not exist anywhere.

## Historical stable evidence

These observations are reported in [HIST] from 2026-10-30. They were not reproduced during this inspection, and an exact stable build/fixture hash is not recorded in that note. Re-run before treating them as evidence for a current release.

| Key | Reported result | Scope limit |
|---|---|---|
| H-AUTO | Explicit sampleSet 0 on timing points matched Soft with Drum and Normal map defaults | Missing sampleSet field is a different case and remains unverified; do not generalize to every format/version |
| H-FILE | A present explicit filename played only that file, without normal/additions | Does not settle missing-file fallback, all object types or skins |
| H-SLIDER | Slider-level custom index and volume were ignored in tested node/body examples | Does not establish all slider optional-field behaviour |
| H-TICK | Green-line changes inside sliders changed later ticks' custom index and volume | Does not settle slide/whistle loop transition semantics or every boundary case |

Retain both the official documented expectation and observed exception when they disagree. Add a precise reproducer rather than rewriting the expected result to make the current implementation pass.

## Source-inspected gaps and open policies

| Item | Evidence | Consequence for this matrix |
|---|---|---|
| Normal Save does not stop on compiler conflicts before writing compatible edits | [SAVE-SRC] compiles then writes any edited copy; [DOC-SRC] forwards wishes without an unanchored-hit gate | E01/E02/S10 need implementation and tests to satisfy selected behaviour |
| Save reloads the map after a write | [APP-SAVE] Save calls Open after saved.Written | Test dirty state and undo preservation for a future blocked save; current partial-save path can reset session state |
| Passive slider changes are notes, not conflicts | [COMP-SRC] PassiveChanges produces CompileResult.Notes | L05 policy is open; conflict-free does not mean no unintended sound changes |
| Generate can proceed with a plan containing conflicts | [APP-MAP] includes plan.Conflicts in confirmation but still permits Generate | G04 needs a command-specific decision and checks |
| Copy from an open source compiles unsaved state and takes its File without checking compile conflicts | [APP-MAP] CopyHitsounds | C07 needs source-conflict handling; fixing Save alone is insufficient |
| Mix rendering filters unavailable layers and returns null only when all layers are unavailable | [APP-MAP] Generate and Copy rendering callbacks | G03 needs separate one-layer/all-layer failure cases; current null-render test covers only all-layer failure |
| Generate cleanup can leave unneeded files after a deletion failure | [GENWRITE-SRC] catches deletion failures | G06/R04 must examine actual final mapset contents; successful diff output is not proof of upload-folder hygiene |

Policy decisions still needed before the affected rows can pass:

1. Should Generate and Copy block by default on unresolved source/target conflicts? The agreed blocking decision currently covers normal Save.
2. Should a change to unselected slider ticks/loops block a save, require explicit approval, or be prevented through a different encoding?
3. Is Copy transactional across all selected targets, or can individually reported targets succeed? Define backup/recovery for the chosen behaviour.
4. Is a render missing one intended layer always a blocking error? Recommended for the stated every-intended-sound goal; not yet implemented or verified.
5. How should pre-existing ranking problems be reported separately from problems introduced by the editor?
6. What session recovery is promised across close/reopen after blocked or unsaved edits? In-session retention is agreed; crash recovery has not been specified.

## Proposed 15–30 minute release check

This is a **steady-state target after fixtures and expected answers exist**. Initial stable research, fixture creation and failure investigation are separate work. The estimates below are unmeasured; time the first complete run and adjust scope honestly.

Fixture names below are planned groups, not claims that new maps/files already exist. Existing material in [FIXTURES] is a starting point only.

| Group | Cases | Exact task and observations | Budget |
|---|---|---|---|
| F1 Save and preservation | I01, E01/E02/E04/E05, S10 | On disposable fixture copies, check no-edit byte equality; valid+conflicting and valid+unanchored edits must block without file changes; edits/undo remain; external change blocks | 4 min |
| F2 Source identity and inheritance | S01/S03/S04/S07, T01 | Use contrasting labelled samples. Compare known control group to auto/explicit banks, index variants, override and shared volumes; listen in stable with recorded baseline settings | 3 min |
| F3 Slider and boundary interactions | T04/T06, L01/L03/L05 | Compare head/repeat/tail, tick and loop results before/after local edits; check adjacent events near lookup boundary; inspect SV/kiai and unintended passive changes | 4 min |
| F4 Generate and layer integrity | G01/G02/G03 | Generate then reopen and copy. Identify every expected mix layer by ear. One-layer failure must have the explicitly decided outcome, not silent omission | 4 min |
| F5 Copy to playable targets | C01/C07 | Verify target nodes and volumes plus source conflicts/per-target outcomes; inspect a target with different slider structure, not only helper circles | 3 min |
| F6 Audibility, ranking and final-folder audit | P01, R01–R05 | Listen with song and active gameplay feedback; check impact/encoding/length, silence, timing-point structure, zero-byte/unused files, and helper artifacts | 5 min |
| Setup and result capture | All selected | Record hashes, versions, settings and outcomes; attach diff/listening evidence; restore disposable copies | 2 min |

Estimated total: **25 minutes**, provided the compact fixtures, automation and expected listening references are ready. A failed or ambiguous result ends a passing run and opens investigation; do not force it into the budget. Some CORE cases are automated structural checks, not listening tasks.

Run the full available automated suite on a suitable Windows/.NET environment and record actual run counts, conditional tests and fixture availability. Then add CHANGE rows for the touched areas and their interactions. If selected manual work cannot fit the budget, narrow the release change or schedule extra verification; do not declare selected untested cases safe.

For audibility use a real, recorded baseline: stable build, skin, beatmap hitsounds setting, layered-hitsounds setting, effect/music/master levels and output device. Default skin in dawsu and an arbitrary user's skin are not necessarily identical. First establish expected fallback behaviour, then evaluate it.

Auto/Autoplay is useful for repeatability but does not by itself prove that sounds are tied to player input rather than storyboard time. Include a played-versus-missed active click check when validating feedback origin or duplicate storyboard audio.

## Per-case result record

Copy this record for each executable case instance. Keep runs tied to a release commit and fixture bytes; never overwrite old observations with a new pass.

| Field | Value to record |
|---|---|
| Case ID and family | e.g. T04.a; exact event type, offsets and all other variable values |
| Intended action | Before state, edited field/sound and expected target |
| Fixture | Path/difficulty, input hash, shared-file manifest and expected-result source |
| Product | App version and commit; command and copier options |
| Client | Exact stable build, skin/settings/levels and output device |
| Automated result | Exact test name, run ID, pass/fail/blocked/skipped; note whether body actually executed |
| Sound gate | Expected versus observed source, time, volume and audibility; evidence |
| Preservation gate | Before/after file hashes and field diff; untouched active/passive sounds and other difficulties |
| Ranking gate | Applicable RC IDs; mandatory rule result; guideline/exception notes; source revision |
| Stable result | Pass/fail/blocked/unknown/not selected; date, observer and observed sounds |
| Release decision | Core/change-selected/not selected with reason; open issue and retest trigger |

## Expansion and completion rules

- Create an independently understood expected outcome before encoding a test for an UNKNOWN row.
- Split each family into concrete fixtures for its actual parameter values. Name the instance and inventory its assets.
- Link each bug to a reproducer and regression case; add the interaction that made it easy to miss.
- Track expected-behaviour confidence, automated execution and stable observations separately. Do not average the three acceptance gates into a score.
- Report coverage only against explicitly enumerated instance counts. This document alone cannot justify a percentage or "all variables covered".
- Review the variable inventory when adding a feature, changing format/lookup/compiler/mixer/copier code, or updating ranking sources.
- Keep the comprehensive inventory and per-release selections together. A deferred unknown remains visible with its scope and next trigger.
- Use a final playable mapset for acceptance; a generated helper difficulty passing its tests is not sufficient.

## Source index

- **RC-G**: [General ranking criteria](https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Ranking_criteria/en.md).
- **RC-O**: [osu!standard ranking criteria](https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Ranking_criteria/osu!/en.md).
- **FORMAT**: [Official .osu format](https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Client/File_formats/osu_(file_format)/en.md).
- **CUSTOM**: [Custom hitsound naming guide](https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Guides/Using_custom_hitsounds/en.md).
- **LAZER**: [Lazer parser comparator (not stable proof)](https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs).
- **HIST**: [Existing project research, including reported stable listening](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/docs/osu-hitsound-rules.md).
- **FIXTURES**: [Existing listening instructions](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/testmaps/README.md).
- **COMP-SRC**: [Compiler](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/Compiler.cs).
- **SAVE-SRC**: [Save writer](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/MapSaver.cs).
- **DOC-SRC**: [Document save/compile](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/MapDocument.cs).
- **APP-SAVE**: [Save command and reload](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.App/MainWindow.axaml.cs).
- **APP-MAP**: [Generate/copy commands and rendering](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.App/MainWindow.Map.cs).
- **GENWRITE-SRC**: [Generate writer](https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/GenerateWriter.cs).

[RC-G]: https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Ranking_criteria/en.md
[RC-O]: https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Ranking_criteria/osu!/en.md
[FORMAT]: https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Client/File_formats/osu_(file_format)/en.md
[CUSTOM]: https://github.com/ppy/osu-wiki/blob/fe4c485e83007b421361dc895da2df4e06631d85/wiki/Guides/Using_custom_hitsounds/en.md
[LAZER]: https://github.com/ppy/osu/blob/b77b028f9a0256ce23b94ef7eb916648641553e7/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs
[HIST]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/docs/osu-hitsound-rules.md
[FIXTURES]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/testmaps/README.md
[COMP-SRC]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/Compiler.cs
[SAVE-SRC]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/MapSaver.cs
[DOC-SRC]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/MapDocument.cs
[APP-SAVE]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.App/MainWindow.axaml.cs
[APP-MAP]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.App/MainWindow.Map.cs
[GENWRITE-SRC]: https://github.com/YogeGod/dawsu-hitsounding-editor/blob/9afbb33e32d45d29e5bdbb7a2c431f17cb5f9548/src/OsuHitsound.Lanes/GenerateWriter.cs
