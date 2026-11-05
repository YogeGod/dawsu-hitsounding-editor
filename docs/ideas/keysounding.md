# Keysounding - planning interview

Current scope: the separate Drums VST workflow supports live timeline playback, lane audition and explicit WAV export. Keysound Milestone 1 (2026-11-05) adds the start screen's Keysounding panel, editable and saveable Keysound projects with a copied reference snapshot, instrument tracks, an undoable piano roll and song playback. Milestone 2 adds the bundled piano (FreePats Upright Piano KW, CC0) for playback and editing audition. Milestone 3 adds editing after Cubase 15's Key Editor and MIDI import (tracks by channel, beat placement, sustain/modulation/pitch bend). Milestone 4 adds MIDI keyboard playing and replacement recording (count-in, metronome, controller restoration). VST instruments, Sync and Generate remain to be built. The interview below records design intent; it is not a claim that those features are implemented.

## Confirmed
- Access Keysounding from the app's start menu through its Keysounding panel (confirmed 2026-11-05).
- All requirements here apply only to Keysound mode. Hitsound mode retains its existing sample lanes, editing behavior and export workflow.
- Keysounds layer over the existing song audio. Existing instrument parts remain audible; replacing parts via a separate backing track is not part of the agreed workflow.
- Full workflow: draw or import MIDI, play through VST instruments, render keysound samples, export samples and placements into an osu! beatmap.
- Keep editable MIDI notes and instrument settings in the project.
- Saving a Keysound project copies the reference beatmap and song audio into its own project folder for reliable reopening and portability. External VST installations are still required (confirmed 2026-11-05).
- Saving also copies the visual assets referenced by the saved beatmap and storyboard into the project, so later exports still work if the original map folder is moved or deleted (confirmed 2026-11-05).
- Missing referenced backgrounds, videos or storyboard images allow export to continue with a warning. Missing song audio blocks export (confirmed 2026-11-05).
- Changes to the original beatmap do not automatically replace the project's saved reference. Update the reference only through an explicit user action, previewing the update and flagging affected sync assignments before applying it (confirmed 2026-11-05).
- Generate defaults to a separate output folder inside the Keysound project, keeping the generated difficulty, rendered WAVs and required song audio together for import into osu! (confirmed 2026-11-05).
- After successful generation, offer "Export .osz" for direct import into osu!, while retaining the generated output folder (confirmed 2026-11-05).
- Preserve the reference's background, video and visual storyboard in the generated map, copying their required visual assets into the output and including them in .osz export. Exclude storyboard sound events from generated output so only the user's keysounds play over the song; leave the source unchanged (confirmed 2026-11-05).
- In Keysound mode, Save project / Ctrl+S saves editable project data (MIDI, tracks, controllers and instrument settings). Generate renders samples and creates [Keysounds]; the Save .osu button is relabeled Save project only in this mode.
- MIDI import targets only the Keysound project. It must not import MIDI notes into or modify a beatmap file. Generate is the separate action producing [Keysounds] output.
- Import multiple MIDI tracks as separate instrument tracks, preserving their track names and allowing an instrument to be assigned to each (confirmed 2026-11-05).
- The MIDI import preview offers instrument assignment before auditioning imported tracks, with the bundled piano as the default and percussion tracks clearly flagged for assignment (confirmed 2026-11-05).
- Use the reference beatmap's red lines as the Keysound project tempo/timing authority. MIDI notes are placed against that timing; importing MIDI does not replace the beatmap timing or write to the source .osu.
- When MIDI tempo differs from the reference beatmap, import preserves musical beat positions rather than elapsed time, following the reference beatmap's tempo changes (confirmed 2026-11-05). Bar numbers coincide only when the meter and musical origin also align.
- With a reference beatmap selected, MIDI import lets the user choose where the file's first beat lands. Default to the reference's first red timing point, with an adjustable start position and a preview before importing (confirmed 2026-11-05).
- Include physical MIDI keyboard audition and recording alongside drawing notes and importing MIDI.
- Defer loop recording from the first version. Ordinary replacement recording remains in scope; repeated-take behavior must be decided before adding loop recording (confirmed 2026-11-05).
- MIDI recording replaces existing notes whose start falls within the recorded time range on the armed track. Notes starting before that range stay intact even if they extend into it; notes starting after it are preserved. The entire recording pass is undoable in one step (confirmed 2026-11-05).
- MIDI recording preserves the player's actual timing without automatically snapping notes to the grid. Quantize is an explicit action available afterward (confirmed 2026-11-05).
- Provide independently toggleable recording metronome and count-in controls. Enable a one-bar count-in by default (confirmed 2026-11-05).
- Replacement recording also replaces sustain, pitch-bend and modulation events within the recorded range on the armed track. These controller changes belong to the same undo step as the recorded notes (confirmed 2026-11-05).
- At the end of replacement recording, restore the sustain, pitch-bend and modulation state that the original track would have had at that boundary, preserving controller behavior for untouched music afterward. Boundary restoration is part of the same recording undo step (confirmed 2026-11-05).
- When recording stops, end any still-held recorded notes at the stop point, allowing the instrument's release tails to sound naturally (confirmed 2026-11-05).
- Preserve sustain pedal, pitch bend and modulation from imported/recorded MIDI in playback and bake their effect into each independently rendered note.
- Allow starting from an existing beatmap or starting a new project from audio/MIDI without a beatmap. Generate requires selecting a reference beatmap first and uses its red lines.
- When starting from MIDI without a reference beatmap, initially use the MIDI file's tempo map. When a reference beatmap is selected later, preview the timing change before adopting its red lines (confirmed 2026-11-05).
- For projects started from audio alone, provide manually editable BPM and time signature until a reference beatmap is selected. Defer automatic tempo detection (confirmed 2026-11-05).
- Audio-only projects also provide an adjustable beat-one offset, allowing the first musical beat and grid to align after an intro or leading silence (confirmed 2026-11-05).
- Changing the project's tempo preserves notes' musical beat positions, so their playback times follow the new tempo. Song audio remains unchanged: neither time-stretched nor shifted (confirmed 2026-11-05).
- Sustain, pitch-bend and modulation events also retain their musical beat positions when project tempo changes, keeping controller movements aligned with the notes (confirmed 2026-11-05).
- Render each note independently into its own keysound sample, including the instrument release tail.
- Preserve instrument dynamics and timbre from MIDI velocity. Do not normalize every rendered note to the same loudness; track volume adjusts the overall balance.
- Include a ready-to-play piano and allow users to load their own VST instruments. No specific third-party piano plug-in has been selected.
- Newly created instrument tracks start with the bundled piano loaded and ready to play; the user can replace it with a VST (confirmed 2026-11-05).
- Prioritize a small download for the bundled piano over a larger, more realistic instrument (confirmed 2026-11-05). The specific piano and its redistribution licensing remain to be researched.
- Support multiple instrument tracks, each with its own instrument (bundled piano or VST3). The piano roll shows the selected track; select tracks through the inspector within the existing editor layout.
- Provide a toggle to show other tracks' notes as faint, uneditable ghost notes in the piano roll. Only the selected track is editable (confirmed 2026-11-05).
- Provide optional vertical markers for reference gameplay-object positions in the piano roll, showing potential sync targets while composing (confirmed 2026-11-05).
- Drawing a note exactly on a gameplay-object marker does not automatically assign it to that object. Explicit Sync is still required before export (confirmed 2026-11-05).
- Instrument tracks have no arbitrary fixed count limit; practical capacity depends on available system resources (confirmed 2026-11-05).
- Track mute and solo affect editing playback only. A separate per-track "Include in Generate" switch controls whether that track's keysounds are exported, so auditioning does not change export inclusion (confirmed 2026-11-05).
- Newly created and imported tracks have "Include in Generate" enabled by default. Clearly mark tracks excluded from Generate (confirmed 2026-11-05).
- If a VST instrument crashes, contain the failure to that instrument: other tracks keep playing, the project remains editable, and the user can reload the failed instrument from its last saved state (confirmed 2026-11-05). The hosting implementation remains to be designed and verified.
- Generate creates separate [Keysounds] output for notes synced to reference gameplay objects. Unsynced MIDI notes are highlighted and are not exported. Do not automatically create circles for unmatched notes. Simultaneous notes synced to one object share it; existing playable difficulties remain untouched.
- Before rendering, Generate summarizes unsynced notes that will be skipped, showing the skipped count per track and allowing the user to continue or return to fix them (confirmed 2026-11-05).
- In generated [Keysounds] output, replace the reference difficulty's original hitsounds so only the exported keysounds sound. This changes the generated copy only; the source difficulty remains untouched.
- Provide an explicit Sync to gameplay objects action for selected MIDI notes: preview proposed start-time shifts to nearest objects, then apply while preserving pitch, length and velocity. MIDI import alone does not move notes.
- Sync offers both "Selected notes" and "All included tracks" scopes. The latter covers notes on tracks enabled for Generate. Both scopes use the same preview and maximum start-time shift limit (confirmed 2026-11-05).
- Bulk Sync preserves existing valid assignments by default and processes only unsynced notes or invalid assignments. Provide an explicit option to re-sync everything in the chosen scope (confirmed 2026-11-05).
- For equally close gameplay targets, the Sync preview allows manual target selection and shows each candidate's time and object type. Notes with unresolved matches remain unsynced (confirmed 2026-11-05).
- Applying a Sync preview is one undo step across all affected tracks, restoring original note times and previous assignments (confirmed 2026-11-05).
- Changing a synced note's start time clears its gameplay-object assignment and requires another explicit Sync. Pitch, velocity and length edits retain the assignment (confirmed 2026-11-05).
- If reference gameplay objects change, flag sync assignments with missing or moved targets for review before Generate. Affected notes require explicit re-syncing; do not silently assign them to other targets (confirmed 2026-11-05).

