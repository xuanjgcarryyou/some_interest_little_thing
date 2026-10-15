# Processes the AI-generated art and voice in assets\<id>\raw into assets\<id>\build, which build.ps1 embeds.
# Voice clips are converted with ffmpeg in a throwaway Docker container (Docker Engine in WSL Ubuntu).
param(
    [string]$Id = 'xiaoshu',
    [string]$AssetsRoot = (Join-Path $PSScriptRoot 'assets')
)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

function ConvertTo-WslPath([string]$p) {
    $full = (Resolve-Path $p).Path
    '/mnt/' + $full.Substring(0, 1).ToLower() + $full.Substring(2).Replace('\', '/')
}

$toolBin = Join-Path $PSScriptRoot 'tools\bin'
New-Item -ItemType Directory -Force $toolBin | Out-Null
$builder = Join-Path $toolBin 'AssetBuilder.exe'
& $csc /nologo /optimize+ /out:$builder /r:System.Drawing.dll (Join-Path $PSScriptRoot 'tools\AssetBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw 'AssetBuilder build failed' }

$raw = Join-Path $AssetsRoot "$Id\raw"
$out = Join-Path $AssetsRoot "$Id\build"
if (-not (Test-Path $raw)) { throw "No raw assets at $raw" }
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

& $builder $raw $out
if ($LASTEXITCODE -ne 0) { throw 'Asset processing failed' }

$audioExt = '.wav', '.mp3', '.m4a', '.ogg', '.flac'
$audio = @(Get-ChildItem $raw -File | Where-Object { $_.Extension -in $audioExt })
$staging = Join-Path $AssetsRoot "$Id\voice-split"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null

# A single all.<ext> holding every line in script order is cut into per-line clips at its pauses.
$all = $audio | Where-Object { $_.BaseName -eq 'all' } | Select-Object -First 1
if ($all) {
    $log = wsl -d Ubuntu -- docker run --rm -v "$(ConvertTo-WslPath $raw):/in:ro" alpine:3.20 sh -c `
        "apk add --no-cache ffmpeg >/dev/null 2>&1 && ffmpeg -hide_banner -i '/in/$($all.Name)' -af silencedetect=noise=-40dB:d=0.12 -f null - 2>&1"
    if ($LASTEXITCODE -ne 0) { throw 'Silence detection failed (is Docker running in WSL?)' }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $m = [regex]::Match(($log -join "`n"), 'Duration: (\d+):(\d+):([\d.]+)')
    $duration = [int]$m.Groups[1].Value * 3600 + [int]$m.Groups[2].Value * 60 + [double]::Parse($m.Groups[3].Value, $inv)
    $starts = @([regex]::Matches(($log -join "`n"), 'silence_start: ([\d.]+)') | ForEach-Object { [double]::Parse($_.Groups[1].Value, $inv) })
    $ends = @([regex]::Matches(($log -join "`n"), 'silence_end: ([\d.]+)') | ForEach-Object { [double]::Parse($_.Groups[1].Value, $inv) })
    if ($ends.Count -lt $starts.Count) { $ends += $duration } # silence running to the end of the file

    # Speech segments sit between pauses of 0.45 s or more; shorter ones are commas and ellipses inside a line.
    $segments = @()
    $segStart = 0.0
    for ($i = 0; $i -lt $starts.Count; $i++) {
        $s = $starts[$i]; $e = $ends[$i]
        if ($s -le 0.01) { $segStart = $e; continue }                          # leading silence
        if ($e -ge $duration - 0.01 -or $e - $s -ge 0.45) {
            if ($s -gt $segStart) { $segments += , @($segStart, $s) }
            $segStart = $e
        }
    }
    if ($segStart -lt $duration - 0.05) { $segments += , @($segStart, $duration) }

    # The TTS also pauses at every sentence stop inside a line, so a line with n of them spans n+1 segments.
    # Punctuation is built from code points: this file must stay ASCII, because Windows PowerShell reads it
    # in the ANSI code page, where a UTF-8 multibyte char can even swallow the line break after it.
    $stops = -join ([char[]](0xFF01, 0xFF1F, 0x3002)) + '!?'   # fullwidth ! ? and the ideographic full stop
    $soft = -join ([char[]](0xFF5E, 0x2026, 0xFF0C, 0x3001))     # fullwidth tilde, ellipsis, comma, enumeration comma
    $lines = [regex]::Matches((Get-Content -Raw -Encoding utf8 (Join-Path $PSScriptRoot "src\Xiaoshu.cs")), '\{\s*"(\w+)",\s*"([^"]*)"\s*\}') |
        ForEach-Object {
            $text = $_.Groups[2].Value
            $body = $text -replace "[$stops$soft\s]+$", ''
            [pscustomobject]@{ Id = $_.Groups[1].Value; Text = $text; Pieces = ([regex]::Matches($body, "[$stops]")).Count + 1 }
        }
    $expected = ($lines | Measure-Object -Property Pieces -Sum).Sum
    if ($expected -ne $segments.Count) {
        throw "all.$($all.Extension.TrimStart('.')): found $($segments.Count) spoken segments but the script needs $expected. Regenerate the lines that don't line up as separate files named by id."
    }

    $cmds = @()
    $k = 0
    foreach ($line in $lines) {
        $from = [Math]::Max(0.0, $segments[$k][0] - 0.04) # 0.0, not 0: Max(int, double) rounds to an int
        $to = [Math]::Min($duration, $segments[$k + $line.Pieces - 1][1] + 0.1)
        $k += $line.Pieces
        $cmds += "ffmpeg -loglevel error -y -i '/in/$($all.Name)' -ss $($from.ToString('0.000', $inv)) -to $($to.ToString('0.000', $inv)) -c:a pcm_s16le '/split/$($line.Id).wav'"
        "  {0,-9} {1,5:0.00}s  {2}" -f $line.Id, ($to - $from), $line.Text
    }
    # LF line endings: a CRLF script would leave a stray \r on every output file name.
    [IO.File]::WriteAllText((Join-Path $staging 'split.sh'), ((@('apk add --no-cache ffmpeg > /dev/null') + $cmds) -join "`n") + "`n")
    wsl -d Ubuntu -- docker run --rm -v "$(ConvertTo-WslPath $raw):/in:ro" -v "$(ConvertTo-WslPath $staging):/split" alpine:3.20 sh /split/split.sh
    if ($LASTEXITCODE -ne 0) { throw 'Splitting all.wav failed' }
    Remove-Item (Join-Path $staging 'split.sh')
}

if ($audio.Count -gt 0) {
    $voiceOut = Join-Path $out 'voice'
    New-Item -ItemType Directory -Force $voiceOut | Out-Null
    wsl -d Ubuntu -- docker run --rm `
        -v "$(ConvertTo-WslPath $raw):/in:ro" `
        -v "$(ConvertTo-WslPath $staging):/split:ro" `
        -v "$(ConvertTo-WslPath $voiceOut):/out" `
        -v "$(ConvertTo-WslPath (Join-Path $PSScriptRoot 'tools')):/tools:ro" `
        alpine:3.20 sh /tools/convert-voice.sh
    if ($LASTEXITCODE -ne 0) { throw 'Voice conversion failed (is Docker running in WSL?)' }
}
Remove-Item -Recurse -Force $staging
"Done: $out"
