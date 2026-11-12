# Saves leave every unedited line byte-identical

Mappers don't trust hitsound tools to write their maps. Losing hitsound data on save or export is the second most common complaint in the Mapping Tools and lazer issue trackers. So every write dawsu! makes to a `.osu` file follows one rule: **a line nobody edited is written back as its original bytes.** That includes comments, number spelling, line endings, the BOM, unknown sections and keys, `[Events]` and `[Colours]`. The rule covers files made by stable and files exported by lazer. Lazer's quirks are kept as they are, not "fixed". Normal Save already works this way (`OsuFile` / `FieldLine` patch only changed fields). This ADR extends the rule to every other write path and makes it visible.

- **Generate and New hitsound diff** start from the source difficulty's bytes and replace only `[HitObjects]` and the `Version:`/`BeatmapID:` lines. Today `HitsoundDiff.DiffText` rebuilds the file. It drops `[Events]`, `[Colours]` and green lines (kiai and SV), forces CRLF and writes no BOM.
- **A change summary is shown before writing.** Each difficulty gets one row of counts (objects changed, green lines added/edited/removed, other lines), and each row expands to the before and after of every changed line.
  - It always appears for Copy hitsounds, Generate and green-line clean-up.
  - On a normal Save it appears only when "other lines" isn't zero or there are conflicts. Otherwise the save goes through and the counts show in the status bar.
- **Copy hitsounds is all-or-nothing.** Every target is planned first, under the same conflict rules as Save. Writing starts only when every target is clean, or when the mapper accepts specific conflicts. If any write fails, the targets already written are restored from their backups.
- **Green lines are only removed by an explicit command.** "Clean up green lines" removes any green line that changes nothing (same sample set, index, volume, SV and kiai as the settings in force), whoever wrote it. It never runs as part of Save.
- **Backups:** keep the newest 20 per difficulty. A backup that an in-progress Copy rollback still needs is never pruned.

## Considered Options

- **"osu! reads it the same" (semantic equality).** This would allow reformatting, such as rounded floats or reordered keys. The writer would be simpler, but every diff would be noisy, so you couldn't prove nothing else changed.
- **Only hitsound fields guaranteed.** The smallest scope, but it allows exactly the "lost my storyboard" bugs mappers report.
- **Clean up green lines on every save.** Cleaner maps, but saves would then touch lines nobody edited, which breaks the rule.
- **Keep Copy per target (like Mapping Tools).** Simpler, but a failure halfway leaves a mapset with some difficulties written and others not.

## Consequences

- Any new write path has to go through line-level patching. Building a whole file from text is not allowed for existing difficulties.
- The change summary needs a diff between the planned bytes and the bytes on disk for every write. That diff is also the base for a future "apply to every difficulty with a preview".
- Fidelity tests have to run on a fresh clone. A small committed test pack is the main proof: the owner's own maps, the same maps exported from lazer, synthetic edge-case files, and mania, taiko and catch files saved untouched. The local `corpus/` test becomes optional and skips when the folder is missing. No other mappers' maps are committed.
- Editing mania hold-note sounds and other per-mode features is out of scope here. Files in those modes only have to survive a save untouched.
