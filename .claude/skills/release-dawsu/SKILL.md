---
name: release-dawsu
description: Build and publish a dawsu! release (version bump, release commit, self-contained publish, Velopack pack, checksums, tag and GitHub release) the way V3.1.1 and V3.1.2 were shipped. Run only when the user types /release-dawsu.
disable-model-invocation: true
---

# Release dawsu!

Usage: `/release-dawsu <X.Y.Z>`

Installed copies update through Velopack, which reads the **latest** GitHub release's `releases.win.json`. A release that is wrong reaches every user, and re-uploading the same version never reaches anyone who already updated. So: the checks pass first, and nothing leaves this PC before the user's OK.

**Gate** means: stop and wait for the user's reply in chat.

## 0. Before you start
- On `main`, clean tree, up to date with `origin/main`. Release from the main checkout, not a worktree.
- Run the `verify-dawsu` skill. Any failure or skipped corpus test stops the release.
- `CHANGELOG.md` has an **Unreleased** section describing what users will notice. If it's empty, ask what this release is for.
- Drive C: is often nearly full. Do all build work under the git-ignored `artifacts/release-X.Y.Z/` on E:, and point the temp folders there. NuGet packages come from the shared cache named by the user-level `NUGET_PACKAGES` variable, so a release doesn't download them again:

```powershell
$v = 'X.Y.Z'; $R = "E:\GitHub\dawsu-hitsounding-editor\artifacts\release-$v"
New-Item -ItemType Directory -Force "$R\tmp","$R\clihome","$R\tools","$R\pub","$R\out","$R\native" | Out-Null
$env:TEMP = "$R\tmp"; $env:TMP = "$R\tmp"; $env:DOTNET_CLI_HOME = "$R\clihome"
# Read from the user settings: a shell opened before the variable was set doesn't have it.
# Never set on this PC: use a per-release folder, which keeps packages off C:.
$env:NUGET_PACKAGES = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'User') ?? "$R\nuget"
```

## 1. Version bump and release commit
1. `<Version>` in `src/OsuHitsound.App/OsuHitsound.App.csproj`.
2. `CHANGELOG.md`: turn **Unreleased** into `## X.Y.Z`. Write `docs/releases/vX.Y.Z.md` in the style of the previous one (a `# dawsu! VX.Y.Z` title, one summary line, then bullets, then the **Windows x64** install line with the new version).
3. The current-version line in `README.md`, `docs/TUTORIAL.md` and the first paragraph of `docs/project-context.md`.
4. **Gate:** show the diff and the release notes. After the OK, commit `Release dawsu! VX.Y.Z` **before building**, so the build stamp is the release commit. Don't push yet.

## 2. Native VST3 files
The package ships `dawsu_vst3.dll`, `dawsu_vst3_scan.exe` and `dawsu_vst3_host.exe`. Check `git log V<previous>..HEAD -- native`.
- **No changes:** take all three from the previous release's full package, `lib/app/` inside `dawsu-<previous>-full.nupkg` (it's a zip). Download it with `gh release download V<previous> -p "*-full.nupkg" -D "$R\native"` and extract.
- **Changes:** rebuild with `native/VstHost/README.md` (CMake, JUCE pinned) and `native/Vst3IsolatedHost/build.ps1`, and tell the user the new native code isn't proven with a real VST until they try one.

## 3. Publish
```powershell
dotnet publish src/OsuHitsound.App -c Release -r win-x64 --self-contained true `
  -p:Vst3HostDll="$R\native\dawsu_vst3.dll" -p:Vst3IsolatedHostExe="$R\native\dawsu_vst3_host.exe" -o "$R\pub"
Remove-Item "$R\pub\*.pdb", "$R\pub\createdump.exe" -ErrorAction SilentlyContinue
```
The app comes out as `dawsu!.exe`. Check that the three native files are in `$R\pub`.

## 4. Pack
`vpk` must match the Velopack package version in the csproj (1.2.161 as of 3.1.2; read the `Velopack` PackageReference):
```powershell
dotnet tool install vpk --version <velopack version> --tool-path "$R\tools"
& "$R\tools\vpk.exe" pack --packId dawsu --packVersion $v --packDir "$R\pub" --mainExe "dawsu!.exe" `
  --packTitle "dawsu!" --packAuthors dawsu --icon src/OsuHitsound.App/Assets/dawsu.ico `
  --releaseNotes "docs/releases/v$v.md" --channel win --outputDir "$R\out"
```
No delta packages, no code signing. Expect six files: `assets.win.json`, `dawsu-X.Y.Z-full.nupkg`, `dawsu-win-Portable.zip`, `dawsu-win-Setup.exe`, `RELEASES` and `releases.win.json`. Compare their names and rough sizes with the previous release (`gh release view V<previous> --json assets`); a big size change needs a reason.

Then `SHA256SUMS.txt` over those six, in `sha256sum` format (hash, two spaces, name). Git Bash's `sha256sum` writes `hash *name` unless given `-t`:
```bash
cd "$R/out" && sha256sum -t assets.win.json dawsu-*-full.nupkg dawsu-win-Portable.zip dawsu-win-Setup.exe RELEASES releases.win.json > SHA256SUMS.txt
```

## 5. Smoke test
Extract `dawsu-win-Portable.zip` into `$R\portable` and start `dawsu!.exe` from there with a **copy** of a map (never one in `F:\osu!\Songs`). Confirm it opens and shows the new version (the start screen, with no map, shows `hitsound studio X.Y.Z`). To screenshot it, capture the window with `PrintWindow`: a screen copy catches whatever window is in front, which may be the user's own. Say plainly that sound, VST loading and updating from the previous version weren't checked unless they were.

## 6. Publish
**Gate:** list what will go out (commit, tag, the seven files, the notes) and wait for an explicit OK to publish. Then:
```powershell
git push origin main
git tag -a "V$v" -m "dawsu! V$v"; git push origin "V$v"
gh release create "V$v" --title "dawsu! V$v" --notes-file "docs/releases/v$v.md" --latest (Get-ChildItem "$R\out" -File).FullName
```

## 7. Check it reached users
- `gh api repos/{owner}/{repo}/releases/latest --jq .tag_name` prints `VX.Y.Z`.
- The release has seven assets, and `releases.win.json` on it names X.Y.Z.
- Report what was published, the checksums file, and anything not verified.

## Afterwards
- `artifacts/release-X.Y.Z/` is git-ignored. Tell the user it can be deleted once they've tried the update; keep the `out/` folder until then.
- If a step here turned out wrong, fix this file in the same session.
