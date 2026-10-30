# Spec: V6.39 port, wave 1

Status: approved by the user on 2026-10-30. The decisions behind it are in `docs/merge-decisions.md`. V6.39's behaviour comes from `src/app.js`, `src/model.js` and `src/help.js` on tag `V6.39` (read them with `git show V6.39:<path>`).

Wave 1 has three parts: the Range tool, scale by volume, and Cubase keys for both.

## Ground rules (from the existing editor)

- Sounds are `EditHit`s: a lane, a time, and the circle or slider end they sit on (`Anchor`). Volume belongs to that circle or slider end (`EditState.Volumes`, keyed by `MomentKey`), not to a single sound.
- Moving a sound in time already works through `EditSession.Move`. A sound that lands where there is no object becomes unanchored: in Apply mode it is a red conflict, and in Generate mode it gets a new circle. The Range tool uses the same path and adds no new export rules.
- Gameplay objects are never moved, split or deleted. That matches V6.39: "Linked gameplay objects are never moved or split."
- Each completed edit is one undo step.

## 1. Range tool

**Tool.** New tool `toolRange` on key **2**, between Select (1) and Draw (3), with a toolbar button and F1 help text.

**Dragging a range.**
- Drag in the lane grid to mark a time range. Its start and end snap to the grid when Snap is on.
- The range covers **the lanes you drag across**: from the lane where the drag starts to the lane where it ends (Cubase behaviour).
- The range is drawn as a shaded box over those lanes. Clicking outside it, pressing Escape or switching tools clears it.
- The range is separate from the hit selection and from the cycle locators. "Locators to selection" (P) uses the range when one exists, as in V6.39.
- The range end is excluded: a sound exactly on the end time is outside it.

**Editing a range.** An **Edit range…** command (in the Edit menu and the Range panel, no default key) opens a dialog:
- **Operation:** Copy, Move or Delete.
- **Offset (ms):** defaults to the range length, so Copy places the copy right after the range. Not used by Delete.
- The dialog repeats: "The range end is excluded. Gameplay objects are never moved."
- **Copy** adds the range's sounds again at `time + offset` in the same lanes. Volumes follow the circles they land on.
- **Move** moves the range's sounds by the offset (the same as `Move` on those hits).
- **Delete** removes the range's sounds.
- If no range is marked, the command shows a status message: "Choose the Range tool (2) and drag a time range first."
- The status bar shows the range as "start → end ms" while one exists.

V6.39's "split crossing MIDI lengths" option is left out, because this editor has no sound lengths.

## 2. Scale by volume

**Command.** New command `scaleByVolume`, "Scale by volume…" in the Edit menu. It has no default key.

**Dialog.**
- **Scope:** Selected sounds (the default when something is selected), Lanes of the selected sounds, or All sounds. (Changed while building: the editor has no "active lane", so selecting one sound in a lane stands in for it.)
- **Volume from / to:** 1–100 %. A circle or slider end matches when its volume is inside this range.
- **Scale by:** a percentage. 50 % halves the volume and 150 % makes it louder.
- A live preview line: "Changes N circles/slider ends."
- OK applies the change as one undo step.

**Rules.**
- The unit is the circle or slider end (`MomentKey`), because that is where volume lives. A moment matches when at least one in-scope sound sits on it and its volume is in range.
- Scaling a moment changes the volume of **every** sound on it, including sounds on other lanes outside the scope. The dialog says so in one line.
- Results are rounded and kept within 1–100, like the volume panel's Relative edit (`VolumeRamp.Shift`), so scaling never makes a circle silent. Stable plays anything below 8 % at 8 %, and the dialog doesn't hide that.
- Unanchored sounds have no volume yet and are skipped.
- Selecting, setting and muting by volume are **not** in wave 1 (the user chose scale only).

## 3. Cubase keys

The Cubase Key Commands import is already built (`KeyBindings.cs`, commit `cfe95c5`). Wave 1 adds the two new commands:

- `toolRange` → `Tool|Range Tool` (the name appears in the user's Cubase 9.5 key file; check its category when implementing).
- `editRange` and `scaleByVolume` have no Cubase equivalent and keep their built-in keys (none by default).
- The osu! preset leaves `toolRange` without a key: it already gives 2 to Draw, as osu!'s editor uses 2 for placing circles. (Changed while building.)

The uncommitted keybind changes in the working tree (a parse cache and comparing `KeyGesture` values directly) come from another session. They are not part of this spec. Commit or drop them before wave 1 starts.

## Tests

- **Lanes:** a range Copy, Move or Delete keeps or changes only the expected hits, and objects stay put. Copying onto a circle takes that circle's volume. Moving off a circle gives an unanchored hit (Apply shows a conflict, Generate adds a circle). A sound on the range end is not included.
- **Lanes:** scale-by-volume picks the right moments, scales the whole moment, rounds and clamps, and skips unanchored hits.
- **App:** the range follows the lanes you drag across and snaps when Snap is on. Key 2 picks the Range tool in the built-in and osu! presets, and a Cubase file with a Range Tool key maps to it.
- **Corpus:** a no-edit round-trip is still byte-identical.

## Not in wave 1

Quantize and Repeat (wave 2), MIDI (wave 3), select/set/mute by volume, and Cubase-style dragging of a range.
