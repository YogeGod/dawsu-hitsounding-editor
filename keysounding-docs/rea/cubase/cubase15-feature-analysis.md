# Cubase 15 (15.0.30.287): Hub, Key Commands, Key Editor, Drum Editor

This is a static analysis of the local install at `C:\Program Files\Steinberg\Cubase 15`. Nothing was executed or modified.

**Scope:** how these features are built and how they behave, as far as the shipped files show. I did not look at licensing or copy protection.

**Sources:**
- **Ghidra 12.1.4 through REA 6.8.0:** `Components\hubservice.dll`, fully analysed (9,217 functions).
- **GUI descriptions:** extracted from `Skins\skin.srf` (844 entries, including 55 XML layout files) and from the `SKIN.SRF` resource inside `hubservice.dll`. The extractor is in the scratchpad; the SRF format is documented below.
- **Strings and RTTI from `Cubase15.exe`:** 446k strings and 2,060 RTTI type names.
- **Key command catalogues:** your `%APPDATA%\Steinberg\Cubase 15_64\Key Commands.xml` and the four factory presets.

**Not done yet:** a Ghidra analysis of `Cubase15.exe` itself. It holds 92 MB of code and roughly 390k functions; see "Next step".

---

## 0. How Cubase builds its UI (applies to all four features)

- **The UI is declarative.** Every window, dialog, toolbar and inspector is an XML `<template>` in skin files, written in Steinberg's own GUI description language. It has `flow-layout`, `variant condition=…`, `exposure`, `for variable="[i]"` loops, `customsettings` (user-configurable toolbars) and `controller name=…` bindings.
- **Native code supplies the rest:**
  - **Parameters:** named values such as `searchText` or `drumSolo`, which the XML binds by `name=`.
  - **Custom views:** a view factory creates them when the XML says `<view name="X"/>`.
  - **Commands:** addressed as `"Category|Command"`, for example `command="MIDI|Pitch Visibility: On/Off"`. These are the same strings that appear in the Key Commands dialog.
- **Class-name prefixes in the executable:** `M…` is a model or controller (`MKeyEditController`, `MDrumEditController`, `MMidiEditController`). `P…` is a presentation or panel (`PKeyEditViewer`, `PDrumViewer`, `PDrumMap*`). `U…` and `G…` are the UI framework and globals (`GKeyCommand`, `UKeyCommandMemo`).
- **SRF archive format:**
  - Header: `"Steinberg Resource File\r\n"`, followed by zlib blobs.
  - Directory of `{u32 flags, u64 offset, u64 csize[, u64 usize], name\0}` entries:
    - Flag `0x09` is a folder; a name enters it and `..` leaves it.
    - Flag `0x0a` is a stored file.
    - Flag `0x0e` is a zlib-compressed file.
  - Trailer: a 32-character ID, `u32 dirSize`, `u32 count`, `u32 48`, then `"SRF3"`.

## 1. Hub landing page

**Where it lives:**
- `Components\hubservice.dll`: a VST-style component with an embedded `SKIN.SRF` → `skin.xml` (17.6 KB) and `TRANSLATION.XML`.
- `Cubase15.exe` hosts it through `Hub::Access` (`open`, `requestOpenHub`, `allWindowsClosed`, `requestQuit`) and the window template `SmtgHubWindow`.

**Layout** (`SmtgHubWindow`, 900×600 minimum):
- **Header:** the application bitmap, plus an audio quick-setup on the right:
  - audio driver menu (`baiosDriver`)
  - stereo output device
  - a Play Test Signal button with L/R meters
  - "Open Driver Control Panel"
  - or, for complex output setups, a shortcut to `Devices|VST Connections`
- **Left navigation:**
  - A radio pair: **Projects** or **Tutorials**.
  - Three external links: **Deals**, **User Manuals** and **MySteinberg**.
  - **Hub Settings**.
  - An optional "new maintenance update" notice.
  - An optional news tile (`HubNewsWebArticle`, 165×143), shown only while "Show Web Content" is on and web news has loaded.
- **Projects page:**
  - **Create Empty** button.
  - An optional default-location row: Use Default Location toggle, path, project folder name. In one layout variant this row sits on the page; otherwise it is in Settings.
  - **Search** field.
  - Then one collapsible list per category (`category[i]`, up to 100), each a table of icon, Name, Preview (a play button), and Date. Each list also has a sorting-options menu.
  - Footer: **Choose File…** and **Open Selected** (the label and tooltip switch to "Create New Project from Selected Template" when a template is selected).