- Eligible sync targets are circles, slider heads/repeats/tails and spinner ends, matching the existing Hitsound-mode positions. Provide type filters in the Sync dialog.
- Sync uses an adjustable maximum start-time shift, default 20 ms. Notes outside the limit stay unsynced; equally close target matches are flagged for review rather than resolved automatically.
- Export preserves separate rendered notes whenever native osu! fields can represent the chord. Mix only otherwise, retaining individual note renders and editable MIDI in the Keysound project.
- Use Cubase 15's Key Editor as the reference for exact editing behavior.
- Keep the existing dawsu! interface; replace sample lanes with a piano roll in keysounding mode.
- Initial plug-in scope: Windows 64-bit VST3 instruments, their native editor windows and saved plug-in state/settings. VST2, 32-bit plug-ins and effect-insert chains are deferred.
- First version uses one saved instrument preset per track. Sustain, pitch bend and modulation vary over time; arbitrary VST parameter/knob automation is deferred.
- User describes final output as individual MIDI notes rendered to new .wav files for the gameplay objects they are synced with. WAV is the required rendered sample format.
- Render every exported note occurrence independently. Reuse an existing WAV only when the resulting rendered audio is identical; preserve distinct VST variations as separate files.
- If a required instrument is missing or fails during Generate, stop the entire generation, preserve the previous output intact and identify the affected track. Do not publish an incomplete difficulty (confirmed 2026-11-05).
- A missing VST does not prevent opening the project or editing its notes. Preserve the missing instrument's saved settings, mark it unavailable and allow the user to locate or replace it. Generate remains blocked when that instrument is required (confirmed 2026-11-05).
- A track whose VST is missing stays silent until the user locates that instrument or explicitly chooses a replacement. Do not automatically substitute the bundled piano (confirmed 2026-11-05).
- Replacing a track's instrument is one undo step that restores the previous instrument and its saved settings. Instrument replacement leaves notes and controllers unchanged (confirmed 2026-11-05).
- Deleting an instrument track is undoable in one step, restoring its notes, controllers, instrument settings and sync assignments (confirmed 2026-11-05).
- Cancelling Generate leaves the previous successful output intact. Prepare new output separately and replace the previous version only after generation succeeds (confirmed 2026-11-05).

