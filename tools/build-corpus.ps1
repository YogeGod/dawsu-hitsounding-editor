# Copies every .osu and .osb file from the local osu!lazer library into corpus/ for the round-trip tests.
# Lazer stores the files as downloaded, named by hash, so the content type is detected from the first line.
# corpus/ is git-ignored: the maps belong to their mappers.
# -Archives also reads beatmap packs (.zip of .osz) and loose .osz files from a folder, taking only their .osu and .osb
# files. They are named pack-<pack>-<hash> so repeated runs don't duplicate them. Nothing in an archive is run.

param([string]$Source = "$env:APPDATA\osu\files", [string]$Archives)

$dst = Join-Path $PSScriptRoot "..\corpus"
New-Item -ItemType Directory -Force $dst | Out-Null
$nOsu = 0; $nOsb = 0
foreach ($x in Get-ChildItem $Source -Recurse -File | Where-Object Length -lt 8MB) {
    $s = [IO.File]::OpenRead($x.FullName); $b = New-Object byte[] 200; $n = $s.Read($b, 0, 200); $s.Close()
    if ($n -lt 8 -or ($b[0..($n - 1)] -contains 0)) { continue }  # binary file (also skips rare UTF-16 maps)
    $t = [Text.Encoding]::UTF8.GetString($b, 0, $n).TrimStart([char]0xFEFF).TrimStart()
    if ($t -match '^osu file format v\d+') { Copy-Item $x.FullName "$dst\$($x.Name).osu"; $nOsu++ }
    elseif ($t -match '^\[(Events|Variables)\]' -or ($t -match '^//' -and $t -match '\[(Events|Variables)\]')) { Copy-Item $x.FullName "$dst\$($x.Name).osb"; $nOsb++ }
}

if ($Archives) {
    Add-Type -AssemblyName System.IO.Compression
    $sha = [Security.Cryptography.SHA1]::Create()
    # Copies the .osu/.osb entries of one zip; .osz entries inside a pack are opened in memory, one level deep.
    function Read-Zip([IO.Stream]$stream, [string]$tag, [bool]$nested) {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read)
        foreach ($e in $zip.Entries) {
            $ext = [IO.Path]::GetExtension($e.Name).ToLowerInvariant()
            if ($ext -eq '.osz' -and -not $nested) {
                $ms = New-Object IO.MemoryStream; $es = $e.Open(); $es.CopyTo($ms); $es.Close(); $ms.Position = 0
                Read-Zip $ms $tag $true; $ms.Dispose()
            }
            elseif ($ext -in '.osu', '.osb' -and $e.Length -lt 8MB) {
                $ms = New-Object IO.MemoryStream; $es = $e.Open(); $es.CopyTo($ms); $es.Close()
                $bytes = $ms.ToArray(); $ms.Dispose()
                $hash = -join ($sha.ComputeHash($bytes)[0..7] | ForEach-Object { $_.ToString('x2') })
                $out = Join-Path $dst "pack-$tag-$hash$ext"
                if (-not (Test-Path -LiteralPath $out)) {
                    [IO.File]::WriteAllBytes($out, $bytes)
                    if ($ext -eq '.osu') { $script:nOsu++ } else { $script:nOsb++ }
                }
            }
        }
        $zip.Dispose()
    }
    foreach ($a in Get-ChildItem -LiteralPath $Archives -File | Where-Object { $_.Extension -in '.zip', '.osz' }) {
        $tag = ($a.BaseName -split ' ')[0] -replace '[^A-Za-z0-9]', ''
        $fs = [IO.File]::OpenRead($a.FullName)
        try { Read-Zip $fs $tag $false } catch { Write-Warning "Skipped $($a.Name): $($_.Exception.Message)" } finally { $fs.Dispose() }
    }
}
"Copied $nOsu .osu and $nOsb .osb files to $dst"