- **Tutorials page:** built entirely from web data. It holds up to 100 `webSection[i]` blocks, each a headline and text, optionally with an expandable grid of 300×210 article cards. With no data it shows "No web content loaded".
- **Hub Settings dialog:**
  - Project Location.
  - A "Projects and User Locations" table (visible checkbox, Name, Folder Path) with add, remove, move up and move down.
  - Four switches: Display File Paths and Template Descriptions, Show Project Location on 'Projects' Page, Show Web Content, Open Hub When Closing Projects.

**What the code shows (Ghidra):**
- **Controller constructor `FUN_18012ebb0`.** This is `HubService::Controller(IGuiDescription5*, Hub::IProjectStorage*, Web::Gateway*, IHubServiceCallback*)`. It:
  - registers 31 UI parameters (tags 0–30, in `FUN_180131520`): `content`, `contentProjects`, `contentLearn`, `link--deals/manuals/account`, `search`, `settings`, update notice, `locationDefault/Path/Folder`, `categoryList`, `add/remove/moveCategoryUp/Down`, the four settings switches, `createEmpty`, `openOther`, `openSelected`, `isTemplateSelected`, two dynamic titles, and the change triggers `categoriesChanged` and `webContentChanged`.
  - loads project categories from `IProjectStorage` and creates one media-list model per category.
  - **enables "Add User Location" only while there are fewer than 10 locations** (`addCategory` enabled when count < 10).
  - disables Open Selected until something is selected.
  - **hard-codes the link targets:**
    - Deals → `helpcenter.steinberg.de/hc/de/?…utm_campaign=support…`. This is a support page, not a deals page, despite the "Show latest deals" tooltip.
    - User Manuals → `steinberg.help`.
    - MySteinberg → `steinberg.net/steinbergid/`.
    - All the UTM tags still say `cubase-pro-14`.
  - subscribes two callbacks to the `Web::Gateway`, which deliver tutorial sections and the news article. **No content URLs exist in the DLL**; the host's gateway, via `SteinbergRuntimeAccess::HubContentParameters`, supplies them.
- **View factory `FUN_180130500`.** It creates three custom views: `DropAndOpenFileView` (dropping a project file onto the Hub opens it), `ApplicationBitmap`, and `HubNewsWebArticle` (only if news has loaded). It also answers the XML conditions:
  - `hasCategory[i]` is true when i < category count.
  - `hasWebSection[i]` is true when i < web-section count.
  - `hasHubNews` follows the news-loaded flag.
  - `WINDOWS` is true for the platform check.
- **Persistent settings IDs:** `AutoReopenEnabledId`, `DisplayListInfoId`, `DisplayLocationMainWindowId`, `DisplayWebContentId`, `ExpandedWebSectionId`, `UpdateNotificationClosedId`, `UseDefaultLocationId`, `ProjectCategoriesId`, `ProjectCategoryId`.
- **Template and project model names:** `MediaTemplateCategory`, `MediaContentSummary`, `ProjectPreview`. A template's preview audio is what the Preview column plays.

## 2. Key Commands dialog

**Layout** (`KeyCommands` template in `skin.xml`; native class `KeyCommandDialog`, with a custom tree renderer `initRenderer`):
- **Search row:** a keyboard toggle (`searchKey`, "Press Key/Search Command") switches the search box between finding commands by name and capturing a key press to find what is bound to it. There are also Expand All and Collapse All buttons.
- **Filter:** All, Assigned, Customized or Unassigned. There is also a preset menu (factory presets: Ableton Live, Logic Pro, Pro Tools, Sonar).
- **Command tree** (`commandTree`) with Command and Key columns. Clicking the Key cell enters a new shortcut.
- **Buttons:** Defaults ("Reset to Default Assignments") and Clear All.
- **Alternative Key Sets** (shown when the `alternateKeyCommands` condition is true): add a set, choose a set, delete a set, and a "Store Selected Items Only" option. These are named partial key sets you can switch between.
- **Macros** (`macroCommands` condition): New, Duplicate and Delete Macro; Add or Remove Command; move a command up or down; Run Macro. A macro is an ordered list of commands that is itself bindable (the `Macro` category).
- **On-Screen Keyboard filtering:** while the on-screen keyboard is active it can consume key commands ("The On-Screen Keyboard filtered this Key Command.").

