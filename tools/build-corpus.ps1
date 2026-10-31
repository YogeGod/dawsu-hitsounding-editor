# Copies every .osu and .osb file from the local osu!lazer library into corpus/ for the round-trip tests.
# Lazer stores the files as downloaded, named by hash, so the content type is detected from the first line.
# corpus/ is git-ignored: the maps belong to their mappers.

param([string]$Source = "$env:APPDATA\osu\files")

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
"Copied $nOsu .osu and $nOsb .osb files to $dst"
