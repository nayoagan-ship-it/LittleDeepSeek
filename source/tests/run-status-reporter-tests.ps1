$ErrorActionPreference = 'Stop'

$sourceRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Missing .NET Framework C# compiler.' }

$output = Join-Path $PSScriptRoot 'StatusReporterTests.exe'
$statusSource = Join-Path $sourceRoot 'WeatherHardwareStatus.cs'
$testSource = Join-Path $PSScriptRoot 'StatusReporterTests.cs'
& $compiler /nologo /target:exe /utf8output /codepage:65001 /main:LittleDeepSeek.StatusReporterTests /out:$output `
    /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll `
    $statusSource $testSource
if ($LASTEXITCODE -ne 0) { throw "Test compile failed: $LASTEXITCODE" }
& $output
if ($LASTEXITCODE -ne 0) { throw "Test failed: $LASTEXITCODE" }
