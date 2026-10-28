# Portable Windows packaging

Run package-windows.py with an existing matching Electron runtime folder, the Node executable, and the local @electron/asar module. --xz additionally builds a smaller tar.xz download; the standard ZIP uses Windows-compatible Deflate. Both archives contain the same complete portable folder. Extraction is required before running the EXE.

The builder uses an explicit runtime allow-list, retains Chromium graphics fallbacks, FFmpeg, ICU Unicode data and licence notices, and includes en-US/en-GB language packs for the English editor. The package excludes screenshots, reference project copies, validation reports and the duplicate unpacked application. Production dependencies are resolved from package.json, flattened once, and archived with the complete app and sample set. Native audio executables and DLLs remain external to ASAR. Existing releases are never overwritten.

Runtime/source version compatibility is the caller's responsibility; use the Electron version already verified for this editor. No Electron downgrade, executable packing, external WebView dependency or runtime download is introduced.

Verify a release with the core/renderer suites, ZIP CRC and extraction, packaged MIDI/ZIP dependency round trips, the twelve default sample files, native audio helper output, and an actual GUI launch from the extracted folder. File sizes, checksums and verification results are recorded in package-validation-vVERSION.json.