## Remaining design decisions

On 2026-11-05, the user delegated remaining routine design choices to the agent's recommended defaults and asked to stop the question-by-question interview. Preserve the confirmed decisions above. Research technical facts and document concrete implementation choices rather than continuing routine preference questions. This delegation does not itself authorize a release or push.

## Consolidated implementation sequence

1. Inspect the current Keysound and VST implementation, verify reusable components and research the remaining Cubase gestures, lightweight piano redistribution options and per-instrument crash isolation. Record evidence and select concrete implementation approaches.
2. Wire the start menu's Keysounding panel to the editable Keysound project. Establish asset persistence, reference snapshots, tempo authority, instrument tracks and reversible editing commands. Preserve existing Hitsound-mode behavior.
3. Build the piano roll and MIDI import/recording workflows using the confirmed interaction, timing, controller and undo rules. Add ghost notes and gameplay markers.
4. Integrate the lightweight bundled piano and isolated VST instruments, saved state, missing-instrument handling and recovery.
5. Implement explicit Sync previews and assignments, then cancellable independent-note WAV rendering, safe output replacement and .osz packaging.
6. Validate persistence and undo, MIDI/controller timing, instrument crash recovery, exports on disposable beatmaps and existing Hitsound regressions. Distinguish automated checks from live UI, MIDI hardware and audio verification.

