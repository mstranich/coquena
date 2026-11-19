# Background chat requests to a demo preset, so the Live charts have activity.
# The default targets studio/chat, the 12B reasoning model. Each request sends a long,
# different excerpt of the user guide with a demanding task: a long prompt shows the
# prompt phase, the task keeps the generation running, and a unique opening avoids
# reusing the prompt cache between requests.
param([int]$Count = 40, [string]$Model = 'studio/chat', [int]$Port = 9931,
      [int]$MaxTokens = 3000, [int]$PromptChars = 24000, [int]$Delay = 5)
# Parallel requests share the preset's ctx-size: keep jobs × (prompt + MaxTokens) under it.
# With ctx-size 32768, three jobs of about 7k prompt tokens and 3k output tokens (reasoning included) fit.

# Start-Job runs the file without $PSScriptRoot, in the current folder: the repository root.
$guideDir = if ($PSScriptRoot) { Join-Path $PSScriptRoot '..\..\user-guide' } else { Join-Path $PWD 'docs\user-guide' }
$guide = (Get-ChildItem $guideDir -Filter *.md |
    Sort-Object Name | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n`n"

# Wait for the marker that tui-live.tape writes just before opening the TUI.
$ready = Join-Path $PWD 'docs\media\demo\out\tui-ready'
while (-not (Test-Path $ready)) { Start-Sleep -Milliseconds 200 }
Start-Sleep $Delay

1..$Count | ForEach-Object {
    $start = Get-Random -Maximum ([Math]::Max(1, $guide.Length - $PromptChars))
    $excerpt = $guide.Substring($start, [Math]::Min($PromptChars, $guide.Length - $start))
    $content = "Review $([guid]::NewGuid()).`n`n" +
        "Below is an excerpt of a command-line tool's manual. Write a thorough technical review: " +
        "list every command and option it mentions and explain what each does, point out " +
        "ambiguities or missing steps, and propose a rewritten, clearer version of the hardest " +
        "section.`n`n---`n$excerpt"
    $body = @{
        model = $Model
        messages = @(@{ role = 'user'; content = $content })
        max_tokens = $MaxTokens
    } | ConvertTo-Json -Depth 4
    try { Invoke-RestMethod "http://127.0.0.1:$Port/v1/chat/completions" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 600 | Out-Null } catch {}
}
