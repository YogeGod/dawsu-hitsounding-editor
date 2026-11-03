# Export Merge Priorities

## Problem Statement
Let hitsounders choose which osu! slot carries overlapping sounds, audition the result, and reuse that policy without resolving individual overlaps repeatedly.

## Agreed Behavior
Generate opens a priority window with an ordered list of all 12 standard hit slots, independent of editor lane order. Every regular overlap chooses the highest-priority addition represented by its inputs, even when the additions differ. The compulsory normal sound does not outrank additions; normal-only inputs follow normal-slot priorities. Arbitrarily named inputs with no recognized slot use the first configured slot. All input sounds and their occurrence-specific volumes remain in the mix.

Slider ticks, slide loops, and whistle loops are preserved as separate timed hits and never enter the regular priority combinations, including when they occur at the same time as a regular hit.

The priority window provides optional individual-input and mixed-result auditions for each regular combination. Preview uses its first occurrence's volumes. Export uses every occurrence's actual volumes. Preview and export share the renderer; starting an audition replaces the previous one.

Users explicitly save and load JSON presets. Presets contain the priority order and optional existing combination overrides; an explicit loaded override wins over priorities. Nothing is loaded or remembered across projects automatically. Combination identity preserves lane source, normalized filename, available content hash, multiplicity, and loop identity, while ignoring volume and duration. A + B + C differs from A + B. Unmatched loaded overrides are counted and ignored; automatic priorities handle new combinations without another popup.

All 12 standard hit slots are valid destinations. An addition destination includes a silent normal sample at the same generated index to prevent osu! from adding an extra normal sound. New filenames skip occupied indexes, and regeneration reuses an addition/normal pair only when both files and inputs remain valid. Existing shared files are protected.

## MVP Scope
- Independent export priority list and automatic destination selection.
- Optional sample and mix auditions within that window.
- Explicit preset save/load and validation.
- Separate preservation of slider sounds.
- Safe cancellation before any generated files are written.

## Not Doing
- Per-combination decision popups: automatic priority resolution supersedes the earlier proposal.
- Automatic cross-project remembering: presets require explicit save/load.
- Per-hit exceptions or genre/instrument lane layouts: separate features.
- Changes to Copy hitsounds: this feature applies to Generate.

## Assumptions and Verification Limits
- Preview and exported PCM should match; verify volume handling and headroom with rendering tests and actual listening.
- Renamed or changed inputs must not accidentally inherit a saved override; validate identities and fallback behavior.
- Model and automated UI/export tests do not establish playback compatibility in osu!stable, Mapping Tools, or on physical audio hardware.

## Status
The user approved the original feature, then corrected it to automatic priorities for all regular combinations and separate preservation of slider sounds. The corrected behavior is implemented. Commit scope is export merging only; unrelated editor workflow changes remain pending separately.