Present this sequence for implementation approval before starting feature code, as required by the workspace instructions and handoff.
## Refined export workflow
1. Select a reference beatmap; its red lines control timing.
2. Draw, import or record MIDI into the Keysound project, without writing MIDI into the beatmap.
3. Preview and apply explicit Sync for selected notes (all supported target types, filters, adjustable 20 ms default maximum shift, ambiguous matches flagged).
4. Highlight and skip unsynced notes. Do not add gameplay objects for them.
5. Independently render each synced note occurrence to WAV with its instrument/controller response, natural velocity dynamics, duration and release tail; reuse only identical rendered audio.
6. Assign rendered sounds to their synced gameplay positions in separate [Keysounds] output, using native combinations where possible and mixing only otherwise. Replace original hitsounds only in the generated copy.
7. Play keysounds over the existing song audio; keep the source map untouched. Save project preserves MIDI and instrument state; Generate produces the playable keysound output.


## Approved build plan
1. Establish a separate Keysound project accessed through the start menu's Keysounding panel, with a piano roll using the existing editor layout. Reuse the inspector for instrument tracks, controller area for velocity/MIDI controls, and transport for recording.
2. Match Cubase 15 Key Editor interactions for drawing, selecting, moving, resizing, duplicating, splitting/gluing, quantizing, velocity editing, auditioning, pan/zoom and relevant shortcuts. Verify concrete gestures against the installed reference.
3. Add the bundled piano, MIDI import, live keyboard audition/recording and sustain/pitch-bend/modulation handling. Use reference beatmap red lines for project/export timing; require a reference beatmap before Generate.
4. Add Windows x64 VST3 hosting, native plug-in editors and saved instrument state. Research and validate the host boundary and built-in piano licensing before implementation of those components. VST2, 32-bit and insert-effect chains remain deferred.
5. Add individual WAV note rendering with release tails and native-first [Keysounds] export for synced notes only, highlighting/skipping unsynced notes and mixing only unrepresentable chords. Reuse byte-identical rendered audio without changing note placements. Detect non-ending tails and ask for a render limit rather than silently truncating them.
6. Validate MIDI/controller persistence, Cubase editing gestures, rendering/exports on disposable beatmaps, VST recovery and existing Hitsound-mode regressions.

The user approved the scope/build plan and subsequently authorized the JUCE VST3 host proof, first requesting GGD Matt Garstka and then switching to Kontakt plus GGD PV (installed as P5 Matt Halpern Signature Pack). See docs/validation/vst3-host-proof.md. No new release or push is authorized by this planning document.
