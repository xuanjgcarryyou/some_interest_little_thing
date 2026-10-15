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

$audio = @(Get-ChildItem $raw -File | Where-Object { $_.Extension -in '.wav', '.mp3', '.m4a', '.ogg', '.flac' })
if ($audio.Count -gt 0) {
    $voiceOut = Join-Path $out 'voice'
    New-Item -ItemType Directory -Force $voiceOut | Out-Null
    wsl -d Ubuntu -- docker run --rm `
        -v "$(ConvertTo-WslPath $raw):/in:ro" `
        -v "$(ConvertTo-WslPath $voiceOut):/out" `
        -v "$(ConvertTo-WslPath (Join-Path $PSScriptRoot 'tools')):/tools:ro" `
        alpine:3.20 sh /tools/convert-voice.sh
    if ($LASTEXITCODE -ne 0) { throw 'Voice conversion failed (is Docker running in WSL?)' }
}
"Done: $out"
