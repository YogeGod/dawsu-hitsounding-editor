# Table test for .claude/hooks/guard.ps1. Feeds hook inputs straight into the script and checks the exit code
# (2 = blocked, 0 = allowed). Nothing here touches a real file: the guard only reads the command text.
# Run after any change to the guard:  pwsh -NoProfile -File tools/guard-test.ps1   (exit 1 if a case fails)
$guard = Join-Path $PSScriptRoot '..\.claude\hooks\guard.ps1'
# The guard protects this exact checkout path, so the cases use it whichever checkout runs the test.
$main = 'E:\GitHub\dawsu-hitsounding-editor'
$wt = 'E:\GitHub\dawsu-wt-x'
# expect, tool, agent, cwd, input
$cases = @(
  @(2,'Write','', $main, @{file_path='F:\osu!\Songs\1 a\x.osu'}),
  @(2,'Edit','', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\corpus\a.osu'}),
  @(0,'Edit','', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\src\OsuHitsound.Format\A.cs'}),
  @(0,'Edit','', $wt, @{file_path='E:\GitHub\dawsu-wt-x\corpus\a.osu'}),
  @(2,'Bash','', $main, @{command='rm -f "/f/osu!/Songs/x.osu"'}),
  @(2,'PowerShell','', $main, @{command='Copy-Item C:\tmp\a.osu "F:\osu!\Songs\1 a\"'}),
  @(0,'PowerShell','', $main, @{command='Copy-Item "F:\osu!\Songs\1 a\x.osu" C:\tmp\'}),
  @(0,'Bash','', $main, @{command='ls "/f/osu!/Songs"'}),
  @(2,'Bash','', $main, @{command='echo hi > "/f/osu!/Songs/x.txt"'}),
  @(2,'PowerShell','', $main, @{command='Remove-Item -Recurse corpus'}),
  @(2,'Bash','', $wt, @{command='rm -rf /e/GitHub/dawsu-hitsounding-editor/corpus'}),
  @(0,'PowerShell','', $wt, @{command='Remove-Item -Recurse corpus'}),
  @(0,'PowerShell','', $wt, @{command='robocopy E:\GitHub\dawsu-hitsounding-editor\corpus corpus /E /NFL /NDL /NJH /NJS'}),
  @(2,'PowerShell','', $wt, @{command='New-Item -ItemType Junction -Path corpus -Target E:\GitHub\dawsu-hitsounding-editor\corpus'}),
  @(0,'PowerShell','', $main, @{command='dotnet test tests/OsuHitsound.Format.Tests'}),
  @(2,'Edit','format-guardian', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\src\A.cs'}),
  @(0,'PowerShell','format-guardian', $main, @{command='git diff origin/main...HEAD; dotnet test tests/OsuHitsound.Lanes.Tests 2>&1 | Select-Object -Last 5'}),
  @(0,'PowerShell','format-guardian', $main, @{command='Get-Content docs\adr\0002.md 2>$null > $null'}),
  @(2,'PowerShell','format-guardian', $main, @{command='Set-Content src\A.cs "x"'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git diff > /tmp/d.txt'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git checkout -- .'}),
  @(0,'Bash','scope-reviewer', $main, @{command='git log --oneline -5 && git diff --stat'}),
  @(0,'Edit','dawsu-ui-tweak', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\src\OsuHitsound.App\MainWindow.axaml'}),
  @(2,'Edit','dawsu-ui-tweak', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\src\OsuHitsound.Format\A.cs'}),
  @(2,'Write','dawsu-ui-tweak', $main, @{file_path='E:\GitHub\dawsu-hitsounding-editor\CLAUDE.md'}),
  @(2,'PowerShell','dawsu-ui-tweak', $main, @{command='git commit -am x'}),
  @(0,'Read','format-guardian', $main, @{file_path='F:\osu!\Songs\x.osu'}),
  @(0,'Bash','scope-reviewer', $main, @{command='git tag -l "V3*" -n1'}),
  @(0,'Bash','scope-reviewer', $main, @{command='git tag'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git tag -a V9.9.9 -m x'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git tag V9.9.9'}),
  @(0,'Bash','scope-reviewer', $main, @{command='git stash list'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git stash'}),
  @(0,'Bash','scope-reviewer', $main, @{command='rg -n "=>" src | head'}),
  @(0,'Bash','scope-reviewer', $main, @{command='grep -rn "->" native'}),
  @(2,'Bash','scope-reviewer', $main, @{command='git log >log.txt'}),
  # dawsu-ui-tweak in the shell: its build, check and launch loop works; writing source files doesn't.
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='Get-Process dawsu*,OsuHitsoundEditor -ErrorAction SilentlyContinue | Stop-Process'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='dotnet build src/OsuHitsound.App -v q 2>&1 | Select-Object -Last 5'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='dotnet run --project tools/UiLayoutCheck -- $env:TEMP\layout'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='Copy-Item testmaps\a.osu $env:TEMP\tweak\a.osu; New-Item -ItemType Directory -Force $env:TEMP\tweak'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='Remove-Item -Recurse $env:TEMP\tweak'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='Start-Process src\OsuHitsound.App\bin\Debug\net10.0\dawsu!.exe'}),
  @(0,'PowerShell','dawsu-ui-tweak', $main, @{command='git diff --stat; git status --short'}),
  @(2,'PowerShell','dawsu-ui-tweak', $main, @{command='Set-Content src\OsuHitsound.Format\A.cs "x"'}),
  @(2,'PowerShell','dawsu-ui-tweak', $main, @{command='"x" | Out-File CLAUDE.md'}),
  @(2,'Bash','dawsu-ui-tweak', $main, @{command='sed -i s/a/b/ src/OsuHitsound.Lanes/A.cs'}),
  @(2,'Bash','dawsu-ui-tweak', $main, @{command='echo x > docs/design-system.md'}),
  @(2,'PowerShell','dawsu-ui-tweak', $main, @{command='Remove-Item src\OsuHitsound.Audio\A.cs'}),
  @(2,'Bash','dawsu-ui-tweak', $main, @{command='git checkout -- src/OsuHitsound.Format'}),
  @(2,'Bash','dawsu-ui-tweak', $main, @{command='git stash'})
)
$fail = 0; $i = 0
foreach ($c in $cases) {
  $i++
  $json = @{ tool_name=$c[1]; agent_type=$c[2]; cwd=$c[3]; tool_input=$c[4] } | ConvertTo-Json -Compress
  $err = $json | pwsh -NoProfile -File $guard 2>&1
  $code = $LASTEXITCODE
  $mark = if ($code -eq $c[0]) { 'ok  ' } else { $fail++; 'FAIL' }
  "{0} #{1} expect {2} got {3}  {4} {5}  {6}" -f $mark, $i, $c[0], $code, $c[1], $c[2], ($c[4].Values -join '')
}
"$($cases.Count - $fail)/$($cases.Count) passed"
if ($fail) { exit 1 }
