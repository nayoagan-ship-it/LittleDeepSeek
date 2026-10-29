param([string]$OutputPath, [string]$AssetDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $AssetDirectory) { $AssetDirectory = Join-Path $projectRoot 'assets' }
$AssetDirectory = [IO.Path]::GetFullPath($AssetDirectory)
$assetNames = @('pet.png','pet.ico','pet-side-alpha.png','pet-back-alpha.png','pose-sleep.png','pose-flower.png','pose-rice.png')
# Original third-party character art is excluded from this source release.
if (-not $PSBoundParameters.ContainsKey('AssetDirectory') -and -not (Test-Path -LiteralPath (Join-Path $AssetDirectory 'pet.png'))) {
    $AssetDirectory = Join-Path $projectRoot 'artifacts\placeholder-assets'
    & (Join-Path $PSScriptRoot 'make-placeholder-assets.ps1') -OutputDirectory $AssetDirectory
    Write-Host 'Using geometric placeholders. See ASSETS.md to supply your own art.'
}
& (Join-Path $PSScriptRoot 'restore-dependencies.ps1')
$packageRoot = Join-Path $PSScriptRoot 'packages\Microsoft.Web.WebView2.1.0.4129.50'
$webViewCore = Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$webViewForms = Join-Path $packageRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$webViewLoader = Join-Path $packageRoot 'runtimes\win-x64\native\WebView2Loader.dll'
$speechAssembly = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll'
$compiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $compiler) { throw 'Install or enable .NET Framework 4.x before building.' }
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'artifacts\bin\LittleDeepSeek.exe' }
$outputFile = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFile
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$sourceFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$requiredFiles = @($speechAssembly,(Join-Path $PSScriptRoot 'app.manifest'),(Join-Path $projectRoot 'todo-app\index.html'))
$requiredFiles += @($assetNames | ForEach-Object { Join-Path $AssetDirectory $_ })
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Required build input is missing: $requiredFile" }
}
$compilerArgs = @(
    '/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll',
    '/reference:System.Management.dll','/reference:System.Web.Extensions.dll',
    "/reference:$speechAssembly","/reference:$webViewCore","/reference:$webViewForms",
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')",
    "/win32icon:$(Join-Path $AssetDirectory 'pet.ico')","/out:$outputFile"
)
$resources = @{
    'pet.png'='pet.png';'pet.ico'='pet.ico';'pet-side-alpha.png'='side.png';
    'pet-back-alpha.png'='back.png';'pose-sleep.png'='sleep.png';
    'pose-flower.png'='flower.png';'pose-rice.png'='rice.png'
}
foreach ($asset in $assetNames) { $compilerArgs += "/resource:$(Join-Path $AssetDirectory $asset),LittleDeepSeek.$($resources[$asset])" }
foreach ($animation in @('idle-breath','stretch','yawn','nap','think','daydream','note','flowers','bubbles','celebrate')) {
    $animationPath = Join-Path $AssetDirectory ("animations\$animation.gif")
    if (Test-Path -LiteralPath $animationPath -PathType Leaf) { $compilerArgs += "/resource:$animationPath,LittleDeepSeek.dsh.$animation.gif" }
}
$compilerArgs += $sourceFiles
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
foreach ($dependency in @($webViewCore,$webViewForms,$webViewLoader)) {
    Copy-Item -LiteralPath $dependency -Destination (Join-Path $outputDirectory ([IO.Path]::GetFileName($dependency))) -Force
}
$todoOutput = Join-Path $outputDirectory 'todo-app'
$null = New-Item -ItemType Directory -Path $todoOutput -Force
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'todo-app') -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $todoOutput -Recurse -Force }
foreach ($notice in @('LICENSE','THIRD_PARTY_NOTICES.md','ASSETS.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $outputDirectory -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $outputDirectory -Recurse -Force
Write-Host "Build complete: $outputFile"