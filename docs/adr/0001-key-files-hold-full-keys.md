# Key files hold a preset's full keys, not its key changes

Exported `.dawsukeys` files are shared between mappers, and the DAW-like key preset depends on each person's own Cubase key commands. So a file holds every command's final keys, not the key changes on top of a preset. On import, those keys become the key changes of whichever key preset is active, so the result matches the file on any machine, with or without Cubase. The file is JSON with a `version` number and a `keys` map from command ID to key list, written in the same key text as the preferences file. It names its source preset for information only.

## Considered Options

- **Key changes only.** Smaller, but only meaningful on top of the same preset. A DAW-like file means nothing to someone whose Cubase keys differ, or who has no Cubase.
- **No import/export (copy the preferences file).** Fine for one person moving machines, but not for sharing with other mappers.

## Consequences

- Command IDs in `EditorCommands.cs` are now a public format. Renaming one breaks shared files unless the import maps the old ID.
- Across versions: commands the file names but this version lacks are skipped. Commands the file doesn't mention keep the active preset's keys, unless an imported key already uses them.
- A key the file gives to two commands stays with the first one. Keys that can't be read are skipped, and both are counted in the import summary rather than rejecting the file. The wheel setting is not part of the file.
