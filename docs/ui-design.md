# Main screen UI design

Final design chosen 2026-10-30. Design canvas: https://claude.ai/artifact/C3ChZkS3d9onU2eF5bHW41
(private until shared from its Share menu). The final design is the artboard
**"FINAL · solid lanes, grouped by set"** in the bottom row ("Final design").
Everything above it (A–D, v1, v2, E–G) is earlier exploration, kept for reference.

All mockups are drawn at 1920×1080. **Every design uses diamond notes**; that is
a fixed rule.

**Direction:** a dark DAW-style arrange view. Lanes are grouped by sample set,
and each lane row is filled **solid** with its lane colour, with dark diamonds on
it. The control bar is dark grey with an LCD-style display. Rows are 30 px.
Moments that can't be saved show as red stripes.

Not included, by choice: the whole-song overview strip, a song-sections row and
a side inspector. They can come back later as optional panels.

This is framework-neutral: the values below apply equally to WPF and Avalonia.

## Layout (top to bottom)

1. **File title** (≈24 px, centred, small): `Artist - Title (Mapper) [Diff].osu — Hitsounds`.
2. **Control bar** (≈44 px), in three parts:
   - Left: view toggles (volume lane, sample browser).
   - Centre: transport (go to start, stop, play, loop), then an **LCD display**,
     a dark rounded box with big mono numbers and tiny captions:
     `bar beat tick` · `time` · `BPM` · `4/4 /16`.
   - Right: **Save .osu**.
3. **Edit bar** (≈34 px): Edit / Functions / View menus, the tool group (Select,
   Draw, Erase; the active tool is blue), then on the right **Snap** and a
   horizontal zoom slider.
4. **Arrange area** (fills the rest, scrolls sideways):
   - **Header row** (34 px): an add-lane `+` button and a count of lanes and
     groups, over the timeline ruler.
   - **Number column** (28 px): each row's number on a strip of its colour.
   - **Lane list** (300 px). **Group rows** (24 px) have a collapse chevron, M/S
     buttons and a bold group name. The default groups are the sample sets:
     Soft set, Drum set, Normal set, Own files, Skin (no file). **Lane rows**
     (30 px, with a 2 px dark gap below) are indented, with M, S, the sample file
     name (ellipsised) and `idx N`. The selected lane's header is lighter
     (`#4a4a50`).
   - **Grid.** Each lane row is filled solid with its lane colour, and a group
     row shows its name in the group colour on a faint tint (~15%). Grid lines
     are dark and see-through over the colour: bar lines `rgba(0,0,0,.45)` and
     beat lines `rgba(0,0,0,.2)`; sub-beat lines are hidden.
   - **Bottom panel**, with tabs: **Volume** | **What gets saved**. The Volume tab
     (130 px) shows one bar per moment that has hits, with height = volume
     0–100. Its label says the value is shared by every lane that plays at that
     moment (osu! has one volume per object).
5. **Status bar** (≈26 px): on the left, the count and positions of moments that
   can't be saved, with the reason. On the right, lane and hit counts and the
   current selection.

## Colours

| Token | Hex | Use |
|---|---|---|
| bg | `#262628` | arrange area / window |
| chrome | `#3a3a3c` | title + control bar |
| chrome-2 | `#2f2f31` | edit bar, ruler, tabs, status bar |
| panel-low | `#212123` | volume lane |
| divider | `#1c1c1e` | all separators and gaps between lanes |
| button | `#48484c` (lit `#55555a`) | default buttons |
| lcd | `#161618`, text `#e8eef6`, captions `#8e8e93` | transport display |
| lane-head | `#343437` / `#323235` alternating, selected `#4a4a50` | lane rows in the list |
| group-head | `#39393c` | group rows in the list |
| text / dim | `#e4e4e6` / `#a4a4a9` | |
| playhead | `#ffffff`, 2 px | with a 4 px white mark on the ruler |
| play icon | `#6fdc7a` | |
| loop on | `#8a6d1e` bg, `#ffe08a` icon | |
| active tool / selection ring | `#2f6fd0` | |
| mute on | `#3d8fd1` | M button lit |
| solo on | `#ffcc33` | S button lit |
| note | `#141416` + 1 px `rgba(255,255,255,.35)` ring | diamond on a solid lane |
| conflict ring / bar | `#ff453a` | clashing note ring, volume bar |
| conflict stripes (on lanes) | `rgba(20,0,0,.55)` / `rgba(255,69,58,.75)`, 135°, 5 px bands | over solid lane colour |
| conflict stripes (volume lane) | `#3a1517` / `#6b1f24`, 135°, 5 px bands | |
| conflict text | `#ff9a92` | status-bar message |

**Lane colour palette.** Each group gets a hue family, and lanes inside it step
through neighbouring shades. Pure red is kept out of the palette so it stays free
for conflicts. All lane colours are mid-tones, so the dark diamonds stay visible
on them.

| Group | Group colour | Lane shades |
|---|---|---|
| Soft set | `#c04fd0` | `#9b4fd6`, `#c24dc4`, `#d0499e`, `#d4507a` |
| Drum set | `#d98a2b` | `#d9772b`, `#c9a42a` |
| Normal set | `#3fb5b0` | `#3fb5b0`, `#4cc3d1` |
| Own files | `#4cb05e` | `#4cb05e`, `#7cbf3a` |
| Skin (no file) | `#3d8fd1` | `#3d8fd1` |
| Muted lane | `#4a5560` (whole row) | |

Users can recolour a lane from a colour grid: a row of hues over a few rows of
darker shades. Mockup F on the canvas shows one.

Fonts: Inter for the UI, JetBrains Mono for times, indexes and numbers. Base size
13 px.

## Key components

- **Note:** a 12 px diamond (a square rotated 45°) in near-black `#141416`, with a
  faint light ring, on the solid lane colour. Clicking an empty cell in a lane
  places that lane's sample only. A selected note is white with a 2 px blue ring.
- **Moment that can't be saved ("conflict"):** the whole column across all lanes
  and the volume lane gets the red stripes. The clashing notes turn white with a
  2 px red ring, and the volume bar turns red. Stripes plus the ring, not just
  colour, so it still reads for colour-blind users and on top of coloured lanes.
  A typical cause is lanes that need different indexes (or volumes) at the same
  instant.
- **Group row:** collapses and expands its lanes. Its M and S buttons act on the
  whole group.
- **Mute / Solo:** 22×18 px rounded buttons, `M` and `S`. Mute lights blue and
  Solo lights yellow. A muted lane turns slate grey across the whole row.
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