**Data model:**
- Your `Key Commands.xml` holds **69 categories and 2,695 commands, 239 of them with keys bound**.
- Format: `<list name="Categories"><item><string name="Name"/><list name="Commands"><item><string name="Name"/><string name="Key"/>…`. Only deviations from the defaults are meaningful; presets use the same structure under `<KeyCommandsPreset>`.
- **Largest categories:** Process Plug-in (859, one per plug-in), Edit (184), Transport (153), Preferences (141, so preferences can be toggled by key), Process Project Logical Editor (130), Mixer (123), Process Logical Preset (113), MIDI (77), Score Editor (71).
- **Hub category:** a single command, "Steinberg Hub".
- **Full catalogues** (category → command → keys) are in `cubase/keycommands-*.json`.

## 3. Key Editor (piano roll)

**Classes:** `MKeyEditController` (controller), `PKeyEditViewer`/`KEMainView` (the note grid, native), `KeyAndDrumViewerEventImage` (event drawing shared with the Drum Editor), and `MMidiEditController` (shared MIDI-editor base, including the controller-lane menu).

**Layout** (`KeyEditorInside`, the same template whether in a window or the Lower Zone):
- toolbar (40 px, or 32 px in the Lower Zone)
- status line
- info line
- in a window only: an editor visibility list (`KEVisibilityList`, 150–274 px) and an inspector (`KEInspector`, 180–280 px)
- the main grid
- In the Lower Zone, the visibility list and inspector are dropped and only the grid remains.

**Toolbar** (`CSKeyEditToolBar`, user-configurable; items marked *hidden* are off by default):
- Keep Editor Contents
- Solo/Record group (Solo Editor and its mode menu, Record in Editor)
- Retrospective Record
- **Pitch Visibility** (keyboard folding), when the `foldableKeyeditor` condition is true. It can show only pitches that have events or only pitches in the Scale Assistant scale. It reports why it is disabled: no note events, or no compatible instrument.
- Auto-Scroll
- Acoustic Feedback
- Tool Buttons
- *Auto Select Controllers*
- *Independent Track Loop*
- *Show Note Expression Data*
- *Indicate Transpositions*
- *Insert Velocity*
- *Nudge*
- *Transpose palette*
- Snap
- Quantize
- *Length Quantize*
- Multiple Part Controls
- *Step/MIDI Input*
- Event Colors
- Edit VST Instrument
- Window Zone controls (break out editor, visibility list, left zone, controller lanes, layout setup)

**Status line:** Mouse Time Position, Mouse Note Position, Current Chord Display, Independent Track Loop.

**Inspector** (`MidiEditorInspectorContent`):
- Quantize section: Apply Quantize, Auto Apply, Quantize Strength, Swing, Tuplet, Catch Range, Pre-Quantize, Rough, Soft, Quantize Ends and Lengths, Freeze MIDI Quantize, Reset.
- Transpose: Semitones, Scale Correction, Keep Notes in Range, Use Chord Track.
- Length: Fixed Lengths, Legato, Overlap, Extend to Next Selected, Freeze MIDI Lengths, Pedals to Note Length, Delete Overlaps (mono and poly).
- Chord Editing: triads and 4-note chords, Inversions, Drop 2, Drop 3, Drop 2+4, Move Up and Down, Create Chord Events, Add to Chord Track.
- **Scale Assistant:** Editor Scale, Use Chord Track, Scale Suggestions, Show Scale Note Guides, Snap Pitch Editing, Snap Live Input, Quantize Pitches.
- Note Expression, Expression Map, Global Tracks, VariAudio and Hitpoints sections.

**Per-note properties the editor can set** (undo names):
- Set Note Pitch
- Set Note Velocity
- Set Off Velocity
- **Set Play Probability**
- **Set Velocity Variance** (with Apply and Reset "MIDI Play Probability" and "MIDI Velocity Variance")
- Set Event Channel
- Set Articulation
- Set Release Length
- Set Curve Type

Probability and variance are per-note randomisation features.

**Controller lanes:**
- Create or remove a lane, Show/Hide Controller Lanes, Show Velocity Lane Only, Show Used Controllers.
- 16 bindable lane setups (`Controller Lane Setup 1–16`) with presets ("Add Preset…", "Organize Presets…").
- "Type of New Controller Events: Step/Ramp", Grid Type per lane, and the dynamics mapping dialog.
- Messages for when lanes don't fit: "Not enough space for velocity lane".

