param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('LittleDeepSeekBuild.NativeIcon' -as [type])) {
    Add-Type -TypeDefinition 'namespace LittleDeepSeekBuild { public static class NativeIcon { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool DestroyIcon(System.IntPtr handle); } }'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$images = @{
    'pet.png'='#5279E8';'pet-side-alpha.png'='#5279E8';'pet-back-alpha.png'='#5279E8';
    'pose-sleep.png'='#8C88C9';'pose-flower.png'='#C971A0';'pose-rice.png'='#D5A34A'
}
foreach ($name in $images.Keys) {
    $bitmap = New-Object System.Drawing.Bitmap 256,256
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($images[$name]))
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.FillEllipse($brush,24,24,208,208)
        $graphics.FillRectangle([System.Drawing.Brushes]::White,104,104,48,48)
        $bitmap.Save((Join-Path $OutputDirectory $name),[System.Drawing.Imaging.ImageFormat]::Png)
        if ($name -eq 'pet.png') {
            $small = New-Object System.Drawing.Bitmap $bitmap,64,64
            $handle = $small.GetHicon()
            $borrowedIcon = [System.Drawing.Icon]::FromHandle($handle)
            $ownedIcon = $borrowedIcon.Clone()
            $stream = [System.IO.File]::Create((Join-Path $OutputDirectory 'pet.ico'))
            try { $ownedIcon.Save($stream) }
            finally {
                $stream.Dispose(); $ownedIcon.Dispose(); $borrowedIcon.Dispose()
                $null = [LittleDeepSeekBuild.NativeIcon]::DestroyIcon($handle)
                $small.Dispose()
            }
        }
    }
    finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
