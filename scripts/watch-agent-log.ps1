# Real-time agent timeline viewer (Unicode-decoded, with context).
# Usage:
#   .\scripts\watch-agent-log.ps1              # multi-line context (default)
#   .\scripts\watch-agent-log.ps1 -Compact     # one-line summary
#   .\scripts\watch-agent-log.ps1 -Raw         # full pretty JSON
#   .\scripts\watch-agent-log.ps1 -Path '...\agent-timeline-xxx.jsonl'

param(
    [string]$Path,
    [switch]$Raw,
    [switch]$Compact,
    [int]$Tail = 8,
    [int]$InputChars = 600,
    [int]$ResultChars = 500
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [Console]::OutputEncoding
try { chcp 65001 | Out-Null } catch { }

function Find-LatestTimeline {
    $userData = $env:CSII_USERDATAPATH
    if ([string]::IsNullOrWhiteSpace($userData)) {
        $userData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
    }
    $root = Join-Path $userData 'Mods\CitiesSkylines2Agent\logs'
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem -LiteralPath $root -Filter 'agent-timeline-*.jsonl' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }
}

function Expand-JsonBlob([object]$value) {
    if ($null -eq $value) { return '' }
    $cur = [string]$value
    for ($i = 0; $i -lt 5; $i++) {
        if ([string]::IsNullOrWhiteSpace($cur)) { return $cur }
        $trimmed = $cur.Trim()
        $isObj = $trimmed.StartsWith([char]0x7B)   # {
        $isArr = $trimmed.StartsWith([char]0x5B)   # [
        $isStr = $trimmed.StartsWith([char]0x22) -and $trimmed.EndsWith([char]0x22)  # "
        if (-not ($isObj -or $isArr -or $isStr)) { return $cur }
        try {
            $parsed = $cur | ConvertFrom-Json
        } catch {
            return $cur
        }
        if ($parsed -is [string]) {
            $cur = $parsed
            continue
        }
        if ($parsed -is [System.Management.Automation.PSCustomObject]) {
            if ($null -ne $parsed.PSObject.Properties['error']) {
                $cur = [string]$parsed.error
                continue
            }
            if ($null -ne $parsed.PSObject.Properties['message']) {
                return [string]$parsed.message
            }
            return ($parsed | ConvertTo-Json -Compress -Depth 8)
        }
        return ($parsed | ConvertTo-Json -Compress -Depth 8)
    }
    return $cur
}

function Limit-Text([string]$text, [int]$max) {
    if ([string]::IsNullOrEmpty($text)) { return '' }
    $t = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    if ($t.Length -le $max) { return $t }
    return '...' + $t.Substring($t.Length - $max)
}

function Format-Jsonish([object]$value, [int]$maxChars) {
    if ($null -eq $value) { return '' }
    if ($value -is [string]) {
        $trimmed = $value.Trim()
        $isObj = $trimmed.StartsWith([char]0x7B)
        $isArr = $trimmed.StartsWith([char]0x5B)
        if ($isObj -or $isArr) {
            try {
                $text = ($value | ConvertFrom-Json | ConvertTo-Json -Depth 8)
            } catch {
                $text = $value
            }
        } else {
            $text = Expand-JsonBlob $value
        }
    } else {
        $text = ($value | ConvertTo-Json -Depth 8)
    }
    return (Limit-Text $text $maxChars)
}

