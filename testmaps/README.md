# Stable listening test (milestone 1)

`hitsound-rules-test.osz` holds one diff for each open question about how osu!stable plays hitsounds.
To regenerate it, run `dotnet run tools/make-test-maps.cs` from the repo root.

## Setup

1. Drag `hitsound-rules-test.osz` onto the osu!stable window to import it.
2. In Options, make sure **Ignore beatmap hitsounds** is **off**.
3. Use Autoplay (Ctrl+M or the Mods menu), or open each diff in the editor and play it from the start.

The music track is silent, so every sound you hear comes from the map.

## Diff "1 Ticks": does a green line in the middle of a slider change the ticks after it?

The diff has four long sliders. Each one has 3 ticks, and a green line sits between tick 1 and tick 2.

- **Sliders 1–2:** the green line switches the tick sound from a **high beep** to a **low beep**.
  - You hear high, high, high → ticks keep the slider-start settings (like lazer).
  - You hear high, low, low → each tick reads the green line at its own time.
- **Sliders 3–4:** the green line drops the volume from 80 to 15.
  - You hear loud, loud, loud → the slider-start settings are kept.
  - You hear loud, quiet, quiet → each tick reads its own green line.

The slider head and tail always play the same mid click. It only marks where each slider starts and ends.

## Diff "2 Slider body": does stable use a slider's own index and volume?

The diff has six short sliders that alternate:

- **Odd sliders (1, 3, 5)** are controls. They follow the timing point: low click, low hum, volume 40.
- **Even sliders (2, 4, 6)** ask for their own index and volume: high click, high hum, volume 100.

Listen for which of these you hear on the even sliders:

- **The same as the odd ones** → the slider's own index and volume are ignored.
- **A high click at the head and tail** → the head and tail use the slider's own index.
- **A high hum during the slide** → the body uses the slider's own index.
- **Louder than the odd ones** → the slider's own volume is used.

## Diff "3 SampleSet 0 v2": what does sampleset 0 ("auto") on a timing point mean?

The map's default sampleset (`[General] SampleSet`) is **Drum**. The diff has five groups of four circles, with a short gap between groups:

| Group | Green line says | Sound |
|---|---|---|
| 1 | auto | ? |
| 2 | Drum | high beep |
| 3 | Normal | low beep |
| 4 | Soft | middle beep |
| 5 | auto | ? |

Listen for which group 1 and 5 match exactly: 2 (the map default), 3 (Normal, as in lazer) or 4 (Soft). The first run (2026-10-30, without group 4) heard "between high and low", and v2 matched group 4 (Soft).

Diff **"3 SampleSet 0 v3 (default Normal)"** is the same, but the map's default is **Normal**. If groups 1 and 5 still match group 4 (Soft), auto always means Soft. If they match group 3 (Normal), auto means the map default and stable ignored the Drum default in v2.

## Optional

Play the same diffs in lazer to hear where the two clients disagree.
