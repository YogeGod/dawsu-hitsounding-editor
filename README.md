![dawsu! hitsound editor](docs/banner.png)

# dawsu! hitsounding editor

A Windows editor for dawsu! hitsounds, with sample lanes, a song waveform and a volume controller.

## Tutorial

First launch offers a seven-step **Quick start**: open a map, choose a lane, assign a sample, place a hit, listen, and choose an output. You can use the editor while the guide is open, skip it, or reopen it from **File → Quick start**. **File → Full interface tour** keeps the complete control reference; **F1** lists shortcuts.

### Open, save and generate

![Open, Save .osu and Generate buttons](docs/tutorial/open-save-generate.png)

- **Open…** (Ctrl+O): open a `.osu` from your Songs folder, or drag a `.osu` or `.osz` onto the window. Each sample file in the mapset becomes a lane you can draw in.
- **Save .osu** (Ctrl+S): write your hitsounds back into the map. A backup of the old file is kept outside the mapset.
- **HS mode** beside Save: available for difficulties that started empty or whose difficulty name contains the word **HS** or **hitsounds**. Turning it on backfills loose sounds and creates a centred circle whenever a new time needs one. Layers at the same time share a circle; deleting its last sound deletes a circle created by this mode. Existing objects are protected. Off keeps the circles, and enabling resumes maintenance. Undo/Redo includes object changes; normal Save writes this difficulty using the current export settings. Mode, eligibility and ownership are kept in the editor project; externally changed maps lose old ownership to protect their objects.
- **File → Revert to last saved… (Ctrl+L)**: reload the saved `.osu` after confirmation, discarding unsaved hits and bookmarks and clearing undo history. Lane layout, playback settings, playhead and timeline view are kept. Change the shortcut in Preferences → Keys.
- **Generate…**: make a separate hitsound difficulty from your hits, ready to copy into the other difficulties with **Map → Copy hitsounds…** (see [Copy hitsounds](#copy-hitsounds)).

Opening a difficulty automatically creates a separate working mapset in the **osu! Songs folder configured in Preferences**, then edits that copy. Later opens reuse it without overwriting your work. Copied beatmap IDs are cleared; the source mapset is kept intact. The map title's tooltip shows the opened path.

The **Working copy** path below the editor shows the actual save destination. **Open working folder** opens it in Explorer. **Output guide** explains the output choices and offers backups and Quick start. Errors that need setup show a **Preferences…** action. Operation results remain below the editor until dismissed or replaced by a later message, including per-difficulty Copy results.

Closing with unsaved edits offers **Save**, **Discard** and **Cancel**. Save closes only after saving succeeds; cancelling an export confirmation or a failed save keeps the editor open. Discard restores the loaded project's loose hits and sound-set state, so automatic project saves do not bring discarded edits back on reopening. **Ctrl+S** and **F1** also work while a text field has focus; ordinary typing and text-editing shortcuts stay in that field.

**Next problem** selects and reveals the next conflict or hit without a gameplay object, clearing the lane search when necessary and wrapping to the first problem. **What gets saved** explains the selected hit's output; **Advanced: osu! fields** retains the raw object and timing-point fields. The top **Volume at times** control changes all sounds at the selected gameplay times, including sounds in other lanes.

In **Copy hitsounds**, choose the source, destinations and replacement mode first. The summary shows how many difficulties will be affected. **Advanced: timing, sliders and storyboard** contains the existing detailed options; their defaults and export behavior are unchanged. No destinations are selected initially. Use **Select all** for every other difficulty, or choose individual targets. The action button names the operation and target count; it stays disabled until a target is selected. Destination choices are retained when you change the source. After Generate, you can stay in the editor, open the generated difficulty, or open it and copy its sounds into other difficulties.

The **Audition** toggle beside the snap controls switches sample playback on/off when drawing or painting new notes. Its state is saved globally. Lane preview buttons and normal playback remain available.

**Grid…**, **View…**, **Song…** and **Playback…** keep secondary settings in the same place at every window width. Snap spacing and the editing tools remain on the toolbar; locators, playback behavior and speed are grouped under Playback. Inspector sections have fold indicators and dotted drag grips. New preferences start with only Active lane expanded; existing saved layouts are retained. Transport, Help and Settings use matching icons from the supplied skin, and hit diamonds are slightly larger.

The inspector's **Active lane** card identifies the lane whose sample you are editing. Choose an existing sample or **Browse samples…** to replace that lane's sample; its notes move to the replacement and can be undone. Imported audio gets a fresh sample index, keeping existing shared files intact. **File browser** switches between sample browsing/auditioning and map/difficulty browsing. Click a lane name before choosing its sample. Selecting hits is separate; **Use selected hit's lane** explicitly activates the lane of the selected hits when they all share one lane. Its folder controls let you browse external sample libraries too.

The **right file browser** shows parent folders with **+ / -** controls. Selecting a sample, double-clicking it or pressing Enter only previews it. **Add to selected lane** replaces the active sample for every hit; dragging a sample onto a lane does the same for that target lane. Read-only lanes cannot be replaced. **View > File browser** shows or hides the sidebar.

Choose your main **Hitsound folder** in Preferences. Open it from File or the inspector's folder buttons; the inspector also has a **Map folder** button. Sample pickers remember the last folder globally, falling back to the active mapset when it no longer exists. **Z** and **V** seek the first/last gameplay object or placed hit, including muted and folded lanes; a quick second press seeks the song start/end.

The snap selector includes **2/1, 3/1 and 4/1**: every 2, 3 or 4 osu! beats. These grids start at each red timing point; green lines and editor-only time-signature changes keep the same grid. Purple lines mark the active long-snap positions while beat/bar lines remain visible. Drawing, moving, quantize, duplicate and repeat use the selected spacing. Repeat crosses onto a new red line before following its new grid. Alt+wheel includes 4/1 and 2/1 in the straight cycle, or 3/1 with Triplets on. Shift+1–9 still chooses 1/n.

A sample lane's **+** immediately adds the next numbered layer, as in Electron V6.39. With a standard family selected, the header **+** and **Map → Add lane** do the same. The new lane starts with no hits and uses that family's bundled default sound, even when its parent uses a custom sample. Choose a different sound through **Active lane**. Existing files and indexes referenced by other difficulties are protected; the visible slot number can differ from its safe backing filename. Undo hides the added lane; its audio file is conservatively retained.

Click a base sound's disclosure arrow to fold or unfold its numbered sample lanes above index 1. Folded notes still play and export normally; the folded state is saved with the project. Searching the lane list reveals matching numbered lanes even while the family is folded.

Click a lane name to select that lane, **Ctrl-click** to toggle individual lanes, or **Shift-click** to select a range in the visible lane order. **Ctrl+Shift-click** adds a range. Selected lanes share a highlight and a selection count. **M** or **S** on any selected lane applies that button's next mute/solo state to all selected lanes, even when their previous states differ. Clicking another lane without modifiers replaces the group; Escape clears it. Deselecting leaves the current mute/solo states in place. This lane selection is separate from selected hits, and sample replacement still affects only the active lane.

### Drum maps

Click the keyboard button immediately left of the lane header's **+ / − / collapse** buttons to switch between Sample view and the flat **Drum-map view**. Right-click it for **Drum Map Setup**. Instrument rows show only their instrument names. Edit assignments in Setup or inspect the selected sample in Active lane; drag their grips to reorder them across sample families. Other samples stay below the named instruments. The regular Sample view keeps its own names, order and folders.

**Yoge Expanded Kit** and **Yoge Simple Kit** start unassigned. Click an unassigned instrument to choose its sample. Setup offers **New Map**, **New Copy**, **Rename**, **Remove**, **Edit rows**, **Save** and **Load**. These are editing presets shared by the mapset's difficulties. New Map creates a preset. Save writes a portable `.dawsudrum` with snapshots of assigned audio; Load asks whether to use those samples or match your own, then opens assignment review. Accepting bundled samples installs fresh files without overwriting existing samples. If an assigned numbered layer has no custom audio, it gets the bundled default for its family. Instruments with no assignment stay unassigned. Canceling review installs nothing. In **Edit rows**, choose a family and **Add numbered layer** to make another layer available to every instrument, then choose it in Assigned sample. Pending layers create no audio files until you save the setup.

Aliases assigned to the same sample mirror its hits and play once. Existing editing tools, snapping, copy/paste, volume and export continue to use the same note data. Changing an assignment defaults to leaving hits where they are; explicitly moving them previews destination overlaps, requires a handling choice and affects only the current difficulty. Undo restores notes and assignments together. The Active lane sample-replacement command also keeps drum-map aliases linked to the replacement. Undo retains imported sample files conservatively.

### Pairing and reusable sound sets

Save, Generate and Copy hitsounds share the same pairing rules. Distinct sound types stay separate through compatible numbered copies. Simultaneous finishes fuse into a new **hitfinish** sample; the kick/hitnormal stays separate. Source samples remain unchanged. The editor project retains the original editable layers.

Native osu! combinations take priority over saved merge overrides. When the existing hit object's banks, addition flags, sample index and shared volume can play the combination exactly, Save, Generate and Copy keep the separate samples without rendering merged audio or creating replacement files. If indexes or banks need alignment, compatible roles may be copied byte-for-byte. Fusion or volume baking remains available only where native fields cannot preserve the sounds and volumes; no extra gameplay objects are added to avoid a mix.

**File → Review output…** shows prepared filenames and lets you preview each output or the original combination. Choose the addition sampleset for one timestamp or matching combinations in this map. Matching includes input audio and relative balance. Existing sampleset priorities and explicit presets remain available. Keeping incompatible original files blocks export with an explanation. Changed choices are planned on the next review or save.

The inspector's **Sound sets** card saves reusable combinations of sounds and relative volumes. Select sounds at one timestamp and choose **Save selection…**, or use **New set…** to choose samples and levels. Choose the set, then Draw; select **Single sample** to return to ordinary lane drawing. **Apply** uses selected times or the playhead.

**Add** keeps identical sounds once and preserves an adopted sound's volume. **Replace** previews its changes and affects only the sound types supplied by the set, preserving unrelated sounds. Placements follow the map's saved set; editing the set updates its linked placements. The card offers placement volume, individual sound volume, **Select whole set**, **Reset to set** and **Make independent**. If two sets share a sound, the first placement controls that sound, including its volume. Whole-set copy/paste retains links and copies required audio when pasting into another map.

Sound sets retain frozen audio copies. **Save to library** writes a self-contained `.dawsuset` under `Sound sets` in your configured hitsound folder. **Import…** creates a map-local copy; **Update…** explicitly applies a newer library version while retaining placement overrides. Library changes do not alter other maps automatically. Audio trimming/effects remain outside this feature. Original source samples are retained, and snapshot files are conservatively kept after Undo.

**File → Export settings** offers **Green lines** (default) and **Per-object settings**. Unchanged objects retain their existing fields. Slider nodes use green lines; a circle inside an active slider can use object fields to protect the slider's sound. Review output explains these exceptions.

Unchanged prepared audio is reused. New sample indexes start at the smallest available number from 2, protecting occupied indexes and other difficulties. Changed inputs invalidate reuse. Canceling the export confirmation writes no map or output samples. Older generated files are conservatively retained in pairing mode; check for unused assets before submitting a mapset.

A single existing OGG, WAV or MP3 sample on a circle or spinner is referenced directly when possible, including after a volume edit. Save and Generate do not create numbered copies just to fit that sample into a sampleset. Layer combinations and slider-node sounds still use pairing or mixing when needed.

**Generate…** defaults to normal, whistle, finish and clap hitsounds at circles, slider heads/repeats/tails and spinner ends. Slider-body sounds and ticks are excluded by default to avoid creating unnecessary samples for a hitsound-copying difficulty. Enable **Include slider-body sounds and ticks** in the confirmation to render that audio into timed samples. Counts update before export; Generate is unavailable while planning or when the selected export has conflicts. Cancel writes nothing. Save and Copy retain their existing slider-audio behavior. Generate stops and names the sample if a required sound cannot be read or decoded.

You can also import an `.osz`, reopen a recent map, or open the map's folder (**File → Open map folder**, Ctrl+Shift+O) from the **File** menu. Sample files you put in that folder show up as lanes as soon as you switch back to the editor.

### Menus and settings

![File, Edit, Playback, Map and View menus](docs/tutorial/menus.png)

Every command is also in the menus: File, Edit, Playback, Map and View.

Smaller windows keep the main editing, snap, file and transport controls visible. **Grid…**, **View…**, **Song…** and **Playback…** hold the secondary controls when space is limited. The inspector starts collapsed in narrow windows, and the controller starts collapsed in short windows; reopen them with **Inspector** or **View… → Controller**. Resizing remembers your panel choices separately for compact and larger layouts during the current session.

![Map name, Preferences and Keys / F1](docs/tutorial/map-preferences-keys.png)

- **Preferences**: your Songs folder, the audio output (WASAPI shared or exclusive, ASIO, DirectSound or MME) and its device, and key commands. Hitsounds are mixed with the song sample-accurately, so they stay in time on every output; if an output can't start (an ASIO device that isn't connected, say), the editor uses WASAPI shared and tells you why.
- **Keys / F1**: a searchable list of every shortcut and mouse action.

### Tools

![Undo and redo](docs/tutorial/undo-redo.png) ![The six tools](docs/tutorial/tools.png)

Undo and redo with Ctrl+Z and Ctrl+Y. Pick a tool with its button or number key:

| Tool | Key | What it does |
|---|---|---|
| Select | 1 | Click a hit to select it, Ctrl/Shift+click to add more, drag a box around several, drag to move them. |
| Grab | 2 | Pan time and lane rows without changing hits. Range selection is available from View. |
| Draw | 3 | Click or drag along a lane to place hits. Hold Alt to draw with any tool. |
| Erase | 4 | Drag across hits to remove them. Right-click deletes a hit with any tool. |
| Mute | 5 | Silence a hit while you listen. Mutes are never saved into the map. |
| Ramp | 6 | Drag in the volume controller to draw a smooth volume ramp. |

### Snap, view and zoom

![Snap, snap mode and beat division](docs/tutorial/snap.png)

- **Snap** (J): when on, hits land on the beat grid.
- **Grid / Grid relative**: put moved hits on grid lines, or move them by whole steps and keep their offset.
- **1/4, 1/8…**: how fine the grid is (Shift+1–9).
- **Triplets**: filters Alt+wheel to 1/1, 1/3, 1/6 and 1/12. With it off, Alt+wheel cycles 1/1, 1/2, 1/4, 1/8 and 1/16. The drop-down and Shift+number shortcuts still offer all divisions.

![View toggles, colour and zoom](docs/tutorial/view-colour-zoom.png)

- **Inspector, Info, Controller**: show or hide those panels.
- **Colour**: colour hits by lane, or by volume (quiet hits dark, loud hits bright).
- **Zoom** in time (also Ctrl+wheel), Ctrl+Shift+wheel to make the rows taller or shorter, and **Fit** to see the whole map.

### Selection and song

![Selection bar](docs/tutorial/selection-bar.png)

Shows what's selected. Type a volume and press **Apply**: *Relative* adds it to each hit, *Set all* gives them all that value.

![Song volume, mute and offset](docs/tutorial/song.png)

The song's volume, mute and offset (in ms). These are for listening only and aren't saved into the map.

The Offset box is remembered for each map. **Preferences → Song offset for all maps (ms)** adds a global adjustment (default −17 ms). Both adjustments apply to song playback and its waveform.

### Inspector

![The inspector](docs/tutorial/inspector.png)

The left inspector keeps **Active lane** and **Bookmarks** handy. Optional bookmark labels stay in the editor project and appear in the list and on ruler-flag hover. **View > History** and **View > Sound sets** show optional panels. Click a card title to fold it; drag the grip to reorder cards.

### Lanes and the grid

Drag a dotted lane grip to reorder that lane inside its folder. Drag a folder grip to move it with all its lanes. Layouts are remembered per map; Ctrl+Z and Ctrl+Y undo and redo layout moves alongside note edits.

![Sample lanes and the grid](docs/tutorial/lanes-grid.png)

- **Sample lanes**: grouped by sampleset. Each group starts with osu!'s default sounds (hitnormal, whistle, finish, clap), with the map's own sample files under the sound they replace. The default sounds are built in, so they play even without osu! installed. Use the filter to find a lane. Above the list, **+** adds a lane for a sample file already in the mapset, **−** deletes the highlighted lane (the one you touched last, marked with a blue bar), and **‹** hides or shows the lane names.
- **Lane buttons**: **+** picks an audio file and copies it into the mapset as a new sample for that sound (e.g. `drum-hitclap5.wav`, using a number no difficulty uses yet), **●** plays the sample, **M** mutes the lane and **S** solos it. The **Song audio** row has **M** and **S** too: solo the song to hear it without hitsounds.
- **Song audio**: the song's waveform, coloured by frequency: blue for lows (kicks, bass), purple for mids (snares, vocals) and yellow for highs (hi-hats, cymbals), so you can see where each drum hits. The colours can be changed in the UI inspector (Ctrl+Shift+I).
- **The grid**: your hitsounds in time, left to right. Small markers above the lanes explain save/export problems on hover. **File → Warning markers** controls export problems (shown initially) and hits without objects (hidden initially) separately. Hiding markers does not bypass export validation.

![The song waveform, coloured by frequency](docs/tutorial/waveform.png)

### Volume

![The volume controller](docs/tutorial/volume.png)

One bar per circle or slider end. Drag a bar to change its volume, Alt-drag to paint, Shift-drag for a ramp. osu! has one volume per object, so all lanes on that object share it.

### Playback

![Transport and position](docs/tutorial/transport-position.png)

**L ◀** and **R ◀** jump to the L and R markers; type a time in ms in their boxes to move them. Then go to start, stop, play (Space) and **Cycle** between the L and R markers (L). Ctrl+click the ruler to set L, Alt+click to set R. Next to it: the time, bar.beat.tick, tempo and time signature.

![Playback speed](docs/tutorial/speed.png)

**Speed** plays slower or faster without changing the pitch, from 25 % to 150 %.

![Global volume](docs/tutorial/global-volume.png)

**Global volume** turns the song, hitsounds and previews up or down together.

### Copy hitsounds

![The Copy hitsounds dialog](docs/tutorial/copy-hitsounds.png)

**Map → Copy hitsounds…** copies hitsounds from one difficulty of the mapset (usually your hitsound diff) into the others, the way [Mapping Tools](https://github.com/OliBomby/Mapping_Tools)' Hitsound Copier does, with the same options:

- **Overwrite everything** clears the targets' hitsounds first; **Overwrite only defined** changes only the hit points the source has a hit for.
- **Temporal leniency**: how far apart (ms) a source and a target hit may be to match.
- Copy hitsounds, slider-body sounds, sample sets and indexes, and volumes; always keep 5 % volumes.
- **Copy to slider ticks / slides** (Overwrite only defined): a source hit with no target hit goes onto a slider tick or slide as a new mixed sample, from the start index up.
- **Copy storyboarded samples**, skipping ones a hitsound already plays, or any at a hitsound.
- **Mute slider ends**: slider ends the source has nothing for play a hitnormal at 5 %, filtered by snap (first matching divisor wins) and minimum length.

Each target is saved with a backup, like **Save .osu**, and only the lines that change are rewritten. A slider body's index and volume follow the target's own green lines.

When several source hits at exactly the same time land on one target hit, compatible copies keep distinct sound types separate and finish layers fuse into finish. Keep **Copy sample sets and indexes** enabled for prepared source sounds. Copy includes unsaved edits from the open source without saving that source. If a required sample cannot be rendered, that target is left unchanged and the editor reports the problem.

The editor previews slider-slide and whistle loops, including sample and volume changes inside a slider. Tick copies keep the slider's sample bank, and **Copy volumes** and **Always keep 5 % volumes** also apply to generated samples. Copying to ticks or slides requires **Copy sample sets and indexes**, since each generated sample needs a new index.

## Download

Download the newest **C# editor preview** from [Releases](https://github.com/YogeGod/dawsu-hitsounding-editor/releases), extract the whole ZIP, and run `OsuHitsoundEditor.exe`. Keep the extracted files together.

## Building from source

With Windows and the .NET 10 SDK:

```powershell
dotnet run --project src/OsuHitsound.App
dotnet test OsuHitsoundEditor.slnx
```

## Project layout

| Folder | Purpose |
| --- | --- |
| `src/OsuHitsound.App` | The editor window, tools and inspector |
| `src/OsuHitsound.Audio` | Playback of the song and samples |
| `src/OsuHitsound.Format` | Reading and writing `.osu` files |
| `src/OsuHitsound.Lanes` | Turning a map's hitsounds into lanes and back |
| `tests/` | Automated tests |
| `tools/` | Screenshot and round-trip tools |
| `docs/` | Design notes and the tutorial's screenshots |

## Credits

The built-in default hitsounds are from [osu!](https://github.com/ppy/osu-resources) by ppy and contributors, used under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/). Copy hitsounds follows [Mapping Tools](https://github.com/OliBomby/Mapping_Tools)' Hitsound Copier by OliBomby (MIT). Songs and samples are decoded with [BASS](https://www.un4seen.com) (free for non-commercial use), and the sound goes out through [NAudio](https://github.com/naudio/NAudio) (MIT). The waveform's frequency colours follow [osu-framework](https://github.com/ppy/osu-framework)'s Waveform (MIT).

Volume scope defaults to **All lanes**. Choose **Selected lanes** beside the numeric volume edit to change only selected lanes at the affected times. Numeric edits, bar dragging, painting and ramps use that same scope. Export preserves independent levels, preparing audio only when native osu! fields cannot represent them. **Ramp** is in the controller header; shortcut **6** opens it. **Grab** pans time and lane rows without editing hits. Space remains Play/Stop. Range selection remains under View.
