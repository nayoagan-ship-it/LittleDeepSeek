param([string]$AppPath, [switch]$Live)
$ErrorActionPreference = 'Stop'
$petRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $AppPath) { $AppPath = Join-Path $petRoot 'artifacts\bin\LittleDeepSeek.exe' }
if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'Build the desktop pet first.' }
$petTestName = if ($Live) { 'CuteAutoplayLiveProbe' } else { 'CuteInteractionTests' }
$petTestRoot = Join-Path $petRoot ('artifacts\tests\' + $petTestName)
New-Item -ItemType Directory -Path $petTestRoot -Force | Out-Null
$petAssets = Join-Path (Split-Path -Parent $AppPath) 'assets'
if (Test-Path -LiteralPath $petAssets) { Copy-Item -LiteralPath $petAssets -Destination $petTestRoot -Recurse -Force }
$petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$petTestExe = Join-Path $petTestRoot ($petTestName + '.exe')
& $petCompiler /nologo /target:exe /utf8output /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$petTestExe" (Join-Path $PSScriptRoot ($petTestName + '.cs'))
if ($LASTEXITCODE -ne 0) { throw 'Interaction test compilation failed.' }
& $petTestExe $AppPath
if ($LASTEXITCODE -ne 0) { throw 'Interaction tests failed.' }
