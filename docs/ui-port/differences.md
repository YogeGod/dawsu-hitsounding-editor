# V6.39 screen vs the C# editor: differences

Screenshots: `v639.png` shows dawsu! V6.39 (Sample Packing build, 1600×1000, no map open), and `csharp-now.png` shows the C# editor at the same size with a test map open. The goal is that the C# editor **looks and is laid out like V6.39**, while its map engine (byte-exact export, conflicts, Generate) stays as it is.

## Top to bottom, region by region

| # | Region | V6.39 | C# editor now |
|---|---|---|---|
| 1 | App header | A pink "hs" logo and "dawsu!daw hitsounding editor". Text buttons: Open…, Save project, Export MIDI, Export beatmap, Edit. On the right: project name, Timing points, Preferences, Keys, Projects, Key Commands. | A small "HITSOUND EDITOR" title strip, then an "Open .osu…" button and a blue "Save .osu". A menu bar (File, Edit, Playback, Map, View) sits in the row below. |
| 2 | Toolbar | Undo/redo icons, then **icon** tool buttons (select, range, draw, erase, mute, ramp, audition). A blue **Snap** toggle, a Grid mode dropdown and a 1/4 divisor. On the right: Info, a Colour dropdown and Fit. | **Text** tool buttons (Select, Range, Draw…), Snap 1/4, a Grid/Grid relative box and a Zoom slider. The transport buttons and the LCD sit in the **top** bar. |
| 3 | Selection bar | One row of fields across the screen: SELECTION, START ms, LENGTH ms, VOLUME %, MIDI VELOCITY, VALUE EDIT (Relative), GAMEPLAY OBJECT, then "Assign to object…" and "Generate objects…". | None. The selection is shown in the inspector card and the status bar. |
| 4 | Inspector (left) | A collapsible "Inspector ‹" with cards: **Active sample** (name, file, MIDI note/channel, Choose sample…, ↑ Row / ↓ Row, Use for selected events), **Insertion defaults** (volume, length, show lengths, preview tones), **Editing** (Snap selected…, Repeat…, Select / transform…), **Song audio** (load, volume, offset), then Project, Bookmarks, Range and History. | Cards: Selection, Bookmarks, Recent edits (wave 2). |
| 5 | Lane list | A "SAMPLE LANES" header with a filter box and +, −, ‹ buttons. **Song audio** (waveform) and **Gameplay objects** are rows in the list. Each lane has a drag grip, a **sound icon** (hitnormal, whistle, finish, clap), +, ●, M and S. Group rows read e.g. "Soft sampleset 4 M S". Rows are tinted by sound type. | Lanes are numbered with a colour strip per set. They show file names and "idx N". Group rows read e.g. "Soft set (10)". The waveform and objects sit in the header above the lanes, with an overview strip on top. |
| 6 | Timeline | A ruler with bar.beat labels plus a seconds row, and **L / R flags** on the ruler. Red/blue/white grid lines are drawn at full strength. A vertical zoom slider sits on the right. | A ruler with bar.beat labels, fainter grid lines, and the cycle band in the ruler. |
| 7 | Bottom panel | "Controller [Volume ▾]", a ramp shape box ("Line"), the active lane's name and a hint line. The bars have a **100 / 50 / 1** scale. | "Volume" and "What gets saved" tabs, with value, Set all/Apply and Select min–max controls on the left. |
| 8 | Transport (bottom) | An options row: Use selection, ⇄, "Loop 4000 ms", "Start at cursor ▾", Return on stop, Follow, Save image…. Then a **transport bar at the bottom**: L/R locator boxes, \|◀ ■ ▶ ↻ Cycle, a Time LCD, Tempo 120, 4/4, Speed 100 %, a "Ready" status and a Global volume slider. | Transport and LCD at the **top**, and a status bar at the bottom. |
| 9 | Look | Neutral dark grey (#202023) with rounded, macOS-like controls, pink and blue accents, and stronger grid colours. | Blue-grey DAW style, flatter controls. |

## Things the C# editor has that V6.39 doesn't (where should they go?)

- **Save .osu**, Generate, and the red **conflict** marking (needed for byte-exact export).
- The **"What gets saved"** tab, the **overview strip**, lane **idx** numbers, the **Range / Recent edits / Bookmarks** cards and the time signature dialog.

## Proposed approach (for the user's OK)

1. Rebuild regions 1–9 to match V6.39, using `index.html` and V6.39's CSS (`layout-b.css`, `macos-b.css`, `polish-b.css`, `style.css`) for sizes, colours and order.
2. Keep every C# feature, putting each one where V6.39 has its nearest equivalent. For example, Save .osu replaces "Save project", Export beatmap becomes Generate, and Recent edits becomes V6.39's History card.
3. Change only the editor's screen, never its map engine. Check each region with a side-by-side screenshot before moving on.
