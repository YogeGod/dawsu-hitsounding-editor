# Docs index

Which docs describe dawsu! as it is now, and which are dated records. Read this before opening anything else in `docs/`, then open only what your task needs.

Statuses:
- **Current:** kept true. If your change makes one wrong, update it in the same change.
- **Reference:** facts about something outside dawsu! (osu!, Cubase, the old Electron app), true as of the date in the file. Still useful, but not about dawsu!'s code.
- **Historical:** a plan, spec or report from the time it was written. Don't treat it as the current behaviour or as an instruction; check the code instead.

When docs disagree, trust them in this order: the code, then `CONTEXT.md` and `docs/adr/`, then the other **Current** docs, then everything else.

## Current

| Doc | What it's for |
|---|---|
| [`../CONTEXT.md`](../CONTEXT.md) | Domain words (Lane, Folder, Lane layout…). Use them in code, issues and docs. |
| [`adr/`](adr/) | Decisions that constrain the code. `0002` (unedited lines stay byte-identical) governs every `.osu` write. |
| [`design-system.md`](design-system.md) | UI tokens, the Slate scheme, and the 2026-11-13 UI refresh. The source for colours, sizes and radii. |
| [`TUTORIAL.md`](TUTORIAL.md) | The user-facing tour of the current release. |
| [`drum-auto-fill.md`](drum-auto-fill.md) | How **Edit → Drum hit detection…** works now (signal processing, no model). |
| [`../CHANGELOG.md`](../CHANGELOG.md) | What changed in each release, plus **Unreleased**. |
| `../TASKS.md` | Open work. Git-ignored and local to each checkout, so a fresh clone or worktree may not have it. |
| [`agents/`](agents/) | How agents use the issue tracker, triage labels and domain docs. |
| [`project-context.md`](project-context.md) | **First paragraph only:** the current release and where it's published. Everything below its first heading is the historical planning note from 2026-10-29 and 2026-10-30. |

## Reference

| Doc | About | As of |
|---|---|---|
| [`osu-hitsound-rules.md`](osu-hitsound-rules.md) | How osu!stable and lazer resolve hitsounds (sample set, index, additions, green lines), pinned to source commits. **Check it before any hitsound logic.** | 2026-10-29 |
| [`../keysounding-docs/`](../keysounding-docs/) | Cubase 15 Key Editor behaviour and osu! editor and lazer internals, for the Keysound editor. | 2026-11-06 |
| [`dawsu-electron-architecture.md`](dawsu-electron-architecture.md) | How the old Electron dawsu! (V6.39) was built. Prior art, not a spec. | 2026-10-30 |

## Historical

| Doc | What it was | Status now |
|---|---|---|
| [`spec-wave1.md`](spec-wave1.md), [`spec-wave2.md`](spec-wave2.md) | Specs for porting V6.39 features (Range tool, scale by volume, Quantize, Repeat, .osz import, New hitsound diff…). | Shipped. The code and CHANGELOG are the truth now. |
| [`merge-decisions.md`](merge-decisions.md) | How the V6.39 Electron app was to be ported into this editor (2026-10-30). | Waves 1 and 2 shipped. Wave 3 (MIDI) never got a spec; MIDI import arrived later in Keysound mode (2.0). |
| [`ui-design.md`](ui-design.md) | The 2026-10-30 UI direction. | Replaced by the UI refresh in `design-system.md`. |
| [`ui-port/differences.md`](ui-port/differences.md) | First C# port compared with V6.39, before V2.0. | Describes a screen that no longer exists. |
| [`electron-lane-grouping.md`](electron-lane-grouping.md), [`lane-drag-plan.md`](lane-drag-plan.md) | Lane grouping and lane drag decisions for 1.1 (2026-11-03/04). | Built; later UI work may have changed details. |
| [`ideas/`](ideas/) | Problem statements for features. `drum-maps` and parts of `keysounding` shipped; their top lines say so. | `export-merge-priorities`, `hitsound-difficulty-mode` and `trusted-exports` have no status line: treat them as unscheduled ideas, and check `CHANGELOG.md` before assuming any part is missing. |
| [`testing/hitsound-trust-matrix.md`](testing/hitsound-trust-matrix.md) | Evidence inventory and proposed release gate (2026-11-04). | Not a test report. Its byte-preservation row (PRES) is now ADR 0002. |
| [`validation/`](validation/) | VST3 host and drum VST validation reports (2026-11-05). | Records of what was checked then. |
| [`releases/`](releases/) | Notes for each release, one file per version. | Frozen; `CHANGELOG.md` has the same notes. |
| [`archive/`](archive/) | Agent reports and validation passes from October and November 2026. | See [`archive/README.md`](archive/README.md). |

## Adding a doc

Add a row here in the same change. A plan or spec starts as **Historical** the day it ships; say so in its first line too.
