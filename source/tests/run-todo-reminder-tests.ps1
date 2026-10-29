$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputFile = Join-Path ([System.IO.Path]::GetTempPath()) ('LittleDeepSeek-TodoReminderTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /utf8output /codepage:65001 /reference:System.Web.Extensions.dll "/out:$outputFile" (Join-Path $sourceRoot 'TodoReminderState.cs') (Join-Path $PSScriptRoot 'TodoReminderStateTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Todo reminder test compilation failed.' }
    & $outputFile
    if ($LASTEXITCODE -ne 0) { throw 'Todo reminder tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $outputFile) { Remove-Item -LiteralPath $outputFile -Force }
}
