# Longer snap options

Status: approved by the owner's OK, implemented and verified locally. The owner requested committing to feature/electron-lane-grouping. Continues F07 after commit 33b0acb. Owner confirmed Q40A: the long-snap grid starts at each red timing point, not each measure or a chosen hit. Evidence is in [verification report](2026-11-04-long-snap-verification.md).

## Proposed behavior

- Add 2/1, 3/1 and 4/1 to the existing snap selector: one position every 2, 3 or 4 osu! beats. Retain every existing 1/n option and its behavior.
- Anchor the long grid at the current red timing point. Green lines and editor-only time-signature changes do not restart it. A new red timing point starts a new grid; do not extend the old grid across it. Before the first red line, extend that first grid as the existing model does.
- Use the selected spacing consistently for drawing, snapping drags, keyboard steps, quantize, duplicate and repeat. Preserve relative dragging offsets and the existing undo behavior. Repeat stepping across a red line lands on that line before continuing on the new grid.
- Retain beat/bar reference lines and distinguish the active long-snap positions. Preserve timeline click gestures, gameplay objects, map timing, volume and export behavior.
- Keep all options available in the dropdown. Extend the straight wheel cycle with 4/1 and 2/1, and the triplet cycle with 3/1; preserve the existing finer/coarser direction.
- Represent spacing explicitly; do not encode long snaps as negative divisors or change existing per-beat volume/copy-filter denominators.

## Verification

Check constant BPM, nonzero red-line offsets, green lines, changed BPM, editor-only signatures, boundaries near a new red line, relative dragging, repeat/duplicate/quantize, wheel ordering and unchanged existing 1/n behavior. Run the full suite/corpus and current-source UI checks sequentially. Use isolated maps, keep the running test editor intact and report any native interaction not exercised.

Reusable linked hitsound sets remain the next separate feature. Original crash/offset/unexpected-filename reports remain unconfirmed. Commit/push is limited to the requested feature branch; no release is included.
