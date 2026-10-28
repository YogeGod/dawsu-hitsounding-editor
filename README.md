# osu!daw hitsounding editor

A Windows hitsound editor with a sample timeline, MIDI import, a detailed waveform and osu! beatmap export.

- New projects start with Normal, Soft and Drum sampleset folders. Whistle, Finish and Clap are visually nested beneath Hitnormal with matching icons. Imported numbered samples stay grouped in the same structure.
- Use a family’s arrow to expand numbered layers, or its + to add the next index. Choose sample assigns audio while keeping the family’s export filename; double-click renaming changes the label only.
- Drag the divider below the waveform or gameplay-object lane to adjust its height. Double-click a divider to reset it. Heights are saved automatically.
- Standard Windows title bar controls support minimize, maximize/restore and close.
- Ctrl+A displays volumes for all selected hitsounds in the controller lane. Normal selections keep the active-row view.
- Double-click a sample row with Select to hear its sample and edit its display name.

- Export beatmap asks whether to export normally or create a separate HS diff rhythm guide from the hitsound events.
- Keys / F1 opens a searchable overview of keyboard shortcuts, mouse modifiers and panel controls.
- Inspector panels collapse from their titles and reorder using their grips or Alt+Up/Down.

Extract the complete ZIP and run `Hitsound Editor.exe`. Keep all extracted files together.

- New projects and imported maps automatically expand every sampleset and numbered layer.
- Bookmarks: Ctrl+B adds one at the playhead; Ctrl+Shift+B removes it; [ / ] jumps between bookmarks. Names save in editor projects, while osu! export stores bookmark times.
