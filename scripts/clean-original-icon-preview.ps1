Add-Type -AssemblyName System.Drawing

$sourcePath = 'E:\codex_project\assets\generated\sleep-timer-icon_00005_.png'
$outputPath = 'E:\codex_project\assets\generated\sleep-timer-icon-reference-preview-v2.png'
$source = [System.Drawing.Bitmap]::new($sourcePath)
$clean = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$background = [System.Drawing.Color]::FromArgb(255, 20, 31, 52)
$lavender = [System.Drawing.Color]::FromArgb(255, 204, 191, 255)

for ($y = 0; $y -lt $source.Height; $y++) {
    for ($x = 0; $x -lt $source.Width; $x++) {
        $pixel = $source.GetPixel($x, $y)
        $insideSafeArea = $x -ge 50 -and $x -lt ($source.Width - 50) -and $y -ge 50 -and $y -lt ($source.Height - 50)
        $isLavender = $insideSafeArea -and $pixel.B -gt ($pixel.R + 5) -and $pixel.R -gt 60 -and $pixel.B -gt 100
        $clean.SetPixel($x, $y, $(if ($isLavender) { $lavender } else { $background }))
    }
}

$clean.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$source.Dispose()
$clean.Dispose()
