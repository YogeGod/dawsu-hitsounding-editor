# osu!daw hitsounding editor — source

Source version: **1.0.35** (V6.35).

## Contents

- Electron main process, preload bridge, renderer and HTML/CSS.
- MIDI/osu! import and export, sample grouping, bookmarks, shortcuts and audio code.
- Tests, example projects, build scripts, icons and bundled samples.
- Native Windows audio helper C# source, build script and its pinned dependencies.
- Existing third-party licence and sample attribution files.

The Electron runtime and `node_modules` are not included. The native audio helper and its supporting DLLs are included alongside their source/build instructions.

## Run from source

Use Windows, Node.js 24 or newer, and npm. The packaged release uses Electron **44.5.0**.

From the extracted source folder:

```powershell
npm install
npm install --no-save electron@44.5.0 @electron/asar@3.4.1
npx electron .
```

## Run tests

```powershell
npm test
Get-ChildItem test/renderer-*-check.cjs | ForEach-Object { node $_.FullName; if ($LASTEXITCODE -ne 0) { throw "Renderer check failed" } }
```

## Native audio helper

The prebuilt helper is included. To rebuild it, install .NET Framework 4.8 and run:

```powershell
powershell -File native-audio/build.ps1
```

See `native-audio/README.md` and `dependencies.json` for the output backends and bundled dependency versions.

## Build a portable Windows ZIP

Python 3.11 or newer is required. Prepare the Electron runtime filenames expected by the packaging script, then build:

```powershell
Copy-Item node_modules/electron/dist/electron.exe node_modules/electron/dist/'Hitsound Editor.exe'
Copy-Item node_modules/electron/dist/LICENSE node_modules/electron/dist/LICENSE.electron.txt
$runtimePath = (Resolve-Path node_modules/electron/dist).Path
$asarPath = (Resolve-Path node_modules/@electron/asar).Path
python scripts/package-windows.py --runtime "$runtimePath" --node node --asar-module "$asarPath" --output-dir ./releases
```

The script embeds the application icon, packages production dependencies into ASAR, copies the native audio helper and produces a portable ZIP. See `scripts/README.md` for packaging details.

## Package verification

The app entry points and renderer sources were compared against the packaged 1.0.35 application before this source ZIP was created. `SOURCE-MANIFEST.json` records SHA-256 hashes of every included source/asset file.

This is the source package, not an installer. Extract it before working with the files.
