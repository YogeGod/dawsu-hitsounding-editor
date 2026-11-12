# Reuse single OGG samples during Save and Generate

Reported behavior: existing OGG samples caused unnecessary numbered output samples after Save or Generate. Regression tests reproduced it for custom OGG, WAV and MP3 filenames, including a volume-only edit.

The compiler previously refused to write object filename references. As a result, a single custom sample was treated as requiring bank/index preparation. Generate additionally omitted the direct-encoding result when calling its pairing planner.

The compiler now references one existing mapset sample directly on a circle or spinner when bank encoding is unsuitable or a filename is already in use. It preserves volume inheritance where possible, clears addition flags for a file-only hit, and does not change coordinates, object times or surrounding timing points. Existing indexed hitnormal samples keep their bank encoding when it works. An OGG shadowed by a same-stem WAV can use its exact filename instead of creating a new index.

Generate now supplies the compiler's existing no-mix decision to pairing, consistently with Save. Genuine overlaps, independent layer gains and slider-node constraints still use pairing/mixing when necessary. Filename references are limited to existing files inside the mapset, supported audio extensions, and names that can be represented without corrupting the .osu line. Existing generated files are not deleted by this fix.

Validation in E:\Claude\ogg-export-fix-20261104:

- **613 tests passed**: App 226, Format 52, Lanes 335, including the 2,999-file local corpus.
- Eight direct-export regressions cover OGG/WAV/MP3 reuse, volume edits, OGG addition samples, same-stem WAV shadowing, spinners, new single-sample assignment, and necessary pairing for real overlaps. File reuse tests write Save and Generate outputs with a renderer that throws if called, and assert unchanged audio bytes and no additional sample files.
- Updated the existing rendering-failure fixture to use two overlapping samples, since a single custom file no longer needs rendering. The failure/rollback guarantees remain tested.
- **18 checks passed on three random real-map copies**: IMAGE REBORN ~DIVINITY~, Catalyst and Pedro. Save/Generate retained the chosen OGG filename and volume; Save kept geometry, times and timing points; the original map files and OGG audio remained unchanged. Copies are under artifacts/real-map-copies.
- Inline correctness, simplification and security review: no outstanding concrete findings; no new dependencies, sample overwrites or external file writes.

The former project rule forbidding all filename writes was narrowed to permit this requested direct single-sample path. Live osu! playback and hardware listening were not verified. No release was published for this fix.
