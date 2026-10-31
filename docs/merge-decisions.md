# Combining the C# editor with dawsu! V6.39: decisions

Decided with the user on 2026-10-30. The live map, with the full feature inventory, is the dashboard at https://claude.ai/artifact/VVUhNS5uTAL1aKynTLpo4B.

The two apps share no git history and are written in different languages, so this is a port, not a merge. V6.39 means the Electron app on `dawsu/main` (tag `V6.39`). Read it with `git show V6.39:<path>`.

## 1. Where the UI lives: Avalonia

V6.39's UI, tools and keybinds are ported into the Avalonia app. The C# app stays the only app, and the Electron source is reference only.

Why: one language, one audio clock (BASS, which osu! uses), the existing tests keep covering everything, and the M5 lane grid is reused. With an Electron UI, every edit would cross a process boundary.

## 2. Which features, in what order

- **Wave 1:** the Range tool (key 2), select and scale by volume, and the Cubase Key Commands import (planned in the hs-editor-next session).
- **Wave 2:** Quantize (Q), Repeat (Ctrl+R), more snap modes, time signatures, assigning sounds to objects, event colour modes, the recent-edits list, playback start mode and return on stop, lane rename/reorder/add/delete, inspector panels, `.osz` import and a new empty project, and song volume/offset/speed.
- **Wave 3:** MIDI import and export, MIDI note/channel/velocity per lane (editor-only), event lengths, WASAPI/ASIO output, and the editor screenshot.
- **Dropped:** per-sound volume with mixed WAVs, and preview tones for empty lanes.

The v1 scope's "MIDI out" now means "MIDI comes in wave 3".

## 3. Data model: C# lanes per sample

The C# model wins: one volume per circle or slider end (a `Moment`), as osu! stores it. That keeps export byte-exact and correct for stable. V6.39's extras (display name, MIDI note and channel, colours) become editor-only data that never changes the `.osu`. When MIDI is imported, the loudest note on a moment sets that moment's volume.

## 4. Repo and branch layout

Work continues on `main` here, pushed to `dawsu` as `csharp-rewrite`. Once the port reaches parity, the user makes it the new `main` of the dawsu repo. V6.39 stays available as a tag and release. Agents never push to `dawsu/main`.

## Next

Write a spec for wave 1 (`/to-spec`), then tickets.