## 4. Drum Editor

**Classes:** `MDrumEditController` and `PDrumViewer` (`DrumMainView`). The drum-map system is `PDrumMap`, `PDrumMapEntry`, `PDrumMapDef` (the grid; `editCell` handles inline edits), `PDrumMapPool`, `PDrumMapDevice`, `PDrumMapTrackDevice`, `PDrumMapSetupDevice`, `PDrumMapPanelController`, `PDrumMapLengthSnap` and `PDefaultDrumMapDef`. Score integration is `PScoreInfoDrumMapDef`, and instrument integration is `DrumInstrumentConnector` (`getPitchNameMapping`).

**Layout:** the same zone structure as the Key Editor: toolbar, status line, info line, visibility list (`DrumVisibilityList`), inspector (`DrumInspector`) and main view. On the left of the main view is the drum-sound list, which is native code.

**Toolbar** (`CSDrumEditToolBar`): the same base as the Key Editor, with these differences:
- **Instrument Solo** (`drumSolo`, "Solo Instrument (Requires Drum Map)").
- **Pitch Visibility** (`visibilityAgentActive` with an options menu).
- **Insert Velocity is visible by default.**
- **Insert Length** ("Notes Length", the Drum Editor's insert-length control).
- **Drum Quantize** (`CIDrumEditTBQuantize`): the project quantize with a "use global quantize" option, so each drum sound can follow its own drum-map quantize.
- No Length Quantize, Note Expression or Transpose-indicator items.

**Drum map columns** (native grid): Pitch, Instrument, Quantize, Mute, I-Note (input note), O-Note (output note), Channel, Output, plus the score columns Display Note and Voice. Score mapping in `PScoreInfoDrumMapDef` adds Playback Technique, Noteheads, Instrument Type, Drum Sound, and stem direction or voice.

**Drum map options:**
- Use Snap from Drum Map: each sound's quantize value is used for snapping.
- Show Drum Sounds with Events.
- Show Drum Sounds in use by Instrument: this needs the instrument to report its pitch names through `DrumInstrumentConnector`.
- Reverse Drum Sound List.
- GM Default.
- Drum Editor Length Display: a key command switches between showing note lengths and showing triggers.

**Drum Map Setup dialog** (`DrumMapSetup`):
- Map list with New Map, New Copy, Remove, Save…, Load… and Load from MediaBay….
- An Output menu.
- The main `drumMapView`, at least 800×430.

**Related commands and preferences:** "Create Drum Map from Instrument", "No Drum Map", "Drum Map Setup…", "Drum-Map Link", "GM Map", "Empty Map", and the preference "Use Drum Editor when Drum Map is assigned". With that preference on, the default MIDI editor becomes the Drum Editor whenever a track has a drum map.

**Score drum kits:** `Components\ScoringEngine\drumKitNoteMaps.xml` defines 10 kit maps (General MIDI, Groove Agent SE Studio Kit, Keda Tabla, VDL lines, Yamaha XG) with 522 entries. Each entry maps a pitch to an instrument entity ID and playing technique, for example 37 → snare / `pt.sideStick`. This comes from the Dorico-derived ScoringEngine.

## 5. What remains unknown, and the next step

The **behaviour inside the native views** is only in `Cubase15.exe`, which I haven't analysed with Ghidra yet. That covers: how the grids hit-test, draw and edit notes; how quantize, legato and probability are calculated; how key-command lookup, conflicts and macros run; how drum-map I-Note/O-Note remapping is applied at playback.

That binary has about 390k functions. A Ghidra analysis needs:
- a 16 GB+ JVM heap (`GHIDRA_HEADLESS_MAXMEM`)
- a startup deadline of several hours (`REA_GHIDRA_STARTUP_TIMEOUT_MS`)
- a matching Claude Code per-server `timeout` in `~/.claude.json`
- possibly `REA_GHIDRA_DISABLED_ANALYZERS` (for example `WindowsResourceReference`) to cut the time
- a restart of the Code session so the new settings take effect

Once it finishes, the class-name strings found above (`MKeyEditController`, `MDrumEditController`, `PDrumMapDef::editCell`, `KeyCommandDialog`, `GKeyCommand`) are the entry points for the native code.
