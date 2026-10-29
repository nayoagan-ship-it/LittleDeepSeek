param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$version = '1.0.4129.50'
$packageId = "Microsoft.Web.WebView2.$version"
$expectedHash = 'D3934F482D484B89FB4825DF720C710664E1143A1E90F7B3A60794EF33F473D2'
$cacheRoot = Join-Path $PSScriptRoot 'packages'
$null = New-Item -ItemType Directory -Path $cacheRoot -Force
if (-not $PackagePath) { $PackagePath = Join-Path $cacheRoot ($packageId + '.nupkg') }
$PackagePath = [System.IO.Path]::GetFullPath($PackagePath)
if ($PSBoundParameters.ContainsKey('PackagePath') -and -not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "The supplied offline package does not exist: $PackagePath" }
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    $uri = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg"
    $temporaryDownload = Join-Path $cacheRoot ([Guid]::NewGuid().ToString('N') + '.download')
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $temporaryDownload -TimeoutSec 30
        if ((Get-FileHash -LiteralPath $temporaryDownload -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The WebView2 package checksum does not match the pinned version.' }
        Move-Item -LiteralPath $temporaryDownload -Destination $PackagePath
    }
    finally { if (Test-Path -LiteralPath $temporaryDownload) { Remove-Item -LiteralPath $temporaryDownload -Force } }
}
if ((Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash -ne $expectedHash) { throw "Invalid package checksum: $PackagePath" }
$cachedPackage = [System.IO.Path]::GetFullPath((Join-Path $cacheRoot ($packageId + '.nupkg')))
if (-not [String]::Equals($PackagePath,$cachedPackage,[StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $PackagePath -Destination $cachedPackage -Force
    $PackagePath = $cachedPackage
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageRoot = Join-Path $cacheRoot $packageId
# Only these known paths are extracted, so ZIP entries cannot escape the cache.
$entries = @('lib/net462/Microsoft.Web.WebView2.Core.dll','lib/net462/Microsoft.Web.WebView2.WinForms.dll','runtimes/win-x64/native/WebView2Loader.dll','LICENSE.txt','NOTICE.txt')
$archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    foreach ($entryName in $entries) {
        $entry = $archive.GetEntry($entryName)
        if (-not $entry) { throw "Required NuGet entry is missing: $entryName" }
        $destination = Join-Path $packageRoot ($entryName.Replace('/',[System.IO.Path]::DirectorySeparatorChar))
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
    }
}
finally { $archive.Dispose() }
Write-Host "Restored $packageId (SHA-256 verified)."
