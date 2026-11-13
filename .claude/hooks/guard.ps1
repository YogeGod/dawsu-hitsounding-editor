# PreToolUse guard for dawsu!. Registered in .claude/settings.json.
# Exit 2 blocks the tool call and shows stderr to the agent. Exit 0 allows it.
#
# Rules, for every session and agent:
#   1. Nothing writes into F:\osu!\Songs (copy maps out, never in).
#   2. Nothing deletes, overwrites or links the main checkout's corpus/.
# Rules for one agent:
#   3. format-guardian and scope-reviewer are read-only: no file tools, no writing shell commands.
#   4. dawsu-ui-tweak edits only src/OsuHitsound.App/ and tools/UiLayoutCheck/, and never commits.
#
# These are heuristics on the command text, not a sandbox. They catch mistakes, not intent.

$ErrorActionPreference = 'Stop'
try { $in = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { exit 0 }

$tool  = [string]$in.tool_name
$agent = [string]$in.agent_type
$cwd   = [string]$in.cwd
$ti    = $in.tool_input

function Block([string]$why) {
    [Console]::Error.WriteLine("Blocked by .claude/hooks/guard.ps1: $why")
    exit 2
}

# Lower-case, backslashes, and Git Bash drive paths (/e/...) turned into e:\...
function Norm([string]$s) {
    if (-not $s) { return '' }
    $s = $s.ToLowerInvariant().Replace('/', '\')
    return [regex]::Replace($s, '(^|[\s"''=(])\\([a-z])\\', '$1$2:\')
}

$songs      = 'f:\osu!\songs'
$mainRepo   = 'e:\github\dawsu-hitsounding-editor'
$mainCorpus = "$mainRepo\corpus"
$inMainRepo = (Norm $cwd).TrimEnd('\') -eq $mainRepo

$fileTools = @('Edit', 'Write', 'NotebookEdit', 'MultiEdit')
$shells    = @('Bash', 'PowerShell')

if ($fileTools -contains $tool) {
    $path = Norm ([string]($ti.file_path ?? $ti.notebook_path))
    if ($path -and -not [IO.Path]::IsPathRooted($path) -and $cwd) { $path = Norm (Join-Path $cwd $path) }

    if ($path.StartsWith($songs)) { Block "never write into $songs. Work on a copy of the map." }
    if ($path.StartsWith($mainCorpus)) { Block "the main corpus/ is test input; don't edit it. Rebuild it with tools/build-corpus.ps1 if needed." }

    if ($agent -in @('format-guardian', 'scope-reviewer')) { Block "$agent is a read-only reviewer. Report the fix instead of making it." }

    if ($agent -eq 'dawsu-ui-tweak') {
        $temp = Norm $env:TEMP
        $ok = $path -match '\\src\\osuhitsound\.app\\' -or $path -match '\\tools\\uilayoutcheck\\' -or ($temp -and $path.StartsWith($temp))
        if (-not $ok) { Block "dawsu-ui-tweak may edit only src/OsuHitsound.App/ and tools/UiLayoutCheck/. Hand this back to the main session." }
    }
    exit 0
}

if ($shells -contains $tool) {
    $cmd = Norm ([string]$ti.command)
    $songsRe = [regex]::Escape($songs)

    # Rule 1: writes into Songs. Reading or copying out of Songs is fine.
    if ($cmd -match $songsRe) {
        if ($cmd -match '(remove-item|\brm\b|\bdel\b|\brmdir\b|\brd\b|move-item|\bmv\b|\bmove\b|rename-item|\bren\b|set-content|add-content|clear-content|out-file|new-item|\btee\b|sed\s+-i|\bmklink\b)') {
            Block "this command changes files under $songs. Copy the map out and work on the copy."
        }
        # A copy is fine when Songs is only in its first argument (the source).
        foreach ($m in [regex]::Matches($cmd, '(copy-item|\bcp\b|\bcopy\b|robocopy|xcopy)\s+((?:-(?:path|literalpath|r|recurse|force|f)\s+)*)("[^"]*"|''[^'']*''|\S+)(?<rest>[^;|&\r\n]*)')) {
            if ($m.Groups['rest'].Value -match $songsRe) { Block "this copies into $songs. Copy maps out of Songs, never in." }
        }
        if ($cmd -match "[^2&]>{1,2}\s*[""']?$songsRe") { Block "this redirects output into $songs." }
    }

    # Rule 2: the main corpus. A worktree's own corpus/ copy may be deleted normally.
    $corpusRe = [regex]::Escape($mainCorpus)
    $touchesMainCorpus = $cmd -match $corpusRe -or ($inMainRepo -and $cmd -match '(^|[\s"''\\.])corpus(\\|\s|"|''|$)')
    if ($touchesMainCorpus) {
        if ($cmd -match '(remove-item|\brm\b|\bdel\b|\brmdir\b|\brd\b|move-item|\bmv\b|rename-item|clear-content|set-content|out-file|/mir\b|/purge\b|git\s+clean)') {
            Block "this could delete or overwrite the main corpus/ (it was emptied once on 2026-11-13). Work on a copy in a worktree."
        }
    }
    if ($cmd -match $corpusRe -and $cmd -match '(\bmklink\b|itemtype\s+(junction|symboliclink)|\bln\s+-s)') {
        Block "never link the main corpus/ into a worktree. Copy it (verify-dawsu step 0)."
    }

    # Rule 3: read-only reviewers. dotnet build/test may write bin/ and obj/; that's allowed.
    if ($agent -in @('format-guardian', 'scope-reviewer')) {
        $writes = '(set-content|add-content|clear-content|out-file|new-item|remove-item|move-item|rename-item|copy-item|\brm\b|\bmv\b|\bcp\b|\bdel\b|\btee\b|sed\s+-i|\btouch\b|\bmkdir\b|robocopy|dotnet\s+(format|new|add|remove)|git\s+(commit|push|checkout|switch|reset|restore|merge|rebase|add|rm|mv|clean|apply|cherry-pick|revert|am|worktree\s+(add|remove|prune))|git\s+stash(?!\s+(list|show))|git\s+tag\s+(?!(-l|-n\d*|--list|--contains|--points-at|--sort)\b)[^\s|;&]|git\s+branch\s+-[dDmM]|gh\s+(pr|issue|release)\s+(create|edit|merge|close|comment|delete))'
        if ($cmd -match $writes) { Block "$agent is a read-only reviewer; this command changes files or git state. Report it instead." }
        # Output redirection to a file. 2>$null, 2>&1 and >$null are fine, and so are => and -> in search patterns.
        if ($cmd -match '(^|[^2&>=\-])>{1,2}\s*(?!\$null|&|nul\b|/dev/null|\\dev\\null)\S') { Block "$agent is a read-only reviewer; don't redirect output into files." }
    }

    # Rule 4: the UI tweak agent never commits or pushes, and writes files only with Edit/Write (which are
    # limited to its folders above). Its shell is for building, checking and launching. Commands that name the
    # temp folder may write, delete and redirect there (map copies, screenshots, check output).
    if ($agent -eq 'dawsu-ui-tweak') {
        if ($cmd -match 'git\s+(commit|push|merge|rebase|reset|tag)') {
            Block "dawsu-ui-tweak doesn't commit or push. The main session does."
        }
        if ($cmd -match '(set-content|add-content|clear-content|out-file|\btee\b|sed\s+-i|dotnet\s+(format|new|add|remove)|git\s+(checkout|switch|restore|add|rm|mv|clean|apply|cherry-pick|revert|am)|git\s+stash(?!\s+(list|show)))') {
            Block "dawsu-ui-tweak changes files only with Edit and Write, inside its folders. Don't write files or change git state through the shell."
        }
        $temp = Norm $env:TEMP
        $inTemp = $cmd -match '\$env:te?mp\b' -or ($temp -and $cmd.Contains($temp))
        if (-not $inTemp) {
            if ($cmd -match '(remove-item|move-item|rename-item|\brm\b|\bmv\b|\bdel\b|\bren\b)') {
                Block "dawsu-ui-tweak may delete or move files only in the temp folder. Hand this back to the main session."
            }
            if ($cmd -match '(^|[^2&>=\-])>{1,2}\s*(?!\$null|&|nul\b|/dev/null|\\dev\\null)\S') {
                Block "dawsu-ui-tweak may redirect output only into the temp folder."
            }
        }
    }
}

exit 0
