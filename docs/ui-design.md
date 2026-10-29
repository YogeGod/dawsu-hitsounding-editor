# Main screen UI design

Chosen 2026-10-29. Design canvas: https://claude.ai/artifact/C3ChZkS3d9onU2eF5bHW41
(private until shared from its Share menu). The current design is the artboard
**"Chosen v2 · colourful lanes, grouped by set"** (bottom row, right). "Chosen v1"
next to it is the earlier, plainer version and is kept only for comparison. All mockups are drawn at 1920×1080, and every one
uses diamond notes.

Variations of v2 to pick from (row "More variations of v2"):
- **E · Regions and song sections.** A "Song sections" row (Verse, Chorus) sits
  above the ruler. Each lane shows a coloured region block per section, with a
  name strip, and the diamonds sit inside it. 4 bars are visible.
- **F · Solid lanes + inspector.** Lane rows are filled solid with the lane
  colour, with dark diamonds. A left inspector shows the selected lane (sample,
  preview button, which difficulties use it, a colour grid) and the selected
  moment (a volume slider, whether it can be saved).
- **G · Light chrome, compact rows.** Light grey top and bottom bars around the
  dark arrange area. 22 px rows, 14 lanes, a rainbow of lane colours from top to
  bottom, a sections row, and 4 bars visible.

**Direction:** a dark DAW-style arrange view. Lanes are colourful and grouped by
sample set, in the style of a modern DAW track list. It keeps the earlier
choices: Cubase drum-editor diamonds as notes, a whole-song overview strip, and
striped red columns for moments that can't be saved.

This is framework-neutral: the values below apply equally to WPF and Avalonia.

## Layout (top to bottom)

1. **File title** (≈24 px, centred, small): `Artist - Title (Mapper) [Diff].osu — Hitsounds`.
2. **Control bar** (≈44 px), in three parts:
   - Left: view toggles (lane details, volume lane, sample browser).
   - Centre: transport (go to start, stop, play, loop), then an **LCD display**,
     a dark rounded box with big mono numbers and tiny captions:
     `bar beat tick` · `time` · `BPM` · `4/4 /16`.
   - Right: **Save .osu**.
3. **Edit bar** (≈34 px): Edit / Functions / View menus, the tool group (Select,
   Draw, Erase; the active tool is blue), then on the right **Snap** and a
   horizontal zoom slider.
4. **Overview strip** (≈30 px): the whole song as small bars in the group
   colours. The visible range has a white outline. Spots that can't be saved
   use the red stripes. Clicking jumps there.
5. **Arrange area** (fills the rest, scrolls sideways):
   - **Header row** (34 px): an add-lane `+` button and a count of lanes and
     groups, over the timeline ruler.
   - **Number column** (28 px): each row's number on a strip of its colour.
   - **Lane list** (300 px). **Group rows** (24 px) have a collapse chevron, M/S
     buttons and a bold group name. The default groups are the sample sets:
     Soft set, Normal set, Drum set, Own files, Skin (no file). **Lane rows**
     (30 px) are indented, with a small colour icon, M, S, the sample file name
     (ellipsised) and `idx N`.
   - **Grid.** Each lane row is tinted with its lane colour at about 22% opacity,
     and a group row shows its name in the group colour across the grid.
     Bar lines are brightest, beat lines medium, and sub-beat lines are hidden.
   - **Bottom panel**, with tabs: **Volume** | **What gets saved**. The Volume tab
     shows one bar per moment that has hits, with height = volume 0–100. Its
     label says the value is shared by every lane that plays at that moment
     (osu! has one volume per object).
6. **Status bar** (≈26 px): on the left, the count and positions of moments that
   can't be saved, with the reason. On the right, lane and hit counts and the
   current selection.

## Colours

| Token | Hex | Use |
|---|---|---|
| bg | `#262628` | arrange area / window |
| chrome | `#3a3a3c` | title + control bar |
| chrome-2 | `#2f2f31` | edit bar, ruler, tabs, status bar |
| overview-bg | `#2b2b2d` | overview strip |
| panel-low | `#212123` | volume lane |
| divider | `#1c1c1e` | all separators |
| button | `#48484c` (lit `#55555a`) | default buttons |
| lcd | `#161618`, text `#e8eef6`, captions `#8e8e93` | transport display |
| lane-head | `#343437` / `#323235` alternating | lane rows in the list |
| group-head | `#39393c` | group rows in the list |
| text / dim | `#e4e4e6` / `#a4a4a9` | |
| line-beat / bar | `#3e3e42` / `#5a5a5f` | grid lines |
| playhead | `#ffffff`, 2 px | with a 4 px white mark on the ruler |
| play icon | `#6fdc7a` | |
| loop on | `#8a6d1e` bg, `#ffe08a` icon | |
| active tool / selection ring | `#2f6fd0` | |
| mute on | `#3d8fd1` | M button lit |
| solo on | `#ffcc33` | S button lit |
| conflict ring / bar | `#ff453a` | clashing note ring, volume bar |
| conflict stripes | `#3a1517` / `#6b1f24`, 135°, 5 px bands | whole column |
| conflict text | `#ff9a92` | status-bar message |

**Lane colour palette.** Each group gets a hue family, and lanes inside it step
through neighbouring shades, the way DAWs colour track stacks. Pure red is kept
out of the palette so it stays free for conflicts.

| Group | Group colour | Lane shades |
|---|---|---|
| Soft set | `#c04fd0` | `#a24fd6`, `#c24dc4`, `#d0499e`, `#d4507a` |
| Drum set | `#d98a2b` | `#d9772b`, `#c9a42a` |
| Normal set | `#3fb5b0` (teal) | step toward `#2fa38f`, `#4cc3d1` |
| Own files | `#4cb05e` | step toward `#7cbf3a`, `#3a9e7a` |
| Skin (no file) | `#3d8fd1` | `#3d8fd1` |
| Muted lane | `#5a6a7a` strip, `#6a6a70` notes | |

Users can recolour a lane from a colour grid (a row of hues by a few rows of
darker shades), as DAWs do.

Fonts: Inter for the UI, JetBrains Mono for times, indexes and numbers. Base size
13 px.

## Key components

- **Note:** a 12 px diamond (a square rotated 45°) in the lane's colour,
  brightened, with a 1 px dark ring (`#111113`). Clicking an empty cell in a lane
  places that lane's sample only. A selected note is white with a 2 px blue ring.
- **Moment that can't be saved ("conflict"):** the whole column across all lanes
  and the volume lane gets the red stripes. The clashing notes turn white with a
  2 px red ring, and the volume bar turns red. Stripes plus the ring, not just
  colour, so it still reads for colour-blind users and against colourful lanes.
  A typical cause is lanes that need different indexes (or volumes) at the same
  instant.
- **Group row:** collapses and expands its lanes. Its M and S buttons act on the
  whole group.
- **Mute / Solo:** 22×18 px rounded buttons, `M` and `S`. Mute lights blue and
  Solo lights yellow. A muted lane goes grey (strip, tint and notes).
- **Volume bar:** a 5 px stem with a lighter 5 px cap, bottom-aligned. Drag the cap
  to change the moment's volume.
- **Ruler labels:** the bar number (bold, white) on bar lines, `bar.beat` on beat
  lines.

## Not decided yet

- Exact keybinds (these follow Cubase's drum editor and are listed elsewhere).
- What the "What gets saved" tab shows. The likely content is the encoded
  `hitSound`/`hitSample` and any green lines for the selected moment.
- Whether lanes can be dragged between groups.
- Light theme: not planned for v1.
