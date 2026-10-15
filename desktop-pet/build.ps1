# Builds bin\DesktopPet.exe with the C# compiler bundled in .NET Framework 4.x (no SDK needed).
# Processed character art/voice under assets\<id>\build\ is embedded as resources named "<id>/<path>".
param([string]$AssetsRoot = (Join-Path $PSScriptRoot 'assets'))
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$out = Join-Path $bin 'DesktopPet.exe'

$resources = @()
if (Test-Path $AssetsRoot) {
    foreach ($char in Get-ChildItem $AssetsRoot -Directory) {
        $build = Join-Path $char.FullName 'build'
        if (-not (Test-Path (Join-Path $build 'manifest.txt'))) { continue }
        foreach ($file in Get-ChildItem $build -Recurse -File) {
            $rel = $file.FullName.Substring($build.Length + 1).Replace('\', '/')
            $resources += "/resource:$($file.FullName),$($char.Name)/$rel"
        }
        "Embedding $($char.Name): $(@(Get-ChildItem $build -Recurse -File).Count) files"
    }
}

$wpf = Join-Path (Split-Path $csc) 'WPF' # System.Speech.dll lives here
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /out:$out "/lib:$wpf" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Speech.dll `
    @resources (Join-Path $PSScriptRoot 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
"Built: $out"
