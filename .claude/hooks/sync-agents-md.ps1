# PostToolUse hook: after an Edit or Write to CLAUDE.md or anything under .claude/ (rules, agents, skills),
# regenerate Codex's AGENTS.md files so they never drift from the source. Registered in .claude/settings.json.
# Edits made through a shell aren't seen here; /ship-feature step 8 still runs the sync by hand.

$ErrorActionPreference = 'Stop'
try { $in = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { exit 0 }

$path = [string]($in.tool_input.file_path ?? $in.tool_input.notebook_path)
if (-not $path) { exit 0 }
$project = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { [string]$in.cwd }
if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $project $path }
$rel = [IO.Path]::GetRelativePath($project, $path).Replace('\', '/')

if ($rel -ne 'CLAUDE.md' -and -not ($rel -like '.claude/rules/*' -or $rel -like '.claude/agents/*' -or $rel -like '.claude/skills/*')) { exit 0 }

$sync = Join-Path $env:USERPROFILE '.claude\hooks\sync-codex-instructions.ps1'
if (-not (Test-Path -LiteralPath $sync)) { exit 0 }

$out = & pwsh -NoProfile -File $sync -Project $project 2>&1
$changed = @($out | Where-Object { "$_" -match '^(wrote|removed stale|WARNING)' })
if ($changed.Count) {
    # Shown to Claude after the tool result, so it knows to commit the regenerated files with the change.
    @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'; additionalContext = "AGENTS.md re-synced for Codex after editing $rel. Commit these with the change:`n" + ($changed -join "`n") } } | ConvertTo-Json -Compress
}
exit 0
