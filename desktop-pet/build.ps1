# Builds bin\DesktopPet.exe with the C# compiler bundled in .NET Framework 4.x (no SDK needed).
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$out = Join-Path $bin 'DesktopPet.exe'

& $csc /nologo /target:winexe /codepage:65001 /optimize+ /out:$out `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    (Join-Path $PSScriptRoot 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
"Built: $out"
