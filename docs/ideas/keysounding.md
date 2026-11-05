# Keysounding - planning interview

Current scope: the separate Drums VST workflow now supports live timeline playback, lane audition and explicit WAV export. Melodic keysounding, its piano roll and hardware MIDI input remain deferred. The interview below records earlier design intent; it is not a claim that those features are implemented.

## Confirmed
- All requirements here apply only to Keysound mode. Hitsound mode retains its existing sample lanes, editing behavior and export workflow.
- Keysounds layer over the existing song audio. Existing instrument parts remain audible; replacing parts via a separate backing track is not part of the agreed workflow.
- Full workflow: draw or import MIDI, play through VST instruments, render keysound samples, export samples and placements into an osu! beatmap.
- Keep editable MIDI notes and instrument settings in the project.
- In Keysound mode, Save project / Ctrl+S saves editable project data (MIDI, tracks, controllers and instrument settings). Generate renders samples and creates [Keysounds]; the Save .osu button is relabeled Save project only in this mode.
- MIDI import targets only the Keysound project. It must not import MIDI notes into or modify a beatmap file. Generate is the separate action producing [Keysounds] output.
- Use the reference beatmap's red lines as the Keysound project tempo/timing authority. MIDI notes are placed against that timing; importing MIDI does not replace the beatmap timing or write to the source .osu.
- Include physical MIDI keyboard audition and recording alongside drawing notes and importing MIDI.
- Preserve sustain pedal, pitch bend and modulation from imported/recorded MIDI in playback and bake their effect into each independently rendered note.
- Allow starting from an existing beatmap or starting a new project from audio/MIDI without a beatmap. Generate requires selecting a reference beatmap first and uses its red lines.
- Render each note independently into its own keysound sample, including the instrument release tail.
- Preserve instrument dynamics and timbre from MIDI velocity. Do not normalize every rendered note to the same loudness; track volume adjusts the overall balance.
- Include a ready-to-play piano and allow users to load their own VST instruments. No specific third-party piano plug-in has been selected.
- Support multiple instrument tracks, each with its own instrument (bundled piano or VST3). The piano roll shows the selected track; select tracks through the inspector within the existing editor layout.
- Generate creates separate [Keysounds] output for notes synced to reference gameplay objects. Unsynced MIDI notes are highlighted and are not exported. Do not automatically create circles for unmatched notes. Simultaneous notes synced to one object share it; existing playable difficulties remain untouched.
- In generated [Keysounds] output, replace the reference difficulty's original hitsounds so only the exported keysounds sound. This changes the generated copy only; the source difficulty remains untouched.
- Provide an explicit Sync to gameplay objects action for selected MIDI notes: preview proposed start-time shifts to nearest objects, then apply while preserving pitch, length and velocity. MIDI import alone does not move notes.
- Eligible sync targets are circles, slider heads/repeats/tails and spinner ends, matching the existing Hitsound-mode positions. Provide type filters in the Sync dialog.
- Sync uses an adjustable maximum start-time shift, default 20 ms. Notes outside the limit stay unsynced; equally close target matches are flagged for review rather than resolved automatically.
- Export preserves separate rendered notes whenever native osu! fields can represent the chord. Mix only otherwise, retaining individual note renders and editable MIDI in the Keysound project.
- Use Cubase 15's Key Editor as the reference for exact editing behavior.
- Keep the existing dawsu! interface; replace sample lanes with a piano roll in keysounding mode.
- Initial plug-in scope: Windows 64-bit VST3 instruments, their native editor windows and saved plug-in state/settings. VST2, 32-bit plug-ins and effect-insert chains are deferred.
- First version uses one saved instrument preset per track. Sustain, pitch bend and modulation vary over time; arbitrary VST parameter/knob automation is deferred.
- User describes final output as individual MIDI notes rendered to new .wav files for the gameplay objects they are synced with. WAV is the required rendered sample format.
- Render every exported note occurrence independently. Reuse an existing WAV only when the resulting rendered audio is identical; preserve distinct VST variations as separate files.

## Refined export workflow
1. Select a reference beatmap; its red lines control timing.
2. Draw, import or record MIDI into the Keysound project, without writing MIDI into the beatmap.
3. Preview and apply explicit Sync for selected notes (all supported target types, filters, adjustable 20 ms default maximum shift, ambiguous matches flagged).
4. Highlight and skip unsynced notes. Do not add gameplay objects for them.
5. Independently render each synced note occurrence to WAV with its instrument/controller response, natural velocity dynamics, duration and release tail; reuse only identical rendered audio.
6. Assign rendered sounds to their synced gameplay positions in separate [Keysounds] output, using native combinations where possible and mixing only otherwise. Replace original hitsounds only in the generated copy.
7. Play keysounds over the existing song audio; keep the source map untouched. Save project preserves MIDI and instrument state; Generate produces the playable keysound output.


## Approved build plan
1. Establish a separate Keysound project, mode switch and piano roll using the existing editor layout. Reuse the inspector for instrument tracks, controller area for velocity/MIDI controls, and transport for recording.
2. Match Cubase 15 Key Editor interactions for drawing, selecting, moving, resizing, duplicating, splitting/gluing, quantizing, velocity editing, auditioning, pan/zoom and relevant shortcuts. Verify concrete gestures against the installed reference.
3. Add the bundled piano, MIDI import, live keyboard audition/recording and sustain/pitch-bend/modulation handling. Use reference beatmap red lines for project/export timing; require a reference beatmap before Generate.
4. Add Windows x64 VST3 hosting, native plug-in editors and saved instrument state. Research and validate the host boundary and built-in piano licensing before implementation of those components. VST2, 32-bit and insert-effect chains remain deferred.
5. Add individual WAV note rendering with release tails and native-first [Keysounds] export for synced notes only, highlighting/skipping unsynced notes and mixing only unrepresentable chords. Reuse byte-identical rendered audio without changing note placements. Detect non-ending tails and ask for a render limit rather than silently truncating them.
6. Validate MIDI/controller persistence, Cubase editing gestures, rendering/exports on disposable beatmaps, VST recovery and existing Hitsound-mode regressions.

The user approved the scope/build plan and subsequently authorized the JUCE VST3 host proof, first requesting GGD Matt Garstka and then switching to Kontakt plus GGD PV (installed as P5 Matt Halpern Signature Pack). See docs/validation/vst3-host-proof.md. No new release or push is authorized by this planning document.
