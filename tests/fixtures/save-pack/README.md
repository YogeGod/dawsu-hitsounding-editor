# Save pack

Files that must survive a save from dawsu! with every unedited line byte-identical ([ADR 0002](../../../docs/adr/0002-saves-leave-unedited-lines-byte-identical.md)). `SavePackTests` runs over every file here on every clone, and `Every_pack_file_is_listed` fails when a file is added without a named case.

- `synthetic/`: small hand-written files, one per edge case (BOM, mixed or missing line endings, format v3, v7, v14 and v128, lines that aren't valid UTF-8, storyboard and colours, unknown sections and comments, custom samples and slider edges, mania, taiko and catch).
- `owner/`: the maintainer's (Yoge God) own maps, copied unchanged from osu!stable's Songs folder: Catalyst [Impetus] (standard), Catalyst [Hitsounds] (a hitsound diff), Mukduk (written by osu!lazer: v128 with a BOM), and Periphery - Satellites [18K MIDI Volumes] (mania with per-note keysounds). Only the .osu files, no audio.

No other mappers' maps go here. They belong in the git-ignored `corpus/` (`tools/build-corpus.ps1`).

`.gitattributes` marks this folder `-text`, so git never rewrites the line endings these tests depend on.