function Write-ContextBlock {
    param(
        [string]$Title,
        [string]$Body,
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    if ([string]::IsNullOrWhiteSpace($Body)) { return }
    Write-Host ("  " + $Title) -ForegroundColor DarkCyan
    foreach ($line in ($Body -split "`n")) {
        Write-Host ("    " + $line) -ForegroundColor $Color
    }
}

function Format-EventCompact([pscustomobject]$e, [string]$head) {
    $d = $e.data
    switch ($e.type) {
        'function' {
            $ok = if ($d.success) { 'ok' } else { 'FAIL' }
            $err = if ($d.error) { ' | ' + (Expand-JsonBlob $d.error) } else { '' }
            return ($head + ' ' + $d.tool + ' [' + $ok + ' ' + $d.elapsedMs + 'ms]' + $err)
        }
        'generation' {
            $names = @($d.toolCalls | ForEach-Object { $_.name }) -join ','
            return ($head + ' ' + $d.model + ' tools=' + $names + ' (' + $d.elapsedMs + 'ms)')
        }
        'turn.start' {
            return ($head + ' user=' + (Limit-Text ([string]$d.user) 120))
        }
        default {
            return $head
        }
    }
}

function Show-Event([pscustomobject]$e) {
    $ts = if ($e.ts) { ([datetime]$e.ts).ToLocalTime().ToString('HH:mm:ss') } else { '--:--:--' }
    $d = $e.data
    $head = '[' + $ts + '] #' + $e.seq + ' ' + $e.type

    if ($Compact) {
        Write-Host (Format-EventCompact $e $head)
        return
    }

    switch ($e.type) {
        'function' {
            $ok = if ($d.success) { 'ok' } else { 'FAIL' }
            $color = if ($d.success) { [ConsoleColor]::Green } else { [ConsoleColor]::Red }
            Write-Host ($head + ' ' + $d.tool + ' [' + $ok + ' ' + $d.elapsedMs + 'ms]') -ForegroundColor $color
            Write-ContextBlock -Title 'args' -Body (Format-Jsonish $d.arguments $ResultChars) -Color White
            if ($d.error) {
                Write-ContextBlock -Title 'error' -Body (Expand-JsonBlob $d.error) -Color Yellow
            }
            if ($d.result) {
                Write-ContextBlock -Title 'result' -Body (Format-Jsonish $d.result $ResultChars) -Color Gray
            }
        }
        'generation' {
            Write-Host ($head + ' ' + $d.model + ' in=' + $d.usage.input + ' out=' + $d.usage.output + ' (' + $d.elapsedMs + 'ms)') -ForegroundColor Cyan
            if ($d.reasoning) {
                Write-ContextBlock -Title 'reasoning' -Body (Limit-Text ([string]$d.reasoning) $InputChars) -Color DarkYellow
            }
            $inputText = [string]$d.input
            $assistantIdx = $inputText.LastIndexOf('assistant:')
            if ($assistantIdx -ge 0) {
                $snippet = Limit-Text $inputText.Substring($assistantIdx) $InputChars
            } else {
                $snippet = Limit-Text $inputText $InputChars
            }
            Write-ContextBlock -Title 'context (tail)' -Body $snippet -Color White
            $calls = @($d.toolCalls)
            if ($calls.Count -gt 0) {
                $callText = ($calls | ForEach-Object {
                        $_.name + ' ' + (Format-Jsonish $_.arguments 240)
                    }) -join "`n"
                Write-ContextBlock -Title 'toolCalls' -Body $callText -Color White
            }
        }
        'turn.start' {
            Write-Host $head -ForegroundColor Blue
            Write-ContextBlock -Title 'user' -Body ([string]$d.user) -Color White
        }
        'turn.finish' {
            Write-Host ($head + ' gens=' + $d.generations + ' fns=' + $d.functions + ' (' + $d.elapsedMs + 'ms)') -ForegroundColor DarkBlue
        }
        'interleaved_input' {
            Write-Host ($head + ' state=' + $d.state) -ForegroundColor Blue
            Write-ContextBlock -Title 'text' -Body ([string]$d.text) -Color White
        }
        'error' {
            Write-Host $head -ForegroundColor Red
            Write-ContextBlock -Title 'message' -Body (Expand-JsonBlob $d.message) -Color Yellow
        }
        default {
            Write-Host $head
            if ($d) {
                Write-ContextBlock -Title 'data' -Body ($d | ConvertTo-Json -Depth 8) -Color Gray
            }
        }
    }
    Write-Host ''
}

if (-not $Path) {
    $latest = Find-LatestTimeline
    if (-not $latest) {
        Write-Error 'No agent-timeline-*.jsonl found under the mod logs directory (CSII_USERDATAPATH or LocalLow).'
    }
    $Path = $latest.FullName
}

$mode = if ($Raw) { 'raw JSON' } elseif ($Compact) { 'compact' } else { 'context' }
Write-Host ('Watching: ' + $Path) -ForegroundColor Cyan
Write-Host ('Ctrl+C to stop. Mode: ' + $mode) -ForegroundColor DarkGray
Write-Host ''

Get-Content -LiteralPath $Path -Tail $Tail -Wait -Encoding utf8 | ForEach-Object {
    $line = $_.Trim()
    if (-not $line) { return }
    try {
        $obj = $line | ConvertFrom-Json
        if ($Raw) {
            $obj | ConvertTo-Json -Depth 30
            Write-Host ('-' * 60) -ForegroundColor DarkGray
        } else {
            Show-Event $obj
        }
    } catch {
        Write-Host ('parse error: ' + $_.Exception.Message) -ForegroundColor Yellow
    }
}
