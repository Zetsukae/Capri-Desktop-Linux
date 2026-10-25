# Runs a fixed list of messages through your local Ollama model with her persona, so a change to her personality can be judged by what she
# actually says. It talks only to localhost, the same way the app does (think off, 220 tokens, temperature 0.85).
#
#   powershell -ExecutionPolicy Bypass -File tools\persona-test.ps1
#   powershell -ExecutionPolicy Bypass -File tools\persona-test.ps1 -Model qwen3.5:9b -Persona assets\persona.txt -Runs 2
#
# Replies are flagged when they are long, or sound like an assistant or a poet. The totals at the end show if she leans on one word too much.

param(
    [string]$Model = 'qwen3.5:9b',
    [string]$Persona = (Join-Path $PSScriptRoot '..\assets\persona.txt'),
    [string]$Prompts = (Join-Path $PSScriptRoot 'persona-prompts.txt'),
    [string]$Server = 'http://localhost:11434',
    [int]$Runs = 1
)

if (([Uri]$Server).Host -notin 'localhost', '127.0.0.1', '[::1]') { throw 'Only a server on this computer is allowed.' }
$system = [IO.File]::ReadAllText((Resolve-Path $Persona), [Text.Encoding]::UTF8).Trim()
$lines = Get-Content $Prompts -Encoding UTF8 | Where-Object { $_.Trim() }
$bad = 'as an ai', 'language model', "i'm here to", 'how can i', 'happy to help', 'feel free', 'let me know', 'certainly', 'of course', 'sure!', 'assist', 'remember,', 'it sounds like', 'i understand'
$emoji = '[' + [char]0x2600 + '-' + [char]0x27BF + ']|[' + [char]0xD83C + '-' + [char]0xDBFF + ']'
$flagged = 0
$total = 0
$choom = 0
$cmon = 0
$asks = 0

foreach ($line in $lines) {
    for ($run = 1; $run -le $Runs; $run++) {
        $body = @{
            model    = $Model
            stream   = $false
            think    = $false
            options  = @{ num_predict = 220; temperature = 0.85 }
            messages = @(@{ role = 'system'; content = [string]$system }, @{ role = 'user'; content = [string]$line })
        } | ConvertTo-Json -Depth 5
        try {
            $reply = Invoke-RestMethod -Uri "$Server/api/chat" -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json' -TimeoutSec 120
        } catch {
            $detail = ''
            if ($_.ErrorDetails) { $detail = $_.ErrorDetails.Message }
            Write-Host "FAILED: $line  ($($_.Exception.Message)) $detail" -ForegroundColor Red
            continue
        }
        $text = ($reply.message.content -replace '<think>.*?</think>', '' -replace '\s+', ' ').Trim()
        $words = ($text -split ' ').Count
        $notes = @()
        if ($words -gt 25) { $notes += "long ($words words)" }
        if ($text -match '\*') { $notes += 'action text' }
        if ($text -match $emoji) { $notes += 'emoji' }
        if ($text -match '\b(like|as) (a|an|the) \w+') { $notes += 'maybe a simile' }
        foreach ($phrase in $bad) { if ($text.ToLower().Contains($phrase)) { $notes += "assistant-ish: $phrase" } }
        $total++
        if ($text -match '(?i)\bchoom\b') { $choom++ }
        if ($text -match "(?i)\bc'mon\b") { $cmon++ }
        if ($text.EndsWith('?')) { $asks++ }
        if ($notes.Count -gt 0) { $flagged++ }
        Write-Host ("YOU    " + $line)
        Write-Host ("CAPRI  " + $text) -ForegroundColor Cyan
        if ($notes.Count -gt 0) { Write-Host ("       !! " + ($notes -join '; ')) -ForegroundColor Yellow }
        Write-Host ''
    }
}
Write-Host "choom in $choom, c'mon in $cmon, ending on a question in $asks of $total replies (aim for about a quarter of each)."
Write-Host "$flagged of $total replies flagged." -ForegroundColor $(if ($flagged -eq 0) { 'Green' } else { 'Yellow' })
