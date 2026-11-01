param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$poses = @('front','side','back','sleep','flower','rice','blink','stretch','yawn','think','daydream','note','bubbles','celebrate')
$skins = @(
    @{ Id='default'; Name='小deepseek'; Color='#5279E8' },
    @{ Id='sample-coral'; Name='珊瑚伙伴'; Color='#DC7B86' },
    @{ Id='sample-mint'; Name='薄荷伙伴'; Color='#40A99F' }
)
foreach ($skin in $skins) {
    $folder = Join-Path $OutputDirectory ("skins\" + $skin.Id)
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $atlas = [Drawing.Bitmap]::new(96 * $poses.Count, 128)
    $graphics = [Drawing.Graphics]::FromImage($atlas)
    $body = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($skin.Color))
    $ink = [Drawing.Pen]::new([Drawing.Color]::White, 3)
    $definitions = [ordered]@{}
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        for ($i = 0; $i -lt $poses.Count; $i++) {
            $pose = $poses[$i]
            $state = $graphics.Save()
            $graphics.TranslateTransform($i * 96, 0)
            if ($pose -eq 'sleep') { $graphics.FillEllipse($body, 12, 52, 72, 46) }
            elseif ($pose -eq 'side') { $graphics.FillEllipse($body, 25, 18, 48, 90) }
            elseif ($pose -eq 'stretch') { $graphics.FillEllipse($body, 23, 12, 50, 101) }
            else { $graphics.FillEllipse($body, 12, 18, 72, 90) }
            if ($pose -eq 'back') {
                $graphics.DrawArc($ink, 30, 37, 36, 36, 15, 150)
            } elseif ($pose -in @('sleep','blink')) {
                $eyeY = if ($pose -eq 'sleep') { 72 } else { 50 }
                $graphics.DrawLine($ink, 30, $eyeY, 40, $eyeY)
                $graphics.DrawLine($ink, 57, $eyeY, 67, $eyeY)
            } else {
                $graphics.FillEllipse([Drawing.Brushes]::White, 31, 45, 7, 10)
                if ($pose -ne 'side') { $graphics.FillEllipse([Drawing.Brushes]::White, 58, 45, 7, 10) }
            }
            if ($pose -eq 'yawn') { $graphics.DrawEllipse($ink, 42, 63, 13, 19) }
            if ($pose -eq 'flower') {
                $graphics.FillEllipse([Drawing.Brushes]::Gold, 37, 80, 24, 20)
                $graphics.DrawLine($ink, 49, 91, 49, 111)
            }
            if ($pose -eq 'rice') { $graphics.FillPie([Drawing.Brushes]::White, 28, 78, 42, 27, 0, 180) }
            if ($pose -eq 'note') { $graphics.FillRectangle([Drawing.Brushes]::LemonChiffon, 50, 72, 27, 29) }
            if ($pose -eq 'bubbles') { $graphics.DrawEllipse($ink, 68, 9, 13, 13) }
            if ($pose -eq 'think') { $graphics.FillEllipse([Drawing.Brushes]::White, 70, 14, 9, 9) }
            if ($pose -eq 'daydream') { $graphics.DrawArc($ink, 31, 63, 34, 20, 0, 180) }
            if ($pose -eq 'celebrate') {
                $graphics.DrawLine($ink, 17, 43, 8, 25)
                $graphics.DrawLine($ink, 78, 43, 88, 25)
            }
            $graphics.Restore($state)
            $definitions[$pose] = @{ image='poses.png'; rect=@(($i * 96), 0, 96, 128); height=210 }
        }
        $atlas.Save((Join-Path $folder 'poses.png'), [Drawing.Imaging.ImageFormat]::Png)
        $manifest = @{ schemaVersion=1; id=$skin.Id; name=$skin.Name; sideFacesLeft=$false; mouth=@(.5,.62); poses=$definitions }
        [IO.File]::WriteAllText((Join-Path $folder 'skin.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    } finally { $ink.Dispose(); $body.Dispose(); $graphics.Dispose(); $atlas.Dispose() }
}
