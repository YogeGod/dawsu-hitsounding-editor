# main-editor

Entry: src/OsuHitsound.App/MainWindow.axaml
Dependency tree (native C# references in place of web imports):
- src/OsuHitsound.App/App.axaml (Fluent theme and application font defaults)
- src/OsuHitsound.App/MainWindow.axaml.cs (events, resource application, transport and editor commands)
  - src/OsuHitsound.App/UiTheme.cs (editable settings and brush cache)
  - src/OsuHitsound.App/Paint.cs (shared drawing primitives)
  - src/OsuHitsound.App/ArrangeView.cs (timeline, waveform, diamond hits, lane headers, gestures)
    - src/OsuHitsound.App/ArrangeModel.cs (lane and group rows, grid data)
    - src/OsuHitsound.App/HitEditor.cs (selection and editing state)
    - src/OsuHitsound.App/TimeAxis.cs (time/position conversion)
  - src/OsuHitsound.App/OverviewStrip.cs (whole-song overview)
  - src/OsuHitsound.App/VolumeLane.cs (per-moment volumes and ramp gestures)
  - src/OsuHitsound.App/EditorCommands.cs (shortcut catalog)
  - src/OsuHitsound.App/MainWindow.Map.cs (lane dialogs, filter, generation and per-map project)
    - src/OsuHitsound.App/Dialogs.cs (native modal forms)
  - src/OsuHitsound.App/MainWindow.Prefs.cs (window position, preferences, help)
    - src/OsuHitsound.App/Preferences.cs
    - src/OsuHitsound.App/HelpWindow.cs
  - src/OsuHitsound.App/InspectorWindow.cs (theme editor, optional)
- src/OsuHitsound.App/ui-theme.json (saved visual overrides)

Design payload: MainWindow.axaml in full, the compact theme summary, and design-system.md. ArrangeView is 900+ lines: include render range 199-560 only when needed; screenshot supplies visual ground truth. Exclude audio, file parsing and export internals from design context.
